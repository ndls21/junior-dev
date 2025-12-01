using Microsoft.Extensions.DependencyInjection;
using JuniorDev.Orchestrator;
using JuniorDev.Contracts;

namespace JuniorDev.VcsGit;

public static class VcsGitAdapterRegistration
{
    public static IServiceCollection AddVcsGitAdapter(this IServiceCollection services, VcsConfig config, bool isFake = false)
    {
        services.AddSingleton<IAdapter>(new VcsGitAdapter(config, isFake));
        return services;
    }

    public static IServiceCollection AddVcsGitAdapter(this IServiceCollection services, AppConfig appConfig)
    {
        // Create VcsConfig from AppConfig
        var vcsConfig = new VcsConfig
        {
            RepoPath = appConfig.Workspace?.BasePath ?? "./workspaces",
            RemoteUrl = null, // Will be set per repo/session
            AllowPush = !(appConfig.LivePolicy?.PushEnabled ?? true), // Invert: if PushEnabled is false, then AllowPush is true
            DryRun = appConfig.LivePolicy?.DryRun ?? true,
            IsIntegrationTest = false
        };

        // For now, always use real VCS adapter (Git is well-established)
        // Could add fake detection logic here if needed
        services.AddSingleton<IAdapter>(new VcsGitAdapter(vcsConfig, isFake: false));
        return services;
    }
}
