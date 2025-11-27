using System;
using System.Collections.Generic;

namespace JuniorDev.Contracts;

public static class ContractVersion
{
    public const string Current = "v1";
}

public sealed record WorkItemRef(string Id, string? ProviderHint = null);

public sealed record WorkItemSummary(string Id, string Title, string Status, string? Assignee);

public sealed record WorkItemDetails(string Id, string Title, string Description, string Status, string? Assignee, IReadOnlyList<string> Tags);

public sealed record RepoRef(string Name, string Path);

public sealed record WorkspaceRef(string Path);

public sealed record Correlation(Guid SessionId, Guid? CommandId = null, Guid? ParentCommandId = null, string? PlanNodeId = null);

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

public sealed record RateLimits(int? CallsPerMinute, int? Burst, IReadOnlyDictionary<string, int>? PerCommandCaps);

public sealed record PolicyProfile(
    string Name,
    IReadOnlyList<string>? CommandWhitelist,
    IReadOnlyList<string>? CommandBlacklist,
    IReadOnlyList<string> ProtectedBranches,
    int? MaxFilesPerCommit,
    bool RequireTestsBeforePush,
    bool RequireApprovalForPush,
    IReadOnlyList<string>? AllowedWorkItemTransitions,
    RateLimits? Limits);

public sealed record SessionConfig(
    Guid SessionId,
    Guid? ParentSessionId,
    string? PlanNodeId,
    PolicyProfile Policy,
    RepoRef Repo,
    WorkspaceRef Workspace,
    WorkItemRef? WorkItem,
    string AgentProfile);

public interface IVcsAdapter
{
    // Methods for VCS operations
}
