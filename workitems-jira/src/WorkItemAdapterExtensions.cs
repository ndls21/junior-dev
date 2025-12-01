using Microsoft.Extensions.DependencyInjection;
using JuniorDev.Contracts;
using JuniorDev.Orchestrator;

namespace JuniorDev.WorkItems.Jira;

public static class WorkItemAdapterExtensions
{
    public static IServiceCollection AddWorkItemAdapters(this IServiceCollection services, AppConfig appConfig, bool useReal = false)
    {
        var jiraAuth = appConfig.Auth?.Jira;
        var hasJiraConfig = jiraAuth != null &&
                           !string.IsNullOrEmpty(jiraAuth.BaseUrl) &&
                           !string.IsNullOrEmpty(jiraAuth.Username) &&
                           !string.IsNullOrEmpty(jiraAuth.ApiToken);

        if (useReal && hasJiraConfig)
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
