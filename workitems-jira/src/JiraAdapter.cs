using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

using JuniorDev.Contracts;
using JuniorDev.Orchestrator;
using Microsoft.Extensions.Logging;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Options;
using System.Linq;

namespace JuniorDev.WorkItems.Jira;

public class JiraAdapter : IAdapter
{
    private const int MaxRetries = 3;
    private const int BaseDelayMs = 1000;

    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly string _projectKey;
    private readonly string _authHeader;
    private readonly CircuitBreaker _circuitBreaker;
    private readonly ILogger<JiraAdapter> _logger;
    private readonly Meter _meter;
    private readonly Counter<long> _commandsProcessed;
    private readonly Counter<long> _commandsSucceeded;
    private readonly Counter<long> _commandsFailed;
    private readonly Counter<long> _apiCalls;
    private readonly Counter<long> _apiErrors;
    private readonly IOptionsMonitor<LivePolicyConfig> _livePolicyMonitor;

    public JiraAdapter(AppConfig appConfig, ILogger<JiraAdapter> logger, IOptionsMonitor<LivePolicyConfig>? livePolicyMonitor = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _livePolicyMonitor = livePolicyMonitor ?? new StaticOptionsMonitor<LivePolicyConfig>();

        var jiraAuth = appConfig.Auth?.Jira;
        if (jiraAuth == null)
        {
            throw new InvalidOperationException("Jira authentication configuration is missing. Please configure AppConfig.Auth.Jira in appsettings.json or environment variables.");
        }

        _baseUrl = jiraAuth.BaseUrl ?? throw new InvalidOperationException("Jira BaseUrl is not configured");
        _projectKey = jiraAuth.ProjectKey ?? throw new InvalidOperationException("Jira ProjectKey is not configured");

        var username = jiraAuth.Username ?? throw new InvalidOperationException("Jira Username is not configured");
        var token = jiraAuth.ApiToken ?? throw new InvalidOperationException("Jira ApiToken is not configured");

        _authHeader = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{token}"));
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", _authHeader);

        _circuitBreaker = new CircuitBreaker();

        // Initialize metrics
        _meter = new Meter("JuniorDev.WorkItems.Jira", "1.0.0");
        _commandsProcessed = _meter.CreateCounter<long>("jira_commands_processed", "commands", "Number of Jira commands processed");
        _commandsSucceeded = _meter.CreateCounter<long>("jira_commands_succeeded", "commands", "Number of Jira commands that succeeded");
        _commandsFailed = _meter.CreateCounter<long>("jira_commands_failed", "commands", "Number of Jira commands that failed");
        _apiCalls = _meter.CreateCounter<long>("jira_api_calls", "calls", "Number of API calls made to Jira");
        _apiErrors = _meter.CreateCounter<long>("jira_api_errors", "errors", "Number of API errors from Jira");
    }

    public bool CanHandle(ICommand command)
    {
        return command is TransitionTicket or Comment or SetAssignee;
    }

