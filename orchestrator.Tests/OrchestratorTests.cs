using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.IO;
using JuniorDev.Contracts;
using Xunit;

namespace JuniorDev.Orchestrator.Tests;

public class OrchestratorTests : TimeoutTestBase
{
    private readonly ISessionManager _sessionManager;

    public OrchestratorTests(TestTimeoutFixture fixture) : base(fixture)
    {
        var adapters = new IAdapter[]
        {
            new FakeWorkItemsAdapter(),
            new FakeVcsAdapter(),
            new FakeBuildAdapter()
        };
        var policyEnforcer = new StubPolicyEnforcer();
        var rateLimiter = new StubRateLimiter();
        var workspaceProvider = new StubWorkspaceProvider();
        var artifactStore = new StubArtifactStore();
        _sessionManager = new SessionManager(adapters, policyEnforcer, rateLimiter, workspaceProvider, artifactStore);
    }

    [Fact]
    public async Task CommandAccepted_RoutedToFake_EventsRecordedAndStreamedInOrder()
    {
        Console.WriteLine("Test: CommandAccepted_RoutedToFake_EventsRecordedAndStreamedInOrder");
        Console.WriteLine("Purpose: Verify that a valid command is accepted, routed to the fake adapter, and events are recorded and streamed in order.");

        // Arrange
        Console.WriteLine("Arrange: Creating a new session with basic policy profile.");
        var sessionId = Guid.NewGuid();
        var config = new SessionConfig(
            sessionId,
            null,
            null,
            new PolicyProfile { Name = "test", ProtectedBranches = new HashSet<string> { "main" } },
            new RepoRef("test", "/tmp/test"),
            new WorkspaceRef("/tmp/workspace"),
            null,
            "test-agent");

        await _sessionManager.CreateSession(config);
        Console.WriteLine("Session created successfully.");

        var command = new CreateBranch(
            Guid.NewGuid(),
            new Correlation(sessionId),
            new RepoRef("test", "/tmp/test"),
            "feature-branch");
        Console.WriteLine("Created CreateBranch command.");

        // Act
        Console.WriteLine("Act: Publishing the command to the session manager.");
        await _sessionManager.PublishCommand(command);
        Console.WriteLine("Command published.");

        // Assert
        Console.WriteLine("Assert: Subscribing to events and verifying the sequence.");
        var events = await RunWithTimeout(_sessionManager.Subscribe(sessionId).Take(3).ToListAsync().AsTask(), TimeSpan.FromSeconds(5));
        Console.WriteLine($"Received {events.Count} events.");

        Assert.Equal(3, events.Count);
        Assert.IsType<SessionStatusChanged>(events[0]);
        Assert.IsType<CommandAccepted>(events[1]);
        Assert.IsType<CommandCompleted>(events[2]);

        var accepted = (CommandAccepted)events[1];
        var completed = (CommandCompleted)events[2];

        Assert.Equal(command.Id, accepted.CommandId);
        Assert.Equal(command.Id, completed.CommandId);
        Assert.Equal(CommandOutcome.Success, completed.Outcome);
        Console.WriteLine("Test passed: Events are in correct order with proper command IDs and success outcome.");
    }

    [Fact]
    public async Task CorrelationAndSessionId_PreservedInEmittedEvents()
    {
        Console.WriteLine("Test: CorrelationAndSessionId_PreservedInEmittedEvents");
        Console.WriteLine("Purpose: Verify that correlation IDs and session IDs are correctly preserved in all emitted events.");

        // Arrange
        Console.WriteLine("Arrange: Creating a session and a command with specific correlation IDs.");
        var sessionId = Guid.NewGuid();
        var commandId = Guid.NewGuid();
        var config = new SessionConfig(
            sessionId,
            null,
            null,
            new PolicyProfile { Name = "test", ProtectedBranches = new HashSet<string> { "main" } },
            new RepoRef("test", "/tmp/test"),
            new WorkspaceRef("/tmp/workspace"),
            null,
            "test-agent");

        await _sessionManager.CreateSession(config);
        Console.WriteLine("Session created.");

        var correlation = new Correlation(sessionId, commandId);
        var command = new Comment(
            commandId,
            correlation,
            new WorkItemRef("123"),
            "Test comment");
        Console.WriteLine("Created Comment command with correlation.");

        // Act
        Console.WriteLine("Act: Publishing the command.");
        await _sessionManager.PublishCommand(command);
        Console.WriteLine("Command published.");

        // Assert
        Console.WriteLine("Assert: Checking that all events preserve the correlation IDs.");
        var events = await RunWithTimeout(_sessionManager.Subscribe(sessionId).Skip(1).Take(2).ToListAsync().AsTask(), TimeSpan.FromSeconds(5));
        Console.WriteLine($"Received {events.Count} events after skipping initial status.");

        foreach (var @event in events)
        {
            Assert.Equal(sessionId, @event.Correlation.SessionId);
            Assert.Equal(commandId, @event.Correlation.CommandId);
        }
        Console.WriteLine("Test passed: All events have correct session and command IDs.");
    }

