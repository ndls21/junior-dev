using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Channels;
using System.Threading.Tasks;
using JuniorDev.Contracts;

namespace JuniorDev.Orchestrator;

public class SessionManager : ISessionManager
{
    private readonly ConcurrentDictionary<Guid, SessionState> _sessions = new();
    private readonly IReadOnlyList<IAdapter> _adapters;
    private readonly IPolicyEnforcer _policyEnforcer;
    private readonly IRateLimiter _rateLimiter;
    private readonly IWorkspaceProvider _workspaceProvider;
    private readonly IArtifactStore _artifactStore;

    public SessionManager(
        IEnumerable<IAdapter> adapters,
        IPolicyEnforcer policyEnforcer,
        IRateLimiter rateLimiter,
        IWorkspaceProvider workspaceProvider,
        IArtifactStore artifactStore)
    {
        _adapters = adapters.ToList();
        _policyEnforcer = policyEnforcer;
        _rateLimiter = rateLimiter;
        _workspaceProvider = workspaceProvider;
        _artifactStore = artifactStore;
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

            await session.AddEvent(throttledEvent);
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

    private bool RequiresApproval(ICommand command, SessionConfig config)
    {
        // Stub: require approval for Push if policy says so
        return command is Push && config.Policy.RequireApprovalForPush;
    }

    public async Task PauseSession(Guid sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            throw new InvalidOperationException($"Session {sessionId} not found");
        }

        if (session.Status != SessionStatus.Running)
        {
            throw new InvalidOperationException($"Cannot pause session in status {session.Status}");
        }

        await session.SetStatus(SessionStatus.Paused, "Session paused");
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
    }

    public async Task AbortSession(Guid sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            throw new InvalidOperationException($"Session {sessionId} not found");
        }

        if (session.Status == SessionStatus.Completed || session.Status == SessionStatus.Error)
        {
            throw new InvalidOperationException($"Session {sessionId} is already in terminal state");
        }

        await session.SetStatus(SessionStatus.Error, "Session aborted");
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

    // For testing: complete the event channel for a session
    public void CompleteSession(Guid sessionId)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            session.Complete();
        }
    }
}
