using System;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using DotNetEnv;

namespace JuniorDev.Contracts;

public static class ContractVersion
{
    public const string Current = "v1.4";
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

public sealed record BuildProject(Guid Id, Correlation Correlation, RepoRef Repo, string ProjectPath, string? Configuration = null, string? TargetFramework = null, IReadOnlyList<string>? Targets = null, TimeSpan? Timeout = null)
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

public sealed record ClaimWorkItem(Guid Id, Correlation Correlation, WorkItemRef Item, string Assignee, TimeSpan? ClaimTimeout = null)
    : CommandBase(Id, Correlation, nameof(ClaimWorkItem));

public sealed record ReleaseWorkItem(Guid Id, Correlation Correlation, WorkItemRef Item, string Reason = "Released by user")
    : CommandBase(Id, Correlation, nameof(ReleaseWorkItem));

public sealed record RenewClaim(Guid Id, Correlation Correlation, WorkItemRef Item, TimeSpan? Extension = null)
    : CommandBase(Id, Correlation, nameof(RenewClaim));

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

public enum ClaimResult
{
    Success,
    AlreadyClaimed,
    Rejected,
    UnknownError
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

public sealed record SessionPaused(Guid Id, Correlation Correlation, string Actor, string Reason, Guid? CommandId = null)
    : EventBase(Id, Correlation, nameof(SessionPaused));

public sealed record SessionAborted(Guid Id, Correlation Correlation, string Actor, string Reason, Guid? CommandId = null)
    : EventBase(Id, Correlation, nameof(SessionAborted));

public sealed record PlanUpdated(Guid Id, Correlation Correlation, TaskPlan Plan)
    : EventBase(Id, Correlation, nameof(PlanUpdated));

public sealed record BacklogQueried(Guid Id, Correlation Correlation, IReadOnlyList<WorkItemSummary> Items)
    : EventBase(Id, Correlation, nameof(BacklogQueried));

public sealed record WorkItemQueried(Guid Id, Correlation Correlation, WorkItemDetails Details)
    : EventBase(Id, Correlation, nameof(WorkItemQueried));

public sealed record WorkItemClaimed(Guid Id, Correlation Correlation, WorkItemRef Item, string Assignee, DateTimeOffset ExpiresAt)
    : EventBase(Id, Correlation, nameof(WorkItemClaimed));

public sealed record WorkItemClaimReleased(Guid Id, Correlation Correlation, WorkItemRef Item, string Reason)
    : EventBase(Id, Correlation, nameof(WorkItemClaimReleased));

public sealed record ClaimRenewed(Guid Id, Correlation Correlation, WorkItemRef Item, DateTimeOffset NewExpiresAt)
    : EventBase(Id, Correlation, nameof(ClaimRenewed));

public sealed record ClaimExpired(Guid Id, Correlation Correlation, WorkItemRef Item, string PreviousAssignee)
    : EventBase(Id, Correlation, nameof(ClaimExpired));

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
    public int AutoPauseErrorThreshold { get; init; } = 3;
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

public sealed record SessionInfo(
    Guid SessionId,
    SessionStatus Status,
    string AgentProfile,
    string RepoName,
    DateTimeOffset CreatedAt,
    string? CurrentTask = null);

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
    PolicyConfig? Policy = null,
    LivePolicyConfig? LivePolicy = null,
    TranscriptConfig? Transcript = null,
    ReviewerConfig? Reviewer = null,
    WorkItemConfig? WorkItems = null);

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
    string ApiToken,
    string? ProjectKey = null);

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
    string? UserEmail = null,
    string? BranchPrefix = null);

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
/// Configuration for the build adapter.
/// </summary>
public sealed record BuildConfig(
    string WorkspaceRoot,
    TimeSpan DefaultTimeout = default)
{
    public BuildConfig() : this(".", TimeSpan.FromMinutes(5)) { }
}

/// <summary>
/// Simple chat client interface for contracts (adapted to Microsoft.Extensions.AI.IChatClient in implementations)
/// </summary>
public interface IChatClient
{
    string Provider { get; }
    string Model { get; }
}

/// <summary>
/// Chat client factory for creating AI clients per agent
/// </summary>
public interface IChatClientFactory
{
    /// <summary>
    /// Gets a chat client for the specified agent profile, with fallback to defaults
    /// </summary>
    IChatClient GetClientFor(string agentProfile);
    
    /// <summary>
    /// Gets the underlying Microsoft.Extensions.AI.IChatClient for DevExpress integration
    /// </summary>
    object GetUnderlyingClientFor(string agentProfile);
}