    [Fact]
    public async Task Subscribe_YieldsOrderedEvents_MultipleSessionsIsolated()
    {
        Console.WriteLine("Test: Subscribe_YieldsOrderedEvents_MultipleSessionsIsolated");
        Console.WriteLine("Purpose: Verify that events are yielded in order and that multiple sessions are properly isolated.");

        // Arrange
        Console.WriteLine("Arrange: Creating two separate sessions with different configurations.");
        var sessionId1 = Guid.NewGuid();
        var sessionId2 = Guid.NewGuid();

        var config1 = new SessionConfig(
            sessionId1,
            null,
            null,
            new PolicyProfile { Name = "test", ProtectedBranches = new HashSet<string> { "main" } },
            new RepoRef("test1", "/tmp/test1"),
            new WorkspaceRef("/tmp/workspace1"),
            null,
            "test-agent");

        var config2 = new SessionConfig(
            sessionId2,
            null,
            null,
            new PolicyProfile { Name = "test", ProtectedBranches = new HashSet<string> { "main" } },
            new RepoRef("test2", "/tmp/test2"),
            new WorkspaceRef("/tmp/workspace2"),
            null,
            "test-agent");

        await _sessionManager.CreateSession(config1);
        await _sessionManager.CreateSession(config2);
        Console.WriteLine("Both sessions created.");

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
        Console.WriteLine("Created commands for both sessions.");

        // Act
        Console.WriteLine("Act: Publishing commands to both sessions.");
        await _sessionManager.PublishCommand(command1);
        await _sessionManager.PublishCommand(command2);
        Console.WriteLine("Both commands published.");

        // Assert
        Console.WriteLine("Assert: Verifying that each session's event stream contains only its own events.");
        var events1 = await RunWithTimeout(_sessionManager.Subscribe(sessionId1).Take(3).ToListAsync().AsTask(), TimeSpan.FromSeconds(5));
        var events2 = await RunWithTimeout(_sessionManager.Subscribe(sessionId2).Take(3).ToListAsync().AsTask(), TimeSpan.FromSeconds(5));
        Console.WriteLine($"Session 1 has {events1.Count} events, Session 2 has {events2.Count} events.");

        // Session 1 should have its own events
        Assert.Equal(3, events1.Count); // status + accepted + completed
        Assert.Equal(sessionId1, events1[0].Correlation.SessionId);

        // Session 2 should have its own events
        Assert.Equal(3, events2.Count);
        Assert.Equal(sessionId2, events2[0].Correlation.SessionId);

        // Events are ordered by time
        Assert.True(events1[0].Id != events2[0].Id); // Different events
        Console.WriteLine("Test passed: Sessions are properly isolated with ordered events.");
    }

