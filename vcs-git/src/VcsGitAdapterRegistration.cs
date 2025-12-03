using Microsoft.Extensions.DependencyInjection;
using JuniorDev.Orchestrator;
using JuniorDev.Contracts;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

namespace JuniorDev.VcsGit;

public static class VcsGitAdapterRegistration
{
    public static IServiceCollection AddVcsGitAdapter(this IServiceCollection services, VcsConfig config, bool isFake = false, AppConfig? appConfig = null)
    {
        services.AddSingleton<IAdapter>(sp => new VcsGitAdapter(config, isFake, appConfig, sp.GetService<ILogger<VcsGitAdapter>>(), sp.GetService<IOptionsMonitor<LivePolicyConfig>>()));
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
        services.AddSingleton<IAdapter>(sp => new VcsGitAdapter(vcsConfig, isFake: false, appConfig, sp.GetService<ILogger<VcsGitAdapter>>(), sp.GetService<IOptionsMonitor<LivePolicyConfig>>()));
        return services;
    }
}
