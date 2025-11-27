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

        // Handle query commands directly (no adapter needed)
        if (command is QueryBacklog queryBacklog)
        {
            await HandleQueryBacklog(queryBacklog, session);
            return;
        }

        if (command is QueryWorkItem queryWorkItem)
        {
            await HandleQueryWorkItem(queryWorkItem, session);
            return;
        }

        // Find adapter that can handle this command
        var adapter = _adapters.FirstOrDefault(a => a.CanHandle(command));
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

    private async Task HandleQueryBacklog(QueryBacklog command, SessionState session)
    {
        // Fake implementation: return some sample work items
        var items = new List<WorkItemSummary>
        {
            new WorkItemSummary("PROJ-123", "Implement user authentication", "Open", "developer1"),
            new WorkItemSummary("PROJ-124", "Add database migration", "In Progress", "developer2"),
            new WorkItemSummary("PROJ-125", "Fix UI bug in dashboard", "Open", null)
        };

        // Apply filter if provided (simple string contains)
        if (!string.IsNullOrEmpty(command.Filter))
        {
            items = items.Where(i => i.Title.Contains(command.Filter, StringComparison.OrdinalIgnoreCase) ||
                                   i.Id.Contains(command.Filter, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        var queriedEvent = new BacklogQueried(
            Guid.NewGuid(),
            command.Correlation,
            items);

        await session.AddEvent(queriedEvent);
    }

    private async Task HandleQueryWorkItem(QueryWorkItem command, SessionState session)
    {
        // Fake implementation: return details for the requested item
        var details = command.Item.Id switch
        {
            "PROJ-123" => new WorkItemDetails(
                "PROJ-123",
                "Implement user authentication",
                "Add JWT-based authentication system with login/logout endpoints",
                "Open",
                "developer1",
                new[] { "backend", "security" }),
            "PROJ-124" => new WorkItemDetails(
                "PROJ-124",
                "Add database migration",
                "Create migration scripts for the new user table schema",
                "In Progress",
                "developer2",
                new[] { "database", "migration" }),
            "PROJ-125" => new WorkItemDetails(
                "PROJ-125",
                "Fix UI bug in dashboard",
                "The dashboard chart is not displaying data correctly on mobile devices",
                "Open",
                null,
                new[] { "frontend", "bug" }),
            _ => new WorkItemDetails(
                command.Item.Id,
                $"Unknown item {command.Item.Id}",
                "This is a placeholder for unknown work items",
                "Unknown",
                null,
                Array.Empty<string>())
        };

        var queriedEvent = new WorkItemQueried(
            Guid.NewGuid(),
            command.Correlation,
            details);

        await session.AddEvent(queriedEvent);
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
