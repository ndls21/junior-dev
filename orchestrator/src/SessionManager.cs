using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Channels;
using System.Threading.Tasks;
using JuniorDev.Contracts;
using System.Diagnostics.Metrics;

namespace JuniorDev.Orchestrator;

public class SessionManager : ISessionManager, IDisposable
{
    private readonly ConcurrentDictionary<Guid, SessionState> _sessions = new();
    private readonly IReadOnlyList<IAdapter> _adapters;
    private readonly IPolicyEnforcer _policyEnforcer;
    private readonly IRateLimiter _rateLimiter;
    private readonly IWorkspaceProvider _workspaceProvider;
    private readonly IArtifactStore _artifactStore;
    private readonly ClaimManager _claimManager;
    private readonly WorkItemConfig _workItemConfig;
    private readonly System.Threading.Timer _cleanupTimer;
    private bool _disposed;

    // Metrics for session management operations
    private readonly Meter _meter;
    private readonly Counter<long> _sessionsCreated;
    private readonly Counter<long> _sessionsPaused;
    private readonly Counter<long> _sessionsResumed;
    private readonly Counter<long> _sessionsAborted;
    private readonly Counter<long> _sessionsCompleted;
    private readonly Counter<long> _commandsRejectedForPausedSession;
    private readonly Counter<long> _sessionsAutoPaused;

    public SessionManager(
        IEnumerable<IAdapter> adapters,
        IPolicyEnforcer policyEnforcer,
        IRateLimiter rateLimiter,
        IWorkspaceProvider workspaceProvider,
        IArtifactStore artifactStore,
        WorkItemConfig? workItemConfig = null)
    {
        _adapters = adapters.ToList();
        _policyEnforcer = policyEnforcer;
        _rateLimiter = rateLimiter;
        _workspaceProvider = workspaceProvider;
        _artifactStore = artifactStore;
        _workItemConfig = workItemConfig ?? new WorkItemConfig();
        _claimManager = new ClaimManager(_workItemConfig);

        // Initialize metrics
        _meter = new Meter("JuniorDev.Orchestrator.SessionManager", "1.0.0");
        _sessionsCreated = _meter.CreateCounter<long>("sessions_created", "sessions", "Number of sessions created");
        _sessionsPaused = _meter.CreateCounter<long>("sessions_paused", "sessions", "Number of sessions paused");
        _sessionsResumed = _meter.CreateCounter<long>("sessions_resumed", "sessions", "Number of sessions resumed");
        _sessionsAborted = _meter.CreateCounter<long>("sessions_aborted", "sessions", "Number of sessions aborted");
        _sessionsCompleted = _meter.CreateCounter<long>("sessions_completed", "sessions", "Number of sessions completed");
        _commandsRejectedForPausedSession = _meter.CreateCounter<long>("commands_rejected_paused_session", "commands", "Number of commands rejected due to paused session");
        _sessionsAutoPaused = _meter.CreateCounter<long>("sessions_auto_paused", "sessions", "Number of sessions automatically paused due to error threshold");

        // Start background cleanup timer
        _cleanupTimer = new System.Threading.Timer(
            CleanupExpiredClaimsCallback,
            null,
            _workItemConfig.CleanupInterval,
            _workItemConfig.CleanupInterval);
    }

    public async Task CreateSession(SessionConfig config)
    {
        var workspacePath = await _workspaceProvider.GetWorkspacePath(config);
        var sessionState = new SessionState(config, workspacePath);
        if (!_sessions.TryAdd(config.SessionId, sessionState))
        {
            throw new InvalidOperationException($"Session {config.SessionId} already exists");
        }

        var statusEvent = new SessionStatusChanged(
            Guid.NewGuid(),
            new Correlation(config.SessionId),
            SessionStatus.Running,
            "Session created");

        await sessionState.AddEvent(statusEvent);

        // Record session creation metric
        _sessionsCreated.Add(1);
    }

    public async Task PublishCommand(ICommand command)
    {
        await ProcessCommand(command, bypassApproval: false);
    }

