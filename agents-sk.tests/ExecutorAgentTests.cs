using JuniorDev.Agents;
using JuniorDev.Agents.Sk;
using JuniorDev.Contracts;
using JuniorDev.Orchestrator;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Moq;
using Xunit;

namespace JuniorDev.Agents.Sk.Tests;

public class ExecutorAgentTests
{
    private readonly Mock<ISessionManager> _sessionManagerMock;
    private readonly Mock<ILogger<ExecutorAgent>> _loggerMock;
    private readonly Kernel _kernel;
    private readonly AgentSessionContext _context;
    private readonly ExecutorAgent _agent;

    public ExecutorAgentTests()
    {
        _sessionManagerMock = new Mock<ISessionManager>();
        _loggerMock = new Mock<ILogger<ExecutorAgent>>();
        _kernel = new Kernel();

        var sessionConfig = new SessionConfig(
            Guid.NewGuid(),
            null,
            null,
            new PolicyProfile("test", null, null, Array.Empty<string>(), null, false, false, null, null),
            new RepoRef("test-repo", "/repos/test-repo"),
            new WorkspaceRef("/workspaces/test-ws"),
            null,
            "test-agent");

        var agentConfig = AgentConfig.CreateDeterministic();

        _context = new AgentSessionContext(
            Guid.NewGuid(),
            sessionConfig,
            _sessionManagerMock.Object,
            agentConfig,
            _loggerMock.Object);

        _agent = new ExecutorAgent(_kernel);
        // Don't start the agent in constructor to avoid plugin registration conflicts
    }

    [Fact]
    public void AgentType_ReturnsExecutor()
    {
        Assert.Equal("executor", _agent.AgentType);
    }

