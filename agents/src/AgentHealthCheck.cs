using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace JuniorDev.Agents;

/// <summary>
/// Health check for the agent SDK components.
/// </summary>
public class AgentHealthCheck : IHealthCheck
{
    private readonly AgentEventDispatcher _dispatcher;
    private readonly AgentEventLoopService _eventLoop;
    private readonly ILogger<AgentHealthCheck> _logger;

    public AgentHealthCheck(
        AgentEventDispatcher dispatcher,
        AgentEventLoopService eventLoop,
        ILogger<AgentHealthCheck> logger)
    {
        _dispatcher = dispatcher;
        _eventLoop = eventLoop;
        _logger = logger;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Check if event loop is running
            var eventLoopHealthy = _eventLoop.IsRunning;

            // Check if dispatcher has agents registered
            var hasAgents = _dispatcher.GetRegisteredAgentCount() > 0;

            var status = eventLoopHealthy && hasAgents ? HealthStatus.Healthy : HealthStatus.Degraded;

            var data = new Dictionary<string, object>
            {
                { "event_loop_running", eventLoopHealthy },
                { "agents_registered", _dispatcher.GetRegisteredAgentCount() },
                { "timestamp", DateTimeOffset.UtcNow }
            };

            var description = eventLoopHealthy && hasAgents
                ? "Agent SDK is healthy"
                : $"Agent SDK degraded: EventLoop={eventLoopHealthy}, Agents={hasAgents}";

            _logger.LogDebug("Agent health check: {Description}", description);

            return Task.FromResult(new HealthCheckResult(status, description, data: data));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Agent health check failed");
            return Task.FromResult(HealthCheckResult.Unhealthy("Agent health check failed", ex));
        }
    }
}