    public async Task PublishEvent(IEvent @event)
    {
        if (!_sessions.TryGetValue(@event.Correlation.SessionId, out var session))
        {
            throw new InvalidOperationException($"Session {@event.Correlation.SessionId} not found");
        }

        await session.AddEvent(@event);

        // Track errors and auto-pause if threshold exceeded
        if (@event is CommandCompleted completed)
        {
            if (completed.Outcome == CommandOutcome.Failure)
            {
                session.IncrementErrorCount();
                
                // Check if we've hit the error threshold
                var threshold = session.Config.Policy.AutoPauseErrorThreshold;
                if (threshold > 0 && session.ConsecutiveErrors >= threshold && session.Status == SessionStatus.Running)
                {
                    // Auto-pause the session
                    await AutoPauseSession(session, completed.CommandId);
                }
            }
            else if (completed.Outcome == CommandOutcome.Success)
            {
                // Reset error count on successful command
                session.ResetErrorCount();
            }
        }
    }

    private async Task ProcessCommand(ICommand command, bool bypassApproval)
    {
        if (!_sessions.TryGetValue(command.Correlation.SessionId, out var session))
        {
            throw new InvalidOperationException($"Session {command.Correlation.SessionId} not found");
        }

        // Check session status
        if (session.Status == SessionStatus.Paused)
        {
            var rejectedEvent = new CommandRejected(
                Guid.NewGuid(),
                command.Correlation,
                command.Id,
                "Session is paused");

            await session.AddEvent(rejectedEvent);
            
            // Record metric for rejected command due to paused session
            _commandsRejectedForPausedSession.Add(1);
            return;
        }

        if (session.Status == SessionStatus.Completed || session.Status == SessionStatus.Error)
        {
            var rejectedEvent = new CommandRejected(
                Guid.NewGuid(),
                command.Correlation,
                command.Id,
                $"Session is in terminal state: {session.Status}");

            await session.AddEvent(rejectedEvent);
            return;
        }

        // Check approval for commands requiring it
        if (!bypassApproval && RequiresApproval(command, session.Config) && !session.IsApproved)
        {
            // Queue command and move session to NeedsApproval
            session.EnqueuePending(command);
            if (session.Status != SessionStatus.NeedsApproval)
            {
                await session.SetStatus(SessionStatus.NeedsApproval, "Session requires approval");
            }

            return;
        }

        // Check policy
        var policyResult = _policyEnforcer.CheckPolicy(command, session.Config);
        if (!policyResult.Allowed)
        {
            var rejectedEvent = new CommandRejected(
                Guid.NewGuid(),
                command.Correlation,
                command.Id,
                "Policy violation",
                policyResult.Rule);

            await session.AddEvent(rejectedEvent);
            return;
        }

        // Check rate limit
        var rateResult = await _rateLimiter.CheckRateLimit(command, session.Config);
        if (!rateResult.Allowed)
        {
            var throttledEvent = new Throttled(
                Guid.NewGuid(),
                command.Correlation,
                "Rate limit exceeded",
                rateResult.RetryAfter ?? DateTimeOffset.UtcNow.AddMinutes(1));

            // Log throttling event
            Console.WriteLine($"[RATE LIMIT] Command {command.Kind} throttled for session {command.Correlation.SessionId}, retry after: {throttledEvent.RetryAfter}");

            await session.AddEvent(throttledEvent);
            return;
        }

        // Handle claim commands directly
        var claimResult = await HandleClaimCommand(command, session);
        if (claimResult != null)
        {
            await session.AddEvent(claimResult);
            return;
        }

        // Find adapter that can handle this command
        // Prefer real adapters over fake ones
        var adapter = _adapters.Where(a => a.CanHandle(command))
                              .OrderByDescending(a => !(a is FakeAdapter))
                              .FirstOrDefault();
        if (adapter == null)
        {
            var rejectedEvent = new CommandRejected(
                Guid.NewGuid(),
                command.Correlation,
                command.Id,
                "No adapter found for command");

            await session.AddEvent(rejectedEvent);
            return;
        }

        // Publish to adapter
        await adapter.HandleCommand(command, session);
    }

