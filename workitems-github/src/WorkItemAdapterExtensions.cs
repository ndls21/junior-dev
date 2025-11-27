using Microsoft.Extensions.DependencyInjection;
using JuniorDev.Orchestrator;

namespace JuniorDev.WorkItems.GitHub;

public static class WorkItemAdapterExtensions
{
    public static IServiceCollection AddGitHubWorkItemAdapter(this IServiceCollection services)
    {
        // Check if GitHub credentials are available
        var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        var repo = Environment.GetEnvironmentVariable("GITHUB_REPO");

        if (!string.IsNullOrEmpty(token) && !string.IsNullOrEmpty(repo))
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