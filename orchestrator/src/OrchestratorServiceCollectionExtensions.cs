using Microsoft.Extensions.DependencyInjection;
using JuniorDev.Contracts;

namespace JuniorDev.Orchestrator;

public static class OrchestratorServiceCollectionExtensions
{
    public static IServiceCollection AddOrchestrator(this IServiceCollection services)
    {
        // Register core orchestrator services
        services.AddSingleton<ISessionManager, SessionManager>();
        services.AddSingleton<IPolicyEnforcer, StubPolicyEnforcer>();
        services.AddSingleton<IRateLimiter, StubRateLimiter>();
        services.AddSingleton<IWorkspaceProvider, StubWorkspaceProvider>();
        services.AddSingleton<IArtifactStore, StubArtifactStore>();

        // Register fake adapters (can be overridden by real adapters)
        services.AddSingleton<IAdapter, FakeVcsAdapter>();
        services.AddSingleton<IAdapter, FakeWorkItemsAdapter>();

        return services;
    }
}
