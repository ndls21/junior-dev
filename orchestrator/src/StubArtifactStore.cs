using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using JuniorDev.Contracts;

namespace JuniorDev.Orchestrator;

public class StubArtifactStore : IArtifactStore
{
    private readonly ConcurrentDictionary<(Guid SessionId, string Name), Artifact> _artifacts = new();

    public Task StoreArtifact(Guid sessionId, Artifact artifact)
    {
        _artifacts[(sessionId, artifact.Name)] = artifact;
        return Task.CompletedTask;
    }

    public Task<Artifact?> GetArtifact(Guid sessionId, string name)
    {
        _artifacts.TryGetValue((sessionId, name), out var artifact);
        return Task.FromResult(artifact);
    }
}