using System;
using System.Linq;
using System.Threading.Tasks;
using JuniorDev.Contracts;
using JuniorDev.Orchestrator;
using JuniorDev.WorkItems.Jira;
using Xunit;

namespace JuniorDev.WorkItems.Jira.Tests;

[Trait("Category", "Integration")]
public class JiraAdapterIntegrationTests
{
    private readonly JiraAdapter? _adapter;

    public JiraAdapterIntegrationTests()
    {
        // Only create adapter if environment variables are set
        var baseUrl = Environment.GetEnvironmentVariable("JIRA_BASE_URL");
        var username = Environment.GetEnvironmentVariable("JIRA_USERNAME");
        var apiToken = Environment.GetEnvironmentVariable("JIRA_API_TOKEN");
        var projectKey = Environment.GetEnvironmentVariable("JIRA_PROJECT_KEY");

        if (!string.IsNullOrEmpty(baseUrl) && !string.IsNullOrEmpty(username) &&
            !string.IsNullOrEmpty(apiToken) && !string.IsNullOrEmpty(projectKey))
        {
            var appConfig = new AppConfig
            {
                Auth = new AuthConfig
                {
                    Jira = new JiraAuthConfig(baseUrl, username, apiToken)
                }
            };
            _adapter = new JiraAdapter(appConfig);
        }
        // If environment variables are not set, _adapter remains null and tests will be skipped
    }

    [Fact]
    public void CanHandle_ReturnsTrue_ForSupportedCommands()
    {
        if (_adapter == null) return; // Skip if no adapter

        var comment = new Comment(Guid.NewGuid(), new Correlation(Guid.NewGuid()), new WorkItemRef("TEST-1"), "Test comment");
        var transition = new TransitionTicket(Guid.NewGuid(), new Correlation(Guid.NewGuid()), new WorkItemRef("TEST-1"), "Done");
        var assign = new SetAssignee(Guid.NewGuid(), new Correlation(Guid.NewGuid()), new WorkItemRef("TEST-1"), "user@example.com");

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

        // This test requires valid JIRA credentials and an existing work item
        // It tests the happy path for commenting
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
        
        // Use a work item that exists in the configured JIRA instance
        var existingWorkItemId = Environment.GetEnvironmentVariable("JIRA_ISSUE_KEY") ?? "TEST-1";
        var command = new Comment(Guid.NewGuid(), correlation, new WorkItemRef(existingWorkItemId), "Integration test comment");

        await _adapter.HandleCommand(command, session);

        // Should emit CommandAccepted, ArtifactAvailable, CommandCompleted
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

        // This test requires valid JIRA credentials and an existing work item with valid transitions
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
        
        // Use a work item and transition that exists in the configured JIRA instance
        var existingWorkItemId = Environment.GetEnvironmentVariable("JIRA_ISSUE_KEY") ?? "TEST-1";
        var validTransition = Environment.GetEnvironmentVariable("JIRA_TRANSITION") ?? "Done";
        var command = new TransitionTicket(Guid.NewGuid(), correlation, new WorkItemRef(existingWorkItemId), validTransition);

        await _adapter.HandleCommand(command, session);

        // Should emit CommandAccepted, ArtifactAvailable, CommandCompleted
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
        // We'll use environment variables to simulate invalid credentials
        var originalUser = Environment.GetEnvironmentVariable("JIRA_USERNAME");
        var originalToken = Environment.GetEnvironmentVariable("JIRA_API_TOKEN");

        try
        {
            Environment.SetEnvironmentVariable("JIRA_USERNAME", "invalid");
            Environment.SetEnvironmentVariable("JIRA_API_TOKEN", "invalid");

            var invalidAppConfig = new AppConfig
            {
                Auth = new AuthConfig
                {
                    Jira = new JiraAuthConfig(
                        Environment.GetEnvironmentVariable("JIRA_BASE_URL") ?? "https://invalid.atlassian.net",
                        "invalid",
                        "invalid")
                }
            };
            var invalidAdapter = new JiraAdapter(invalidAppConfig);

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
            var command = new Comment(Guid.NewGuid(), correlation, new WorkItemRef("INVALID-1"), "Test comment");

            await invalidAdapter.HandleCommand(command, session);

            // Should emit CommandAccepted and CommandRejected
            Assert.Equal(2, session.Events.Count);
            Assert.IsType<CommandAccepted>(session.Events[0]);
            Assert.IsType<CommandRejected>(session.Events[1]);

            var rejected = (CommandRejected)session.Events[1];
            Assert.Equal(command.Id, rejected.CommandId);
            Assert.Equal(correlation, rejected.Correlation);
            Assert.Equal("AUTH_ERROR", rejected.Reason);
        }
        finally
        {
            // Restore original environment variables
            Environment.SetEnvironmentVariable("JIRA_USERNAME", originalUser);
            Environment.SetEnvironmentVariable("JIRA_API_TOKEN", originalToken);
        }
    }

