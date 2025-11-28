using System;
using System.Linq;
using System.Threading.Tasks;
using JuniorDev.Contracts;
using JuniorDev.Orchestrator;
using Xunit;

namespace JuniorDev.WorkItems.GitHub.Tests;

public class FakeGitHubAdapterTests
{
    [Fact]
    public void CanHandle_ReturnsTrue_ForSupportedCommands()
    {
        var adapter = new FakeGitHubAdapter();

        var comment = new Comment(Guid.NewGuid(), new Correlation(Guid.NewGuid()), new WorkItemRef("1"), "Test comment");
        var transition = new TransitionTicket(Guid.NewGuid(), new Correlation(Guid.NewGuid()), new WorkItemRef("1"), "Done");
        var assign = new SetAssignee(Guid.NewGuid(), new Correlation(Guid.NewGuid()), new WorkItemRef("1"), "user@example.com");

        Assert.True(adapter.CanHandle(comment));
        Assert.True(adapter.CanHandle(transition));
        Assert.True(adapter.CanHandle(assign));
    }

    [Fact]
    public void CanHandle_ReturnsFalse_ForUnsupportedCommands()
    {
        var adapter = new FakeGitHubAdapter();

        var commit = new Commit(Guid.NewGuid(), new Correlation(Guid.NewGuid()), new RepoRef("test", "/tmp/test"), "Test commit", Array.Empty<string>());

        Assert.False(adapter.CanHandle(commit));
    }

    [Fact]
    public async Task HandleComment_Succeeds()
    {
        var adapter = new FakeGitHubAdapter();
        var config = new SessionConfig(
            Guid.NewGuid(),
            null,
            null,
            new PolicyProfile { Name = "test", ProtectedBranches = new HashSet<string>() }, null, false, false, null, null),
            new RepoRef("test", "/tmp/test"),
            new WorkspaceRef("/tmp/workspace"),
            null,
            "test-agent");
        var session = new TestSessionState(config);
        var correlation = new Correlation(Guid.NewGuid());
        var command = new Comment(Guid.NewGuid(), correlation, new WorkItemRef("1"), "Test comment");

        await adapter.HandleCommand(command, session);

        Assert.Equal(3, session.Events.Count);
        Assert.IsType<CommandAccepted>(session.Events[0]);
        Assert.IsType<ArtifactAvailable>(session.Events[1]);
        Assert.IsType<CommandCompleted>(session.Events[2]);

        var completed = (CommandCompleted)session.Events[2];
        Assert.Equal(command.Id, completed.CommandId);
        Assert.Equal(CommandOutcome.Success, completed.Outcome);
    }

    [Fact]
    public async Task HandleTransition_InvalidTransition_EmitsCommandRejected()
    {
        var adapter = new FakeGitHubAdapter();
        var config = new SessionConfig(
            Guid.NewGuid(),
            null,
            null,
            new PolicyProfile { Name = "test", ProtectedBranches = new HashSet<string>() }, null, false, false, null, null),
            new RepoRef("test", "/tmp/test"),
            new WorkspaceRef("/tmp/workspace"),
            null,
            "test-agent");
        var session = new TestSessionState(config);
        var correlation = new Correlation(Guid.NewGuid());
        var command = new TransitionTicket(Guid.NewGuid(), correlation, new WorkItemRef("1"), "Invalid");

        await adapter.HandleCommand(command, session);

        Assert.Equal(2, session.Events.Count);
        Assert.IsType<CommandAccepted>(session.Events[0]);
        Assert.IsType<CommandRejected>(session.Events[1]);

        var rejected = (CommandRejected)session.Events[1];
        Assert.Equal(command.Id, rejected.CommandId);
        Assert.Equal(correlation, rejected.Correlation);
        Assert.Equal("VALIDATION_ERROR", rejected.PolicyRule);
    }
}

// Test implementation of SessionState for testing
internal class TestSessionState : SessionState
{
    public TestSessionState(SessionConfig config) : base(config, "/tmp/test-workspace")
    {
    }

    // Use the base class Events property
}
