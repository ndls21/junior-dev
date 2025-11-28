using System;
using System.Threading.Tasks;
using JuniorDev.Contracts;

namespace JuniorDev.Orchestrator;

public interface IArtifactStore
{
    Task StoreArtifact(Guid sessionId, Artifact artifact);
    Task<Artifact?> GetArtifact(Guid sessionId, string name);
}