    public async Task HandleCommand(ICommand command, SessionState session)
    {
        _commandsProcessed.Add(1, new KeyValuePair<string, object?>("command_type", command.GetType().Name));

        // Emit CommandAccepted
        var acceptedEvent = new CommandAccepted(
            Guid.NewGuid(),
            command.Correlation,
            command.Id);

        await session.AddEvent(acceptedEvent);

        // Check for dry-run mode (read dynamically from configuration)
        var livePolicy = _livePolicyMonitor.CurrentValue;
        if (livePolicy?.DryRun ?? true)
        {
            _logger.LogInformation("Dry-run mode: Command {CommandType} would be executed", command.GetType().Name);
            var dryRunEvent = new CommandCompleted(
                Guid.NewGuid(),
                command.Correlation,
                command.Id,
                CommandOutcome.Success);

            await session.AddEvent(dryRunEvent);

            var artifact = new Artifact(
                "workitem-dry-run",
                $"Dry Run: {command.GetType().Name}",
                $"Command {command.GetType().Name} would be executed (dry-run mode)",
                null,
                null,
                "text/plain");

            await session.AddEvent(new ArtifactAvailable(Guid.NewGuid(), command.Correlation, artifact));
            _commandsSucceeded.Add(1, new KeyValuePair<string, object?>("command_type", command.GetType().Name));
            return;
        }

        try
        {
            switch (command)
            {
                case Comment comment:
                    await HandleComment(comment, session);
                    break;
                case TransitionTicket transition:
                    await HandleTransition(transition, session);
                    break;
                case SetAssignee assign:
                    await HandleSetAssignee(assign, session);
                    break;
                default:
                    throw new NotSupportedException($"Command type {command.GetType()} not supported");
            }

            // Emit CommandCompleted on success
            var completedEvent = new CommandCompleted(
                Guid.NewGuid(),
                command.Correlation,
                command.Id,
                CommandOutcome.Success);

            await session.AddEvent(completedEvent);
            _commandsSucceeded.Add(1, new KeyValuePair<string, object?>("command_type", command.GetType().Name));
        }
        catch (CircuitBreakerOpenException ex)
        {
            _logger.LogWarning(ex, "Circuit breaker open for command {CommandType}", command.GetType().Name);
            var rejectedEvent = new CommandRejected(
                Guid.NewGuid(),
                command.Correlation,
                command.Id,
                $"Circuit breaker open: {ex.Message}",
                "CIRCUIT_BREAKER");

            await session.AddEvent(rejectedEvent);
            _commandsFailed.Add(1, new KeyValuePair<string, object?>("command_type", command.GetType().Name), new KeyValuePair<string, object?>("error_type", "CIRCUIT_BREAKER"));
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            _logger.LogError(ex, "Authentication failed for command {CommandType}", command.GetType().Name);
            var rejectedEvent = new CommandRejected(
                Guid.NewGuid(),
                command.Correlation,
                command.Id,
                "Authentication failed. Check Jira credentials in configuration.",
                "AUTH_ERROR");

            await session.AddEvent(rejectedEvent);
            _commandsFailed.Add(1, new KeyValuePair<string, object?>("command_type", command.GetType().Name), new KeyValuePair<string, object?>("error_type", "AUTH_ERROR"));
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            _logger.LogError(ex, "Access forbidden for command {CommandType}", command.GetType().Name);
            var rejectedEvent = new CommandRejected(
                Guid.NewGuid(),
                command.Correlation,
                command.Id,
                "Access forbidden. Check user permissions in Jira.",
                "PERMISSION_ERROR");

            await session.AddEvent(rejectedEvent);
            _commandsFailed.Add(1, new KeyValuePair<string, object?>("command_type", command.GetType().Name), new KeyValuePair<string, object?>("error_type", "PERMISSION_ERROR"));
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning(ex, "Work item not found for command {CommandType}", command.GetType().Name);
            var rejectedEvent = new CommandRejected(
                Guid.NewGuid(),
                command.Correlation,
                command.Id,
                "Work item not found in Jira.",
                "NOT_FOUND");

            await session.AddEvent(rejectedEvent);
            _commandsFailed.Add(1, new KeyValuePair<string, object?>("command_type", command.GetType().Name), new KeyValuePair<string, object?>("error_type", "NOT_FOUND"));
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        {
            _logger.LogWarning(ex, "Rate limit exceeded for command {CommandType}", command.GetType().Name);
            var rejectedEvent = new CommandRejected(
                Guid.NewGuid(),
                command.Correlation,
                command.Id,
                "Rate limit exceeded. Please retry later.",
                "RATE_LIMIT");

            await session.AddEvent(rejectedEvent);
            _commandsFailed.Add(1, new KeyValuePair<string, object?>("command_type", command.GetType().Name), new KeyValuePair<string, object?>("error_type", "RATE_LIMIT"));
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error for command {CommandType}: {StatusCode}", command.GetType().Name, ex.StatusCode);
            var rejectedEvent = new CommandRejected(
                Guid.NewGuid(),
                command.Correlation,
                command.Id,
                $"HTTP error: {ex.StatusCode} - {ex.Message}",
                "HTTP_ERROR");

            await session.AddEvent(rejectedEvent);
            _commandsFailed.Add(1, new KeyValuePair<string, object?>("command_type", command.GetType().Name), new KeyValuePair<string, object?>("error_type", "HTTP_ERROR"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "General error for command {CommandType}", command.GetType().Name);
            var rejectedEvent = new CommandRejected(
                Guid.NewGuid(),
                command.Correlation,
                command.Id,
                ex.Message,
                "GENERAL_ERROR");

            await session.AddEvent(rejectedEvent);
            _commandsFailed.Add(1, new KeyValuePair<string, object?>("command_type", command.GetType().Name), new KeyValuePair<string, object?>("error_type", "GENERAL_ERROR"));
        }
    }

    private async Task HandleComment(Comment comment, SessionState session)
    {
        var issueKey = GetIssueKey(comment.Item.Id);
        var url = $"{_baseUrl}/rest/api/2/issue/{issueKey}/comment";

        var requestBody = new
        {
            body = comment.Body
        };

        var response = await ExecuteWithRetry(() => _httpClient.PostAsJsonAsync(url, requestBody));
        response.EnsureSuccessStatusCode();

        var responseData = await response.Content.ReadFromJsonAsync<JiraCommentResponse>();
        if (responseData == null) throw new InvalidOperationException("Invalid response from Jira");

        // Create artifact with comment details
        var artifact = new Artifact(
            "workitem-comment",
            $"Comment on {comment.Item.Id}",
            $"Comment added to {comment.Item.Id}: {comment.Body}",
            null,
            null,
            "text/plain");

        await session.AddEvent(new ArtifactAvailable(Guid.NewGuid(), comment.Correlation, artifact));
    }

    private async Task HandleTransition(TransitionTicket transition, SessionState session)
    {
        var issueKey = GetIssueKey(transition.Item.Id);
        var url = $"{_baseUrl}/rest/api/2/issue/{issueKey}/transitions";

        // First get available transitions
        var transitionsResponse = await ExecuteWithRetry(() => _httpClient.GetAsync(url));
        transitionsResponse.EnsureSuccessStatusCode();

        var transitionsData = await transitionsResponse.Content.ReadFromJsonAsync<JiraTransitionsResponse>();
        if (transitionsData == null) throw new InvalidOperationException("Invalid transitions response from Jira");

        var targetTransition = transitionsData.transitions.FirstOrDefault(t => t.name.Equals(transition.State, StringComparison.OrdinalIgnoreCase));
        if (targetTransition == null)
        {
            throw new InvalidOperationException($"Transition '{transition.State}' not available for issue {issueKey}. Available transitions: {string.Join(", ", transitionsData.transitions.Select(t => t.name))}");
        }

        var requestBody = new
        {
            transition = new
            {
                id = targetTransition.id
            }
        };

        var response = await ExecuteWithRetry(() => _httpClient.PostAsJsonAsync(url, requestBody));
        response.EnsureSuccessStatusCode();

        // Create artifact with transition details
        var artifact = new Artifact(
            "workitem-transition",
            $"Transition {transition.Item.Id}",
            $"Work item {transition.Item.Id} transitioned to {transition.State}",
            null,
            null,
            "text/plain");

        await session.AddEvent(new ArtifactAvailable(Guid.NewGuid(), transition.Correlation, artifact));
    }

    private async Task HandleSetAssignee(SetAssignee assign, SessionState session)
    {
        var issueKey = GetIssueKey(assign.Item.Id);
        var url = $"{_baseUrl}/rest/api/2/issue/{issueKey}/assignee";

        var requestBody = new
        {
            name = assign.Assignee
        };

        var response = await ExecuteWithRetry(() => _httpClient.PutAsJsonAsync(url, requestBody));
        response.EnsureSuccessStatusCode();

        // Create artifact with assignment details
        var artifact = new Artifact(
            "workitem-assignment",
            $"Assignment {assign.Item.Id}",
            $"Work item {assign.Item.Id} assigned to {assign.Assignee}",
            null,
            null,
            "text/plain");

        await session.AddEvent(new ArtifactAvailable(Guid.NewGuid(), assign.Correlation, artifact));
    }

    private string GetIssueKey(string workItemId)
    {
        // If workItemId already contains project key, use it as-is
        if (workItemId.Contains('-'))
        {
            return workItemId;
        }
        // Otherwise, prepend project key
        return $"{_projectKey}-{workItemId}";
    }

    private async Task<HttpResponseMessage> ExecuteWithRetry(Func<Task<HttpResponseMessage>> operation)
    {
        return await _circuitBreaker.ExecuteAsync(async () =>
        {
            for (int attempt = 0; attempt < MaxRetries; attempt++)
            {
                try
                {
                    _apiCalls.Add(1);
                    var response = await operation();
                    if (response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                        response.StatusCode == System.Net.HttpStatusCode.Forbidden || response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        return response;
                    }
                    // For other errors, check if retryable
                    if (IsRetryableError(response.StatusCode) && attempt < MaxRetries - 1)
                    {
                        _apiErrors.Add(1, new KeyValuePair<string, object?>("error_type", response.StatusCode.ToString()));
                        var delay = CalculateDelayWithJitter(attempt);
                        _logger.LogWarning("Retryable error ({StatusCode}), attempt {Attempt}/{MaxRetries}, delaying {Delay}ms", response.StatusCode, attempt + 1, MaxRetries, delay);
                        await Task.Delay(delay);
                        continue;
                    }
                    return response;
                }
                catch (HttpRequestException ex) when (IsTransientNetworkError(ex))
                {
                    _apiErrors.Add(1, new KeyValuePair<string, object?>("error_type", "NETWORK_ERROR"));
                    if (attempt < MaxRetries - 1)
                    {
                        var delay = CalculateDelayWithJitter(attempt);
                        _logger.LogWarning(ex, "Network error, attempt {Attempt}/{MaxRetries}, delaying {Delay}ms", attempt + 1, MaxRetries, delay);
                        await Task.Delay(delay);
                        continue;
                    }
                    throw;
                }
            }
            throw new InvalidOperationException("Max retries exceeded");
        });
    }

    private static bool IsRetryableError(System.Net.HttpStatusCode? statusCode)
    {
        return statusCode == System.Net.HttpStatusCode.TooManyRequests ||
               statusCode == System.Net.HttpStatusCode.RequestTimeout ||
               statusCode == System.Net.HttpStatusCode.InternalServerError ||
               statusCode == System.Net.HttpStatusCode.BadGateway ||
               statusCode == System.Net.HttpStatusCode.ServiceUnavailable ||
               statusCode == System.Net.HttpStatusCode.GatewayTimeout;
    }

    private static bool IsTransientNetworkError(HttpRequestException ex)
    {
        // Network errors that should be retried
        return ex.InnerException is System.Net.Sockets.SocketException ||
               ex.InnerException is System.IO.IOException ||
               ex.StatusCode == null; // Connection failures
    }

    private static int CalculateDelayWithJitter(int attempt)
    {
        // Exponential backoff: baseDelay * 2^attempt
        var exponentialDelay = BaseDelayMs * Math.Pow(2, attempt);
        // Add jitter: random value between 0 and exponentialDelay/2
        var jitter = Random.Shared.Next(0, (int)(exponentialDelay / 2));
        var totalDelay = (int)exponentialDelay + jitter;
        // Cap at max delay (30 seconds)
        return Math.Min(totalDelay, 30000);
    }

    private class JiraCommentResponse
    {
        public string id { get; set; } = "";
        public string body { get; set; } = "";
    }

    private class JiraTransitionsResponse
    {
        public JiraTransition[] transitions { get; set; } = Array.Empty<JiraTransition>();
    }

    private class JiraTransition
    {
        public string id { get; set; } = "";
        public string name { get; set; } = "";
    }
}
