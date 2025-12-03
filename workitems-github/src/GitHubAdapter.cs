using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using JuniorDev.Contracts;
using JuniorDev.Orchestrator;
using Microsoft.Extensions.Logging;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Options;
using System.Linq;

namespace JuniorDev.WorkItems.GitHub;

public class GitHubAdapter : IAdapter
{
    private readonly HttpClient _httpClient;
    private readonly string _repo;
    private readonly CircuitBreaker _circuitBreaker;
    private readonly ILogger<GitHubAdapter> _logger;
    private readonly Meter _meter;
    private readonly Counter<long> _commandsProcessed;
    private readonly Counter<long> _commandsSucceeded;
    private readonly Counter<long> _commandsFailed;
    private readonly Counter<long> _apiCalls;
    private readonly Counter<long> _apiErrors;
    private readonly IOptionsMonitor<LivePolicyConfig> _livePolicyMonitor;
    private const int MaxRetries = 3;
    private const int BaseDelayMs = 1000;
    private const int MaxDelayMs = 30000;
    private static readonly Random _random = new();

    public GitHubAdapter(AppConfig appConfig, ILogger<GitHubAdapter>? logger = null, IOptionsMonitor<LivePolicyConfig>? livePolicyMonitor = null)
    {
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<GitHubAdapter>.Instance;
        _livePolicyMonitor = livePolicyMonitor ?? new OptionsMonitor<LivePolicyConfig>(new OptionsFactory<LivePolicyConfig>(Enumerable.Empty<IConfigureOptions<LivePolicyConfig>>(), Enumerable.Empty<IPostConfigureOptions<LivePolicyConfig>>()), Enumerable.Empty<IOptionsChangeTokenSource<LivePolicyConfig>>(), new OptionsCache<LivePolicyConfig>());

        var gitHubAuth = appConfig.Auth?.GitHub;
        if (gitHubAuth == null || string.IsNullOrEmpty(gitHubAuth.Token))
        {
            throw new InvalidOperationException("GitHub authentication configuration is missing. Please configure AppConfig.Auth.GitHub.Token in appsettings.json or environment variables.");
        }

        var token = gitHubAuth.Token;
        _repo = !string.IsNullOrEmpty(gitHubAuth.DefaultOrg) && !string.IsNullOrEmpty(gitHubAuth.DefaultRepo)
            ? $"{gitHubAuth.DefaultOrg}/{gitHubAuth.DefaultRepo}"
            : "owner/repo"; // fallback

        var baseUrl = "https://api.github.com";

        _httpClient = new HttpClient { BaseAddress = new Uri(baseUrl) };
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("JuniorDev", "1.0"));

        _circuitBreaker = new CircuitBreaker();

