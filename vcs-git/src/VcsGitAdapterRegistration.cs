using Microsoft.Extensions.DependencyInjection;
using JuniorDev.Orchestrator;
using JuniorDev.Contracts;

namespace JuniorDev.VcsGit;

public static class VcsGitAdapterRegistration
{
    public static IServiceCollection AddVcsGitAdapter(this IServiceCollection services, VcsConfig config, bool isFake = false, AppConfig? appConfig = null)
    {
        services.AddSingleton<IAdapter>(new VcsGitAdapter(config, isFake, appConfig));
        return services;
    }
}