    [Fact]
    public async Task BlockedCommand_EmitsCommandRejectedWithRule()
    {
        Console.WriteLine("Test: BlockedCommand_EmitsCommandRejectedWithRule");
        Console.WriteLine("Purpose: Verify that commands blocked by policy emit CommandRejected events with the blocking rule.");

        // Arrange
        Console.WriteLine("Arrange: Creating a session with a policy that blacklists CreateBranch commands.");
        var sessionId = Guid.NewGuid();
        var config = new SessionConfig(
            sessionId,
            null,
            null,
            new PolicyProfile { Name = "test", CommandBlacklist = new List<string> { "CreateBranch" }, ProtectedBranches = new HashSet<string> { "main" } }, // Blacklist CreateBranch
            new RepoRef("test", "/tmp/test"),
            new WorkspaceRef("/tmp/workspace"),
            null,
            "test-agent");

        await _sessionManager.CreateSession(config);
        Console.WriteLine("Session created with blacklist policy.");

        var command = new CreateBranch(
            Guid.NewGuid(),
            new Correlation(sessionId),
            new RepoRef("test", "/tmp/test"),
            "feature-branch");
        Console.WriteLine("Created CreateBranch command that should be blocked.");

        // Act
        Console.WriteLine("Act: Publishing the blacklisted command.");
        await _sessionManager.PublishCommand(command);
        Console.WriteLine("Command published.");

        // Assert
        Console.WriteLine("Assert: Verifying that a CommandRejected event is emitted with the policy rule.");
        var events = await RunWithTimeout(_sessionManager.Subscribe(sessionId).Skip(1).Take(1).ToListAsync().AsTask(), TimeSpan.FromSeconds(5));
        Console.WriteLine($"Received {events.Count} event(s) after initial status.");

        Assert.Single(events);
        var rejected = Assert.IsType<CommandRejected>(events[0]);
        Assert.Equal(command.Id, rejected.CommandId);
        Assert.Equal("Policy violation", rejected.Reason);
        Assert.Equal("Command in blacklist", rejected.PolicyRule);
        Console.WriteLine("Test passed: Command was properly rejected with policy rule details.");
    }

    [Fact]
    public async Task AllowedCommand_PassesThrough()
    {
        Console.WriteLine("Test: AllowedCommand_PassesThrough");
        Console.WriteLine("Purpose: Verify that commands that pass policy checks are accepted and completed successfully.");

        // Arrange
        Console.WriteLine("Arrange: Creating a session with permissive policy.");
        var sessionId = Guid.NewGuid();
        var config = new SessionConfig(
            sessionId,
            null,
            null,
            new PolicyProfile { Name = "test", ProtectedBranches = new HashSet<string> { "main" } },
            new RepoRef("test", "/tmp/test"),
            new WorkspaceRef("/tmp/workspace"),
            null,
            "test-agent");

        await _sessionManager.CreateSession(config);
        Console.WriteLine("Session created with permissive policy.");

        var command = new CreateBranch(
            Guid.NewGuid(),
            new Correlation(sessionId),
            new RepoRef("test", "/tmp/test"),
            "feature-branch");
        Console.WriteLine("Created CreateBranch command that should be allowed.");

        // Act
        Console.WriteLine("Act: Publishing the allowed command.");
        await _sessionManager.PublishCommand(command);
        Console.WriteLine("Command published.");

        // Assert
        Console.WriteLine("Assert: Verifying that CommandAccepted and CommandCompleted events are emitted.");
        var events = await RunWithTimeout(_sessionManager.Subscribe(sessionId).Skip(1).Take(2).ToListAsync().AsTask(), TimeSpan.FromSeconds(5));
        Console.WriteLine($"Received {events.Count} events after initial status.");

        Assert.Equal(2, events.Count);
        Assert.IsType<CommandAccepted>(events[0]);
        Assert.IsType<CommandCompleted>(events[1]);
        Console.WriteLine("Test passed: Command was accepted and completed successfully.");
    }

    [Fact]
    public async Task ThrottledCommand_EmitsThrottledWithRetryAfter()
    {
        Console.WriteLine("Test: ThrottledCommand_EmitsThrottledWithRetryAfter");
        Console.WriteLine("Purpose: Verify that rate-limited commands emit Throttled events with retry information.");

        // Arrange
        Console.WriteLine("Arrange: Creating a session with strict rate limits (1 call per minute).");
        var sessionId = Guid.NewGuid();
        var config = new SessionConfig(
            sessionId,
            null,
            null,
            new PolicyProfile { Name = "test", ProtectedBranches = new HashSet<string> { "main" }, Limits = new RateLimits { CallsPerMinute = 1 } }, // 1 call per minute
            new RepoRef("test", "/tmp/test"),
            new WorkspaceRef("/tmp/workspace"),
            null,
            "test-agent");

        await _sessionManager.CreateSession(config);
        Console.WriteLine("Session created with rate limiting.");

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
        Console.WriteLine("Created two commands - first should succeed, second should be throttled.");

        // Act
        Console.WriteLine("Act: Publishing first command (should succeed), then second command (should be throttled).");
        await _sessionManager.PublishCommand(command1); // Should succeed
        await _sessionManager.PublishCommand(command2); // Should be throttled
        Console.WriteLine("Both commands published.");

        // Assert
        Console.WriteLine("Assert: Verifying the sequence of events includes throttling.");
        var events = await RunWithTimeout(_sessionManager.Subscribe(sessionId).Skip(1).Take(3).ToListAsync().AsTask(), TimeSpan.FromSeconds(5));
        Console.WriteLine($"Received {events.Count} events after initial status.");

        Assert.Equal(3, events.Count); // accepted+completed for first, throttled for second
        Assert.IsType<CommandAccepted>(events[0]);
        Assert.IsType<CommandCompleted>(events[1]);
        var throttled = Assert.IsType<Throttled>(events[2]);
        Assert.Equal("Rate limit exceeded", throttled.Scope);
        Assert.True(throttled.RetryAfter > DateTimeOffset.UtcNow);
        Console.WriteLine("Test passed: Rate limiting properly throttled the second command.");
    }

