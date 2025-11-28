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

    public GitHubAdapter()
    {
        var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        _repo = Environment.GetEnvironmentVariable("GITHUB_REPO") ?? "owner/repo";
        var baseUrl = Environment.GetEnvironmentVariable("GITHUB_BASE_URL") ?? "https://api.github.com";

        _httpClient = new HttpClient { BaseAddress = new Uri(baseUrl) };
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("JuniorDev", "1.0"));
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
        var response = await _httpClient.PostAsync(url, content);
        response.EnsureSuccessStatusCode();
    }

    private async Task HandleTransition(TransitionTicket command)
    {
        var issueNumber = int.Parse(command.Item.Id);
        var url = $"/repos/{_repo}/issues/{issueNumber}";
        var state = command.State == "Done" ? "closed" : "open";
        var body = new { state };
        var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        var response = await _httpClient.PatchAsync(url, content);
        response.EnsureSuccessStatusCode();
    }

    private async Task HandleSetAssignee(SetAssignee command)
    {
        var issueNumber = int.Parse(command.Item.Id);
        var url = $"/repos/{_repo}/issues/{issueNumber}";
        var body = new { assignees = new[] { command.Assignee } };
        var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        var response = await _httpClient.PatchAsync(url, content);
        response.EnsureSuccessStatusCode();
    }
}