/// <summary>
/// Agent service provider configuration
/// </summary>
public sealed record AgentServiceProviderConfig(
    string Provider, // "openai" or "azure-openai"
    string Model,
    string? ApiKey = null, // If null, uses global provider key
    int? MaxTokens = null,
    double? Temperature = null,
    string? DeploymentName = null); // For Azure OpenAI

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
    string DefaultProvider, // "openai" or "azure-openai"
    string DefaultModel,
    int? MaxTokens = null,
    double? Temperature = null,
    string? ProxyUrl = null,
    TimeSpan? Timeout = null,
    Dictionary<string, AgentServiceProviderConfig>? AgentServiceProviders = null,
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

/// <summary>
/// Live policy configuration for safety controls
/// </summary>
public sealed record LivePolicyConfig(
    bool PushEnabled = false,
    bool DryRun = true,
    bool RequireCredentialsValidation = true,
    int AutoPauseErrorThreshold = 3);

/// <summary>
/// Configuration for transcript persistence and management
/// </summary>
public sealed record TranscriptConfig(
    bool Enabled = true,
    int MaxMessagesPerTranscript = 1000,
    long MaxTranscriptSizeBytes = 10485760, // 10MB default
    TimeSpan MaxTranscriptAge = default, // 30 days default in constructor
    int TranscriptContextMessages = 10,
    string? StorageDirectory = null)
{
    public TranscriptConfig() : this(
        true, // Enabled
        1000, // MaxMessagesPerTranscript
        10 * 1024 * 1024, // MaxTranscriptSizeBytes (10MB)
        TimeSpan.FromDays(30), // MaxTranscriptAge
        10, // TranscriptContextMessages
        null // StorageDirectory
    ) { }
}

/// <summary>
/// Configuration for reviewer agent repository-wide analysis
/// </summary>
public sealed record ReviewerConfig(
    bool EnableRepositoryAnalysis = true,
    int MaxFilesToAnalyze = 50,
    int MaxAnalysisDepth = 3,
    TimeSpan AnalysisCacheTimeout = default, // 1 hour default in constructor
    List<string> AnalysisFocusAreas = default!, // ["structure", "quality", "security", "performance", "dependencies"]
    long MaxFileSizeBytes = 1048576, // 1MB default
    int MaxTokensPerAnalysis = 4000,
    RepositoryAnalysisConfig? Analysis = null)
{
    public ReviewerConfig() : this(
        true, // EnableRepositoryAnalysis
        50, // MaxFilesToAnalyze
        3, // MaxAnalysisDepth
        TimeSpan.FromHours(1), // AnalysisCacheTimeout
        new List<string> { "structure", "quality" }, // AnalysisFocusAreas (conservative defaults)
        1024 * 1024, // MaxFileSizeBytes (1MB)
        4000, // MaxTokensPerAnalysis
        new RepositoryAnalysisConfig() // Analysis
    ) { }
}

/// <summary>
/// Configuration for repository-wide analysis (Phase 2)
/// </summary>
public sealed record RepositoryAnalysisConfig(
    bool Enabled = false,
    List<string> EnabledAreas = default!, // ["structure", "quality", "security", "performance", "dependencies"]
    int MaxFiles = 100,
    long MaxFileBytes = 1048576, // 1MB per file
    long MaxTotalBytes = 10485760, // 10MB total
    int MaxTokens = 8000,
    decimal MaxCost = 0.10m, // $0.10 max cost
    TimeSpan MaxDuration = default) // 5 minutes default
{
    public RepositoryAnalysisConfig() : this(
        false, // Enabled
        new List<string> { "structure", "quality" }, // EnabledAreas (conservative defaults)
        100, // MaxFiles
        1024 * 1024, // MaxFileBytes (1MB)
        10 * 1024 * 1024, // MaxTotalBytes (10MB)
        8000, // MaxTokens
        0.10m, // MaxCost ($0.10)
        TimeSpan.FromMinutes(5) // MaxDuration
    ) { }
}

/// <summary>
/// Result of a repository analysis finding
/// </summary>
public sealed record AnalysisFinding(
    string Path,
    string Kind, // "structure", "quality", "security", "performance", "dependencies"
    string Severity, // "info", "warning", "error", "critical"
    string Summary,
    string Details = "",
    string? Recommendation = null)
{
    public AnalysisFinding(string Path, string Kind, string Severity, string Summary)
        : this(Path, Kind, Severity, Summary, "", null) { }
}

/// <summary>
/// Work item management configuration
/// </summary>
public sealed record WorkItemConfig(
    TimeSpan DefaultClaimTimeout = default,
    int MaxConcurrentClaimsPerAgent = 3,
    int MaxConcurrentClaimsPerSession = 5,
    TimeSpan ClaimRenewalWindow = default,
    bool AutoReleaseOnInactivity = true,
    TimeSpan CleanupInterval = default)
{
    public WorkItemConfig() : this(
        TimeSpan.FromHours(2), // 2 hours default claim timeout
        3, // Max 3 concurrent claims per agent
        5, // Max 5 concurrent claims per session
        TimeSpan.FromMinutes(30), // Renew within 30 minutes of expiry
        true, // Auto-release inactive claims
        TimeSpan.FromMinutes(5) // Check for expired claims every 5 minutes
    ) { }
}

