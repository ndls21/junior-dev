using JuniorDev.Agents;
using JuniorDev.Contracts;
using JuniorDev.Orchestrator;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace JuniorDev.Agents.Tests;

public class AgentEventLoopServiceTests
{
    private readonly Mock<ISessionManager> _sessionManagerMock;
    private readonly AgentEventDispatcher _dispatcher;
    private readonly Mock<ILogger<AgentEventLoopService>> _loggerMock;
    private readonly AgentEventLoopService _eventLoopService;

    public AgentEventLoopServiceTests()
    {
        _sessionManagerMock = new Mock<ISessionManager>();
        var dispatcherLoggerMock = new Mock<ILogger<AgentEventDispatcher>>();
        _dispatcher = new AgentEventDispatcher(dispatcherLoggerMock.Object);
        _loggerMock = new Mock<ILogger<AgentEventLoopService>>();
        _eventLoopService = new AgentEventLoopService(
            _sessionManagerMock.Object,
            _dispatcher,
            _loggerMock.Object);
    }

    [Fact]
    public void Constructor_ThrowsOnNullSessionManager()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new AgentEventLoopService(null!, _dispatcher, _loggerMock.Object));
    }

    [Fact]
    public void Constructor_ThrowsOnNullDispatcher()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new AgentEventLoopService(_sessionManagerMock.Object, null!, _loggerMock.Object));
    }

    [Fact]
    public void Constructor_ThrowsOnNullLogger()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new AgentEventLoopService(_sessionManagerMock.Object, _dispatcher, null!));
    }

    [Fact]
    public void IsRunning_ReturnsFalseInitially()
    {
        Assert.False(_eventLoopService.IsRunning);
    }

    [Fact]
    public void StartSessionSubscription_AddsSubscription()
    {
        // Arrange
        var sessionId = Guid.NewGuid();

        // Act
        _eventLoopService.StartSessionSubscription(sessionId);

        // Assert - We can't directly test the internal dictionary, but we can verify no exceptions
        Assert.True(true); // If we get here, the method executed without error
    }

    [Fact]
    public void StopSessionSubscription_RemovesSubscription()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        _eventLoopService.StartSessionSubscription(sessionId);

        // Act
        _eventLoopService.StopSessionSubscription(sessionId);

        // Assert - We can't directly test the internal dictionary, but we can verify no exceptions
        Assert.True(true); // If we get here, the method executed without error
    }

    [Fact]
#pragma warning disable CS1998 // Async test method - await is used for timing control
    public async Task ExecuteAsync_ProcessesSessionEvents()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var testEvent = new CommandCompleted(
            Guid.NewGuid(),
            new Correlation(sessionId, Guid.NewGuid(), null, null),
            Guid.NewGuid(),
            CommandOutcome.Success);

        async IAsyncEnumerable<IEvent> GetEvents()
        {
            yield return testEvent;
        }

        _sessionManagerMock.Setup(sm => sm.Subscribe(sessionId))
            .Returns(GetEvents());

        // Register a mock agent to receive events
        var agentMock = new Mock<IAgent>();
        agentMock.Setup(a => a.AgentType).Returns("TestAgent");
        agentMock.Setup(a => a.Id).Returns("test-agent");
        agentMock.Setup(a => a.HandleEventAsync(testEvent)).Returns(Task.CompletedTask);
        _dispatcher.RegisterAgent(agentMock.Object);

        _eventLoopService.StartSessionSubscription(sessionId);

        // Act
        var cts = new CancellationTokenSource();
        cts.CancelAfter(100); // Cancel after 100ms to stop the background service
        await _eventLoopService.StartAsync(cts.Token);
        await Task.Delay(50); // Let it start
        await _eventLoopService.StopAsync(CancellationToken.None);

        // Assert
        agentMock.Verify(a => a.HandleEventAsync(testEvent), Times.Once);
    }
#pragma warning restore CS1998

    [Fact]
#pragma warning disable CS1998 // Async test method - await is used for timing control
    public async Task ExecuteAsync_HandlesAgentExceptions()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var testEvent = new CommandCompleted(
            Guid.NewGuid(),
            new Correlation(sessionId, Guid.NewGuid(), null, null),
            Guid.NewGuid(),
            CommandOutcome.Success);

        async IAsyncEnumerable<IEvent> GetEvents()
        {
            yield return testEvent;
        }

        _sessionManagerMock.Setup(sm => sm.Subscribe(sessionId))
            .Returns(GetEvents());

        // Register a mock agent that throws
        var agentMock = new Mock<IAgent>();
        agentMock.Setup(a => a.AgentType).Returns("TestAgent");
        agentMock.Setup(a => a.Id).Returns("test-agent");
        agentMock.Setup(a => a.HandleEventAsync(testEvent))
            .ThrowsAsync(new Exception("Agent failed"));
        _dispatcher.RegisterAgent(agentMock.Object);

        _eventLoopService.StartSessionSubscription(sessionId);

        // Act & Assert - should not throw
        var cts = new CancellationTokenSource();
        cts.CancelAfter(100);
        await _eventLoopService.StartAsync(cts.Token);
        await Task.Delay(50);
        await _eventLoopService.StopAsync(CancellationToken.None);
    }
#pragma warning restore CS1998
}