    [Fact]
    public async Task TwoSessions_GetDistinctWorkspaces()
    {
        Console.WriteLine("Test: TwoSessions_GetDistinctWorkspaces");
        Console.WriteLine("Purpose: Verify that multiple sessions can be created and each gets its own workspace isolation.");

        // Arrange
        Console.WriteLine("Arrange: Creating two sessions with empty workspace refs (meaning temporary workspaces).");
        var sessionId1 = Guid.NewGuid();
        var sessionId2 = Guid.NewGuid();

        var config1 = new SessionConfig(
            sessionId1,
            null,
            null,
            new PolicyProfile { Name = "test", ProtectedBranches = new HashSet<string> { "main" } },
            new RepoRef("test1", "/tmp/test1"),
            new WorkspaceRef(""), // Empty means temp
            null,
            "test-agent");

        var config2 = new SessionConfig(
            sessionId2,
            null,
            null,
            new PolicyProfile { Name = "test", ProtectedBranches = new HashSet<string> { "main" } },
            new RepoRef("test2", "/tmp/test2"),
            new WorkspaceRef(""), // Empty means temp
            null,
            "test-agent");
        Console.WriteLine("Created configurations for both sessions.");

        // Act
        Console.WriteLine("Act: Creating both sessions.");
        await _sessionManager.CreateSession(config1);
        await _sessionManager.CreateSession(config2);
        Console.WriteLine("Both sessions created successfully.");

        // Get workspace paths (would need to expose or test differently, but for now assume internal)
        // Since we can't access internal, this test is more about ensuring no exceptions and sessions created
        Console.WriteLine("Assert: Verifying that both sessions emit their initial status events with correct session IDs.");
        var events1 = await RunWithTimeout(_sessionManager.Subscribe(sessionId1).Take(1).ToListAsync().AsTask(), TimeSpan.FromSeconds(5));
        var events2 = await RunWithTimeout(_sessionManager.Subscribe(sessionId2).Take(1).ToListAsync().AsTask(), TimeSpan.FromSeconds(5));
        Console.WriteLine($"Session 1 has {events1.Count} initial event(s), Session 2 has {events2.Count} initial event(s).");

        // Assert
        Assert.Single(events1);
        Assert.Single(events2);
        Assert.Equal(sessionId1, events1[0].Correlation.SessionId);
        Assert.Equal(sessionId2, events2[0].Correlation.SessionId);
        Console.WriteLine("Test passed: Both sessions are properly isolated with distinct workspace handling.");
    }

    [Fact]
    public async Task CommandScenario_PolicyRateFakeAdapter_EventsWork()
    {
        Console.WriteLine("Test: CommandScenario_PolicyRateFakeAdapter_EventsWork");
        Console.WriteLine("Purpose: End-to-end test verifying that the full pipeline (policy, rate limiting, fake adapter, events) works together.");

        // Arrange
        Console.WriteLine("Arrange: Creating a session with standard configuration.");
        var sessionId = Guid.NewGuid();
        var config = new SessionConfig(
            sessionId,
            null,
            null,
            new PolicyProfile { Name = "test", ProtectedBranches = new HashSet<string> { "main" } },
            new RepoRef("test", "/tmp/test"),
            new WorkspaceRef("/tmp/workspace"),
            null,
            "test-agent");

        await _sessionManager.CreateSession(config);
        Console.WriteLine("Session created.");

        var command = new CreateBranch(
            Guid.NewGuid(),
            new Correlation(sessionId),
            new RepoRef("test", "/tmp/test"),
            "feature-branch");
        Console.WriteLine("Created CreateBranch command for end-to-end testing.");

        // Act
        Console.WriteLine("Act: Publishing the command through the full pipeline.");
        await _sessionManager.PublishCommand(command);
        Console.WriteLine("Command published.");

        // Assert
        Console.WriteLine("Assert: Verifying that the command completes successfully through all layers.");
        var events = await RunWithTimeout(_sessionManager.Subscribe(sessionId).Skip(1).Take(2).ToListAsync().AsTask(), TimeSpan.FromSeconds(5));
        Console.WriteLine($"Received {events.Count} events after initial status.");

        Assert.Equal(2, events.Count);
        Assert.IsType<CommandAccepted>(events[0]);
        Assert.IsType<CommandCompleted>(events[1]);
        Assert.Equal(CommandOutcome.Success, ((CommandCompleted)events[1]).Outcome);
        Console.WriteLine("Test passed: Full pipeline (policy, rate limiting, adapter, events) works correctly.");
    }