    [Fact]
    public async Task HandleTransition_WithInvalidIssue_EmitsCommandRejected()
    {
        if (_adapter == null) return; // Skip if no adapter

        // Create adapter with invalid credentials to test error handling
        var originalUser = Environment.GetEnvironmentVariable("JIRA_USERNAME");
        var originalToken = Environment.GetEnvironmentVariable("JIRA_API_TOKEN");

        try
        {
            Environment.SetEnvironmentVariable("JIRA_USERNAME", "invalid");
            Environment.SetEnvironmentVariable("JIRA_API_TOKEN", "invalid");

            var invalidAppConfig = new AppConfig
            {
                Auth = new AuthConfig
                {
                    Jira = new JiraAuthConfig(
                        Environment.GetEnvironmentVariable("JIRA_BASE_URL") ?? "https://invalid.atlassian.net",
                        "invalid",
                        "invalid")
                }
            };
            var invalidAdapter = new JiraAdapter(invalidAppConfig);

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
            var command = new TransitionTicket(Guid.NewGuid(), correlation, new WorkItemRef("INVALID-1"), "Done");

            await invalidAdapter.HandleCommand(command, session);

            // Should emit CommandAccepted and CommandRejected
            Assert.Equal(2, session.Events.Count);
            Assert.IsType<CommandAccepted>(session.Events[0]);
            Assert.IsType<CommandRejected>(session.Events[1]);

            var rejected = (CommandRejected)session.Events[1];
            Assert.Equal(command.Id, rejected.CommandId);
            Assert.Equal(correlation, rejected.Correlation);
            Assert.Equal("AUTH_ERROR", rejected.Reason);
        }
        finally
        {
            Environment.SetEnvironmentVariable("JIRA_USERNAME", originalUser);
            Environment.SetEnvironmentVariable("JIRA_API_TOKEN", originalToken);
        }
    }

    [Fact]
    public async Task HandleSetAssignee_WithInvalidIssue_EmitsCommandRejected()
    {
        if (_adapter == null) return; // Skip if no adapter

        // Create adapter with invalid credentials to test error handling
        var originalUser = Environment.GetEnvironmentVariable("JIRA_USERNAME");
        var originalToken = Environment.GetEnvironmentVariable("JIRA_API_TOKEN");

        try
        {
            Environment.SetEnvironmentVariable("JIRA_USERNAME", "invalid");
            Environment.SetEnvironmentVariable("JIRA_API_TOKEN", "invalid");

            var invalidAppConfig = new AppConfig
            {
                Auth = new AuthConfig
                {
                    Jira = new JiraAuthConfig(
                        Environment.GetEnvironmentVariable("JIRA_BASE_URL") ?? "https://invalid.atlassian.net",
                        "invalid",
                        "invalid")
                }
            };
            var invalidAdapter = new JiraAdapter(invalidAppConfig);

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
            var command = new SetAssignee(Guid.NewGuid(), correlation, new WorkItemRef("INVALID-1"), "user@example.com");

            await invalidAdapter.HandleCommand(command, session);

            // Should emit CommandAccepted and CommandRejected
            Assert.Equal(2, session.Events.Count);
            Assert.IsType<CommandAccepted>(session.Events[0]);
            Assert.IsType<CommandRejected>(session.Events[1]);

            var rejected = (CommandRejected)session.Events[1];
            Assert.Equal(command.Id, rejected.CommandId);
            Assert.Equal(correlation, rejected.Correlation);
            Assert.Equal("AUTH_ERROR", rejected.Reason);
        }
        finally
        {
            Environment.SetEnvironmentVariable("JIRA_USERNAME", originalUser);
            Environment.SetEnvironmentVariable("JIRA_API_TOKEN", originalToken);
        }
    }

    [Fact]
    public async Task RetryLogic_HandlesTransientErrors()
    {
        if (_adapter == null) return; // Skip if no adapter

        // Test that retry logic is in place by checking the adapter has the expected behavior
        // This is a basic smoke test - full retry testing would require mocking HttpClient
        
        // We can verify this by attempting an operation that will fail due to network issues
        // and checking that it eventually gives up after retries
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
        
        // Try to operate on a non-existent work item - this should trigger HTTP errors
        var command = new Comment(Guid.NewGuid(), correlation, new WorkItemRef("NONEXISTENT-99999"), "Test comment");

        await _adapter.HandleCommand(command, session);

        // Should emit CommandAccepted and CommandRejected (after retries are exhausted)
        Assert.Equal(2, session.Events.Count);
        Assert.IsType<CommandAccepted>(session.Events[0]);
        Assert.IsType<CommandRejected>(session.Events[1]);

        var rejected = (CommandRejected)session.Events[1];
        Assert.Equal(command.Id, rejected.CommandId);
        // The error should be HTTP-related after retries
        Assert.Contains("HTTP", rejected.Reason);
    }
}
