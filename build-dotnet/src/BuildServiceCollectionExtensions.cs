using Microsoft.Extensions.DependencyInjection;
using JuniorDev.Contracts;
using JuniorDev.Orchestrator;

namespace JuniorDev.Build.Dotnet;

public static class BuildServiceCollectionExtensions
{
    public static IServiceCollection AddDotnetBuildAdapter(this IServiceCollection services)
    {
        services.AddSingleton<IAdapter, DotnetBuildAdapter>();
        services.AddSingleton(new BuildConfig(".", TimeSpan.FromMinutes(10))); // Default config
        return services;
    }
}