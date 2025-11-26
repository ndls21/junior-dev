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
- Commands are intents (e.g., `CreateBranch`, `ApplyPatch`, `RunTests`, `Commit`, `Push`, `TransitionTicket`, `Comment`, `UploadArtifact`, `RequestApproval`).
- Results report success/failure with reasons; artifacts carry payloads (diffs, patches, logs, test output).
- All items carry correlation IDs and optional parent links to reconstruct flows; stored in an append-only session log.

## Command Flow (orchestrator path)
1) Agent or UI emits a Command with correlation/session metadata.
2) Orchestrator checks policy profile and rate limits; reject early with reasons if blocked.
3) Routed to the bound adapter (work items, VCS, agents, etc.) with workspace context.
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

## UI Layout (columnar/dockable)
- Left dock: sessions list with status chips (Running/Paused/NeedsApproval/Error) and filters.
- Center document: active session conversation + event log inline.
- Right dock: artifacts (diffs/patches/test results/logs). Panels dockable/tear-off for multi-screen layouts; layout persisted with reset option.

## Testing Posture
- Over-test bias: unit tests for contracts/serialization; adapter tests with fakes; orchestrator scenario tests (policy, rate limits, isolation); agent golden tests; smoke/integration gated by env vars for live services; UI component tests.
- CI guard: contracts changes require version bump, docs update (with date/reason), and refreshed tests.

## Documentation Discipline
- Any change to contracts, policies, or architecture must:
  - Update ARCHITECTURE.md/CONTRACTS.md (and ADRs if applicable) with date and rationale.
  - Note policy/profile changes impacting agents or UI.
  - Be enforced in code review/CI.
- Governance note (2025-11-26): contract/architecture deviations without synchronized doc/timestamp updates are considered violations; CI should block such changes.
