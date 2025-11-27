# Overarching Development Plan (staged, with prerequisites and test gates)

Use this to coordinate work across modules. Each stage lists what must be ready, dependencies, and tests to run before progressing.

## Stage A: Foundations (Codename: Atlas)
- Scope: repo layout, docs (ARCHITECTURE/CONTRACTS/prompt/module plans), baseline git mirror for workspace seeding, CI skeleton.
- Dependencies: none.
- Tests: build contracts project compiles; CI pipeline runs lint/build placeholders.
- Exit: docs present; scaffolded folder structure in place; baseline mirror configured (or explicitly deferred with fallback to direct clone); CI green.

## Stage B: Contracts Stable (Codename: Beacon)
- Scope: finalize v1 DTOs, serialization settings, versioning rules.
- Dependencies: Stage A.
- Tests: JSON round-trip/golden tests for commands/events/plan/policy; nullability checks; contract-change guard script (fails if docs/timestamps not updated).
- Exit: contract goldens checked in; guard enforced in CI; change policy documented.

## Stage C: Orchestrator Core + Fakes (Codename: Conductor)
- Scope: DI scaffold, in-memory event log, policy gate, rate limiter, workspace provisioner (isolated clone from baseline), fake adapters (work items, git).
- Dependencies: Stage B.
- Tests: scenario with fakes (command accepted, rejected by policy, throttled event); workspace isolation per session; event ordering; pause/resume status events stubbed.
- Exit: orchestrator can run a fake session end-to-end with logged events; rate/policy gates enforce rules.

## Stage D: Adapters (Jira, Git CLI) (Codename: Dock)
- Scope: Jira adapter (comment/transition/assign/attach) with fake; git CLI adapter (branch/diff/patch/apply/commit/push) with dry-run and conflict surfacing.
- Dependencies: Stage C (orchestrator interfaces), Stage B (contracts).
- Tests: unit with fakes; temp-repo scenarios for git (protected branch guard, max-files-per-commit, dry-run); Jira integration gated by env vars; retry/backoff simulation for 429/5xx; conflict emits `ConflictDetected`.
- Exit: adapters usable by orchestrator; dry-run paths verified; integration tests optional but runnable with creds.

## Stage E: Agents (Semantic Kernel) (Codename: Envoy)
- Scope: SK host; executor agent emits commands; planner/reviewer profiles; policy/throttle awareness; correlation IDs preserved.
- Dependencies: Stage C (orchestrator API), Stage B (contracts).
- Tests: golden command emissions for canned tickets; blocked command surfaced to user; throttle backoff reflected; planner emits trivial plan update; reviewer emits comments without writes.
- Exit: agents drive fake-orchestrator loop producing commands/events deterministically with seeds.

## Stage F: UI Shell (Codename: Facade)
- Scope: DevExpress dockable layout (sessions list left, conversation/log center, artifacts right); layout persistence/reset; status chips; blocking banners (approval/conflict/throttled); multi-session controls.
- Dependencies: Stage C event stream contract; Stage B contracts.
- Tests: component/layout snapshot; persistence round-trip; render fixtures for events/banners; interactions for approve/pause/resume/switch; artifact viewers render diff/log fixtures.
- Exit: UI can attach to mock event stream and control mock sessions; layouts persist/reset.

## Stage G: End-to-End Smoke (Codename: Gauntlet)
- Scope: Harness runner; dry-run E2E with fakes; optional live Jira/git (push off by default).
- Dependencies: Stages C, D, E (orchestrator+adapters+agents), F (UI optional for manual runs).
- Tests: E2E “ticket → plan → branch → patch → commit (no push) → comment/transition” with fakes; assert events/artifacts sequence; live run gated by env vars; throttling/backoff observed; push flag gating validated.
- Exit: Repeatable smoke run; reports artifacts; safe defaults (no push) in live mode.

## Module Alignment with Stages
- Contracts (Beacon): completes in Stage B; consumed by all other stages.
- Orchestrator (Conductor): starts Stage C; must expose stable interfaces before adapters/agents/UI integrate.
- Adapters (Dock): align to Stage D; ready for orchestrator integration late C/early D; provide fakes for C.
- Agents (Envoy): align to Stage E; use fake adapters/orchestrator mocks first; integrate with real adapters after Dock.
- UI (Facade): align to Stage F; consume event stream contracts from Conductor; use mocked streams until Dock/Envoy are ready.
- Smoke (Gauntlet): depends on Conductor+Dock+Envoy; UI optional but helpful for manual verification.

