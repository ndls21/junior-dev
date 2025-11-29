# Junior Dev Architecture

## Principles
- Modular, swappable services: work items (Jira-like), VCS (git/GitHub), agents (planner/executor/reviewer), UI.
- Central orchestrator owns sessions, policy enforcement, rate limiting, and event routing.
- Isolation by default: one workspace per session; coordination via git branches/patches and session events.
- Typed action protocol: commands → results → artifacts, all logged with correlation IDs for replay/audit.
- Documentation discipline: any contract/architecture deviation must update relevant docs (this file, CONTRACTS.md, ADRs) with date and reasoning.

## Modules
- `contracts`: shared DTOs/interfaces for commands, events, plans, policy profiles.
- `workitems-<provider>`: Jira first; supports get/update/comment/transition/assign/attach.
- `vcs-<provider>`: git CLI first; later GitHub/ADO. Supports branch/commit/push/diff/patch export.
- `agents-<host>`: Semantic Kernel host for planner/executor/reviewer agents, emitting commands.
- `orchestrator`: wires adapters via DI, enforces policy/rate limits, manages sessions, event log, artifacts.
- `ui-shell`: DevExpress chat/docking UI with columnar layout; sessions list, conversation/log, artifacts/tests.

## Action Protocol (summary)
- Commands are intents (e.g., `CreateBranch`, `ApplyPatch`, `RunTests`, `BuildProject`, `Commit`, `Push`, `TransitionTicket`, `Comment`, `UploadArtifact`, `RequestApproval`, `QueryBacklog`, `QueryWorkItem`).
- Results report success/failure with reasons; artifacts carry payloads (diffs, patches, logs, test output).
- All items carry correlation IDs and optional parent links to reconstruct flows; stored in an append-only session log.

## Command Flow (orchestrator path)
1) Agent or UI emits a Command with correlation/session metadata (e.g., `CreateBranch`, `ApplyPatch`, `RunTests`, `BuildProject`, `Commit`, `Push`, `TransitionTicket`, `Comment`, `QueryBacklog`, `QueryWorkItem`).
2) Orchestrator checks policy profile and rate limits; reject early with reasons if blocked.
3) Routed to the bound adapter (work items, VCS, build, agents, etc.) with workspace context.
4) Adapter returns results/artifacts; orchestrator emits events to the session log/UI.
5) Errors/conflicts/throttling become explicit events; retry/backoff is policy-driven.

## Policy & Guardrails
- Policy profiles are parametric per session/agent: allowed commands, protected branches, max files per commit, required tests-before-push, approval requirements.
- Enforced centrally in orchestrator before dispatch; rejected actions emit events with reasons.
- Power levels: planner/reviewer may be read-mostly; executor standard; privileged profiles by config only.

## Isolation & Workspaces
- Each session gets a dedicated working copy. Optional optimization: seed from a local, read-only baseline mirror to speed clones; detach after seed to keep isolation.
- No shared mutable workspace between agents; interaction happens via git artifacts and orchestrator events.

## Session Lifecycle
1) Create session with config (policy, workspace, repo, optional work item/plan node).
2) Provision workspace (clone from baseline, set branch), attach rate limits and logging.
3) Run agent/UI loop: commands → policy/rate-limit gate → adapters → events/artifacts.
4) Pause/resume/abort reflected via status events; approvals/conflicts block writes until resolved.
5) Complete or teardown: persist final log/artifacts; optionally clean workspace.

## Rate Limiting & Budgets
- Central limiter in orchestrator keyed by adapter and by session. Token bucket/leaky bucket with configurable caps per environment.
- Per-session budgets: API calls, runtime, LLM tokens. Emits `Throttled` events and backoff timing; retries use jittered backoff.

## Multi-Agent Future
- Default now: single-agent autonomous ticket flow.
- Future-proofing: sessions may reference a plan node and parent session; orchestrator can spawn child sessions later.
- Task plans (DAG) are stored and can be expanded by a planner agent later without changing core contracts.

