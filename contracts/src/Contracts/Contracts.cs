using System;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace JuniorDev.Contracts;

public static class ContractVersion
{
    public const string Current = "v1.3";
}

public sealed record WorkItemRef(string Id, string? ProviderHint = null);

public sealed record WorkItemSummary(string Id, string Title, string Status, string? Assignee);

public sealed record WorkItemDetails(string Id, string Title, string Description, string Status, string? Assignee, IReadOnlyList<string> Tags);

public sealed record RepoRef(string Name, string Path);

public sealed record WorkspaceRef(string Path);

public sealed record Correlation(Guid SessionId, Guid? CommandId = null, Guid? ParentCommandId = null, string? PlanNodeId = null, string? IssuerAgentId = null);

public interface ICommand
{
    Guid Id { get; }
    Correlation Correlation { get; }
    string Kind { get; }
}

public abstract record CommandBase(Guid Id, Correlation Correlation, string Kind) : ICommand;

public sealed record CreateBranch(Guid Id, Correlation Correlation, RepoRef Repo, string BranchName, string? FromRef = null)
    : CommandBase(Id, Correlation, nameof(CreateBranch));

public sealed record ApplyPatch(Guid Id, Correlation Correlation, RepoRef Repo, string PatchContent)
    : CommandBase(Id, Correlation, nameof(ApplyPatch));

public sealed record RunTests(Guid Id, Correlation Correlation, RepoRef Repo, string? Filter = null, TimeSpan? Timeout = null)
    : CommandBase(Id, Correlation, nameof(RunTests));

public sealed record Commit(Guid Id, Correlation Correlation, RepoRef Repo, string Message, IReadOnlyList<string> IncludePaths, bool Amend = false)
    : CommandBase(Id, Correlation, nameof(Commit));

public sealed record Push(Guid Id, Correlation Correlation, RepoRef Repo, string BranchName)
    : CommandBase(Id, Correlation, nameof(Push));

public sealed record GetDiff(Guid Id, Correlation Correlation, RepoRef Repo, string Ref = "HEAD")
    : CommandBase(Id, Correlation, nameof(GetDiff));

public sealed record BuildProject(Guid Id, Correlation Correlation, RepoRef Repo, string? Configuration = null, string? Target = null)
    : CommandBase(Id, Correlation, nameof(BuildProject));

public sealed record TransitionTicket(Guid Id, Correlation Correlation, WorkItemRef Item, string State)
    : CommandBase(Id, Correlation, nameof(TransitionTicket));

public sealed record Comment(Guid Id, Correlation Correlation, WorkItemRef Item, string Body)
    : CommandBase(Id, Correlation, nameof(Comment));

public sealed record SetAssignee(Guid Id, Correlation Correlation, WorkItemRef Item, string Assignee)
    : CommandBase(Id, Correlation, nameof(SetAssignee));

public sealed record UploadArtifact(Guid Id, Correlation Correlation, string Name, string ContentType, byte[] Content, string? PathHint = null)
    : CommandBase(Id, Correlation, nameof(UploadArtifact));

public sealed record RequestApproval(Guid Id, Correlation Correlation, string Reason, IReadOnlyList<string> RequiredActions)
    : CommandBase(Id, Correlation, nameof(RequestApproval));

// Reserved for future multi-agent support
public sealed record SpawnSession(Guid Id, Correlation Correlation, SessionConfig Config)
    : CommandBase(Id, Correlation, nameof(SpawnSession));

public sealed record LinkPlanNode(Guid Id, Correlation Correlation, string PlanNodeId)
    : CommandBase(Id, Correlation, nameof(LinkPlanNode));

public sealed record QueryBacklog(Guid Id, Correlation Correlation, string? Filter = null)
    : CommandBase(Id, Correlation, nameof(QueryBacklog));

public sealed record QueryWorkItem(Guid Id, Correlation Correlation, WorkItemRef Item)
    : CommandBase(Id, Correlation, nameof(QueryWorkItem));

public interface IEvent
{
    Guid Id { get; }
    Correlation Correlation { get; }
    string Kind { get; }
}

public abstract record EventBase(Guid Id, Correlation Correlation, string Kind) : IEvent;

public sealed record CommandAccepted(Guid Id, Correlation Correlation, Guid CommandId)
    : EventBase(Id, Correlation, nameof(CommandAccepted));

