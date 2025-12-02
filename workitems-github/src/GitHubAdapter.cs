using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using JuniorDev.Contracts;
using JuniorDev.Orchestrator;

namespace JuniorDev.WorkItems.GitHub;

public class GitHubAdapter : IAdapter
{
    private readonly HttpClient _httpClient;
    private readonly string _repo;
    private readonly bool _dryRun;
    private readonly CircuitBreaker _circuitBreaker;
    private const int MaxRetries = 3;
    private const int BaseDelayMs = 1000;
    private const int MaxDelayMs = 30000;
    private static readonly Random _random = new();

    public GitHubAdapter(AppConfig appConfig)
    {
        _dryRun = appConfig.LivePolicy?.DryRun ?? true;

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

        // Check for dry-run mode
        if (_dryRun)
        {
            await session.AddEvent(new CommandCompleted(Guid.NewGuid(), correlation, command.Id, CommandOutcome.Success));

            var artifact = new Artifact("text", "GitHub Issue Updated (Dry Run)", InlineText: "Command would be executed (dry-run mode)", ContentType: "text/plain");
            await session.AddEvent(new ArtifactAvailable(Guid.NewGuid(), correlation, artifact));
            return;
        }

        try
        {
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
        }
        catch (CircuitBreakerOpenException ex)
        {
            Console.WriteLine($"[GITHUB CIRCUIT] Circuit breaker open for command {command.Kind}: {ex.Message}");
            await session.AddEvent(new CommandRejected(Guid.NewGuid(), correlation, command.Id, $"Circuit breaker open: {ex.Message}", "CIRCUIT_BREAKER"));
        }
        catch (Exception ex)
        {
            await session.AddEvent(new CommandRejected(Guid.NewGuid(), correlation, command.Id, ex.Message, "API_ERROR"));
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
                    var response = await operation();
                    response.EnsureSuccessStatusCode();
                    return response;
                }
                catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    // Rate limited - use exponential backoff with jitter
                    if (attempt < MaxRetries - 1)
                    {
                        var delay = CalculateDelayWithJitter(attempt);
                        Console.WriteLine($"[GITHUB RETRY] Rate limited (429), attempt {attempt + 1}/{MaxRetries}, delaying {delay}ms");
                        await Task.Delay(delay);
                        continue;
                    }
                    throw new InvalidOperationException($"Rate limit exceeded after {MaxRetries} attempts");
                }
                catch (HttpRequestException ex) when (IsTransientError(ex.StatusCode))
                {
                    // Transient error - retry with backoff
                    if (attempt < MaxRetries - 1)
                    {
                        var delay = CalculateDelayWithJitter(attempt);
                        Console.WriteLine($"[GITHUB RETRY] Transient error ({ex.StatusCode}), attempt {attempt + 1}/{MaxRetries}, delaying {delay}ms");
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
                    throw;
                }
                catch (HttpRequestException)
                {
                    // Network errors - retry with backoff
                    if (attempt < MaxRetries - 1)
                    {
                        var delay = CalculateDelayWithJitter(attempt);
                        Console.WriteLine($"[GITHUB RETRY] Network error, attempt {attempt + 1}/{MaxRetries}, delaying {delay}ms");
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
