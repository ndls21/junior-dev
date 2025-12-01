using Microsoft.Extensions.DependencyInjection;
using JuniorDev.Orchestrator;
using JuniorDev.Contracts;

namespace JuniorDev.WorkItems.Jira;

public static class WorkItemAdapterExtensions
{
    public static IServiceCollection AddWorkItemAdapters(this IServiceCollection services, bool useReal = false)
    {
        if (useReal)
        {
            services.AddSingleton<IAdapter, JiraAdapter>();
        }
        else
        {
            services.AddSingleton<IAdapter, FakeWorkItemAdapter>();
        }

        return services;
    }

    public static IServiceCollection AddWorkItemAdapters(this IServiceCollection services, AppConfig appConfig)
    {
        // Check if Jira credentials are configured
        var hasCredentials = appConfig.Auth?.Jira != null &&
                            !string.IsNullOrWhiteSpace(appConfig.Auth.Jira.BaseUrl) &&
                            !string.IsNullOrWhiteSpace(appConfig.Auth.Jira.Username) &&
                            !string.IsNullOrWhiteSpace(appConfig.Auth.Jira.ApiToken) &&
                            !appConfig.Auth.Jira.BaseUrl.Contains("your-") &&
                            !appConfig.Auth.Jira.Username.Contains("your-") &&
                            !appConfig.Auth.Jira.ApiToken.Contains("your-");

        if (hasCredentials)
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