public sealed record CommandRejected(Guid Id, Correlation Correlation, Guid CommandId, string Reason, string? PolicyRule = null)
    : EventBase(Id, Correlation, nameof(CommandRejected));

public enum CommandOutcome
{
    Success,
    Failure
}

public sealed record CommandCompleted(Guid Id, Correlation Correlation, Guid CommandId, CommandOutcome Outcome, string? Message = null, string? ErrorCode = null)
    : EventBase(Id, Correlation, nameof(CommandCompleted));

public sealed record ArtifactAvailable(Guid Id, Correlation Correlation, Artifact Artifact)
    : EventBase(Id, Correlation, nameof(ArtifactAvailable));

public sealed record Throttled(Guid Id, Correlation Correlation, string Scope, DateTimeOffset RetryAfter)
    : EventBase(Id, Correlation, nameof(Throttled));

public sealed record ConflictDetected(Guid Id, Correlation Correlation, RepoRef Repo, string Details, string? PatchContent = null)
    : EventBase(Id, Correlation, nameof(ConflictDetected));

public enum SessionStatus
{
    Unknown,
    Running,
    Paused,
    NeedsApproval,
    Error,
    Completed
}

public sealed record SessionStatusChanged(Guid Id, Correlation Correlation, SessionStatus Status, string? Reason = null)
    : EventBase(Id, Correlation, nameof(SessionStatusChanged));

public sealed record PlanUpdated(Guid Id, Correlation Correlation, TaskPlan Plan)
    : EventBase(Id, Correlation, nameof(PlanUpdated));

public sealed record BacklogQueried(Guid Id, Correlation Correlation, IReadOnlyList<WorkItemSummary> Items)
    : EventBase(Id, Correlation, nameof(BacklogQueried));

public sealed record WorkItemQueried(Guid Id, Correlation Correlation, WorkItemDetails Details)
    : EventBase(Id, Correlation, nameof(WorkItemQueried));

public sealed record Artifact(string Kind, string Name, string? InlineText = null, string? PathHint = null, Uri? DownloadUri = null, string? ContentType = null);

public sealed record TaskPlan(IReadOnlyList<TaskNode> Nodes);

public sealed record TaskNode(string Id, string Title, IReadOnlyList<string> DependsOn, WorkItemRef? WorkItem, string? AgentHint, string? SuggestedBranch, IReadOnlyList<string> Tags);

/// <summary>
/// Rate limits configuration
/// </summary>
public sealed class RateLimits
{
    public int? CallsPerMinute { get; set; }
    public int? Burst { get; set; }
    public Dictionary<string, int>? PerCommandCaps { get; set; }
}

/// <summary>
/// Policy profile configuration
/// </summary>
public sealed class PolicyProfile
{
    public string Name { get; init; } = "";
    public HashSet<string> ProtectedBranches { get; init; } = null!;
    public bool RequireTestsBeforePush { get; init; } = false;
    public bool RequireApprovalForPush { get; init; } = false;
    public List<string>? CommandWhitelist { get; init; } = null;
    public List<string>? CommandBlacklist { get; init; } = null;
    public int? MaxFilesPerCommit { get; init; } = null;
    public List<string>? AllowedWorkItemTransitions { get; init; } = null;
    public RateLimits? Limits { get; init; } = null;
}

public sealed record SessionConfig(
    Guid SessionId,
    Guid? ParentSessionId,
    string? PlanNodeId,
    PolicyProfile Policy,
    RepoRef Repo,
    WorkspaceRef Workspace,
    WorkItemRef? WorkItem,
    string AgentProfile);

// Configuration Classes

/// <summary>
/// Main application configuration container
/// </summary>
public sealed record AppConfig(
    AuthConfig? Auth = null,
    AdaptersConfig? Adapters = null,
    SemanticKernelConfig? SemanticKernel = null,
    UiConfig? Ui = null,
    WorkspaceConfig? Workspace = null,
    PolicyConfig? Policy = null);

/// <summary>
/// Authentication configuration for external services
/// </summary>
public sealed record AuthConfig(
    JiraAuthConfig? Jira = null,
    GitHubAuthConfig? GitHub = null,
    GitAuthConfig? Git = null,
    OpenAIAuthConfig? OpenAI = null,
    AzureOpenAIAuthConfig? AzureOpenAI = null);

/// <summary>
/// Jira authentication settings
/// </summary>
public sealed record JiraAuthConfig(
    string BaseUrl,
    string Username,
    string ApiToken);

