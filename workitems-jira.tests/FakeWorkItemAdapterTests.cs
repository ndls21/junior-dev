using System;
using System.Linq;
using System.Threading.Tasks;
using JuniorDev.Contracts;
using JuniorDev.Orchestrator;
using JuniorDev.WorkItems.Jira;
using Xunit;

namespace JuniorDev.WorkItems.Jira.Tests;

public class FakeWorkItemAdapterTests
{
    private readonly FakeWorkItemAdapter _adapter = new();

    [Fact]
    public void CanHandle_ReturnsTrue_ForSupportedCommands()
    {
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
        var commit = new Commit(Guid.NewGuid(), new Correlation(Guid.NewGuid()), new RepoRef("test", "/tmp/test"), "Test commit", Array.Empty<string>());

        Assert.False(_adapter.CanHandle(commit));
    }

    [Fact]
    public async Task HandleComment_AddsCommentAndEmitsEvents()
    {
        var config = new SessionConfig(
            Guid.NewGuid(),
            null,
            null,
            new PolicyProfile("test", null, null, Array.Empty<string>(), null, false, false, null, null),
            new RepoRef("test", "/tmp/test"),
            new WorkspaceRef("/tmp/workspace"),
            null,
            "test-agent");
        var session = new TestSessionState(config);
        var correlation = new Correlation(Guid.NewGuid());
        var command = new Comment(Guid.NewGuid(), correlation, new WorkItemRef("TEST-1"), "This is a test comment");

        await _adapter.HandleCommand(command, session);

        // Should emit CommandAccepted, ArtifactAvailable, CommandCompleted
        Assert.Equal(3, session.Events.Count);
        Assert.IsType<CommandAccepted>(session.Events[0]);
        Assert.IsType<ArtifactAvailable>(session.Events[1]);
        Assert.IsType<CommandCompleted>(session.Events[2]);

        var accepted = (CommandAccepted)session.Events[0];
        Assert.Equal(command.Id, accepted.CommandId);
        Assert.Equal(correlation, accepted.Correlation);

        var artifactEvent = (ArtifactAvailable)session.Events[1];
        Assert.Equal(correlation, artifactEvent.Correlation);
        Assert.Equal("workitem-comment", artifactEvent.Artifact.Kind);

        var completed = (CommandCompleted)session.Events[2];
        Assert.Equal(command.Id, completed.CommandId);
        Assert.Equal(CommandOutcome.Success, completed.Outcome);
    }

    [Fact]
    public async Task HandleTransition_ValidTransition_Succeeds()
    {
        var config = new SessionConfig(
            Guid.NewGuid(),
            null,
            null,
            new PolicyProfile("test", null, null, Array.Empty<string>(), null, false, false, null, null),
            new RepoRef("test", "/tmp/test"),
            new WorkspaceRef("/tmp/workspace"),
            null,
            "test-agent");
        var session = new TestSessionState(config);
        var correlation = new Correlation(Guid.NewGuid());
        var command = new TransitionTicket(Guid.NewGuid(), correlation, new WorkItemRef("TEST-1"), "In Progress");

        await _adapter.HandleCommand(command, session);

        Assert.Equal(3, session.Events.Count);
        var completed = (CommandCompleted)session.Events[2];
        Assert.Equal(CommandOutcome.Success, completed.Outcome);
    }

    [Fact]
    public async Task HandleTransition_InvalidTransition_EmitsCommandRejected()
    {
        var config = new SessionConfig(
            Guid.NewGuid(),
            null,
            null,
            new PolicyProfile("test", null, null, Array.Empty<string>(), null, false, false, null, null),
            new RepoRef("test", "/tmp/test"),
            new WorkspaceRef("/tmp/workspace"),
            null,
            "test-agent");
        var session = new TestSessionState(config);
        var correlation = new Correlation(Guid.NewGuid());
        var command = new TransitionTicket(Guid.NewGuid(), correlation, new WorkItemRef("TEST-1"), "InvalidState");

        await _adapter.HandleCommand(command, session);

        Assert.Equal(2, session.Events.Count); // Accepted + Rejected
        Assert.IsType<CommandAccepted>(session.Events[0]);
        Assert.IsType<CommandRejected>(session.Events[1]);

        var rejected = (CommandRejected)session.Events[1];
        Assert.Equal(command.Id, rejected.CommandId);
        Assert.Equal(correlation, rejected.Correlation);
        Assert.Equal("VALIDATION_ERROR", rejected.PolicyRule);
        Assert.Contains("Invalid transition", rejected.Reason);
    }