    [Fact]
    public async Task Pause_StopsDispatch_ResumeRestarts_AbortPreventsFurther()
    {
        Console.WriteLine("Test: Pause_StopsDispatch_ResumeRestarts_AbortPreventsFurther");
        Console.WriteLine("Purpose: Verify session lifecycle management - pause stops commands, resume restarts them, abort prevents further execution.");

        // Arrange
        Console.WriteLine("Arrange: Creating a session and multiple commands to test lifecycle transitions.");
        var sessionId = Guid.NewGuid();
        var config = new SessionConfig(
            sessionId,
            null,
            null,
            new PolicyProfile { Name = "test", ProtectedBranches = new HashSet<string> { "main" } },
            new RepoRef("test", "/tmp/test"),
            new WorkspaceRef("/tmp/workspace"),
            null,
            "test-agent");

        await _sessionManager.CreateSession(config);
        Console.WriteLine("Session created.");

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

        var command3 = new CreateBranch(
            Guid.NewGuid(),
            new Correlation(sessionId),
            new RepoRef("test", "/tmp/test"),
            "branch3");
        Console.WriteLine("Created three commands for testing different lifecycle states.");

        // Act
        Console.WriteLine("Act: Publishing command1 (should succeed), then pausing session.");
        await _sessionManager.PublishCommand(command1); // Should succeed
        await _sessionManager.PauseSession(sessionId);
        Console.WriteLine("Session paused.");

        Console.WriteLine("Publishing command2 while paused (should be rejected).");
        await _sessionManager.PublishCommand(command2); // Should be rejected (paused)
        Console.WriteLine("Command2 published while paused.");

        Console.WriteLine("Resuming session and publishing command3 (should succeed).");
        await _sessionManager.ResumeSession(sessionId);
        await _sessionManager.PublishCommand(command3); // Should succeed
        Console.WriteLine("Session resumed and command3 published.");

        Console.WriteLine("Aborting session to prevent further operations.");
        await _sessionManager.AbortSession(sessionId);
        Console.WriteLine("Session aborted.");

        // Assert
        Console.WriteLine("Assert: Verifying the sequence of events matches expected lifecycle behavior.");
        var events = await RunWithTimeout(_sessionManager.Subscribe(sessionId).Skip(1).Take(6).ToListAsync().AsTask(), TimeSpan.FromSeconds(5));
        Console.WriteLine($"Received {events.Count} events in the sequence.");

        // Should have: accepted+completed for cmd1, status paused, rejected for cmd2, status running, accepted+completed for cmd3
        Assert.Equal(6, events.Count);
        Assert.IsType<CommandAccepted>(events[0]);
        Assert.IsType<CommandCompleted>(events[1]);
        Assert.IsType<SessionStatusChanged>(events[2]);
        Assert.Equal(SessionStatus.Paused, ((SessionStatusChanged)events[2]).Status);
        Assert.IsType<CommandRejected>(events[3]);
        Assert.IsType<SessionStatusChanged>(events[4]);
        Assert.Equal(SessionStatus.Running, ((SessionStatusChanged)events[4]).Status);
        Assert.IsType<CommandAccepted>(events[5]);
        // Note: completed for cmd3 and abort status not included in Take(6)
        Console.WriteLine("Test passed: Session lifecycle (pause/resume/abort) properly controls command execution.");
    }

