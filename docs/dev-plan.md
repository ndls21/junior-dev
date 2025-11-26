# Overarching Development Plan (staged, with prerequisites and test gates)

Use this to coordinate work across modules. Each stage lists what must be ready, dependencies, and tests to run before progressing.

## Stage A: Foundations
- Scope: repo layout, docs (ARCHITECTURE/CONTRACTS/prompt/module plans), baseline git mirror for workspace seeding, CI skeleton.
- Dependencies: none.
- Tests: build contracts project compiles; CI pipeline runs lint/build placeholders.
- Exit: docs present; baseline mirror configured (or explicitly deferred with fallback to direct clone); CI green.

## Stage B: Contracts Stable
- Scope: finalize v1 DTOs, serialization settings, versioning rules.
- Dependencies: Stage A.
- Tests: JSON round-trip/golden tests for commands/events/plan/policy; nullability checks; contract-change guard script (fails if docs/timestamps not updated).
- Exit: contract goldens checked in; guard enforced in CI; change policy documented.

## Stage C: Orchestrator Core + Fakes
- Scope: DI scaffold, in-memory event log, policy gate, rate limiter, workspace provisioner (isolated clone from baseline), fake adapters (work items, git).
- Dependencies: Stage B.
- Tests: scenario with fakes (command accepted, rejected by policy, throttled event); workspace isolation per session; event ordering; pause/resume status events stubbed.
- Exit: orchestrator can run a fake session end-to-end with logged events; rate/policy gates enforce rules.

## Stage D: Adapters (Jira, Git CLI)
- Scope: Jira adapter (comment/transition/assign/attach) with fake; git CLI adapter (branch/diff/patch/apply/commit/push) with dry-run and conflict surfacing.
- Dependencies: Stage C (orchestrator interfaces), Stage B (contracts).
- Tests: unit with fakes; temp-repo scenarios for git (protected branch guard, max-files-per-commit, dry-run); Jira integration gated by env vars; retry/backoff simulation for 429/5xx; conflict emits `ConflictDetected`.
- Exit: adapters usable by orchestrator; dry-run paths verified; integration tests optional but runnable with creds.

## Stage E: Agents (Semantic Kernel)
- Scope: SK host; executor agent emits commands; planner/reviewer profiles; policy/throttle awareness; correlation IDs preserved.
- Dependencies: Stage C (orchestrator API), Stage B (contracts).
- Tests: golden command emissions for canned tickets; blocked command surfaced to user; throttle backoff reflected; planner emits trivial plan update; reviewer emits comments without writes.
- Exit: agents drive fake-orchestrator loop producing commands/events deterministically with seeds.

## Stage F: UI Shell
- Scope: DevExpress dockable layout (sessions list left, conversation/log center, artifacts right); layout persistence/reset; status chips; blocking banners (approval/conflict/throttled); multi-session controls.
- Dependencies: Stage C event stream contract; Stage B contracts.
- Tests: component/layout snapshot; persistence round-trip; render fixtures for events/banners; interactions for approve/pause/resume/switch; artifact viewers render diff/log fixtures.
- Exit: UI can attach to mock event stream and control mock sessions; layouts persist/reset.

## Stage G: End-to-End Smoke
- Scope: Harness runner; dry-run E2E with fakes; optional live Jira/git (push off by default).
- Dependencies: Stages C, D, E (orchestrator+adapters+agents), F (UI optional for manual runs).
- Tests: E2E “ticket → plan → branch → patch → commit (no push) → comment/transition” with fakes; assert events/artifacts sequence; live run gated by env vars; throttling/backoff observed; push flag gating validated.
- Exit: Repeatable smoke run; reports artifacts; safe defaults (no push) in live mode.

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

## Stage Acceptance Checklists (for tickets/Jira)
- Stage A: docs in repo; tool versions noted; CI skeleton runs; baseline mirror plan documented (or explicitly skipped).
- Stage B: contract goldens added; guard script enabled in CI; CONTRACTS/ARCHITECTURE updated with date/reason.
- Stage C: orchestrator runs fake end-to-end scenario; policy + rate limit gates reject/emit events; isolated workspace creation tested.
- Stage D: adapters handle happy path with fakes; git temp-repo tests cover protected branch/max files; Jira integration gated; conflict emits event.
- Stage E: agents emit deterministic commands with seeds; policy/throttle signals surfaced; planner emits plan update stub.
- Stage F: UI renders mocked event stream with badges/banners; layout persists/resets; controls dispatch actions in tests.
- Stage G: smoke runner executes dry-run E2E with fakes; optional live run gated by env; push disabled by default; artifacts saved.
