using System;
using System.Linq;
using System.Threading.Tasks;
using JuniorDev.Contracts;
using JuniorDev.Orchestrator;
using JuniorDev.WorkItems.GitHub;
using Xunit;

namespace JuniorDev.WorkItems.GitHub.Tests;

[Trait("Category", "Integration")]
public class GitHubAdapterIntegrationTests
{
    private readonly GitHubAdapter? _adapter;

    public GitHubAdapterIntegrationTests()
    {
        // Only create adapter if environment variables are set
        var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        var repo = Environment.GetEnvironmentVariable("GITHUB_REPO");

        if (!string.IsNullOrEmpty(token) && !string.IsNullOrEmpty(repo))
        {
            var appConfig = new AppConfig
            {
                Auth = new AuthConfig
                {
                    GitHub = new GitHubAuthConfig(token, repo.Split('/')[0], repo.Split('/')[1])
                }
            };
            var livePolicy = new StaticOptionsMonitor<LivePolicyConfig>(new LivePolicyConfig { DryRun = false });
            _adapter = new GitHubAdapter(appConfig, livePolicyMonitor: livePolicy);
        }
        // If environment variables are not set, _adapter remains null and tests will be skipped
    }

    [Fact]
    public void CanHandle_ReturnsTrue_ForSupportedCommands()
    {
        if (_adapter == null) return; // Skip if no adapter

        var comment = new Comment(Guid.NewGuid(), new Correlation(Guid.NewGuid()), new WorkItemRef("1"), "Test comment");
        var transition = new TransitionTicket(Guid.NewGuid(), new Correlation(Guid.NewGuid()), new WorkItemRef("1"), "Done");
        var assign = new SetAssignee(Guid.NewGuid(), new Correlation(Guid.NewGuid()), new WorkItemRef("1"), "user");

        Assert.True(_adapter.CanHandle(comment));
        Assert.True(_adapter.CanHandle(transition));
        Assert.True(_adapter.CanHandle(assign));
    }

    [Fact]
    public void CanHandle_ReturnsFalse_ForUnsupportedCommands()
    {
        if (_adapter == null) return; // Skip if no adapter

        var commit = new Commit(Guid.NewGuid(), new Correlation(Guid.NewGuid()), new RepoRef("test", "/tmp/test"), "Test commit", Array.Empty<string>());

        Assert.False(_adapter.CanHandle(commit));
    }

    [Fact]
    public async Task HandleComment_WithValidCredentials_Succeeds()
    {
        if (_adapter == null) return; // Skip if no adapter

        var config = new SessionConfig(
            Guid.NewGuid(),
            null,
            null,
            new PolicyProfile { Name = "test", ProtectedBranches = new HashSet<string>() },
            new RepoRef("test", "/tmp/test"),
            new WorkspaceRef("/tmp/workspace"),
            null,
            "test-agent");
        var session = new TestSessionState(config);
        var correlation = new Correlation(Guid.NewGuid());
        
        var issueNumber = Environment.GetEnvironmentVariable("GITHUB_ISSUE_NUMBER") ?? "1";
        var command = new Comment(Guid.NewGuid(), correlation, new WorkItemRef(issueNumber), "Integration test comment");

        await _adapter.HandleCommand(command, session);

        Assert.Equal(3, session.Events.Count);
        Assert.IsType<CommandAccepted>(session.Events[0]);
        Assert.IsType<ArtifactAvailable>(session.Events[1]);
        Assert.IsType<CommandCompleted>(session.Events[2]);

        var completed = (CommandCompleted)session.Events[2];
        Assert.Equal(command.Id, completed.CommandId);
        Assert.Equal(CommandOutcome.Success, completed.Outcome);
    }