See `docs/module-plan.md` for per-module staged milestones and tests; use this section to time integrations.

## Expected Module Stage by Phase (reference to module Stage 0/1/2)
- Atlas: repo scaffolding only; no module stages required (optional: create empty projects/READMEs).
- Beacon: Contracts should reach module Stage 1–2 (goldens + guard). Others remain at Stage 0 or unstarted.
- Conductor: Orchestrator to module Stage 0–1 (DI/event log/policy+rate, fakes). Adapters provide Stage 0 fakes. Agents/UI can stub Stage 0 if helpful for integration tests.
- Dock: Adapters advance to Stage 1 (real ops + dry-run/conflict paths), aiming for Stage 2 by end of Dock. Orchestrator finishes Stage 2 (artifact store/status/approvals). Contracts stable.
- Envoy: Agents reach Stage 1 (planner/reviewer with policy/throttle awareness), aiming for Stage 2 (plan updates). Adapters should be at least Stage 1. UI can progress to Stage 1 using mocked/real streams.
- Facade: UI completes Stage 1–2 (layout, blockers, multi-session). Agents/Adapters/Orchestrator should be stable enough to feed real event streams.
- Gauntlet: All core modules at Stage 2 readiness; smoke harness runs end-to-end with fakes by default, live optional.

## Module Stage Definitions (summary)
- Contracts: Stage 0 = DTOs compile; Stage 1 = JSON goldens/round-trips; Stage 2 = contract guard in CI with doc/timestamp enforcement.
- Orchestrator: Stage 0 = DI + in-memory event log + fakes; Stage 1 = policy/rate gates + workspace provisioner; Stage 2 = artifact store + status/pause/resume/approvals.
- WorkItems-Jira: Stage 0 = interface + fake; Stage 1 = real Jira ops gated by env vars, retries; Stage 2 = error/throttle surfacing, pagination robustness.
- VCS-Git: Stage 0 = interface + fake; Stage 1 = git CLI branch/diff/patch/commit/push with dry-run, guards; Stage 2 = conflict detection + patch export/import.
- Agents-SK: Stage 0 = executor emits commands; Stage 1 = planner/reviewer with policy/throttle awareness; Stage 2 = plan updates (single-node → DAG stub).
- UI-Shell: Stage 0 = layout skeleton + persistence/reset; Stage 1 = render event stream, status chips, blocking banners; Stage 2 = multi-session controls, artifact viewers, shortcuts.
- Smoke/Integration: Stage 0 = harness scaffold; Stage 1 = E2E with fakes (no push); Stage 2 = opt-in live Jira/git, push disabled by default.

## Per-Stage Module Expectations (module Stage 0/1/2)
- Atlas:
  - Modules: Contracts optional Stage 0 scaffold; others unstarted (or shells/READMEs).
  - Integration: none.
  - Tests: build/CI smoke only.
- Beacon:
  - Modules: Contracts to Stage 1–2 (goldens + guard). Orchestrator/Adapters/Agents/UI/Smoke at Stage 0; fakes optional.
  - Integration: contracts referenced only.
  - Tests: contract goldens/guard; no cross-module wiring.
- Conductor:
  - Modules: Orchestrator Stage 0–1 (DI, event log, policy/rate, workspace). Adapters supply Stage 0 fakes. Agents/UI Stage 0 stubs. Contracts stay Stage 2.
  - Integration: orchestrator + contracts + fake adapters; event stream visible.
  - Tests: fake end-to-end, policy/rate gating, workspace isolation.
- Dock:
  - Modules: Adapters to Stage 1 aiming Stage 2 (real ops, dry-run/conflicts). Orchestrator finishes Stage 2. Agents Stage 0–1. UI Stage 0–1. Smoke Stage 0 scaffold.
  - Integration: orchestrator + real adapters (git/Jira) via env configs; dry-run by default.
  - Tests: temp-repo scenarios, conflict surfacing, env-gated Jira happy/negative paths.
- Envoy:
  - Modules: Agents Stage 1 aiming Stage 2 (plan updates). Adapters ≥ Stage 1. Orchestrator Stage 2. UI Stage 1. Smoke Stage 1 (fakes E2E, no push).
  - Integration: agents with orchestrator using fakes first, then real adapters; policy/throttle round-trip.
  - Tests: golden command emission, blocked/throttled flows, plan update events.
