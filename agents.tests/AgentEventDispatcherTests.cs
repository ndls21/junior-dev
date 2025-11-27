using System.Collections.Concurrent;
using JuniorDev.Agents;
using JuniorDev.Contracts;
using JuniorDev.Orchestrator;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace JuniorDev.Agents.Tests;

public class AgentEventDispatcherTests
{
    private readonly Mock<ILogger<AgentEventDispatcher>> _loggerMock;
    private readonly AgentEventDispatcher _dispatcher;

    public AgentEventDispatcherTests()
    {
        _loggerMock = new Mock<ILogger<AgentEventDispatcher>>();
        _dispatcher = new AgentEventDispatcher(_loggerMock.Object);
    }

    [Fact]
    public async Task DispatchEventAsync_SendsEventToAllAgents()
    {
        // Arrange
        var agent1 = new Mock<IAgent>();
        agent1.Setup(a => a.AgentType).Returns("TestAgent");
        agent1.Setup(a => a.Id).Returns("agent1");

        var agent2 = new Mock<IAgent>();
        agent2.Setup(a => a.AgentType).Returns("TestAgent");
        agent2.Setup(a => a.Id).Returns("agent2");

        _dispatcher.RegisterAgent(agent1.Object);
        _dispatcher.RegisterAgent(agent2.Object);

        var testEvent = new CommandCompleted(
            Guid.NewGuid(),
            new Correlation(Guid.NewGuid(), Guid.NewGuid(), null, null),
            Guid.NewGuid(),
            CommandOutcome.Success);

        // Act
        await _dispatcher.DispatchEventAsync(testEvent);

        // Assert
        agent1.Verify(a => a.HandleEventAsync(testEvent), Times.Once);
        agent2.Verify(a => a.HandleEventAsync(testEvent), Times.Once);
    }

    [Fact]
    public async Task DispatchEventAsync_HandlesAgentExceptions()
    {
        // Arrange
        var agent = new Mock<IAgent>();
        agent.Setup(a => a.AgentType).Returns("TestAgent");
        agent.Setup(a => a.Id).Returns("agent1");
        agent.Setup(a => a.HandleEventAsync(It.IsAny<IEvent>()))
            .ThrowsAsync(new Exception("Test exception"));

        _dispatcher.RegisterAgent(agent.Object);

        var testEvent = new CommandCompleted(
            Guid.NewGuid(),
            new Correlation(Guid.NewGuid(), Guid.NewGuid(), null, null),
            Guid.NewGuid(),
            CommandOutcome.Success);

        // Act & Assert - should not throw
        await _dispatcher.DispatchEventAsync(testEvent);

        // Verify logger was called for the error
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void RegisterAgent_AddsAgentToRegistry()
    {
        // Arrange
        var agent = new Mock<IAgent>();
        agent.Setup(a => a.AgentType).Returns("TestAgent");
        agent.Setup(a => a.Id).Returns("agent1");

        // Act
        _dispatcher.RegisterAgent(agent.Object);

        // Assert
        var agents = _dispatcher.GetAgentsByType("TestAgent");
        Assert.Single(agents);
        Assert.Equal(agent.Object, agents.First());
    }

    [Fact]
    public void UnregisterAgent_RemovesAgentFromRegistry()
    {
        // Arrange
        var agent = new Mock<IAgent>();
        agent.Setup(a => a.AgentType).Returns("TestAgent");
        agent.Setup(a => a.Id).Returns("agent1");

        _dispatcher.RegisterAgent(agent.Object);
        Assert.Single(_dispatcher.GetAgentsByType("TestAgent"));

        // Act
        _dispatcher.UnregisterAgent(agent.Object);

        // Assert
        Assert.Empty(_dispatcher.GetAgentsByType("TestAgent"));
    }
}