using Microsoft.Extensions.DependencyInjection;
using JuniorDev.Contracts;
using JuniorDev.Orchestrator;
using JuniorDev.Contracts;

namespace JuniorDev.WorkItems.GitHub;

public static class WorkItemAdapterExtensions
{
    public static IServiceCollection AddGitHubWorkItemAdapter(this IServiceCollection services, AppConfig appConfig)
    {
        // Check if GitHub credentials are configured and valid
        var gitHubAuth = appConfig.Auth?.GitHub;
        var hasValidCredentials = gitHubAuth != null && 
                                 !string.IsNullOrWhiteSpace(gitHubAuth.Token) && 
                                 !gitHubAuth.Token.Contains("your-");

        if (hasValidCredentials)
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
