using System.Threading.Tasks;
using JuniorDev.Contracts;

namespace JuniorDev.Agents;

/// <summary>
/// Core interface that all agents must implement.
/// Agents are responsible for processing events and issuing commands within a session.
/// </summary>
public interface IAgent
{
    /// <summary>
    /// Gets the unique identifier for this agent instance.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Gets the type/name of this agent (e.g., "executor", "planner", "reviewer").
    /// </summary>
    string AgentType { get; }

    /// <summary>
    /// Starts the agent with the given session context.
    /// This is called when the agent is assigned to a session.
    /// </summary>
    /// <param name="context">The session context containing session ID, config, and orchestrator reference.</param>
    Task StartAsync(AgentSessionContext context);

    /// <summary>
    /// Stops the agent gracefully.
    /// This is called when the session ends or the agent is removed.
    /// </summary>
    Task StopAsync();

    /// <summary>
    /// Handles an event from the orchestrator.
    /// Agents should process relevant events and may issue commands in response.
    /// </summary>
    /// <param name="event">The event to handle.</param>
    Task HandleEventAsync(IEvent @event);
}