        // Initialize metrics
        _meter = new Meter("JuniorDev.WorkItems.GitHub", "1.0.0");
        _commandsProcessed = _meter.CreateCounter<long>("commands_processed", "commands", "Number of commands processed");
        _commandsSucceeded = _meter.CreateCounter<long>("commands_succeeded", "commands", "Number of commands that succeeded");
        _commandsFailed = _meter.CreateCounter<long>("commands_failed", "commands", "Number of commands that failed");
        _apiCalls = _meter.CreateCounter<long>("api_calls", "calls", "Number of API calls made");
        _apiErrors = _meter.CreateCounter<long>("api_errors", "errors", "Number of API errors encountered");
    }

    public bool CanHandle(ICommand command) => command is Comment or TransitionTicket or SetAssignee;

    public async Task HandleCommand(ICommand command, SessionState session)
    {
        var correlation = command switch
        {
            Comment c => c.Correlation,
            TransitionTicket t => t.Correlation,
            SetAssignee s => s.Correlation,
            _ => throw new NotSupportedException()
        };

        await session.AddEvent(new CommandAccepted(Guid.NewGuid(), correlation, command.Id));

        // Record command processed metric
        _commandsProcessed.Add(1, new KeyValuePair<string, object?>("command_type", command.Kind));

        // Check for dry-run mode (read dynamically from configuration)
        var livePolicy = _livePolicyMonitor.CurrentValue;
        if (livePolicy?.DryRun ?? true)
        {
            _logger.LogInformation("Processing command {CommandType} in dry-run mode", command.Kind);
            await session.AddEvent(new CommandCompleted(Guid.NewGuid(), correlation, command.Id, CommandOutcome.Success));

            var artifact = new Artifact("text", "GitHub Issue Updated (Dry Run)", InlineText: "Command would be executed (dry-run mode)", ContentType: "text/plain");
            await session.AddEvent(new ArtifactAvailable(Guid.NewGuid(), correlation, artifact));
            _commandsSucceeded.Add(1, new KeyValuePair<string, object?>("command_type", command.Kind));
            return;
        }

        try
        {
            _logger.LogInformation("Processing command {CommandType} for issue", command.Kind);
            switch (command)
            {
                case Comment c:
                    await HandleComment(c);
                    break;
                case TransitionTicket t:
                    await HandleTransition(t);
                    break;
                case SetAssignee s:
                    await HandleSetAssignee(s);
                    break;
            }

            var artifact = new Artifact("text", "GitHub Issue Updated", InlineText: "Issue updated successfully", ContentType: "text/plain");
            await session.AddEvent(new ArtifactAvailable(Guid.NewGuid(), correlation, artifact));
            await session.AddEvent(new CommandCompleted(Guid.NewGuid(), correlation, command.Id, CommandOutcome.Success));
            _commandsSucceeded.Add(1, new KeyValuePair<string, object?>("command_type", command.Kind));
        }
        catch (CircuitBreakerOpenException ex)
        {
            _logger.LogWarning(ex, "Circuit breaker open for command {CommandType}: {Message}", command.Kind, ex.Message);
            await session.AddEvent(new CommandRejected(Guid.NewGuid(), correlation, command.Id, $"Circuit breaker open: {ex.Message}", "CIRCUIT_BREAKER"));
            _commandsFailed.Add(1, new KeyValuePair<string, object?>("command_type", command.Kind), new KeyValuePair<string, object?>("failure_reason", "circuit_breaker"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process command {CommandType}", command.Kind);
            await session.AddEvent(new CommandRejected(Guid.NewGuid(), correlation, command.Id, ex.Message, "API_ERROR"));
            _commandsFailed.Add(1, new KeyValuePair<string, object?>("command_type", command.Kind), new KeyValuePair<string, object?>("failure_reason", "api_error"));
        }
    }

    private async Task HandleComment(Comment command)
    {
        var issueNumber = int.Parse(command.Item.Id);
        var url = $"/repos/{_repo}/issues/{issueNumber}/comments";
        var body = new { body = command.Body };
        var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        await ExecuteWithRetry(() => _httpClient.PostAsync(url, content));
    }

    private async Task HandleTransition(TransitionTicket command)
    {
        var issueNumber = int.Parse(command.Item.Id);
        var url = $"/repos/{_repo}/issues/{issueNumber}";
        var state = command.State == "Done" ? "closed" : "open";
        var body = new { state };
        var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        await ExecuteWithRetry(() => _httpClient.PatchAsync(url, content));
    }

    private async Task HandleSetAssignee(SetAssignee command)
    {
        var issueNumber = int.Parse(command.Item.Id);
        var url = $"/repos/{_repo}/issues/{issueNumber}";
        var body = new { assignees = new[] { command.Assignee } };
        var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        await ExecuteWithRetry(() => _httpClient.PatchAsync(url, content));
    }

    private async Task ExecuteWithRetry(Func<Task<HttpResponseMessage>> operation)
    {
        await _circuitBreaker.ExecuteAsync(async () =>
        {
            for (int attempt = 0; attempt < MaxRetries; attempt++)
            {
                try
                {
                    _apiCalls.Add(1);
                    var response = await operation();
                    response.EnsureSuccessStatusCode();
                    return response;
                }
                catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    // Rate limited - use exponential backoff with jitter
                    _apiErrors.Add(1, new KeyValuePair<string, object?>("error_type", "rate_limit"));
                    if (attempt < MaxRetries - 1)
                    {
                        var delay = CalculateDelayWithJitter(attempt);
                        _logger.LogWarning("Rate limited (429), attempt {Attempt}/{MaxRetries}, delaying {Delay}ms", attempt + 1, MaxRetries, delay);
                        await Task.Delay(delay);
                        continue;
                    }
                    throw new InvalidOperationException($"Rate limit exceeded after {MaxRetries} attempts");
                }
                catch (HttpRequestException ex) when (IsTransientError(ex.StatusCode))
                {
                    // Transient error - retry with backoff
                    _apiErrors.Add(1, new KeyValuePair<string, object?>("error_type", "transient"));
                    if (attempt < MaxRetries - 1)
                    {
                        var delay = CalculateDelayWithJitter(attempt);
                        _logger.LogWarning("Transient error ({StatusCode}), attempt {Attempt}/{MaxRetries}, delaying {Delay}ms", ex.StatusCode, attempt + 1, MaxRetries, delay);
                        await Task.Delay(delay);
                        continue;
                    }
                    throw;
                }
                catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                                                      ex.StatusCode == System.Net.HttpStatusCode.Forbidden ||
                                                      ex.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    // Non-retryable errors
                    _apiErrors.Add(1, new KeyValuePair<string, object?>("error_type", "auth"));
                    throw;
                }
                catch (HttpRequestException)
                {
                    // Network errors - retry with backoff
                    _apiErrors.Add(1, new KeyValuePair<string, object?>("error_type", "network"));
                    if (attempt < MaxRetries - 1)
                    {
                        var delay = CalculateDelayWithJitter(attempt);
                        _logger.LogWarning("Network error, attempt {Attempt}/{MaxRetries}, delaying {Delay}ms", attempt + 1, MaxRetries, delay);
                        await Task.Delay(delay);
                        continue;
                    }
                    throw;
                }
            }
            throw new InvalidOperationException("Max retries exceeded");
        });
    }

    private static bool IsTransientError(System.Net.HttpStatusCode? statusCode)
    {
        return statusCode == System.Net.HttpStatusCode.RequestTimeout ||
               statusCode == System.Net.HttpStatusCode.InternalServerError ||
               statusCode == System.Net.HttpStatusCode.BadGateway ||
               statusCode == System.Net.HttpStatusCode.ServiceUnavailable ||
               statusCode == System.Net.HttpStatusCode.GatewayTimeout;
    }

    private static int CalculateDelayWithJitter(int attempt)
    {
        // Exponential backoff: baseDelay * 2^attempt
        var exponentialDelay = BaseDelayMs * Math.Pow(2, attempt);
        // Add jitter: random value between 0 and exponentialDelay/2
        var jitter = _random.Next(0, (int)(exponentialDelay / 2));
        var totalDelay = (int)exponentialDelay + jitter;
        // Cap at max delay
        return Math.Min(totalDelay, MaxDelayMs);
    }
}