- Facade:
  - Modules: UI to Stage 1–2 (layout, blockers, multi-session). Agents/Adapters/Orchestrator Stage 2. Smoke Stage 1–2 prep.
  - Integration: UI consumes event stream from orchestrator (fakes or dry-run adapters).
  - Tests: fixtures from recorded streams; UI interactions dispatch to orchestrator stubs.
- Gauntlet:
  - Modules: All core at Stage 2; Smoke Stage 2 (live opt-in, push off by default).
  - Integration: full stack with fakes by default; optional live Jira/git.
  - Tests: E2E harness on event/command sequence, artifacts captured, push flag respected; throttling/backoff in live mode.

## Status Checkpoint (2025-11-26)
- Completed: Beacon (contracts stable with goldens/guard); Conductor (orchestrator with policy/rate/lifecycle, fakes); Dock baseline (git adapter with conflict handling, Jira adapter with retries/backoff and CommandRejected mapping, DI helper to choose fake vs real).
- Pending Dock follow-ups: env-gated integration tests for Jira/git; richer git conflict/artifact coverage; align DI wiring into orchestrator startup.
- Next active stage: Envoy (agents).

## Test Deployment per Stage
- Stage A: CI build/check scripts only.
- Stage B: Contract golden tests in CI; guard script wired.
- Stage C: Orchestrator scenario tests in CI; workspace isolation test uses temp dirs.
- Stage D: Adapter unit tests in CI; integration marked `[Category=Integration]` and skipped unless env vars set.
- Stage E: Agent golden tests in CI; deterministic seeds; throttle/policy fixtures.
- Stage F: UI component/tests in CI; snapshots updated intentionally.
- Stage G: Smoke runner in CI as opt-in (env flag); default to fakes; nightly optional live job.

## Cross-Module Prereqs
- Contracts (B) gate everything else; changes require doc/test updates.
- Orchestrator (C) must expose stable command/event interfaces before adapters/agents/UI consume them.
- UI (F) depends on event stream shapes; coordinate snapshots when contracts change.
- Agents (E) require policy/throttle signals from orchestrator; test against fakes before real adapters.
- Smoke (G) waits for minimal adapter + orchestrator readiness; can run with fakes earlier for drift detection.

## Governance Reminders
- Any contract/architecture change must update docs with date/reason; CI guard enforces.
- Default isolation: one workspace per session; no shared mutable FS.
- Centralized rate limiting/policy gates live in orchestrator; adapters should remain lean and report errors cleanly.

## Envoy Development Plan (agents)
- Orchestrator selection: executor can run standalone for simple tickets; planner/reviewer are optional add-ons invoked by orchestrator policy for more complex work; reviewer runs in a separate optional loop.
- Executor agent:
  - Tasks: map chat/ticket context to commands (CreateBranch, ApplyPatch, Commit, Push, Comment, Transition); honor correlation IDs and policy/throttle signals; support dry-run flag; handle Throttled/CommandRejected gracefully.
  - Tests: golden command emission for canned tickets; assert blocked/throttled surfaced; correlation preserved.
- Planner agent:
  - Tasks: emit single-node TaskPlan now; stub for future DAG; suggest branch names respecting protected list; emit PlanUpdated.
  - Tests: golden planner outputs; PlanUpdated content stable; branch suggestion avoids protected branches.
- Reviewer agent:
  - Tasks: consume ArtifactAvailable (diffs/logs); emit Comment/SetAssignee/Transition (review state); no VCS writes.
  - Tests: golden review comments for canned diffs; ensure no write commands; policy compliance.
- Integration:
  - Run against orchestrator + fake adapters first; no real Jira/git required. Mark any live/LLM/integration tests as `[Category=Integration]` and env-gate. Keep prompts deterministic (seeded) for goldens.

## Stage Acceptance Checklists (for tickets/Jira)
- Stage A: docs in repo; tool versions noted; CI skeleton runs; baseline mirror plan documented (or explicitly skipped).
- Stage B: contract goldens added; guard script enabled in CI; CONTRACTS/ARCHITECTURE updated with date/reason.
- Stage C: orchestrator runs fake end-to-end scenario; policy + rate limit gates reject/emit events; isolated workspace creation tested.
- Stage D: adapters handle happy path with fakes; git temp-repo tests cover protected branch/max files; Jira integration gated; conflict emits event.
- Stage E: agents emit deterministic commands with seeds; policy/throttle signals surfaced; planner emits plan update stub.
- Stage F: UI renders mocked event stream with badges/banners; layout persists/resets; controls dispatch actions in tests.
- Stage G: smoke runner executes dry-run E2E with fakes; optional live run gated by env; push disabled by default; artifacts saved.
