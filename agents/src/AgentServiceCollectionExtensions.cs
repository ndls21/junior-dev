using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace JuniorDev.Agents;

/// <summary>
/// Extension methods for registering agents with DI.
/// </summary>
public static class AgentServiceCollectionExtensions
{
    /// <summary>
    /// Adds the agent SDK services to the service collection.
    /// </summary>
    public static IServiceCollection AddAgentSdk(this IServiceCollection services)
    {
        services.AddSingleton<AgentEventDispatcher>();
        services.AddSingleton<AgentEventLoopService>();
        services.AddHostedService(provider => provider.GetRequiredService<AgentEventLoopService>());
        services.AddTransient<AgentConfig>();

        // Add health checks
        services.AddHealthChecks()
            .AddCheck<AgentHealthCheck>("agent-sdk", tags: new[] { "agent", "sdk" });

        return services;
    }

    /// <summary>
    /// Adds the agent SDK services with custom configuration.
    /// </summary>
    public static IServiceCollection AddAgentSdk(this IServiceCollection services, Action<AgentConfig> configure)
    {
        services.AddSingleton<AgentEventDispatcher>();
        services.AddSingleton<AgentEventLoopService>();
        services.AddHostedService(provider => provider.GetRequiredService<AgentEventLoopService>());
        services.AddSingleton(provider =>
        {
            var config = new AgentConfig();
            configure(config);
            return config;
        });

        return services;
    }

    /// <summary>
    /// Registers an agent type with the DI container.
    /// </summary>
    public static IServiceCollection AddAgent<TAgent>(this IServiceCollection services)
        where TAgent : class, IAgent
    {
        services.AddTransient<TAgent>();
        services.AddTransient<IAgent, TAgent>();

        return services;
    }

    /// <summary>
    /// Registers an agent instance factory.
    /// </summary>
    public static IServiceCollection AddAgent(this IServiceCollection services, Func<IServiceProvider, IAgent> factory)
    {
        services.AddTransient<IAgent>(factory);
        return services;
    }
}