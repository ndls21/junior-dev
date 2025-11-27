using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using JuniorDev.Contracts;
using JuniorDev.Orchestrator;
using Microsoft.Extensions.Logging;

namespace JuniorDev.Agents;

/// <summary>
/// Base class for agents providing common functionality.
/// </summary>
public abstract class AgentBase : IAgent
{
    private readonly ConcurrentDictionary<string, AgentMetric> _metrics = new();
    private readonly Meter _meter;
    private readonly Histogram<double> _commandLatency;
    private readonly Counter<long> _commandsIssued;
    private readonly Counter<long> _commandsSucceeded;
    private readonly Counter<long> _commandsFailed;
    private readonly Counter<long> _eventsProcessed;

    protected AgentSessionContext? Context { get; private set; }
    protected ILogger Logger => Context?.Logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
    protected ISessionManager SessionManager => Context?.SessionManager ?? throw new InvalidOperationException("Agent not started");
    protected AgentConfig Config => Context?.AgentConfig ?? throw new InvalidOperationException("Agent not started");

    public string Id { get; } = Guid.NewGuid().ToString();
    public abstract string AgentType { get; }

    protected AgentBase()
    {
        _meter = new Meter($"JuniorDev.Agents.{AgentType}", "1.0.0");
        _commandLatency = _meter.CreateHistogram<double>("command_latency_ms", "ms", "Time taken to execute commands");
        _commandsIssued = _meter.CreateCounter<long>("commands_issued", "commands", "Number of commands issued");
        _commandsSucceeded = _meter.CreateCounter<long>("commands_succeeded", "commands", "Number of commands that succeeded");
        _commandsFailed = _meter.CreateCounter<long>("commands_failed", "commands", "Number of commands that failed");
        _eventsProcessed = _meter.CreateCounter<long>("events_processed", "events", "Number of events processed");
    }

    public async Task StartAsync(AgentSessionContext context)
    {
        Context = context ?? throw new ArgumentNullException(nameof(context));

        Logger.LogInformation("Starting agent {AgentType} ({AgentId}) for session {SessionId}",
            AgentType, Id, context.SessionId);

        await OnStartedAsync();
    }

    public async Task StopAsync()
    {
        if (Context == null)
        {
            return;
        }

        Logger.LogInformation("Stopping agent {AgentType} ({AgentId})", AgentType, Id);

        await OnStoppedAsync();
        Context = null;
    }

    public async Task HandleEventAsync(IEvent @event)
    {
        if (Context == null)
        {
            throw new InvalidOperationException("Agent not started");
        }

        if (Config.EnableMetrics)
        {
            _eventsProcessed.Add(1, new KeyValuePair<string, object?>("event_type", @event.Kind));
        }

        if (Config.EnableDetailedLogging)
        {
            Logger.LogDebug("Processing event {EventType} ({EventId})", @event.Kind, @event.Id);
        }

        await OnEventAsync(@event);
    }

    /// <summary>
    /// Publishes a command to the orchestrator and tracks metrics.
    /// </summary>
    protected async Task PublishCommandAsync(ICommand command)
    {
        await PublishCommandWithRetryAsync(command, Config.MaxRetryAttempts);
    }

    /// <summary>
    /// Publishes a command with retry logic and backoff.
    /// </summary>
    protected async Task PublishCommandWithRetryAsync(ICommand command, int maxRetries)
    {
        if (Context == null)
        {
            throw new InvalidOperationException("Agent not started");
        }

        var attempt = 0;
        var delay = Config.RetryBaseDelayMs;

        while (true)
        {
            attempt++;
            var stopwatch = Stopwatch.StartNew();

            try
            {
                if (Config.EnableDetailedLogging)
                {
                    Logger.LogInformation("Issuing command {CommandType} ({CommandId}) - attempt {Attempt}/{MaxAttempts}",
                        command.Kind, command.Id, attempt, maxRetries + 1);
                }

                if (Config.EnableMetrics)
                {
                    _commandsIssued.Add(1, new KeyValuePair<string, object?>("command_type", command.Kind));
                }

                await SessionManager.PublishCommand(command);

                if (Config.EnableMetrics)
                {
                    _commandsSucceeded.Add(1, new KeyValuePair<string, object?>("command_type", command.Kind));
                }

                break; // Success, exit retry loop
            }
            catch (Exception ex)
            {
                if (Config.EnableMetrics)
                {
                    _commandsFailed.Add(1, new KeyValuePair<string, object?>("command_type", command.Kind));
                }

                if (attempt > maxRetries)
                {
                    Logger.LogError(ex, "Failed to publish command {CommandType} ({CommandId}) after {Attempts} attempts",
                        command.Kind, command.Id, attempt);
                    throw;
                }

                Logger.LogWarning(ex, "Command {CommandType} ({CommandId}) failed on attempt {Attempt}, retrying in {Delay}ms",
                    command.Kind, command.Id, attempt, delay);

                await Task.Delay(delay);

                // Exponential backoff with jitter
                delay = (int)(delay * 1.5) + Random.Shared.Next(0, 100);
            }
            finally
            {
                stopwatch.Stop();
                if (Config.EnableMetrics)
                {
                    _commandLatency.Record(stopwatch.ElapsedMilliseconds, new KeyValuePair<string, object?>("command_type", command.Kind));
                }
            }
        }
    }

    /// <summary>
    /// Called when the agent starts. Override to perform initialization.
    /// </summary>
    protected virtual Task OnStartedAsync() => Task.CompletedTask;

    /// <summary>
    /// Called when the agent stops. Override to perform cleanup.
    /// </summary>
    protected virtual Task OnStoppedAsync() => Task.CompletedTask;

    /// <summary>
    /// Called to handle events. Override to implement event processing logic.
    /// </summary>
    protected abstract Task OnEventAsync(IEvent @event);

    private class AgentMetric
    {
        public long Count { get; set; }
        public TimeSpan TotalDuration { get; set; }
    }
}