    private async Task<IEvent?> HandleClaimCommand(ICommand command, SessionState session)
    {
        switch (command)
        {
            case ClaimWorkItem claimCmd:
                return await HandleClaimWorkItem(claimCmd, session);
            case ReleaseWorkItem releaseCmd:
                return await HandleReleaseWorkItem(releaseCmd, session);
            case RenewClaim renewCmd:
                return await HandleRenewClaim(renewCmd, session);
            default:
                return null;
        }
    }

    private Task<IEvent> HandleClaimWorkItem(ClaimWorkItem command, SessionState session)
    {
        var result = _claimManager.TryClaimWorkItem(
            command.Item,
            command.Assignee,
            command.Correlation.SessionId,
            out var claim,
            command.ClaimTimeout);

        if (result == ClaimResult.Success && claim != null)
        {
            return Task.FromResult<IEvent>(new WorkItemClaimed(
                Guid.NewGuid(),
                command.Correlation,
                command.Item,
                command.Assignee,
                claim.ExpiresAt));
        }
        else
        {
            return Task.FromResult<IEvent>(new CommandRejected(
                Guid.NewGuid(),
                command.Correlation,
                command.Id,
                $"Claim failed: {result}",
                "ClaimPolicy"));
        }
    }

    private Task<IEvent> HandleReleaseWorkItem(ReleaseWorkItem command, SessionState session)
    {
        // Get assignee from correlation's issuer agent ID
        var assignee = command.Correlation.IssuerAgentId ?? "unknown";
        
        var result = _claimManager.ReleaseWorkItem(command.Item.Id, assignee);

        if (result == ClaimResult.Success)
        {
            return Task.FromResult<IEvent>(new WorkItemClaimReleased(
                Guid.NewGuid(),
                command.Correlation,
                command.Item,
                command.Reason));
        }
        else
        {
            return Task.FromResult<IEvent>(new CommandRejected(
                Guid.NewGuid(),
                command.Correlation,
                command.Id,
                $"Release failed: {result}",
                "ClaimPolicy"));
        }
    }

    private Task<IEvent> HandleRenewClaim(RenewClaim command, SessionState session)
    {
        // Get assignee from correlation's issuer agent ID
        var assignee = command.Correlation.IssuerAgentId ?? "unknown";
        
        var result = _claimManager.RenewClaim(
            command.Item.Id,
            assignee,
            command.Extension);

        if (result == ClaimResult.Success)
        {
            // Get the updated claim to get the new expiration time
            var claims = _claimManager.GetClaimsForAssignee(assignee);
            var claim = claims.FirstOrDefault(c => c.WorkItem.Id == command.Item.Id);

            if (claim != null)
            {
                return Task.FromResult<IEvent>(new ClaimRenewed(
                    Guid.NewGuid(),
                    command.Correlation,
                    command.Item,
                    claim.ExpiresAt));
            }
        }

        return Task.FromResult<IEvent>(new CommandRejected(
            Guid.NewGuid(),
            command.Correlation,
            command.Id,
            $"Renewal failed: {result}",
            "ClaimPolicy"));
    }

    private bool RequiresApproval(ICommand command, SessionConfig config)
    {
        // Stub: require approval for Push if policy says so
        return command is Push && config.Policy.RequireApprovalForPush;
    }

    private async Task AutoPauseSession(SessionState session, Guid triggeringCommandId)
    {
        var threshold = session.Config.Policy.AutoPauseErrorThreshold;
        var reason = $"Auto-paused: {session.ConsecutiveErrors} consecutive errors (threshold: {threshold})";
        
        var pauseEvent = new SessionPaused(
            Guid.NewGuid(),
            new Correlation(session.Config.SessionId),
            "system-auto-pause",
            reason,
            triggeringCommandId);

        await session.AddEvent(pauseEvent);
        await session.SetStatus(SessionStatus.Paused, reason);
        
        // Record metrics
        _sessionsPaused.Add(1);
        _sessionsAutoPaused.Add(1);
        
        // Log for auditability
        Console.WriteLine($"[AUTO-PAUSE] Session {session.Config.SessionId} auto-paused after {session.ConsecutiveErrors} consecutive errors. Command: {triggeringCommandId}");
    }

