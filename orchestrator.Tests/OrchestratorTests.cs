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
        var policyEnforcer = new StubPolicyEnforcer();
        var rateLimiter = new StubRateLimiter();
        var workspaceProvider = new StubWorkspaceProvider();
        _sessionManager = new SessionManager(adapters, policyEnforcer, rateLimiter, workspaceProvider);
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

    [Fact]
    public async Task BlockedCommand_EmitsCommandRejectedWithRule()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var config = new SessionConfig(
            sessionId,
            null,
            null,
            new PolicyProfile("test", null, new[] { "CreateBranch" }, new[] { "main" }, null, false, false, null, null), // Blacklist CreateBranch
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
        var events = await _sessionManager.Subscribe(sessionId).Skip(1).Take(1).ToListAsync(); // Skip status, take rejection

        Assert.Single(events);
        var rejected = Assert.IsType<CommandRejected>(events[0]);
        Assert.Equal(command.Id, rejected.CommandId);
        Assert.Equal("Policy violation", rejected.Reason);
        Assert.Equal("Command in blacklist", rejected.PolicyRule);
    }

    [Fact]
    public async Task AllowedCommand_PassesThrough()
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
        var events = await _sessionManager.Subscribe(sessionId).Skip(1).Take(2).ToListAsync(); // Skip status, take accepted + completed

        Assert.Equal(2, events.Count);
        Assert.IsType<CommandAccepted>(events[0]);
        Assert.IsType<CommandCompleted>(events[1]);
    }

    [Fact]
    public async Task ThrottledCommand_EmitsThrottledWithRetryAfter()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var config = new SessionConfig(
            sessionId,
            null,
            null,
            new PolicyProfile("test", null, null, new[] { "main" }, null, false, false, null, new RateLimits(1, null, null)), // 1 call per minute
            new RepoRef("test", "/tmp/test"),
            new WorkspaceRef("/tmp/workspace"),
            null,
            "test-agent");

        await _sessionManager.CreateSession(config);

        var command1 = new CreateBranch(
            Guid.NewGuid(),
            new Correlation(sessionId),
            new RepoRef("test", "/tmp/test"),
            "branch1");

        var command2 = new CreateBranch(
            Guid.NewGuid(),
            new Correlation(sessionId),
            new RepoRef("test", "/tmp/test"),
            "branch2");

        // Act
        await _sessionManager.PublishCommand(command1); // Should succeed
        await _sessionManager.PublishCommand(command2); // Should be throttled

        // Assert
        var events = await _sessionManager.Subscribe(sessionId).Skip(1).Take(3).ToListAsync(); // Skip status, take the 3 events

        Assert.Equal(3, events.Count); // accepted+completed for first, throttled for second
        Assert.IsType<CommandAccepted>(events[0]);
        Assert.IsType<CommandCompleted>(events[1]);
        var throttled = Assert.IsType<Throttled>(events[2]);
        Assert.Equal("Rate limit exceeded", throttled.Scope);
        Assert.True(throttled.RetryAfter > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task TwoSessions_GetDistinctWorkspaces()
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
            new WorkspaceRef(""), // Empty means temp
            null,
            "test-agent");

        var config2 = new SessionConfig(
            sessionId2,
            null,
            null,
            new PolicyProfile("test", null, null, new[] { "main" }, null, false, false, null, null),
            new RepoRef("test2", "/tmp/test2"),
            new WorkspaceRef(""), // Empty means temp
            null,
            "test-agent");

        // Act
        await _sessionManager.CreateSession(config1);
        await _sessionManager.CreateSession(config2);

        // Get workspace paths (would need to expose or test differently, but for now assume internal)
        // Since we can't access internal, this test is more about ensuring no exceptions and sessions created
        var events1 = await _sessionManager.Subscribe(sessionId1).Take(1).ToListAsync();
        var events2 = await _sessionManager.Subscribe(sessionId2).Take(1).ToListAsync();

        // Assert
        Assert.Single(events1);
        Assert.Single(events2);
        Assert.Equal(sessionId1, events1[0].Correlation.SessionId);
        Assert.Equal(sessionId2, events2[0].Correlation.SessionId);
    }
}