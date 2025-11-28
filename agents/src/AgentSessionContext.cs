using JuniorDev.Contracts;
using JuniorDev.Orchestrator;
using Microsoft.Extensions.Logging;

namespace JuniorDev.Agents;

/// <summary>
/// Context provided to agents when they start, containing session information and services.
/// </summary>
public class AgentSessionContext
{
    /// <summary>
    /// The unique session identifier.
    /// </summary>
    public Guid SessionId { get; }

    /// <summary>
    /// The session configuration.
    /// </summary>
    public SessionConfig Config { get; }

    /// <summary>
    /// The session manager for publishing commands and subscribing to events.
    /// </summary>
    public ISessionManager SessionManager { get; }

    /// <summary>
    /// Agent-specific configuration.
    /// </summary>
    public AgentConfig AgentConfig { get; }

    /// <summary>
    /// Logger for this agent instance.
    /// </summary>
    public ILogger Logger { get; }

    /// <summary>
    /// The ID of the agent this context belongs to.
    /// </summary>
    public string AgentId { get; }

    public AgentSessionContext(
        Guid sessionId,
        SessionConfig config,
        ISessionManager sessionManager,
        AgentConfig agentConfig,
        ILogger logger,
        string agentId)
    {
        SessionId = sessionId;
        Config = config ?? throw new ArgumentNullException(nameof(config));
        SessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        AgentConfig = agentConfig ?? throw new ArgumentNullException(nameof(agentConfig));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        AgentId = agentId ?? throw new ArgumentNullException(nameof(agentId));
    }

    /// <summary>
    /// Creates a child correlation for multi-step operations.
    /// </summary>
    /// <param name="parentCommandId">The parent command ID, if any.</param>
    /// <param name="planNodeId">The plan node ID, if any.</param>
    public Correlation CreateCorrelation(Guid? parentCommandId = null, string? planNodeId = null)
    {
        return new Correlation(SessionId, null, parentCommandId, planNodeId, AgentId);
    }

    /// <summary>
    /// Creates a correlation for a specific command.
    /// </summary>
    /// <param name="commandId">The command ID.</param>
    /// <param name="parentCommandId">The parent command ID, if any.</param>
    /// <param name="planNodeId">The plan node ID, if any.</param>
    public Correlation CreateCorrelationForCommand(Guid commandId, Guid? parentCommandId = null, string? planNodeId = null)
    {
        return new Correlation(SessionId, commandId, parentCommandId, planNodeId, AgentId);
    }
}
