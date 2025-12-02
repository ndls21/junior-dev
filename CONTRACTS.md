# Contracts (v1.4)

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
- `Correlation { SessionId, CommandId?, ParentCommandId?, PlanNodeId?, IssuerAgentId? }`

## Commands (intent)
- `CreateBranch { RepoRef Repo, string BranchName, string? FromRef }`
- `ApplyPatch { RepoRef Repo, string PatchContent }`
- `RunTests { RepoRef Repo, string? Filter, TimeSpan? Timeout }`
- `Commit { RepoRef Repo, string Message, string[] IncludePaths, bool Amend=false }`
- `Push { RepoRef Repo, string BranchName }`
- `GetDiff { RepoRef Repo, string Ref = "HEAD" }`
- `BuildProject { RepoRef Repo, string ProjectPath, string? Configuration, string? TargetFramework, string[]? Targets, TimeSpan? Timeout }`
- `TransitionTicket { WorkItemRef Item, string State }`
- `Comment { WorkItemRef Item, string Body }`
- `SetAssignee { WorkItemRef Item, string Assignee }`
- `ClaimWorkItem { WorkItemRef Item, string Assignee, TimeSpan? ClaimTimeout }` - Exclusively claim a work item with optional timeout
- `ReleaseWorkItem { WorkItemRef Item, string Reason }` - Release a claimed work item
- `RenewClaim { WorkItemRef Item, TimeSpan? Extension }` - Renew/extend a claim's expiration time
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
- `WorkItemClaimed { WorkItemRef Item, string Assignee, DateTimeOffset ExpiresAt }` - Work item successfully claimed
- `WorkItemClaimReleased { WorkItemRef Item, string Reason }` - Work item claim released
- `ClaimRenewed { WorkItemRef Item, DateTimeOffset NewExpiresAt }` - Claim expiration extended
- `ClaimExpired { WorkItemRef Item, string PreviousAssignee }` - Claim automatically expired due to timeout

## Enums
- `CommandOutcome { Success, Failure }`
- `SessionStatus { Unknown, Running, Paused, NeedsApproval, Error, Completed }`
- `ClaimResult { Success, AlreadyClaimed, Rejected, UnknownError }`

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

## Configuration
- `AdaptersConfig { string WorkItemsAdapter, string VcsAdapter, string TerminalAdapter, string? BuildAdapter }` - Adapter selection (build adapter is opt-in)
- `PolicyConfig { Dictionary<string,PolicyProfile> Profiles, string DefaultProfile, RateLimits GlobalLimits }` - Policy profiles and global limits
- `WorkItemConfig { TimeSpan DefaultClaimTimeout, int MaxConcurrentClaimsPerAgent, int MaxConcurrentClaimsPerSession, TimeSpan ClaimRenewalWindow, bool AutoReleaseOnInactivity, TimeSpan CleanupInterval }` - Work item claim management settings (default: 2h timeout, 3 per agent, 5 per session, 30min renewal window, 5min cleanup interval)

## Serialization & Compatibility
- Contracts are plain DTOs; serialization stable (JSON camelCase).
- Additive changes prefer optional fields; breaking changes require version bump and doc update (with date/reason).

## Documentation Discipline
- Any contract/schema change must update this file (with date/rationale) and ARCHITECTURE.md; add/adjust serialization tests; CI should enforce the rule.

## Change Log
- **2025-12-02**: Added work item claim protocol with ClaimWorkItem/ReleaseWorkItem/RenewClaim commands, WorkItemClaimed/WorkItemClaimReleased/ClaimRenewed/ClaimExpired events, ClaimResult enum, and WorkItemConfig for claim management. Bumped version to v1.4. Implements exclusive claim locking with configurable timeouts and concurrency limits.
- **2025-11-30**: Added BuildProject command for typed build operations with optional Configuration and Target parameters. Bumped version to v1.3. Build adapter is opt-in via AdaptersConfig.BuildAdapter setting.
- **2025-11-28**: Added IssuerAgentId to Correlation record for proper command response routing. Bumped version to v1.2. Response events (CommandCompleted, CommandRejected, etc.) now only route to the originating agent instead of broadcasting to all agents in the session.
- **2025-11-28**: Added QueryBacklog/QueryWorkItem commands and BacklogQueried/WorkItemQueried events to support work item queries via unified IAdapter model. Bumped version to v1.1. Removed legacy IVcsAdapter interface placeholder.
- **2025-11-28**: Implemented SK function bindings for list_backlog/get_item in OrchestratorFunctionBindings, completing end-to-end work item query functionality.
