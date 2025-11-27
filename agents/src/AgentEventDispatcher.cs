using System.Collections.Concurrent;
using JuniorDev.Contracts;
using Microsoft.Extensions.Logging;

namespace JuniorDev.Agents;

/// <summary>
/// Central dispatcher for routing events to registered agents.
/// </summary>
public class AgentEventDispatcher
{
    private readonly ConcurrentDictionary<string, List<IAgent>> _agentsByType = new();
    private readonly ILogger<AgentEventDispatcher> _logger;

    public AgentEventDispatcher(ILogger<AgentEventDispatcher> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Registers an agent for event dispatching.
    /// </summary>
    public void RegisterAgent(IAgent agent)
    {
        if (agent == null)
        {
            throw new ArgumentNullException(nameof(agent));
        }

        var agents = _agentsByType.GetOrAdd(agent.AgentType, _ => new List<IAgent>());
        agents.Add(agent);

        _logger.LogInformation("Registered agent {AgentType} ({AgentId})", agent.AgentType, agent.Id);
    }

    /// <summary>
    /// Unregisters an agent from event dispatching.
    /// </summary>
    public void UnregisterAgent(IAgent agent)
    {
        if (agent == null)
        {
            throw new ArgumentNullException(nameof(agent));
        }

        if (_agentsByType.TryGetValue(agent.AgentType, out var agents))
        {
            agents.Remove(agent);
            _logger.LogInformation("Unregistered agent {AgentType} ({AgentId})", agent.AgentType, agent.Id);
        }
    }

    /// <summary>
    /// Dispatches an event to all registered agents.
    /// </summary>
    public async Task DispatchEventAsync(IEvent @event)
    {
        await DispatchEventAsync(@event, null);
    }

    /// <summary>
    /// Dispatches an event to agents filtered by session ID.
    /// </summary>
    public async Task DispatchEventAsync(IEvent @event, Guid? sessionId)
    {
        if (@event == null)
        {
            throw new ArgumentNullException(nameof(@event));
        }

        _logger.LogDebug("Dispatching event {EventType} ({EventId}) to agents", @event.Kind, @event.Id);

        var tasks = new List<Task>();

        foreach (var agents in _agentsByType.Values)
        {
            foreach (var agent in agents)
            {
                // Filter by session if specified
                if (sessionId.HasValue && !ShouldAgentReceiveEvent(agent, @event, sessionId.Value))
                {
                    continue;
                }

                tasks.Add(DispatchToAgentAsync(agent, @event));
            }
        }

        await Task.WhenAll(tasks);
    }

    private async Task DispatchToAgentAsync(IAgent agent, IEvent @event)
    {
        try
        {
            await agent.HandleEventAsync(@event);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Agent {AgentType} ({AgentId}) failed to handle event {EventType} ({EventId})",
                agent.AgentType, agent.Id, @event.Kind, @event.Id);
        }
    }

    /// <summary>
    /// Determines if an agent should receive a specific event based on advanced filtering.
    /// </summary>
    private bool ShouldAgentReceiveEvent(IAgent agent, IEvent @event, Guid sessionId)
    {
        // 1. Session filtering - event must belong to the session
        if (@event.Correlation.SessionId != sessionId)
        {
            return false;
        }

        // 2. Event type filtering - check if agent has declared specific interests
        if (agent.EventInterests != null && agent.EventInterests.Any())
        {
            if (!agent.EventInterests.Contains(@event.Kind))
            {
                return false;
            }
        }

        // 3. Correlation ID matching for command responses
        // Response events (CommandCompleted, CommandRejected, etc.) should only go to the agent that issued the command
        if (@event.Correlation.CommandId.HasValue && @event.Correlation.IssuerAgentId != null)
        {
            // Only the originating agent should receive command response events
            if (@event.Correlation.IssuerAgentId != agent.Id)
            {
                return false;
            }
        }

        // 4. Agent-specific filtering could be added here in the future
        // e.g., based on agent type, work item assignments, etc.

        return true;
    }

    /// <summary>
    /// Gets all registered agents.
    /// </summary>
    public IEnumerable<IAgent> GetRegisteredAgents()
    {
        return _agentsByType.Values.SelectMany(agents => agents);
    }

    /// <summary>
    /// Gets agents of a specific type.
    /// </summary>
    public IEnumerable<IAgent> GetAgentsByType(string agentType)
    {
        return _agentsByType.TryGetValue(agentType, out var agents) ? agents : Enumerable.Empty<IAgent>();
    }

    /// <summary>
    /// Gets the total count of registered agents.
    /// </summary>
    public int GetRegisteredAgentCount()
    {
        return _agentsByType.Values.Sum(agents => agents.Count);
    }
}