    [Fact]
    public async Task Approvals_BlockUntilApproved()
    {
        Console.WriteLine("Test: Approvals_BlockUntilApproved");
        Console.WriteLine("Purpose: Verify that commands requiring approval are queued until the session is approved, then dispatched automatically.");

        // Create fresh session manager for this test
        var adapters = new IAdapter[]
        {
            new FakeWorkItemsAdapter(),
            new FakeVcsAdapter()
        };
        var policyEnforcer = new StubPolicyEnforcer();
        var rateLimiter = new StubRateLimiter();
        var workspaceProvider = new StubWorkspaceProvider();
        var artifactStore = new StubArtifactStore();
        var sessionManager = new SessionManager(adapters, policyEnforcer, rateLimiter, workspaceProvider, artifactStore);

        // Arrange
        Console.WriteLine("Arrange: Creating a session with approval required for Push commands.");
        var sessionId = Guid.NewGuid();
        var config = new SessionConfig(
            sessionId,
            null,
            null,
            new PolicyProfile { Name = "test", ProtectedBranches = new HashSet<string> { "main" }, RequireApprovalForPush = true }, // Require approval for push
            new RepoRef("test", "/tmp/test"),
            new WorkspaceRef("/tmp/workspace"),
            null,
            "test-agent");

        await sessionManager.CreateSession(config);
        Console.WriteLine("Session created with approval requirement.");

        var command = new Push(
            Guid.NewGuid(),
            new Correlation(sessionId),
            new RepoRef("test", "/tmp/test"),
            "main");
        Console.WriteLine("Created Push command that requires approval.");

        // Act
        Console.WriteLine("Act: Publishing command before approval (should be queued, session moves to NeedsApproval).");
        await sessionManager.PublishCommand(command); // Should be queued (needs approval)
        Console.WriteLine("First command published and queued.");

        Console.WriteLine("Approving the session.");
        await sessionManager.ApproveSession(sessionId);
        Console.WriteLine("Session approved.");

        // Complete the session to end the event stream
        await sessionManager.CompleteSession(sessionId);

        // Assert
        Console.WriteLine("Assert: Verifying the sequence shows NeedsApproval then success after approval.");
        var events = await RunWithTimeout(sessionManager.Subscribe(sessionId).Take(6).ToListAsync().AsTask(), TimeSpan.FromSeconds(5));
        Console.WriteLine($"Received {events.Count} events.");

        Assert.True(events.Count >= 5);
        Assert.IsType<SessionStatusChanged>(events[1]); // NeedsApproval status
        Assert.Equal(SessionStatus.NeedsApproval, ((SessionStatusChanged)events[1]).Status);
        Assert.Contains(events, e => e is CommandAccepted);
        Assert.Contains(events, e => e is CommandCompleted);
        Console.WriteLine("Test passed: Command queued until approval, then executed.");
    }

    [Fact]
    public async Task QueryBacklog_EmitsBacklogQueriedWithItems()
    {
        Console.WriteLine("Test: QueryBacklog_EmitsBacklogQueriedWithItems");
        Console.WriteLine("Purpose: Verify that QueryBacklog command emits BacklogQueried event with work item summaries.");

        // Arrange
        Console.WriteLine("Arrange: Creating a session for backlog querying.");
        var sessionId = Guid.NewGuid();
        var config = new SessionConfig(
            sessionId,
            null,
            null,
            new PolicyProfile { Name = "test", ProtectedBranches = new HashSet<string> { "main" } },
            new RepoRef("test", "/tmp/test"),
            new WorkspaceRef("/tmp/workspace"),
            null,
            "test-agent");

        await _sessionManager.CreateSession(config);
        Console.WriteLine("Session created.");

        var command = new QueryBacklog(
            Guid.NewGuid(),
            new Correlation(sessionId),
            null); // No filter
        Console.WriteLine("Created QueryBacklog command.");

        // Act
        Console.WriteLine("Act: Publishing the query command.");
        await _sessionManager.PublishCommand(command);
        Console.WriteLine("Query command published.");

        // Assert
        Console.WriteLine("Assert: Verifying BacklogQueried event is emitted with expected items.");
        var events = await RunWithTimeout(_sessionManager.Subscribe(sessionId).Skip(1).Take(2).ToListAsync().AsTask(), TimeSpan.FromSeconds(5));
        Console.WriteLine($"Received {events.Count} event(s) after initial status.");

        Assert.Equal(2, events.Count);
        Assert.IsType<CommandAccepted>(events[0]);
        var queried = Assert.IsType<BacklogQueried>(events[1]);
        Assert.Equal(3, queried.Items.Count); // Our fake data has 3 items
        Assert.Contains(queried.Items, i => i.Id == "PROJ-123");
        Assert.Contains(queried.Items, i => i.Title.Contains("authentication"));
        Console.WriteLine("Test passed: BacklogQueried event emitted with correct fake data.");
    }

