using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using JuniorDev.Contracts;

namespace JuniorDev.Orchestrator;

public interface ISessionManager
{
    Task CreateSession(SessionConfig config);
    Task PublishCommand(ICommand command);
    Task PublishEvent(IEvent @event);
    IAsyncEnumerable<IEvent> Subscribe(Guid sessionId);
    Task PauseSession(Guid sessionId, string actor = "system");
    Task ResumeSession(Guid sessionId);
    Task AbortSession(Guid sessionId, string actor = "system");
    Task ApproveSession(Guid sessionId);
    Task CompleteSession(Guid sessionId);
    IReadOnlyList<SessionInfo> GetActiveSessions();
    SessionConfig? GetSessionConfig(Guid sessionId);
}
