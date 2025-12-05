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
        _httpClient.Timeout = TimeSpan.FromSeconds(30);

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
        return command is TransitionTicket or Comment or SetAssignee or QueryWorkItem;
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
                case QueryWorkItem query:
                    await HandleQueryWorkItem(query, session);
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

    private async Task HandleQueryWorkItem(QueryWorkItem query, SessionState session)
    {
        var issueKey = GetIssueKey(query.Item.Id);
        var url = $"{_baseUrl}/rest/api/2/issue/{issueKey}";

        var response = await ExecuteWithRetry(() => _httpClient.GetAsync(url));
        response.EnsureSuccessStatusCode();

        var issueData = await response.Content.ReadFromJsonAsync<JiraIssueResponse>();
        if (issueData == null) throw new InvalidOperationException("Invalid issue response from Jira");

        // Fetch comments
        var commentsUrl = $"{_baseUrl}/rest/api/2/issue/{issueKey}/comment";
        var commentsResponse = await ExecuteWithRetry(() => _httpClient.GetAsync(commentsUrl));
        commentsResponse.EnsureSuccessStatusCode();

        var commentsData = await commentsResponse.Content.ReadFromJsonAsync<JiraCommentsResponse>();
        
        // Parse comments
        var comments = new List<WorkItemComment>();
        if (commentsData?.comments != null)
        {
            foreach (var comment in commentsData.comments)
            {
                var author = comment.author?.displayName ?? comment.author?.name ?? "unknown";
                var body = comment.body ?? "";
                var createdAt = DateTimeOffset.Parse(comment.created ?? DateTimeOffset.UtcNow.ToString());
                comments.Add(new WorkItemComment(author, body, createdAt));
            }
        }

        // Parse links from issue links and comments
        var links = new List<WorkItemLink>();
        
        // Parse issue description for links
        if (!string.IsNullOrEmpty(issueData.fields?.description))
        {
            links.AddRange(ParseJiraLinks(issueData.fields.description, "issue_description"));
        }

        // Parse comments for links
        foreach (var comment in comments)
        {
            links.AddRange(ParseJiraLinks(comment.Body, $"comment_{comment.Author}"));
        }

        // Extract Jira issue links
        if (issueData.fields?.issuelinks != null)
        {
            foreach (var link in issueData.fields.issuelinks)
            {
                var relationship = link.type?.inward ?? link.type?.outward ?? "linked";
                var targetKey = link.inwardIssue?.key ?? link.outwardIssue?.key ?? "";
                var targetTitle = link.inwardIssue?.fields?.summary ?? link.outwardIssue?.fields?.summary ?? "";
                
                if (!string.IsNullOrEmpty(targetKey))
                {
                    links.Add(new WorkItemLink("jira_issue", targetKey, targetTitle, relationship));
                }
            }
        }

        // Extract basic issue info
        var status = issueData.fields?.status?.name ?? "Unknown";
        var assignee = issueData.fields?.assignee?.displayName ?? issueData.fields?.assignee?.name;
        var description = issueData.fields?.description ?? "";
        var title = issueData.fields?.summary ?? $"Issue {issueKey}";

        // Extract labels as tags
        var tags = new List<string>();
        if (issueData.fields?.labels != null)
        {
            tags.AddRange(issueData.fields.labels);
        }

        var details = new WorkItemDetails(
            Id: query.Item.Id,
            Title: title,
            Description: description,
            Status: status,
            Assignee: assignee,
            Tags: tags,
            Comments: comments,
            Links: links
        );

        await session.AddEvent(new WorkItemQueried(Guid.NewGuid(), query.Correlation, details));
    }

    private IEnumerable<WorkItemLink> ParseJiraLinks(string text, string source)
    {
        var links = new List<WorkItemLink>();
        
        // Match patterns like PROJ-123, [PROJ-456], https://company.atlassian.net/browse/PROJ-789
        var patterns = new[]
        {
            @"(?<project>[A-Z][A-Z0-9]*)-(?<number>\d+)",  // PROJ-123
            @"\[(?<project>[A-Z][A-Z0-9]*)-(?<number>\d+)\]",  // [PROJ-123]
            @"browse/(?<project>[A-Z][A-Z0-9]*)-(?<number>\d+)"  // browse/PROJ-123
        };

        foreach (var pattern in patterns)
        {
            var matches = System.Text.RegularExpressions.Regex.Matches(text, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                var targetId = match.Value.Trim('[', ']').Replace("browse/", "");
                var relationship = "mentioned";
                
                // Determine relationship based on context
                if (text.Contains("blocked by") || text.Contains("depends on") || text.Contains("after"))
                    relationship = "depends_on";
                else if (text.Contains("blocks") || text.Contains("required by") || text.Contains("before"))
                    relationship = "blocks";
                else if (text.Contains("related to") || text.Contains("see also"))
                    relationship = "related";
                else if (text.Contains("part of") || text.Contains("parent"))
                    relationship = "child_of";
                else if (text.Contains("contains") || text.Contains("child"))
                    relationship = "parent_of";

                links.Add(new WorkItemLink("jira_issue", targetId, null, relationship));
            }
        }

        return links;
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

    private class JiraIssueResponse
    {
        public string key { get; set; } = "";
        public JiraFields? fields { get; set; }
    }

    private class JiraFields
    {
        public string? summary { get; set; }
        public string? description { get; set; }
        public JiraUser? assignee { get; set; }
        public JiraStatus? status { get; set; }
        public string[]? labels { get; set; }
        public JiraIssueLink[]? issuelinks { get; set; }
    }

    private class JiraUser
    {
        public string? name { get; set; }
        public string? displayName { get; set; }
    }

    private class JiraStatus
    {
        public string? name { get; set; }
    }

    private class JiraIssueLink
    {
        public JiraLinkType? type { get; set; }
        public JiraIssueRef? inwardIssue { get; set; }
        public JiraIssueRef? outwardIssue { get; set; }
    }

    private class JiraLinkType
    {
        public string? inward { get; set; }
        public string? outward { get; set; }
    }

    private class JiraIssueRef
    {
        public string? key { get; set; }
        public JiraFields? fields { get; set; }
    }

    private class JiraCommentsResponse
    {
        public JiraComment[]? comments { get; set; }
    }

    private class JiraComment
    {
        public string? body { get; set; }
        public string? created { get; set; }
        public JiraUser? author { get; set; }
    }
}