    [Fact]
    public async Task StartAsync_NoWorkItem_LogsWaitingMessage()
    {
        // Act
        await _agent.StartAsync(_context);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("waiting for LLM invocation")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task StartAsync_WithWorkItem_ExecutesWorkItem()
    {
        // Arrange
        var kernel = new Kernel();
        var workItem = new WorkItemRef("FEATURE-123");
        var sessionConfig = _context.Config with { WorkItem = workItem };
        var contextWithWorkItem = new AgentSessionContext(
            _context.SessionId,
            sessionConfig,
            _sessionManagerMock.Object,
            _context.AgentConfig,
            _context.Logger);

        var agent = new ExecutorAgent(kernel);

        _sessionManagerMock.Setup(sm => sm.PublishCommand(It.IsAny<ICommand>()))
            .Returns(Task.CompletedTask);

        // Act
        await agent.StartAsync(contextWithWorkItem);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("Executing work item")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task StopAsync_LogsStopMessage()
    {
        // Arrange
        await _agent.StartAsync(_context);

        // Act
        await _agent.StopAsync();

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("Executor agent stopped")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleEventAsync_CommandCompleted_LogsSuccess()
    {
        // Arrange
        await _agent.StartAsync(_context);

        var commandCompleted = new CommandCompleted(
            Guid.NewGuid(),
            new Correlation(_context.SessionId, Guid.NewGuid(), null, null),
            Guid.NewGuid(),
            CommandOutcome.Success);

        // Act
        await _agent.HandleEventAsync(commandCompleted);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("completed successfully")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleEventAsync_CommandRejected_LogsWarningAndCommentsOnWorkItem()
    {
        // Arrange
        var kernel = new Kernel();
        var workItem = new WorkItemRef("FEATURE-123");
        var sessionConfig = _context.Config with { WorkItem = workItem };
        var contextWithWorkItem = new AgentSessionContext(
            _context.SessionId,
            sessionConfig,
            _sessionManagerMock.Object,
            _context.AgentConfig,
            _context.Logger);

        var agent = new ExecutorAgent(kernel);
        await agent.StartAsync(contextWithWorkItem);

        _sessionManagerMock.Setup(sm => sm.PublishCommand(It.IsAny<ICommand>()))
            .Returns(Task.CompletedTask);

        var commandRejected = new CommandRejected(
            Guid.NewGuid(),
            new Correlation(_context.SessionId, Guid.NewGuid(), null, null),
            Guid.NewGuid(),
            "Test rejection reason");

        // Act
        await agent.HandleEventAsync(commandRejected);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("was rejected")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        _sessionManagerMock.Verify(sm => sm.PublishCommand(It.Is<Comment>(c =>
            c.Body.Contains("Command rejected") && c.Item.Id == "FEATURE-123")), Times.Once);
    }

    [Fact]
    public async Task HandleEventAsync_Throttled_LogsWarningAndCommentsOnWorkItem()
    {
        // Arrange
        var kernel = new Kernel();
        var workItem = new WorkItemRef("FEATURE-123");
        var sessionConfig = _context.Config with { WorkItem = workItem };
        var contextWithWorkItem = new AgentSessionContext(
            _context.SessionId,
            sessionConfig,
            _sessionManagerMock.Object,
            _context.AgentConfig,
            _context.Logger);

        var agent = new ExecutorAgent(kernel);
        await agent.StartAsync(contextWithWorkItem);

        _sessionManagerMock.Setup(sm => sm.PublishCommand(It.IsAny<ICommand>()))
            .Returns(Task.CompletedTask);

        var throttled = new Throttled(
            Guid.NewGuid(),
            new Correlation(_context.SessionId, Guid.NewGuid(), null, null),
            "test-scope",
            DateTimeOffset.Now.AddSeconds(30));

        // Act
        await agent.HandleEventAsync(throttled);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("was throttled")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        _sessionManagerMock.Verify(sm => sm.PublishCommand(It.Is<Comment>(c =>
            c.Body.Contains("Operation throttled") && c.Item.Id == "FEATURE-123")), Times.Once);
    }

    [Fact]
    public async Task HandleEventAsync_ConflictDetected_LogsWarningAndTransitionsWorkItem()
    {
        // Arrange
        var kernel = new Kernel();
        var workItem = new WorkItemRef("FEATURE-123");
        var sessionConfig = _context.Config with { WorkItem = workItem };
        var contextWithWorkItem = new AgentSessionContext(
            _context.SessionId,
            sessionConfig,
            _sessionManagerMock.Object,
            _context.AgentConfig,
            _context.Logger);

        var agent = new ExecutorAgent(kernel);
        await agent.StartAsync(contextWithWorkItem);

        _sessionManagerMock.Setup(sm => sm.PublishCommand(It.IsAny<ICommand>()))
            .Returns(Task.CompletedTask);

        var conflict = new ConflictDetected(
            Guid.NewGuid(),
            new Correlation(_context.SessionId, Guid.NewGuid(), null, null),
            new RepoRef("test-repo", "/repos/test-repo"),
            "Merge conflict detected");

        // Act
        await agent.HandleEventAsync(conflict);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("Conflict detected")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        _sessionManagerMock.Verify(sm => sm.PublishCommand(It.Is<Comment>(c =>
            c.Body.Contains("Conflict detected") && c.Item.Id == "FEATURE-123")), Times.Once);
        _sessionManagerMock.Verify(sm => sm.PublishCommand(It.Is<TransitionTicket>(t =>
            t.State == "Blocked" && t.Item.Id == "FEATURE-123")), Times.Once);
    }

    [Fact]
    public async Task ExecuteWorkItemAsync_DryRun_SkipsRiskyOperations()
    {
        // Arrange
        var kernel = new Kernel();
        var workItem = new WorkItemRef("FEATURE-123");
        var agentConfig = new AgentConfig
        {
            DryRun = true,
            RandomSeed = _context.AgentConfig.RandomSeed,
            OperationTimeoutSeconds = _context.AgentConfig.OperationTimeoutSeconds,
            MaxRetryAttempts = _context.AgentConfig.MaxRetryAttempts,
            RetryBaseDelayMs = _context.AgentConfig.RetryBaseDelayMs,
            AgentProfile = _context.AgentConfig.AgentProfile,
            EnableDetailedLogging = _context.AgentConfig.EnableDetailedLogging,
            EnableMetrics = _context.AgentConfig.EnableMetrics
        };
        var sessionConfig = _context.Config with { WorkItem = workItem };
        var contextWithWorkItem = new AgentSessionContext(
            _context.SessionId,
            sessionConfig,
            _sessionManagerMock.Object,
            agentConfig,
            _context.Logger);

        var agent = new ExecutorAgent(kernel);
        await agent.StartAsync(contextWithWorkItem);

        _sessionManagerMock.Setup(sm => sm.PublishCommand(It.IsAny<ICommand>()))
            .Returns(Task.CompletedTask);

        // Assert - Should log dry run messages but not execute push commands
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("[DRY RUN]")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);

        // Should not execute push commands in dry run
        _sessionManagerMock.Verify(sm => sm.PublishCommand(It.IsAny<Push>()), Times.Never);
    }

    [Fact]
    public async Task HandleEventAsync_UnknownEventType_IgnoresEvent()
    {
        // Arrange
        await _agent.StartAsync(_context);

        var unknownEvent = new TestEvent(
            Guid.NewGuid(),
            new Correlation(_context.SessionId, Guid.NewGuid(), null, null));

        // Act
        await _agent.HandleEventAsync(unknownEvent);

        // Assert - Should log debug messages about processing and ignoring
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("Ignoring event")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    // Test event for unknown event type testing
    private class TestEvent : IEvent
    {
        public Guid Id { get; }
        public Correlation Correlation { get; }
        public string Kind => "TestEvent";

        public TestEvent(Guid id, Correlation correlation)
        {
            Id = id;
            Correlation = correlation;
        }
    }
}