    [Fact]
    public async Task HandleSetAssignee_Succeeds()
    {
        var config = new SessionConfig(
            Guid.NewGuid(),
            null,
            null,
            new PolicyProfile("test", null, null, Array.Empty<string>(), null, false, false, null, null),
            new RepoRef("test", "/tmp/test"),
            new WorkspaceRef("/tmp/workspace"),
            null,
            "test-agent");
        var session = new TestSessionState(config);
        var correlation = new Correlation(Guid.NewGuid());
        var command = new SetAssignee(Guid.NewGuid(), correlation, new WorkItemRef("TEST-1"), "newuser@example.com");

        await _adapter.HandleCommand(command, session);

        Assert.Equal(3, session.Events.Count);
        var completed = (CommandCompleted)session.Events[2];
        Assert.Equal(CommandOutcome.Success, completed.Outcome);
    }

    [Fact]
    public async Task MultipleOperations_OnSameWorkItem_PersistState()
    {
        var config = new SessionConfig(
            Guid.NewGuid(),
            null,
            null,
            new PolicyProfile("test", null, null, Array.Empty<string>(), null, false, false, null, null),
            new RepoRef("test", "/tmp/test"),
            new WorkspaceRef("/tmp/workspace"),
            null,
            "test-agent");
        var session = new TestSessionState(config);

        // Comment
        var commentCmd = new Comment(Guid.NewGuid(), new Correlation(Guid.NewGuid()), new WorkItemRef("TEST-1"), "First comment");
        await _adapter.HandleCommand(commentCmd, session);

        // Transition
        var transitionCmd = new TransitionTicket(Guid.NewGuid(), new Correlation(Guid.NewGuid()), new WorkItemRef("TEST-1"), "In Progress");
        await _adapter.HandleCommand(transitionCmd, session);

        // Assign
        var assignCmd = new SetAssignee(Guid.NewGuid(), new Correlation(Guid.NewGuid()), new WorkItemRef("TEST-1"), "assignee@example.com");
        await _adapter.HandleCommand(assignCmd, session);

        // Another comment
        var commentCmd2 = new Comment(Guid.NewGuid(), new Correlation(Guid.NewGuid()), new WorkItemRef("TEST-1"), "Second comment");
        await _adapter.HandleCommand(commentCmd2, session);

        // Should have 4 sets of events (3 events each = 12 total)
        Assert.Equal(12, session.Events.Count);

        // All should be successful
        var completedEvents = session.Events.OfType<CommandCompleted>().ToList();
        Assert.All(completedEvents, e => Assert.Equal(CommandOutcome.Success, e.Outcome));
    }

    [Fact]
    public async Task Operations_OnDifferentWorkItems_Isolated()
    {
        var config = new SessionConfig(
            Guid.NewGuid(),
            null,
            null,
            new PolicyProfile("test", null, null, Array.Empty<string>(), null, false, false, null, null),
            new RepoRef("test", "/tmp/test"),
            new WorkspaceRef("/tmp/workspace"),
            null,
            "test-agent");
        var session = new TestSessionState(config);

        // Operations on TEST-1
        var comment1 = new Comment(Guid.NewGuid(), new Correlation(Guid.NewGuid()), new WorkItemRef("TEST-1"), "Comment on TEST-1");
        await _adapter.HandleCommand(comment1, session);

        // Operations on TEST-2
        var comment2 = new Comment(Guid.NewGuid(), new Correlation(Guid.NewGuid()), new WorkItemRef("TEST-2"), "Comment on TEST-2");
        await _adapter.HandleCommand(comment2, session);

        var transition2 = new TransitionTicket(Guid.NewGuid(), new Correlation(Guid.NewGuid()), new WorkItemRef("TEST-2"), "Closed");
        await _adapter.HandleCommand(transition2, session);

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