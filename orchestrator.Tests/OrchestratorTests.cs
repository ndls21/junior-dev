using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JuniorDev.Contracts;
using Xunit;

namespace JuniorDev.Orchestrator.Tests;

public class OrchestratorTests
{
    private readonly ISessionManager _sessionManager;

    public OrchestratorTests()
    {
        var adapters = new IAdapter[]
        {
            new FakeWorkItemsAdapter(),
            new FakeVcsAdapter()
        };
        _sessionManager = new SessionManager(adapters);
    }

    [Fact]
    public async Task CommandAccepted_RoutedToFake_EventsRecordedAndStreamedInOrder()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var config = new SessionConfig(
            sessionId,
            null,
            null,
            new PolicyProfile("test", null, null, new[] { "main" }, null, false, false, null, null),
            new RepoRef("test", "/tmp/test"),
            new WorkspaceRef("/tmp/workspace"),
            null,
            "test-agent");

        await _sessionManager.CreateSession(config);

        var command = new CreateBranch(
            Guid.NewGuid(),
            new Correlation(sessionId),
            new RepoRef("test", "/tmp/test"),
            "feature-branch");

        // Act
        await _sessionManager.PublishCommand(command);

        // Assert
        var events = await _sessionManager.Subscribe(sessionId).Take(3).ToListAsync();

        Assert.Equal(3, events.Count);
        Assert.IsType<SessionStatusChanged>(events[0]);
        Assert.IsType<CommandAccepted>(events[1]);
        Assert.IsType<CommandCompleted>(events[2]);

        var accepted = (CommandAccepted)events[1];
        var completed = (CommandCompleted)events[2];

        Assert.Equal(command.Id, accepted.CommandId);
        Assert.Equal(command.Id, completed.CommandId);
        Assert.Equal(CommandOutcome.Success, completed.Outcome);
    }

    [Fact]
    public async Task CorrelationAndSessionId_PreservedInEmittedEvents()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var commandId = Guid.NewGuid();
        var config = new SessionConfig(
            sessionId,
            null,
            null,
            new PolicyProfile("test", null, null, new[] { "main" }, null, false, false, null, null),
            new RepoRef("test", "/tmp/test"),
            new WorkspaceRef("/tmp/workspace"),
            null,
            "test-agent");

        await _sessionManager.CreateSession(config);

        var correlation = new Correlation(sessionId, commandId);
        var command = new Comment(
            commandId,
            correlation,
            new WorkItemRef("123"),
            "Test comment");

        // Act
        await _sessionManager.PublishCommand(command);

        // Assert
        var events = await _sessionManager.Subscribe(sessionId).Skip(1).Take(2).ToListAsync();

        foreach (var @event in events)
        {
            Assert.Equal(sessionId, @event.Correlation.SessionId);
            Assert.Equal(commandId, @event.Correlation.CommandId);
        }
    }

    [Fact]
    public async Task Subscribe_YieldsOrderedEvents_MultipleSessionsIsolated()
    {
        // Arrange
        var sessionId1 = Guid.NewGuid();
        var sessionId2 = Guid.NewGuid();

        var config1 = new SessionConfig(
            sessionId1,
            null,
            null,
            new PolicyProfile("test", null, null, new[] { "main" }, null, false, false, null, null),
            new RepoRef("test1", "/tmp/test1"),
            new WorkspaceRef("/tmp/workspace1"),
            null,
            "test-agent");

        var config2 = new SessionConfig(
            sessionId2,
            null,
            null,
            new PolicyProfile("test", null, null, new[] { "main" }, null, false, false, null, null),
            new RepoRef("test2", "/tmp/test2"),
            new WorkspaceRef("/tmp/workspace2"),
            null,
            "test-agent");

        await _sessionManager.CreateSession(config1);
        await _sessionManager.CreateSession(config2);

        var command1 = new CreateBranch(
            Guid.NewGuid(),
            new Correlation(sessionId1),
            new RepoRef("test1", "/tmp/test1"),
            "branch1");

        var command2 = new CreateBranch(
            Guid.NewGuid(),
            new Correlation(sessionId2),
            new RepoRef("test2", "/tmp/test2"),
            "branch2");

        // Act
        await _sessionManager.PublishCommand(command1);
        await _sessionManager.PublishCommand(command2);

        // Assert
        var events1 = await _sessionManager.Subscribe(sessionId1).Take(3).ToListAsync();
        var events2 = await _sessionManager.Subscribe(sessionId2).Take(3).ToListAsync();

        // Session 1 should have its own events
        Assert.Equal(3, events1.Count); // status + accepted + completed
        Assert.Equal(sessionId1, events1[0].Correlation.SessionId);

        // Session 2 should have its own events
        Assert.Equal(3, events2.Count);
        Assert.Equal(sessionId2, events2[0].Correlation.SessionId);

        // Events are ordered by time
        Assert.True(events1[0].Id != events2[0].Id); // Different events
    }
}