### Chat Streams vs Sessions
- ChatStream: { ChatStreamId, SessionId, AgentHints, Status, CreatedAt }. Default mapping is 1:1 with a backend session; future multi-agent or spawned sessions can appear as additional streams.
- Event routing: UI subscribes per SessionId and delivers events to the owning chat stream only (uses Correlation.SessionId and IssuerAgentId for routing). Global artifacts remain filterable by SessionId/ChatStreamId.
- Commands originate from the active chat stream; responses/events stay in that stream’s feed.
## UI Layout (chat streams; dockable)
- **Chat Streams List/Tabs** (left or top): Shows active chat streams (usually 1:1 with backend sessions) with status chips. Filters for Running/Paused/NeedsApproval/Error/Completed.
- **Per-Chat Pane** (center, tabs/columns): Each chat stream hosts the DevExpress AI Chat control as the primary interaction surface plus its **own event feed** (command accepted/completed/rejected/throttled/conflict/artifact). Events are scoped to the stream’s session/correlation.
- **Global Artifacts Panel** (bottom/right dock): Repository for artifacts across streams; filterable by stream/session. Artifact links from chat/event feed open here.
- **Optional Right Dock**: Extra monitoring/metrics or a secondary chat pane for multi-monitor setups.
- Panels are dockable/tear-off; layout and chat stream arrangement are persisted with reset.

## Module Diagram (control/data flow)
```
[UI Shell]  ⇆  [Orchestrator]  ⇆  [Agents Host (SK)]
    │                 │                 │
    │            policy/rate            │
    │                 │                 │
    │                 ↓                 ↓
    │     [WorkItems Adapter]   [VCS Adapter]
    │                 │                 │
    │            (Jira, …)          (Git CLI, …)
    │                 │                 │
    │                 ↓                 ↓
    │          [Build Adapter]     [Other Adapters]
    │                 │                 │
    │            (dotnet CLI, …)   (future adapters)
```
- Orchestrator is the hub: enforces policy/rate limits, manages sessions/workspaces, logs events/artifacts.
- UI consumes session event streams; sends commands (approve, pause/resume, etc.).
- Agents host emits commands; listens to events to adapt.
- Adapters are swappable implementations of stable contracts.

## Adapter Registration Patterns

### **Core Orchestrator (Always Available)**
```csharp
services.AddOrchestrator(); // Registers fake adapters for testing/development
```

### **Optional Adapters (Host Application Choice)**
Build functionality is **optional** and must be explicitly registered by the host application:

```csharp
services.AddOrchestrator()           // Core orchestrator with fake adapters
        .AddDotnetBuildAdapter();     // Optional: adds real build functionality
```

This pattern maintains modular architecture where:
- Core orchestrator remains lightweight with fake adapters
- Real adapters are opt-in based on application needs
- Avoids circular dependencies between projects
- Allows different hosts to choose their adapter mix

## Interaction per session
```
Agent/UI -> Orchestrator: Command (CreateBranch, ApplyPatch, RunTests, BuildProject, ...)
Orchestrator -> Policy/Rate: Check
Policy/Rate -> Orchestrator: Allow/Reject/Throttle
Orchestrator -> Adapter: Invoke (VCS/WorkItem/Build)
Adapter -> Orchestrator: Result/Artifact/Conflict
Orchestrator -> EventLog: Append
Orchestrator -> UI/Agent: Event stream (CommandAccepted, ..., ArtifactAvailable)
```

## Contracts/Class Outline (C# sketch)
```
Contracts
  ICommand / CommandBase (Id, Correlation, Kind)
    CreateBranch, ApplyPatch, RunTests, BuildProject, Commit, Push,
    TransitionTicket, Comment, SetAssignee, UploadArtifact, RequestApproval, SpawnSession (future)
  IEvent / EventBase (Id, Correlation, Kind)
    CommandAccepted, CommandRejected, CommandCompleted,
    ArtifactAvailable, Throttled, ConflictDetected,
    SessionStatusChanged, PlanUpdated, BacklogQueried, WorkItemQueried
  TaskPlan / TaskNode
  PolicyProfile / RateLimits
  SessionConfig (session/workspace/policy/plan node refs)

Orchestrator interfaces
  ISessionManager (CreateSession, PublishCommand, Subscribe)
  IPolicyEnforcer, IRateLimiter, IWorkspaceProvider, IArtifactStore
  IAdapter (unified interface for work items, VCS, and other capabilities)
```

