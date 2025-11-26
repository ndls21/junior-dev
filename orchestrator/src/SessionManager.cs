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

    public SessionManager(IEnumerable<IAdapter> adapters)
    {
        _adapters = adapters.ToList();
    }

    public async Task CreateSession(SessionConfig config)
    {
        var sessionState = new SessionState(config);
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
        if (!_sessions.TryGetValue(command.Correlation.SessionId, out var session))
        {
            throw new InvalidOperationException($"Session {command.Correlation.SessionId} not found");
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

    public IAsyncEnumerable<IEvent> Subscribe(Guid sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            throw new InvalidOperationException($"Session {sessionId} not found");
        }

        return session.GetEvents();
    }


}