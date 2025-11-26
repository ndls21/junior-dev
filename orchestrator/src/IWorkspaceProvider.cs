using System;
using System.Threading.Tasks;
using JuniorDev.Contracts;

namespace JuniorDev.Orchestrator;

public interface IWorkspaceProvider
{
    Task<string> GetWorkspacePath(SessionConfig config);
    Task CleanupWorkspace(Guid sessionId);
}