/// <summary>
/// GitHub authentication settings
/// </summary>
public sealed record GitHubAuthConfig(
    string Token,
    string? DefaultOrg = null,
    string? DefaultRepo = null);

/// <summary>
/// Git authentication settings
/// </summary>
public sealed record GitAuthConfig(
    string? SshKeyPath = null,
    string? PersonalAccessToken = null,
    string? DefaultRemote = null,
    string? UserName = null,
    string? UserEmail = null);

/// <summary>
/// OpenAI authentication settings
/// </summary>
public sealed record OpenAIAuthConfig(
    string ApiKey,
    string? OrganizationId = null);

/// <summary>
/// Azure OpenAI authentication settings
/// </summary>
public sealed record AzureOpenAIAuthConfig(
    string Endpoint,
    string ApiKey,
    string DeploymentName);

/// <summary>
/// Adapter selection and configuration
/// </summary>
public sealed record AdaptersConfig(
    string WorkItemsAdapter, // "jira" or "github"
    string VcsAdapter, // "git" (only git supported currently)
    string TerminalAdapter, // "powershell" or "bash" (only powershell on Windows)
    string? BuildAdapter = null); // "dotnet", "npm", etc. - opt-in build system support

/// <summary>
/// Semantic Kernel / AI configuration
/// </summary>
public sealed record SemanticKernelConfig(
    string Provider, // "openai" or "azure-openai"
    string Model,
    int? MaxTokens = null,
    double? Temperature = null,
    string? ProxyUrl = null,
    TimeSpan? Timeout = null,
    Dictionary<string, AgentProfile>? AgentProfiles = null);

/// <summary>
/// Agent profile configuration
/// </summary>
public sealed record AgentProfile(
    string Name,
    string Description,
    List<string> Capabilities,
    Dictionary<string, string>? Settings = null);

/// <summary>
/// UI configuration
/// </summary>
public sealed record UiConfig(
    UiSettings Settings,
    string? LayoutPathOverride = null,
    string? SettingsPathOverride = null);

/// <summary>
/// UI settings
/// </summary>
public sealed record UiSettings(
    string Theme = "Light",
    int FontSize = 12,
    bool ShowStatusChips = true,
    bool AutoScrollEvents = true,
    bool ShowTimestamps = true,
    int MaxEventHistory = 1000);

/// <summary>
/// Workspace configuration
/// </summary>
public sealed record WorkspaceConfig(
    string BasePath,
    string? BaselineMirrorPath = null,
    bool AutoCreateDirectories = true,
    Dictionary<string, RepoConfig>? KnownRepos = null);

/// <summary>
/// Repository configuration
/// </summary>
public sealed record RepoConfig(
    string Path,
    string DefaultBranch,
    string? RemoteUrl = null);

/// <summary>
/// Policy configuration
/// </summary>
public sealed record PolicyConfig(
    Dictionary<string, PolicyProfile> Profiles,
    string DefaultProfile,
    RateLimits GlobalLimits);

// Configuration Builder Utility

/// <summary>
/// Utility class for building configuration with layered sources
/// </summary>
public static class ConfigBuilder
{
    /// <summary>
    /// Builds configuration with standard layered sources:
    /// 1. appsettings.json (checked in) - unless skipDefaults is true
    /// 2. appsettings.{Environment}.json (checked in)
    /// 3. Environment variables
    /// 4. User secrets (development only)
    /// </summary>
    public static IConfiguration Build(string? environment = null, string? basePath = null, bool skipDefaults = false)
    {
        var builder = new ConfigurationBuilder()
            .SetBasePath(basePath ?? AppContext.BaseDirectory);

        if (!skipDefaults)
        {
            builder.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
        }

        if (!string.IsNullOrEmpty(environment))
        {
            builder.AddJsonFile($"appsettings.{environment}.json", optional: true, reloadOnChange: true);
        }

        builder.AddEnvironmentVariables("JUNIORDEV__")
               .AddUserSecrets(typeof(ConfigBuilder).Assembly, optional: true);

        return builder.Build();
    }

    /// <summary>
    /// Gets the AppConfig from configuration
    /// </summary>
    public static AppConfig GetAppConfig(IConfiguration configuration)
    {
        return configuration.GetSection("AppConfig").Get<AppConfig>() ?? new AppConfig();
    }
}
