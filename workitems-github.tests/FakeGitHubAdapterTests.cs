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
            new PolicyProfile { Name = "test", ProtectedBranches = new HashSet<string>() },
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
            new PolicyProfile { Name = "test", ProtectedBranches = new HashSet<string>() },
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

    [Fact]
    public async Task HandleTransition_ValidTransition_Succeeds()
    {
        var adapter = new FakeGitHubAdapter();
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
        var command = new TransitionTicket(Guid.NewGuid(), correlation, new WorkItemRef("1"), "Done");

        await adapter.HandleCommand(command, session);

        Assert.Equal(3, session.Events.Count);
        var completed = (CommandCompleted)session.Events[2];
        Assert.Equal(CommandOutcome.Success, completed.Outcome);
    }

    [Fact]
    public async Task HandleSetAssignee_Succeeds()
    {
        var adapter = new FakeGitHubAdapter();
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
        var command = new SetAssignee(Guid.NewGuid(), correlation, new WorkItemRef("1"), "newuser@example.com");

        await adapter.HandleCommand(command, session);

        Assert.Equal(3, session.Events.Count);
        var completed = (CommandCompleted)session.Events[2];
        Assert.Equal(CommandOutcome.Success, completed.Outcome);
    }

    [Fact]
    public async Task MultipleOperations_OnSameWorkItem_PersistState()
    {
        var adapter = new FakeGitHubAdapter();
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

        // Comment
        var commentCmd = new Comment(Guid.NewGuid(), new Correlation(Guid.NewGuid()), new WorkItemRef("1"), "First comment");
        await adapter.HandleCommand(commentCmd, session);

        // Transition
        var transitionCmd = new TransitionTicket(Guid.NewGuid(), new Correlation(Guid.NewGuid()), new WorkItemRef("1"), "Done");
        await adapter.HandleCommand(transitionCmd, session);

        // Assign
        var assignCmd = new SetAssignee(Guid.NewGuid(), new Correlation(Guid.NewGuid()), new WorkItemRef("1"), "assignee@example.com");
        await adapter.HandleCommand(assignCmd, session);

        // Another comment
        var commentCmd2 = new Comment(Guid.NewGuid(), new Correlation(Guid.NewGuid()), new WorkItemRef("1"), "Second comment");
        await adapter.HandleCommand(commentCmd2, session);

        // Should have 4 sets of events (3 events each = 12 total)
        Assert.Equal(12, session.Events.Count);

        // All should be successful
        var completedEvents = session.Events.OfType<CommandCompleted>().ToList();
        Assert.All(completedEvents, e => Assert.Equal(CommandOutcome.Success, e.Outcome));
    }

    [Fact]
    public async Task Operations_OnDifferentWorkItems_Isolated()
    {
        var adapter = new FakeGitHubAdapter();
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

        // Operations on issue #1
        var comment1 = new Comment(Guid.NewGuid(), new Correlation(Guid.NewGuid()), new WorkItemRef("1"), "Comment on issue #1");
        await adapter.HandleCommand(comment1, session);

        // Operations on issue #2
        var comment2 = new Comment(Guid.NewGuid(), new Correlation(Guid.NewGuid()), new WorkItemRef("2"), "Comment on issue #2");
        await adapter.HandleCommand(comment2, session);

        var transition2 = new TransitionTicket(Guid.NewGuid(), new Correlation(Guid.NewGuid()), new WorkItemRef("2"), "Closed");
        await adapter.HandleCommand(transition2, session);

        Assert.Equal(9, session.Events.Count); // 3 operations × 3 events each = 9 events
        Assert.All(session.Events.OfType<CommandCompleted>(), e => Assert.Equal(CommandOutcome.Success, e.Outcome));
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
