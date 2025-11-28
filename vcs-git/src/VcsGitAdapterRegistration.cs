using Microsoft.Extensions.DependencyInjection;
using JuniorDev.Orchestrator;

namespace JuniorDev.VcsGit;

public static class VcsGitAdapterRegistration
{
    public static IServiceCollection AddVcsGitAdapter(this IServiceCollection services, VcsConfig config, bool isFake = false)
    {
        services.AddSingleton<IAdapter>(new VcsGitAdapter(config, isFake));
        return services;
    }
}
