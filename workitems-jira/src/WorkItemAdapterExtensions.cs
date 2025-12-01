using Microsoft.Extensions.DependencyInjection;
using JuniorDev.Contracts;
using JuniorDev.Orchestrator;
using JuniorDev.Contracts;

namespace JuniorDev.WorkItems.Jira;

public static class WorkItemAdapterExtensions
{
    public static IServiceCollection AddWorkItemAdapters(this IServiceCollection services, AppConfig appConfig)
    {
        // Check if Jira credentials are configured and valid
        var jiraAuth = appConfig.Auth?.Jira;
        var hasValidCredentials = jiraAuth != null &&
                                 !string.IsNullOrWhiteSpace(jiraAuth.BaseUrl) &&
                                 !string.IsNullOrWhiteSpace(jiraAuth.Username) &&
                                 !string.IsNullOrWhiteSpace(jiraAuth.ApiToken) &&
                                 !jiraAuth.BaseUrl.Contains("your-") &&
                                 !jiraAuth.Username.Contains("your-") &&
                                 !jiraAuth.ApiToken.Contains("your-");

        if (hasValidCredentials)
        {
            services.AddSingleton<IAdapter>(sp => new JiraAdapter(appConfig));
        }
        else
        {
            services.AddSingleton<IAdapter, FakeWorkItemAdapter>();
        }

        return services;
    }
}