    public async Task PauseSession(Guid sessionId, string actor = "system")
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            throw new InvalidOperationException($"Session {sessionId} not found");
        }

        if (session.Status != SessionStatus.Running)
        {
            throw new InvalidOperationException($"Cannot pause session in status {session.Status}");
        }

        var pauseEvent = new SessionPaused(
            Guid.NewGuid(),
            new Correlation(sessionId),
            actor,
            "Session paused by user/system");

        await session.AddEvent(pauseEvent);

        await session.SetStatus(SessionStatus.Paused, "Session paused");
        
        // Record session pause metric
        _sessionsPaused.Add(1);
    }

    public async Task ResumeSession(Guid sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            throw new InvalidOperationException($"Session {sessionId} not found");
        }

        if (session.Status != SessionStatus.Paused)
        {
            throw new InvalidOperationException($"Cannot resume session in status {session.Status}");
        }

        await session.SetStatus(SessionStatus.Running, "Session resumed");
        
        // Record session resume metric
        _sessionsResumed.Add(1);
    }

    public async Task AbortSession(Guid sessionId, string actor = "system")
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            throw new InvalidOperationException($"Session {sessionId} not found");
        }

        if (session.Status == SessionStatus.Completed || session.Status == SessionStatus.Error)
        {
            throw new InvalidOperationException($"Session {sessionId} is already in terminal state");
        }

        var abortEvent = new SessionAborted(
            Guid.NewGuid(),
            new Correlation(sessionId),
            actor,
            "Session aborted by user/system");

        await session.AddEvent(abortEvent);

        await session.SetStatus(SessionStatus.Error, "Session aborted");
        
        // Record session abort metric
        _sessionsAborted.Add(1);
    }

    public Task ApproveSession(Guid sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            throw new InvalidOperationException($"Session {sessionId} not found");
        }

        session.SetApproved(true);
        // If session was waiting on approval, move back to running and process queued commands
        if (session.Status == SessionStatus.NeedsApproval)
        {
            return ProcessPending(session);
        }

        return Task.CompletedTask;
    }

    public async Task CompleteSession(Guid sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            throw new InvalidOperationException($"Session {sessionId} not found");
        }

        if (session.Status == SessionStatus.Completed || session.Status == SessionStatus.Error)
        {
            throw new InvalidOperationException($"Session {sessionId} is already in terminal state");
        }

        await session.SetStatus(SessionStatus.Completed, "Session completed");
        
        // Record session completion metric
        _sessionsCompleted.Add(1);
    }

    private async Task ProcessPending(SessionState session)
    {
        await session.SetStatus(SessionStatus.Running, "Session approved");

        while (session.TryDequeuePending(out var pending) && pending != null)
        {
            await ProcessCommand(pending, bypassApproval: true);
        }
    }

    public IAsyncEnumerable<IEvent> Subscribe(Guid sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            throw new InvalidOperationException($"Session {sessionId} not found");
        }

        return session.GetEvents();
    }

    public IReadOnlyList<SessionInfo> GetActiveSessions()
    {
        return _sessions.Values.Select(s => new SessionInfo(
            s.Config.SessionId,
            s.Status,
            s.Config.AgentProfile,
            s.Config.Repo.Name,
            s.CreatedAt,
            s.CurrentTask
        )).ToList();
    }

    public SessionConfig? GetSessionConfig(Guid sessionId)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            return session.Config;
        }
        return null;
    }

    private void CleanupExpiredClaimsCallback(object? state)
    {
        try
        {
            var expiredClaims = _claimManager.CleanupExpiredClaims();

            // Publish expiration events for each expired claim
            foreach (var expiredClaim in expiredClaims)
            {
                var correlation = new Correlation(expiredClaim.SessionId);
                var expiredEvent = new ClaimExpired(
                    Guid.NewGuid(),
                    correlation,
                    expiredClaim.WorkItem,
                    expiredClaim.Assignee);

                // Try to publish to the session if it still exists
                if (_sessions.TryGetValue(expiredClaim.SessionId, out var session))
                {
                    _ = session.AddEvent(expiredEvent);
                }
            }
        }
        catch (Exception ex)
        {
            // Log error but continue the loop
            Console.Error.WriteLine($"Error in claim cleanup: {ex.Message}");
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _cleanupTimer?.Dispose();
            }
            _disposed = true;
        }
    }
}
