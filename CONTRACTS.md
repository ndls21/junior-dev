# Contracts (v1.1)

Versioning rule: bump contract version when changing shapes; update docs with date/reason. Contracts changes must ship with serialization tests and CI checks.

## Serialization Options
Contracts are serialized using System.Text.Json with the following options:
- PropertyNamingPolicy: CamelCase
- WriteIndented: true (for readability in tests)
- DefaultIgnoreCondition: WhenWritingNull (null values omitted)
- Converters: JsonStringEnumConverter (enums as strings)
- Encoder: UnsafeRelaxedJsonEscaping (allows special characters like > in strings)

## Core Types
- `SessionId`, `CommandId`, `EventId`, `PlanNodeId`: GUIDs/strings with stable casing.
- `WorkItemRef { string Id, string? ProviderHint }`
- `WorkItemSummary { string Id, string Title, string Status, string? Assignee }`
- `WorkItemDetails { string Id, string Title, string Description, string Status, string? Assignee, string[] Tags }`
- `RepoRef { string Name, string Path }`
- `WorkspaceRef { string Path }` (per-session workspace root)
- `Correlation { CommandId?, ParentCommandId?, PlanNodeId?, SessionId }`

## Commands (intent)
- `CreateBranch { RepoRef Repo, string BranchName, string? FromRef }`
- `ApplyPatch { RepoRef Repo, string PatchContent }`
- `RunTests { RepoRef Repo, string? Filter, TimeSpan? Timeout }`
- `Commit { RepoRef Repo, string Message, string[] IncludePaths, bool Amend=false }`
- `Push { RepoRef Repo, string BranchName }`
- `GetDiff { RepoRef Repo, string Ref = "HEAD" }`
- `TransitionTicket { WorkItemRef Item, string State }`
- `Comment { WorkItemRef Item, string Body }`
- `SetAssignee { WorkItemRef Item, string Assignee }`
- `UploadArtifact { string Name, string ContentType, byte[]/Stream Content }`
- `RequestApproval { string Reason, string[] RequiredActions }`
- `QueryBacklog { string? Filter }`
- `QueryWorkItem { WorkItemRef Item }`
- Reserved for future: `SpawnSession`, `LinkPlanNode`.

## Events (results/notifications)
- `CommandAccepted { CommandId }`
- `CommandRejected { CommandId, string Reason, string? PolicyRule }`
- `CommandCompleted { CommandId, Success|Failure, string? Message, string? ErrorCode }`
- `ArtifactAvailable { CommandId?, string Kind, Uri? DownloadUri, string? InlineText, string? PathHint }`
- `Throttled { string Scope, DateTimeOffset RetryAfter }`
- `ConflictDetected { RepoRef Repo, string Details, string? PatchContent }`
- `SessionStatusChanged { SessionId, Status, string? Reason }`
- `PlanUpdated { SessionId, Plan }`
- `BacklogQueried { WorkItemSummary[] Items }`
- `WorkItemQueried { WorkItemDetails Details }`

## Artifacts (by Kind)
- `Diff`, `Patch`, `TestResults`, `Log`, `Plan`, `ErrorReport`, `Conflict`.

## Plans (task graph)
- `TaskPlan { IReadOnlyList<TaskNode> Nodes }`
- `TaskNode { string Id, string Title, string[] DependsOn, WorkItemRef? WorkItem, string? AgentHint, string? SuggestedBranch, string[] Tags }`
- Initial flow may use a single-node plan derived from the ticket; richer DAGs can be emitted by a planner agent later.

## Policy Profiles
- `PolicyProfile { string Name, CommandWhitelist?, CommandBlacklist?, string[] ProtectedBranches, int? MaxFilesPerCommit, bool RequireTestsBeforePush, bool RequireApprovalForPush, string[] AllowedWorkItemTransitions, RateLimits? Limits }`
- `RateLimits { int? CallsPerMinute, int? Burst, Dictionary<string,int>? PerCommandCaps }`
- Applied per session/agent; enforced in orchestrator before dispatch; rejections emit `CommandRejected`.

## Sessions
- `SessionConfig { SessionId, string? ParentSessionId, PlanNodeId? PlanNodeId, PolicyProfile Policy, RepoRef Repo, WorkspaceRef Workspace, WorkItemRef? WorkItem, string AgentProfile }`
- `SessionEvent` stream delivers ordered events for UI and replay.

## Serialization & Compatibility
- Contracts are plain DTOs; serialization stable (JSON camelCase).
- Additive changes prefer optional fields; breaking changes require version bump and doc update (with date/reason).

## Documentation Discipline
- Any contract/schema change must update this file (with date/rationale) and ARCHITECTURE.md; add/adjust serialization tests; CI should enforce the rule.

## Change Log
- **2025-11-28**: Added QueryBacklog/QueryWorkItem commands and BacklogQueried/WorkItemQueried events to support work item queries via unified IAdapter model. Bumped version to v1.1. Removed legacy IVcsAdapter interface placeholder.