// Configuration Builder Utility

/// <summary>
/// Utility class for building configuration with layered sources
/// </summary>
public static class ConfigBuilder
{
    /// <summary>
    /// Builds configuration with standard layered sources:
    /// 1. .env.local (ignored by git, highest priority)
    /// 2. appsettings.json (checked in) - unless skipDefaults is true
    /// 3. appsettings.{Environment}.json (checked in)
    /// 4. Environment variables
    /// 5. User secrets (development only)
    /// </summary>
    public static IConfiguration Build(string? environment = null, string? basePath = null, bool skipDefaults = false)
    {
        var basePathValue = basePath ?? AppContext.BaseDirectory;
        
        // Load .env.local if it exists (highest priority, ignored by git)
        var envFilePath = Path.Combine(basePathValue, ".env.local");
        if (File.Exists(envFilePath))
        {
            Env.Load(envFilePath);
        }

        var builder = new ConfigurationBuilder()
            .SetBasePath(basePathValue);

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

    /// <summary>
    /// Validates live adapter configuration and credentials.
    /// This should be called when live adapters are configured to ensure safety settings are honored.
    /// </summary>
    public static void ValidateLiveAdapters(AppConfig appConfig)
    {
        var adapters = appConfig.Adapters ?? new AdaptersConfig("fake", "fake", "powershell");
        var livePolicy = appConfig.LivePolicy ?? new LivePolicyConfig();

        // Check if any live adapters are configured
        bool hasLiveAdapters = 
            (adapters.WorkItemsAdapter?.ToLower() == "github" || adapters.WorkItemsAdapter?.ToLower() == "jira") ||
            adapters.VcsAdapter?.ToLower() == "git";

        if (hasLiveAdapters && livePolicy.RequireCredentialsValidation)
        {
            // Validate credentials for live adapters
            try
            {
                ValidateLiveAdapterCredentials(appConfig);
            }
            catch (InvalidOperationException ex)
            {
                throw new InvalidOperationException("Live adapter credentials validation failed: " + ex.Message, ex);
            }
        }
    }

    /// <summary>
    /// Validates that authentication credentials are properly configured for live adapters.
    /// This method validates credentials for the adapters that are configured to be live.
    /// </summary>
    /// <param name="appConfig">The application configuration to validate.</param>
    /// <exception cref="InvalidOperationException">Thrown when required credentials are missing or incomplete.</exception>
    public static void ValidateLiveAdapterCredentials(AppConfig appConfig)
    {
        var adapters = appConfig.Adapters ?? new AdaptersConfig("fake", "fake", "powershell");
        var auth = appConfig.Auth ?? new AuthConfig();

        // Validate GitHub adapter credentials if configured
        if (adapters.WorkItemsAdapter?.ToLower() == "github")
        {
            if (auth.GitHub == null)
            {
                throw new InvalidOperationException("GitHub authentication not configured. Please configure GitHub credentials in the Auth section.");
            }
            if (string.IsNullOrWhiteSpace(auth.GitHub.Token))
            {
                throw new InvalidOperationException("GitHub Token is required but not configured.");
            }
            if (string.IsNullOrWhiteSpace(auth.GitHub.DefaultOrg))
            {
                throw new InvalidOperationException("GitHub DefaultOrg is required for live operations but not configured.");
            }
            if (string.IsNullOrWhiteSpace(auth.GitHub.DefaultRepo))
            {
                throw new InvalidOperationException("GitHub DefaultRepo is required for live operations but not configured.");
            }
        }

        // Validate Jira adapter credentials if configured
        if (adapters.WorkItemsAdapter?.ToLower() == "jira")
        {
            if (auth.Jira == null)
            {
                throw new InvalidOperationException("Jira authentication not configured. Please configure Jira credentials in the Auth section.");
            }
            if (string.IsNullOrWhiteSpace(auth.Jira.BaseUrl))
            {
                throw new InvalidOperationException("Jira BaseUrl is required but not configured.");
            }
            if (string.IsNullOrWhiteSpace(auth.Jira.Username))
            {
                throw new InvalidOperationException("Jira Username is required but not configured.");
            }
            if (string.IsNullOrWhiteSpace(auth.Jira.ApiToken))
            {
                throw new InvalidOperationException("Jira ApiToken is required but not configured.");
            }
            if (string.IsNullOrWhiteSpace(auth.Jira.ProjectKey))
            {
                throw new InvalidOperationException("Jira ProjectKey is required for live operations but not configured.");
            }
        }

        // Validate VCS adapter credentials if configured for git
        if (adapters.VcsAdapter?.ToLower() == "git")
        {
            // Git adapter typically doesn't need explicit auth config in this setup
            // as it uses system git config or SSH keys, but we could add validation here if needed
        }
    }
}
