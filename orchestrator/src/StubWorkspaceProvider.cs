using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading.Tasks;
using JuniorDev.Contracts;

namespace JuniorDev.Orchestrator;

public class StubWorkspaceProvider : IWorkspaceProvider
{
    private readonly ConcurrentDictionary<Guid, string> _workspaces = new();
    private readonly string _basePath;

    public StubWorkspaceProvider(string? basePath = null)
    {
        _basePath = basePath ?? Path.Combine(Path.GetTempPath(), "JuniorDevWorkspaces");
        Directory.CreateDirectory(_basePath);
    }

    public async Task<string> GetWorkspacePath(SessionConfig config)
    {
        if (_workspaces.TryGetValue(config.SessionId, out var path))
        {
            return path;
        }

        // If mirror path specified, use it
        if (!string.IsNullOrEmpty(config.Workspace.Path))
        {
            path = config.Workspace.Path;
        }
        else
        {
            // Stub: create temp dir, "clone" by creating a marker file
            path = Path.Combine(_basePath, config.SessionId.ToString());
            Directory.CreateDirectory(path);
            await File.WriteAllTextAsync(Path.Combine(path, ".git"), "stub repo");
        }

        _workspaces[config.SessionId] = path;
        return path;
    }

    public Task CleanupWorkspace(Guid sessionId)
    {
        if (_workspaces.TryRemove(sessionId, out var path))
        {
            try
            {
                Directory.Delete(path, true);
            }
            catch
            {
                // Ignore cleanup errors in stub
            }
        }
        return Task.CompletedTask;
    }
}