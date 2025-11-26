# Module Plans (staged milestones + tests and guidance)

Use these as hand-offs to juniors: each stage lists goal, deliverables, and tests to write/pass before moving on.

## contracts
- Stage 0: Goal: compile-ready DTOs and version constant.
  - Deliverables: `Contracts.csproj`, DTOs, version string.
  - Tests: build succeeds.
- Stage 1: Goal: stable wire format.
  - Deliverables: JSON serialization/golden fixtures for commands/events/plan/policy.
  - Tests: round-trip golden snapshots, nullability coverage.
- Stage 2: Goal: guard contract drift.
  - Deliverables: CI rule (script) failing if contracts change without docs/timestamp and refreshed goldens.
  - Tests: rule enforcement test (e.g., diff detection), golden update required on schema change.

## orchestrator
- Stage 0: Goal: basic plumbing.
  - Deliverables: DI scaffold, in-memory event log, fake adapters.
  - Tests: event ordering, command acceptance/echo via fakes.
- Stage 1: Goal: enforce policy/rate limits + isolation.
  - Deliverables: policy gate, rate limiter, workspace provisioner (isolated clone from baseline).
  - Tests: blocked command emits `CommandRejected`; throttled emits `Throttled`; separate sessions get separate workspaces.
- Stage 2: Goal: operational lifecycle.
  - Deliverables: artifact store, status transitions, pause/resume/abort, approvals gating.
  - Tests: scenario flow (command → adapter → events), pause/resume respected, teardown cleans workspace.

## workitems-jira adapter
- Stage 0: Goal: contract-fit shim.
  - Deliverables: interface + in-memory fake.
  - Tests: fake returns expected transitions/comments; serialization-safe outputs.
- Stage 1: Goal: real Jira ops.
  - Deliverables: client with comment/transition/assign/attach; env-var gated.
  - Tests: unit with fakes; integration (if env set) for happy path; retry/backoff on 429/5xx simulated.
- Stage 2: Goal: resilience/reporting.
  - Deliverables: error mapping to `CommandRejected/Completed`; pagination/field robustness.
  - Tests: negative cases (bad state, missing fields), throttling handling emits proper events.

## vcs-git adapter
- Stage 0: Goal: interface + fake.
  - Deliverables: fake repo/commit log.
  - Tests: branch/commit/push mocked flows, diff/patch echoes.
- Stage 1: Goal: real git CLI.
  - Deliverables: branch/create/apply patch/diff/commit/push; dry-run mode.
  - Tests: temp repo scenarios, protected branch guard, max-files-per-commit guard, dry-run no-op.
- Stage 2: Goal: conflict surfacing.
  - Deliverables: conflict detection/reporting; patch export/import.
  - Tests: forced conflicts emit `ConflictDetected` with patch; patch round-trip works.

## agents-sk
- Stage 0: Goal: executor agent emits commands.
  - Deliverables: host scaffold, deterministic prompts.
  - Tests: golden command emission for canned ticket; correlation IDs present.
- Stage 1: Goal: planner/reviewer + policy awareness.
  - Deliverables: planner/reviewer profiles; throttle/policy signal handling.
  - Tests: blocked command surfaced to user; throttle backoff respected in outputs.
- Stage 2: Goal: plan updates.
  - Deliverables: optional plan expansion (single-node → DAG stub) emitting `PlanUpdated`.
  - Tests: plan update event content stable; goldens for planner text.

## ui-shell
- Stage 0: Goal: layout skeleton.
  - Deliverables: DevExpress dockable layout (sessions list left, conversation/log center, artifacts right); layout persistence/reset.
  - Tests: component/layout snapshot; persistence round-trip.
- Stage 1: Goal: render event stream and blockers.
  - Deliverables: event rendering for commands/results/artifacts; status chips; blocking banners (approval/conflict/throttled).
  - Tests: render fixtures show correct badges/banners; approve/block interactions dispatch actions.
- Stage 2: Goal: multi-session ops.
  - Deliverables: pause/resume/abort/switch; artifact viewers (diff/test logs); shortcuts.
  - Tests: session switch maintains state; artifact viewer renders diff/log; controls invoke handlers.

## smoke/integration
- Stage 0: Goal: harness.
  - Deliverables: runner with env-var gating.
  - Tests: dry-run pipeline executes with fakes.
- Stage 1: Goal: E2E (local/fake).
  - Deliverables: “ticket → branch → patch → commit (no push) → comment/transition” using fakes.
  - Tests: assertions on emitted events/artifacts.
- Stage 2: Goal: opt-in live.
  - Deliverables: Jira/git live run (push disabled by default; flag to enable).
  - Tests: throttling/backoff observed; artifacts saved; push flag gating verified.