## Repo Layout (proposed)
```
/contracts                (shared DTOs, tests)
/orchestrator             (core session manager, policy/rate, workspace, event log, adapters)
/workitems-jira           (Jira impl + fake) ✅ Dev J complete
/vcs-git                  (git CLI impl + fake)
/agents/sk-host           (Semantic Kernel agents)
/ui-shell                 (DevExpress UI)
/docs                     (architecture, plans, setup, prompts)
/scripts                  (guards, helpers)
.github/workflows         (CI)
global.json, Directory.Packages.props, .editorconfig, .gitignore
```

## Agent Terminal Access & Skills

### **Current Architecture: Typed Commands via SK Functions**
Agents interact with the system through Semantic Kernel functions that emit typed commands:
- **VCS Operations**: `create_branch`, `commit`, `push` → CreateBranch, Commit, Push commands
- **Work Items**: `claim_item`, `comment`, `transition` → Claim, Comment, TransitionTicket commands  
- **General**: `upload_artifact`, `request_approval` → UploadArtifact, RequestApproval commands

### **Terminal Access Question**
Should agents have direct PowerShell terminal access, or should all operations go through the typed command system?

**Recommendation: Extend Typed Commands, Not Direct Terminal Access**

### **Why Not Direct Terminal Access?**
- **Security Risks**: Agents could execute dangerous commands (`rm -rf /`, `format c:`)
- **Lost Auditability**: Commands bypass event logging and correlation tracking
- **Policy Bypass**: Rate limits and command restrictions become ineffective
- **Testing Challenges**: Hard to mock/stub arbitrary terminal operations

### **Better Approach: Skills as Typed Commands**
Create new commands and corresponding SK functions for common terminal operations:

#### **Package Management Commands**
```csharp
// New commands for package operations
public sealed record InstallPackage(Guid Id, Correlation Correlation, string PackageManager, string PackageName, string? Version = null)
    : CommandBase(Id, Correlation, nameof(InstallPackage));

public sealed record RunScript(Guid Id, Correlation Correlation, string ScriptPath, string[] Args, TimeSpan? Timeout = null)
    : CommandBase(Id, Correlation, nameof(RunScript));

// SK Functions
[KernelFunction("install_package")]
public async Task<string> InstallPackageAsync(string packageManager, string packageName, string? version = null)

[KernelFunction("run_script")]  
public async Task<string> RunScriptAsync(string scriptPath, string[] args, int? timeoutSeconds = null)
```

#### **File System Commands**
```csharp
public sealed record CreateDirectory(Guid Id, Correlation Correlation, string Path)
    : CommandBase(Id, Correlation, nameof(CreateDirectory));

public sealed record CopyFiles(Guid Id, Correlation Correlation, string SourcePattern, string Destination)
    : CommandBase(Id, Correlation, nameof(CopyFiles));

// SK Functions
[KernelFunction("create_directory")]
public async Task<string> CreateDirectoryAsync(string path)

[KernelFunction("copy_files")]
public async Task<string> CopyFilesAsync(string sourcePattern, string destination)
```

### **Command Approval & Safety**
- **Safe Command Whitelist**: Only allow approved commands through adapters
- **Parameter Validation**: Strict validation of paths, commands, arguments
- **Sandboxing**: Run in controlled environments with limited permissions
- **Audit Trail**: All operations logged as events with correlation IDs

