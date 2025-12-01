using Microsoft.Extensions.DependencyInjection;
using JuniorDev.Orchestrator;
using JuniorDev.Contracts;

namespace JuniorDev.WorkItems.GitHub;

public static class WorkItemAdapterExtensions
{
    public static IServiceCollection AddGitHubWorkItemAdapter(this IServiceCollection services)
    {
        services.AddSingleton<IAdapter, GitHubAdapter>();
        return services;
    }

    public static IServiceCollection AddGitHubWorkItemAdapter(this IServiceCollection services, AppConfig appConfig)
    {
        // Check if GitHub credentials are configured
        var hasCredentials = appConfig.Auth?.GitHub?.Token != null &&
                            !string.IsNullOrWhiteSpace(appConfig.Auth.GitHub.Token) &&
                            !appConfig.Auth.GitHub.Token.Contains("your-");

        if (hasCredentials)
        {
            services.AddSingleton<IAdapter, GitHubAdapter>();
        }
        else
        {
            services.AddSingleton<IAdapter, FakeGitHubAdapter>();
        }

        return services;
    }
}
