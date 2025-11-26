using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using JuniorDev.Contracts;

namespace JuniorDev.Orchestrator;

public interface ISessionManager
{
    Task CreateSession(SessionConfig config);
    Task PublishCommand(ICommand command);
    IAsyncEnumerable<IEvent> Subscribe(Guid sessionId);
}