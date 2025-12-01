using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

using JuniorDev.Contracts;
using JuniorDev.Orchestrator;

namespace JuniorDev.WorkItems.Jira;

public class JiraAdapter : IAdapter
{
    private const int MaxRetries = 3;
    private const int BaseDelayMs = 1000;

    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly string _projectKey;
    private readonly string _authHeader;
    private readonly bool _dryRun;

    public JiraAdapter(AppConfig appConfig)
    {
        _dryRun = appConfig.LivePolicy?.DryRun ?? true;

        var jiraAuth = appConfig.Auth?.Jira;
        if (jiraAuth == null)
        {
            throw new InvalidOperationException("Jira authentication configuration is missing. Please configure AppConfig.Auth.Jira in appsettings.json or environment variables.");
        }

        _baseUrl = jiraAuth.BaseUrl ?? throw new InvalidOperationException("Jira BaseUrl is not configured");
        _projectKey = Environment.GetEnvironmentVariable("JIRA_PROJECT") ?? "PROJ"; // Default fallback

        var username = jiraAuth.Username ?? throw new InvalidOperationException("Jira Username is not configured");
        var token = jiraAuth.ApiToken ?? throw new InvalidOperationException("Jira ApiToken is not configured");

        _authHeader = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{token}"));
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", _authHeader);
    }

    public bool CanHandle(ICommand command)
    {
        return command is TransitionTicket or Comment or SetAssignee;
    }

    public async Task HandleCommand(ICommand command, SessionState session)
    {
        // Emit CommandAccepted
        var acceptedEvent = new CommandAccepted(
            Guid.NewGuid(),
            command.Correlation,
            command.Id);

        await session.AddEvent(acceptedEvent);

        // Check for dry-run mode
        if (_dryRun)
        {
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
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            var rejectedEvent = new CommandRejected(
                Guid.NewGuid(),
                command.Correlation,
                command.Id,
                "Authentication failed. Check Jira credentials in configuration.",
                "AUTH_ERROR");

            await session.AddEvent(rejectedEvent);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            var rejectedEvent = new CommandRejected(
                Guid.NewGuid(),
                command.Correlation,
                command.Id,
                "Access forbidden. Check user permissions in Jira.",
                "PERMISSION_ERROR");

            await session.AddEvent(rejectedEvent);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            var rejectedEvent = new CommandRejected(
                Guid.NewGuid(),
                command.Correlation,
                command.Id,
                "Work item not found in Jira.",
                "NOT_FOUND");

            await session.AddEvent(rejectedEvent);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        {
            var rejectedEvent = new CommandRejected(
                Guid.NewGuid(),
                command.Correlation,
                command.Id,
                "Rate limit exceeded. Please retry later.",
                "RATE_LIMIT");

            await session.AddEvent(rejectedEvent);
        }
        catch (HttpRequestException ex)
        {
            var rejectedEvent = new CommandRejected(
                Guid.NewGuid(),
                command.Correlation,
                command.Id,
                $"HTTP error: {ex.StatusCode} - {ex.Message}",
                "HTTP_ERROR");

            await session.AddEvent(rejectedEvent);
        }
        catch (Exception ex)
        {
            var rejectedEvent = new CommandRejected(
                Guid.NewGuid(),
                command.Correlation,
                command.Id,
                ex.Message,
                "GENERAL_ERROR");

            await session.AddEvent(rejectedEvent);
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
        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            try
            {
                var response = await operation();
                if (response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                    response.StatusCode == System.Net.HttpStatusCode.Forbidden || response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    return response;
                }
                // For other errors, retry
            }
            catch (HttpRequestException)
            {
                if (attempt == MaxRetries - 1) throw;
            }

            if (attempt < MaxRetries - 1)
            {
                await Task.Delay(BaseDelayMs * (int)Math.Pow(2, attempt));
            }
        }
        throw new InvalidOperationException("Max retries exceeded");
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