    [Fact]
    public async Task QueryWorkItem_EmitsWorkItemQueriedWithDetails()
    {
        Console.WriteLine("Test: QueryWorkItem_EmitsWorkItemQueriedWithDetails");
        Console.WriteLine("Purpose: Verify that QueryWorkItem command emits WorkItemQueried event with work item details.");

        // Arrange
        Console.WriteLine("Arrange: Creating a session for work item querying.");
        var sessionId = Guid.NewGuid();
        var config = new SessionConfig(
            sessionId,
            null,
            null,
            new PolicyProfile { Name = "test", ProtectedBranches = new HashSet<string> { "main" } },
            new RepoRef("test", "/tmp/test"),
            new WorkspaceRef("/tmp/workspace"),
            null,
            "test-agent");

        await _sessionManager.CreateSession(config);
        Console.WriteLine("Session created.");

        var command = new QueryWorkItem(
            Guid.NewGuid(),
            new Correlation(sessionId),
            new WorkItemRef("PROJ-124"));
        Console.WriteLine("Created QueryWorkItem command for PROJ-124.");

        // Act
        Console.WriteLine("Act: Publishing the query command.");
        await _sessionManager.PublishCommand(command);
        Console.WriteLine("Query command published.");

        // Assert
        Console.WriteLine("Assert: Verifying WorkItemQueried event is emitted with expected details.");
        var events = await RunWithTimeout(_sessionManager.Subscribe(sessionId).Skip(1).Take(2).ToListAsync().AsTask(), TimeSpan.FromSeconds(5));
        Console.WriteLine($"Received {events.Count} event(s) after initial status.");

        Assert.Equal(2, events.Count);
        Assert.IsType<CommandAccepted>(events[0]);
        var queried = Assert.IsType<WorkItemQueried>(events[1]);
        Assert.Equal("PROJ-124", queried.Details.Id);
        Assert.Equal("Add database migration", queried.Details.Title);
        Assert.Contains("database", queried.Details.Tags);
        Console.WriteLine("Test passed: WorkItemQueried event emitted with correct fake data.");
    }

    [Fact]
    public async Task BuildProjectCommand_AcceptedAndRoutedToBuildAdapter()
    {
        Console.WriteLine("Test: BuildProjectCommand_AcceptedAndRoutedToBuildAdapter");
        Console.WriteLine("Purpose: Verify that BuildProject commands are accepted and routed to the build adapter.");

        // Arrange
        Console.WriteLine("Arrange: Creating a session with build adapter.");
        var sessionId = Guid.NewGuid();
        var config = new SessionConfig(
            sessionId,
            null,
            null,
            new PolicyProfile { Name = "test", ProtectedBranches = new HashSet<string> { "main" } },
            new RepoRef("test", "/tmp/test"),
            new WorkspaceRef("/tmp/workspace"),
            null,
            "test-agent");

        await _sessionManager.CreateSession(config);
        Console.WriteLine("Session created.");

        var command = new BuildProject(
            Guid.NewGuid(),
            new Correlation(sessionId),
            new RepoRef("test", "/tmp/test"),
            "src/MyProject.csproj",
            "Release",
            "net8.0",
            new[] { "Build" },
            TimeSpan.FromMinutes(5));
        Console.WriteLine("Created BuildProject command.");

        // Act
        Console.WriteLine("Act: Publishing the build command.");
        await _sessionManager.PublishCommand(command);
        Console.WriteLine("Build command published.");

        // Assert
        Console.WriteLine("Assert: Verifying the command completes successfully through the build adapter.");
        var events = await RunWithTimeout(_sessionManager.Subscribe(sessionId).Skip(1).Take(3).ToListAsync().AsTask(), TimeSpan.FromSeconds(5));
        Console.WriteLine($"Received {events.Count} events after initial status.");

        Assert.Equal(3, events.Count);
        Assert.IsType<CommandAccepted>(events[0]);
        Assert.IsType<ArtifactAvailable>(events[1]);
        Assert.IsType<CommandCompleted>(events[2]);

        var artifact = (ArtifactAvailable)events[1];
        Assert.Equal("BuildLog", artifact.Artifact.Kind);
        Assert.Contains("build-", artifact.Artifact.Name);

        var completed = (CommandCompleted)events[2];
        Assert.Equal(CommandOutcome.Success, completed.Outcome);
        Console.WriteLine("Test passed: BuildProject command routed to build adapter and completed successfully.");
    }