### **Adapter Pattern for Terminal Operations**
```csharp
public class TerminalAdapter : IAdapter
{
    public async Task HandleCommand(ICommand command, SessionState session)
    {
        switch (command)
        {
            case InstallPackage install:
                if (!IsApprovedPackageManager(install.PackageManager)) {
                    await EmitRejected(session, command, "Unapproved package manager");
                    return;
                }
                var result = await RunApprovedCommand(install);
                await EmitArtifact(session, command, result);
                break;
                
            case RunScript script:
                if (!IsSafeScriptPath(script.ScriptPath)) {
                    await EmitRejected(session, command, "Unsafe script path");
                    return;
                }
                // Execute with timeout and capture output
                break;
        }
    }
}
```

### **Implementation Plan**
1. **Define New Commands**: Add terminal-related commands to contracts
2. **Create Terminal Adapter**: Implement safe command execution
3. **Add SK Functions**: Bind commands to agent-callable functions  
4. **Policy Integration**: Add command restrictions to PolicyProfile
5. **Testing**: Comprehensive testing of command validation and execution

This approach maintains the typed, auditable command system while giving agents the flexibility they need for complex operations.

## Testing Posture
- Over-test bias: unit tests for contracts/serialization; adapter tests with fakes; orchestrator scenario tests (policy, rate limits, isolation); agent golden tests; smoke/integration gated by env vars for live services; UI component tests.
- CI guard: contracts changes require version bump, docs update (with date/reason), and refreshed tests.

## Documentation Discipline
- Any change to contracts, policies, or architecture must:
  - Update ARCHITECTURE.md/CONTRACTS.md (and ADRs if applicable) with date and rationale.
  - Note policy/profile changes impacting agents or UI.
  - Be enforced in code review/CI.
- Governance note (2025-11-26): contract/architecture deviations without synchronized doc/timestamp updates are considered violations; CI should block such changes.
- Update (2025-11-26): Dev J workitems-jira adapter implemented with fake in-memory and real REST client implementations, comprehensive unit tests added. Interface moved to adapters/common for shared access.
- Update (2025-11-28): Added QueryBacklog/QueryWorkItem commands and BacklogQueried/WorkItemQueried events to support work item queries for SK functions (list_backlog/get_item). Implemented fake query handling in orchestrator for testing; real adapter queries to be added later. Updated architecture to use unified IAdapter model instead of separate service interfaces. SK function bindings for list_backlog/get_item now fully implemented and tested.
- Update (2025-11-28): Clarified UI panel purposes and interactions. Separated AI Chat Panel (interactive AI conversations) from Event Stream Panel (system events). Updated documentation to reflect four-panel layout with clear responsibilities for each panel. Added comprehensive UI_PANELS_GUIDE.md for user interaction guidance.
- Update (2025-11-28): Designed multi-agent chat architecture supporting concurrent AI conversations. Proposed tabbed interface for AI Chat area with combined Monitoring & Artifacts panel. Analyzed panel utilities and screen real estate requirements for laptop usage. Created MULTI_AGENT_CHAT_DESIGN.md with implementation options.
- Update (2025-11-28): Revised multi-agent UI design to integrate per-agent event monitoring within each chat panel. Recognized that each AI agent generates its own event stream requiring dedicated monitoring per agent rather than global combined panel. Updated UI layout to use accordion approach with chat + events per agent, plus global artifacts panel. Alternative layouts explored: docked panels, split-panel, column-based, master-detail. Final decision: accordion layout as recommended default for optimal focus+overview balance.
- Update (2025-11-28): Analyzed agent terminal access requirements. Determined that direct PowerShell access poses security and auditability risks. Recommended extending typed command system with new commands for package management, file operations, and script execution. Proposed TerminalAdapter pattern with SK function bindings for safe, auditable terminal operations. Created GitHub issues #19-23 for implementation phases. Created issue #24 for multi-agent chat UI implementation with accordion layout as recommended default.
- Update (2025-11-29): Added BuildProject command and DotnetBuildAdapter to enable safe agent-accessible build functionality. Updated architecture to include build adapter in module diagram and command flow. Bumped contract version to v1.3 with proper security validation (path checking, target whitelisting, timeout enforcement) and artifact generation.
