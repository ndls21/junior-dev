using Microsoft.Extensions.DependencyInjection;
using JuniorDev.Orchestrator;

namespace JuniorDev.WorkItems.Jira;

public static class WorkItemAdapterExtensions
{
    public static IServiceCollection AddWorkItemAdapters(this IServiceCollection services, bool useReal = false)
    {
        var hasEnv =
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("JIRA_URL")) &&
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("JIRA_USER")) &&
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("JIRA_TOKEN")) &&
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("JIRA_PROJECT"));

        if (useReal && hasEnv)
        {
            services.AddSingleton<IAdapter, JiraAdapter>();
        }
        else
        {
            services.AddSingleton<IAdapter, FakeWorkItemAdapter>();
        }

        return services;
    }
}