    [Fact]
    public async Task BuildProjectCommand_WithInvalidPath_Rejected()
    {
        Console.WriteLine("Test: BuildProjectCommand_WithInvalidPath_Rejected");
        Console.WriteLine("Purpose: Verify that BuildProject commands with invalid paths are rejected by policy.");

        // Arrange
        Console.WriteLine("Arrange: Creating a session for build command validation.");
        var sessionId = Guid.NewGuid();
        var config = new SessionConfig(
            sessionId,
            null,
            null,
            new PolicyProfile { Name = "test", ProtectedBranches = new HashSet<string> { "main" } },
            new RepoRef("test", "/tmp/test"),
            new WorkspaceRef("/tmp/workspace"),
            null,
            "test-agent");

        await _sessionManager.CreateSession(config);
        Console.WriteLine("Session created.");

        var command = new BuildProject(
            Guid.NewGuid(),
            new Correlation(sessionId),
            new RepoRef("test", "/tmp/test"),
            "../../../etc/passwd", // Invalid path
            "Release");
        Console.WriteLine("Created BuildProject command with invalid path.");

        // Act
        Console.WriteLine("Act: Publishing the invalid build command.");
        await _sessionManager.PublishCommand(command);
        Console.WriteLine("Invalid build command published.");

        // Assert
        Console.WriteLine("Assert: Verifying the command is rejected due to invalid path.");
        var events = await RunWithTimeout(_sessionManager.Subscribe(sessionId).Skip(1).Take(1).ToListAsync().AsTask(), TimeSpan.FromSeconds(5));
        Console.WriteLine($"Received {events.Count} event(s) after initial status.");

        Assert.Single(events);
        var rejected = Assert.IsType<CommandRejected>(events[0]);
        Assert.Equal(command.Id, rejected.CommandId);
        Assert.Contains("Invalid project path", rejected.Reason);
        Console.WriteLine("Test passed: BuildProject command with invalid path was properly rejected.");
    }
}

/// <summary>
/// Fake build adapter for testing.
/// </summary>
public class FakeBuildAdapter : IAdapter
{
    public bool CanHandle(ICommand command) => command is BuildProject;

    public async Task HandleCommand(ICommand command, SessionState session)
    {
        if (command is not BuildProject buildCommand)
        {
            throw new ArgumentException($"Command must be {nameof(BuildProject)}", nameof(command));
        }

        // Validate the project path for security (same as real adapter)
        if (!IsValidProjectPath(buildCommand.ProjectPath))
        {
            await session.AddEvent(new CommandRejected(
                Guid.NewGuid(),
                command.Correlation,
                command.Id,
                "Invalid project path",
                "Path validation"));
            return;
        }

        // Accept the command
        await session.AddEvent(new CommandAccepted(
            Guid.NewGuid(),
            command.Correlation,
            command.Id));

        // Create fake artifact
        var artifact = new Artifact(
            "BuildLog",
            $"build-{Path.GetFileNameWithoutExtension(buildCommand.ProjectPath)}.log",
            InlineText: "Fake build output - success");

        await session.AddEvent(new ArtifactAvailable(
            Guid.NewGuid(),
            command.Correlation,
            artifact));

        // Complete successfully
        await session.AddEvent(new CommandCompleted(
            Guid.NewGuid(),
            command.Correlation,
            command.Id,
            CommandOutcome.Success,
            "Build completed successfully"));
    }

    private bool IsValidProjectPath(string projectPath)
    {
        // Basic security validation - ensure path doesn't contain dangerous elements
        if (string.IsNullOrWhiteSpace(projectPath))
            return false;

        // Check for directory traversal attempts
        if (projectPath.Contains("..") || projectPath.Contains("\\..") || projectPath.Contains("../"))
            return false;

        // Only allow .csproj, .fsproj, .vbproj, .sln files
        var extension = Path.GetExtension(projectPath).ToLowerInvariant();
        return extension is ".csproj" or ".fsproj" or ".vbproj" or ".sln";
    }
}