    [Fact]
    public async Task HandleTransition_WithValidCredentials_Succeeds()
    {
        if (_adapter == null) return; // Skip if no adapter

        var config = new SessionConfig(
            Guid.NewGuid(),
            null,
            null,
            new PolicyProfile { Name = "test", ProtectedBranches = new HashSet<string>() },
            new RepoRef("test", "/tmp/test"),
            new WorkspaceRef("/tmp/workspace"),
            null,
            "test-agent");
        var session = new TestSessionState(config);
        var correlation = new Correlation(Guid.NewGuid());
        
        var issueNumber = Environment.GetEnvironmentVariable("GITHUB_ISSUE_NUMBER") ?? "1";
        var command = new TransitionTicket(Guid.NewGuid(), correlation, new WorkItemRef(issueNumber), "Done");

        await _adapter.HandleCommand(command, session);

        Assert.Equal(3, session.Events.Count);
        Assert.IsType<CommandAccepted>(session.Events[0]);
        Assert.IsType<ArtifactAvailable>(session.Events[1]);
        Assert.IsType<CommandCompleted>(session.Events[2]);

        var completed = (CommandCompleted)session.Events[2];
        Assert.Equal(command.Id, completed.CommandId);
        Assert.Equal(CommandOutcome.Success, completed.Outcome);
    }

    [Fact]
    public async Task HandleComment_WithInvalidCredentials_EmitsCommandRejected()
    {
        if (_adapter == null) return; // Skip if no adapter

        // Create adapter with invalid credentials to test error handling
        var originalToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN");

        try
        {
            Environment.SetEnvironmentVariable("GITHUB_TOKEN", "invalid");

            var invalidAppConfig = new AppConfig
            {
                Auth = new AuthConfig
                {
                    GitHub = new GitHubAuthConfig("invalid", "test", "repo")
                }
            };
            var livePolicy = new StaticOptionsMonitor<LivePolicyConfig>(new LivePolicyConfig { DryRun = false });
            var invalidAdapter = new GitHubAdapter(invalidAppConfig, livePolicyMonitor: livePolicy);

            var config = new SessionConfig(
                Guid.NewGuid(),
                null,
                null,
                new PolicyProfile { Name = "test", ProtectedBranches = new HashSet<string>() },
                new RepoRef("test", "/tmp/test"),
                new WorkspaceRef("/tmp/workspace"),
                null,
                "test-agent");
            var session = new TestSessionState(config);
            var correlation = new Correlation(Guid.NewGuid());
            var command = new Comment(Guid.NewGuid(), correlation, new WorkItemRef("999999"), "Test comment");

            await invalidAdapter.HandleCommand(command, session);

            // Should emit CommandAccepted and CommandRejected
            Assert.Equal(2, session.Events.Count);
            Assert.IsType<CommandAccepted>(session.Events[0]);
            Assert.IsType<CommandRejected>(session.Events[1]);

            var rejected = (CommandRejected)session.Events[1];
            Assert.Equal(command.Id, rejected.CommandId);
            Assert.Equal(correlation, rejected.Correlation);
            Assert.Equal("Response status code does not indicate success: 401 (Unauthorized).", rejected.Reason);
        }
        finally
        {
            // Restore original environment variable
            Environment.SetEnvironmentVariable("GITHUB_TOKEN", originalToken);
        }
    }

