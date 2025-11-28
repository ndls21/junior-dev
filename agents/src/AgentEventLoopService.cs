using System.Collections.Concurrent;
using JuniorDev.Contracts;
using JuniorDev.Orchestrator;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JuniorDev.Agents;

/// <summary>
/// Background service that subscribes to orchestrator events and dispatches them to agents.
/// </summary>
public class AgentEventLoopService : BackgroundService
{
    private readonly ISessionManager _sessionManager;
    private readonly AgentEventDispatcher _dispatcher;
    private readonly ILogger<AgentEventLoopService> _logger;
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _activeSubscriptions = new();

    private volatile bool _isRunning;

    public AgentEventLoopService(
        ISessionManager sessionManager,
        AgentEventDispatcher dispatcher,
        ILogger<AgentEventLoopService> logger)
    {
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Gets whether the event loop is currently running.
    /// </summary>
    public bool IsRunning => _isRunning;

    /// <summary>
    /// Starts event subscription for a session.
    /// </summary>
    public void StartSessionSubscription(Guid sessionId)
    {
        var cts = new CancellationTokenSource();
        if (_activeSubscriptions.TryAdd(sessionId, cts))
        {
            _logger.LogInformation("Starting event subscription for session {SessionId}", sessionId);
            // The actual subscription will be handled in ExecuteAsync
        }
    }

    /// <summary>
    /// Stops event subscription for a session.
    /// </summary>
    public void StopSessionSubscription(Guid sessionId)
    {
        if (_activeSubscriptions.TryRemove(sessionId, out var cts))
        {
            cts.Cancel();
            _logger.LogInformation("Stopped event subscription for session {SessionId}", sessionId);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Agent event loop service started");
        _isRunning = true;

        try
        {
            // Process existing subscriptions
            var subscriptionTasks = new List<Task>();

            foreach (var kvp in _activeSubscriptions)
            {
                subscriptionTasks.Add(ProcessSessionEventsAsync(kvp.Key, kvp.Value.Token));
            }

            // Wait for all subscriptions to complete or service to stop
            await Task.WhenAll(subscriptionTasks);
        }
        finally
        {
            _isRunning = false;
            _logger.LogInformation("Agent event loop service stopped");
        }
    }

    private async Task ProcessSessionEventsAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var @event in _sessionManager.Subscribe(sessionId).WithCancellation(cancellationToken))
            {
                try
                {
                    // Filter events for this session and dispatch to appropriate agents
                    await _dispatcher.DispatchEventAsync(@event, sessionId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to dispatch event {EventType} ({EventId}) for session {SessionId}",
                        @event.Kind, @event.Id, sessionId);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Event subscription cancelled for session {SessionId}", sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Event subscription failed for session {SessionId}", sessionId);
        }
    }
}
