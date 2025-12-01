using Microsoft.Extensions.DependencyInjection;
using JuniorDev.Contracts;
using JuniorDev.Orchestrator;

namespace JuniorDev.WorkItems.GitHub;

public static class WorkItemAdapterExtensions
{
    public static IServiceCollection AddGitHubWorkItemAdapter(this IServiceCollection services, AppConfig appConfig)
    {
        // Check if GitHub credentials are available in configuration
        var gitHubAuth = appConfig.Auth?.GitHub;
        var hasGitHubConfig = gitHubAuth != null && !string.IsNullOrEmpty(gitHubAuth.Token);

        if (hasGitHubConfig)
        {
            services.AddSingleton<IAdapter>(sp => new GitHubAdapter(appConfig));
        }
        else
        {
            services.AddSingleton<IAdapter, FakeGitHubAdapter>();
        }

        return services;
    }
}