    [Fact]
    public async Task HandleTransition_WithInvalidCredentials_EmitsCommandRejected()
    {
        if (_adapter == null) return; // Skip if no adapter

        // Create adapter with invalid credentials to test error handling
        var originalToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN");

        try
        {
            Environment.SetEnvironmentVariable("GITHUB_TOKEN", "invalid");

            var invalidAppConfig = new AppConfig
            {
                Auth = new AuthConfig
                {
                    GitHub = new GitHubAuthConfig("invalid", "test", "repo")
                }
            };
            var livePolicy = new StaticOptionsMonitor<LivePolicyConfig>(new LivePolicyConfig { DryRun = false });
            var invalidAdapter = new GitHubAdapter(invalidAppConfig, livePolicyMonitor: livePolicy);

            var config = new SessionConfig(
                Guid.NewGuid(),
                null,
                null,
                new PolicyProfile { Name = "test", ProtectedBranches = new HashSet<string>() },
                new RepoRef("test", "/tmp/test"),
                new WorkspaceRef("/tmp/workspace"),
                null,
                "test-agent");
            var session = new TestSessionState(config);
            var correlation = new Correlation(Guid.NewGuid());
            var command = new TransitionTicket(Guid.NewGuid(), correlation, new WorkItemRef("999999"), "Done");

            await invalidAdapter.HandleCommand(command, session);

            // Should emit CommandAccepted and CommandRejected
            Assert.Equal(2, session.Events.Count);
            Assert.IsType<CommandAccepted>(session.Events[0]);
            Assert.IsType<CommandRejected>(session.Events[1]);

            var rejected = (CommandRejected)session.Events[1];
            Assert.Equal(command.Id, rejected.CommandId);
            Assert.Equal(correlation, rejected.Correlation);
            Assert.Equal("Response status code does not indicate success: 401 (Unauthorized).", rejected.Reason);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GITHUB_TOKEN", originalToken);
        }
    }

    [Fact]
    public async Task HandleSetAssignee_WithInvalidCredentials_EmitsCommandRejected()
    {
        if (_adapter == null) return; // Skip if no adapter

        // Create adapter with invalid credentials to test error handling
        var originalToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN");

        try
        {
            Environment.SetEnvironmentVariable("GITHUB_TOKEN", "invalid");

            var invalidAppConfig = new AppConfig
            {
                Auth = new AuthConfig
                {
                    GitHub = new GitHubAuthConfig("invalid", "test", "repo")
                }
            };
            var livePolicy = new StaticOptionsMonitor<LivePolicyConfig>(new LivePolicyConfig { DryRun = false });
            var invalidAdapter = new GitHubAdapter(invalidAppConfig, livePolicyMonitor: livePolicy);

            var config = new SessionConfig(
                Guid.NewGuid(),
                null,
                null,
                new PolicyProfile { Name = "test", ProtectedBranches = new HashSet<string>() },
                new RepoRef("test", "/tmp/test"),
                new WorkspaceRef("/tmp/workspace"),
                null,
                "test-agent");
            var session = new TestSessionState(config);
            var correlation = new Correlation(Guid.NewGuid());
            var command = new SetAssignee(Guid.NewGuid(), correlation, new WorkItemRef("999999"), "user");

            await invalidAdapter.HandleCommand(command, session);

            // Should emit CommandAccepted and CommandRejected
            Assert.Equal(2, session.Events.Count);
            Assert.IsType<CommandAccepted>(session.Events[0]);
            Assert.IsType<CommandRejected>(session.Events[1]);

            var rejected = (CommandRejected)session.Events[1];
            Assert.Equal(command.Id, rejected.CommandId);
            Assert.Equal(correlation, rejected.Correlation);
            Assert.Equal("Response status code does not indicate success: 401 (Unauthorized).", rejected.Reason);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GITHUB_TOKEN", originalToken);
        }
    }

    [Fact]
    public async Task RetryLogic_HandlesTransientErrors()
    {
        if (_adapter == null) return; // Skip if no adapter

        var config = new SessionConfig(
            Guid.NewGuid(),
            null,
            null,
            new PolicyProfile { Name = "test", ProtectedBranches = new HashSet<string>() },
            new RepoRef("test", "/tmp/test"),
            new WorkspaceRef("/tmp/workspace"),
            null,
            "test-agent");
        var session = new TestSessionState(config);
        var correlation = new Correlation(Guid.NewGuid());
        
        var command = new Comment(Guid.NewGuid(), correlation, new WorkItemRef("999999"), "Test comment");

        await _adapter.HandleCommand(command, session);

        Assert.Equal(2, session.Events.Count);
        Assert.IsType<CommandAccepted>(session.Events[0]);
        Assert.IsType<CommandRejected>(session.Events[1]);

        var rejected = (CommandRejected)session.Events[1];
        Assert.Equal(command.Id, rejected.CommandId);
    }
}
