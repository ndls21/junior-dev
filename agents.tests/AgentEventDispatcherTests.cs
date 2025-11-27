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
    public async Task DispatchEventAsync_FiltersByEventType_WhenAgentHasInterests()
    {
        // Arrange
        var interestedAgent = new Mock<IAgent>();
        interestedAgent.Setup(a => a.AgentType).Returns("TestAgent");
        interestedAgent.Setup(a => a.Id).Returns("interested");
        interestedAgent.Setup(a => a.EventInterests).Returns(new[] { nameof(CommandCompleted) });

        var uninterestedAgent = new Mock<IAgent>();
        uninterestedAgent.Setup(a => a.AgentType).Returns("TestAgent");
        uninterestedAgent.Setup(a => a.Id).Returns("uninterested");
        uninterestedAgent.Setup(a => a.EventInterests).Returns(new[] { nameof(CommandRejected) });

        _dispatcher.RegisterAgent(interestedAgent.Object);
        _dispatcher.RegisterAgent(uninterestedAgent.Object);

        var commandCompletedEvent = new CommandCompleted(
            Id: Guid.NewGuid(),
            Correlation: new Correlation(Guid.NewGuid(), Guid.NewGuid(), null, null),
            CommandId: Guid.NewGuid(),
            Outcome: CommandOutcome.Success);

        var sessionId = commandCompletedEvent.Correlation.SessionId;

        // Act
        await _dispatcher.DispatchEventAsync(commandCompletedEvent, sessionId);

        // Assert
        interestedAgent.Verify(a => a.HandleEventAsync(commandCompletedEvent), Times.Once);
        uninterestedAgent.Verify(a => a.HandleEventAsync(It.IsAny<IEvent>()), Times.Never);
    }

    [Fact]
    public async Task DispatchEventAsync_AllowsAllEvents_WhenAgentHasNoInterests()
    {
        // Arrange
        var agent = new Mock<IAgent>();
        agent.Setup(a => a.AgentType).Returns("TestAgent");
        agent.Setup(a => a.Id).Returns("agent1");
        agent.Setup(a => a.EventInterests).Returns((IReadOnlyCollection<string>?)null); // No interests declared

        _dispatcher.RegisterAgent(agent.Object);

        var commandCompletedEvent = new CommandCompleted(
            Id: Guid.NewGuid(),
            Correlation: new Correlation(Guid.NewGuid(), Guid.NewGuid(), null, null),
            CommandId: Guid.NewGuid(),
            Outcome: CommandOutcome.Success);

        var sessionId = commandCompletedEvent.Correlation.SessionId;

        // Act
        await _dispatcher.DispatchEventAsync(commandCompletedEvent, sessionId);

        // Assert
        agent.Verify(a => a.HandleEventAsync(commandCompletedEvent), Times.Once);
    }

    [Fact]
    public async Task DispatchEventAsync_FiltersBySession()
    {
        // Arrange
        var agent = new Mock<IAgent>();
        agent.Setup(a => a.AgentType).Returns("TestAgent");
        agent.Setup(a => a.Id).Returns("agent1");

        _dispatcher.RegisterAgent(agent.Object);

        var eventSessionId = Guid.NewGuid();
        var differentSessionId = Guid.NewGuid();

        var testEvent = new CommandCompleted(
            Id: Guid.NewGuid(),
            Correlation: new Correlation(eventSessionId, Guid.NewGuid(), null, null),
            CommandId: Guid.NewGuid(),
            Outcome: CommandOutcome.Success);

        // Act - dispatch with different session ID
        await _dispatcher.DispatchEventAsync(testEvent, differentSessionId);

        // Assert - agent should not receive event from different session
        agent.Verify(a => a.HandleEventAsync(It.IsAny<IEvent>()), Times.Never);
    }

    [Fact]
    public async Task DispatchEventAsync_RoutesCommandResponseToOriginatingAgent_WhenIssuerAgentIdIsSet()
    {
        // Arrange
        var originatingAgent = new Mock<IAgent>();
        originatingAgent.Setup(a => a.AgentType).Returns("TestAgent");
        originatingAgent.Setup(a => a.Id).Returns("originating-agent");

        var otherAgent = new Mock<IAgent>();
        otherAgent.Setup(a => a.AgentType).Returns("TestAgent");
        otherAgent.Setup(a => a.Id).Returns("other-agent");

        _dispatcher.RegisterAgent(originatingAgent.Object);
        _dispatcher.RegisterAgent(otherAgent.Object);

        var sessionId = Guid.NewGuid();
        var commandId = Guid.NewGuid();

        // Create a command response event with IssuerAgentId set to the originating agent
        var commandCompletedEvent = new CommandCompleted(
            Id: Guid.NewGuid(),
            Correlation: new Correlation(sessionId, commandId, null, null, "originating-agent"),
            CommandId: commandId,
            Outcome: CommandOutcome.Success);

        // Act
        await _dispatcher.DispatchEventAsync(commandCompletedEvent, sessionId);

        // Assert - only the originating agent should receive the event
        originatingAgent.Verify(a => a.HandleEventAsync(commandCompletedEvent), Times.Once);
        otherAgent.Verify(a => a.HandleEventAsync(It.IsAny<IEvent>()), Times.Never);
    }

    [Fact]
    public async Task DispatchEventAsync_BroadcastsToAllAgents_WhenIssuerAgentIdIsNull()
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

        var sessionId = Guid.NewGuid();
        var commandId = Guid.NewGuid();

        // Create a command response event with IssuerAgentId set to null (legacy behavior)
        var commandCompletedEvent = new CommandCompleted(
            Id: Guid.NewGuid(),
            Correlation: new Correlation(sessionId, commandId, null, null, null),
            CommandId: commandId,
            Outcome: CommandOutcome.Success);

        // Act
        await _dispatcher.DispatchEventAsync(commandCompletedEvent, sessionId);

        // Assert - all agents should receive the event (broadcast behavior)
        agent1.Verify(a => a.HandleEventAsync(commandCompletedEvent), Times.Once);
        agent2.Verify(a => a.HandleEventAsync(commandCompletedEvent), Times.Once);
    }
}