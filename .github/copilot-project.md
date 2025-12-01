# Copilot Instructions (Project-Specific – Junior Dev)

## Project Overview
Junior Dev is a .NET-based platform for AI-assisted software development, featuring modular services (orchestrator, adapters for Jira/git/Semantic Kernel, UI) with typed action protocols, session isolation, and policy enforcement. Key files: `ARCHITECTURE.md`, `CONTRACTS.md`, `docs/module-plan.md`.

## Architecture Patterns
- **Modular Services**: Orchestrator coordinates adapters (workitems-jira, vcs-git, agents-sk, ui-shell). Adapters implement a single `IAdapter` (capability-based routing via `CanHandle/HandleCommand`) using contracts DTOs.
- **Action Protocol**: Use typed commands (e.g., `CreateBranch`, `RunTests`, `BuildProject`) and events (e.g., `CommandCompleted`, `ArtifactAvailable`) with `Correlation` for traceability.
- **Session Isolation**: One workspace per session; interactions via git artifacts/events. Avoid shared mutable state.
- **Policy Enforcement**: Central checks in orchestrator against `PolicyProfile` (whitelists, protected branches, rate limits). Reject with `CommandRejected` events.

## Developer Workflows
- **Build/Test**: Use `dotnet build` on `jDev.sln`, `dotnet test` for unit/integration. Contract changes require bumping `ContractVersion.Current` and doc updates.
- **UI Testing**: `dotnet run -- --test` for quick layout inspection (auto-exits after 2s). Use dummy chat client when no AI key is present.
- **Debugging**: Event logs are append-only; correlate via `Correlation` IDs. Surface throttling/conflicts in UI.
- **CI Guard**: Contract/architecture changes must update docs with date/reason (e.g., `CONTRACTS.md`/`ARCHITECTURE.md`). Guard scripts enforce this.
- **Version Control Discipline**: Commit/push when switching issues/bodies of work to keep state clean and shareable.
- **Issue Management**: Prefix issue titles with stage (e.g., “Envoy – …”). Create detailed issues for TODOs (requirements, considerations, acceptance).
- **TODO Management**: TODOs must reference issues: `// TODO: ... - Issue: #123`.
- **Dependencies**: Note blocked-by/blocks in issue bodies/comments for ordering.

## Copilot Workflow Rules (project)
- **Claiming Work**: When picking up an issue, comment on it and link the work. Reference the issue number in TODOs.
- **Branching Guidance**: Prefer feature branches `agent/<issue>-short-desc`. If working on `master`, include issue and rationale in commit.
- **Issue Closure**: Don't auto-close; wait for explicit go-ahead. Commit/push when transitioning between issues.
- **Commit Message Convention**: Include stage/codename when applicable and issue number (e.g., `Dock – fix: vcs adapter (#10)`).
- **Test Issue Investigation**: Investigate failing tests before changing them; tests are the expected-behavior source of truth.
- **Change verification policy**: Always verify code changes, edits, or test runs immediately after applying them using tools like read_file, run_in_terminal, or grep_search to confirm the actual state of the workspace before summarizing progress, reporting completion, or proceeding to the next step. Do not assume success based on tool call responses alone—explicitly check the codebase to prevent inaccurate reports.
- **Transparency**: Announce major changes (resets, refactors, pivots) and impact; get consent before disruptive actions.

## Conventions and Patterns
- **DTOs**: Sealed records (`WorkItemRef`, `SessionConfig`, `BuildProject`), `ICommand` with `Kind`.
- **Versioning**: Bump `ContractVersion` on schema changes; update `CONTRACTS.md` with rationale/date.
- **Testing Bias**: Over-test: serialization goldens, orchestrator scenarios, smoke tests (fakes by default), AI/integration opt-in.
- **Documentation Discipline**: Contracts/policy/architecture changes must update docs with timestamps/reasons.
- **Placeholder Implementations**: Use clear placeholders that log warnings; include TODOs with issue refs.

## Integration Points
- **External**: Jira (workitems), Git CLI (VCS), Semantic Kernel (agents), DevExpress (UI), optional OpenAI/Azure OpenAI.
- **Communication**: Event-driven; artifacts carry diffs/patches/logs; throttling via `Throttled` events.
- **UI Layout**: Chat-stream-first (accordion default), per-chat events, global artifacts dock; layout persists/resets.

## UI Development Guidelines
- Test with `dotnet run -- --test`; describe layout/visuals. Ensure dockable panels persist/reset.
- Avoid blocking dialogs in tests; set `IsTestMode = true` in automated UI tests.
- Accessibility: clear labels, shortcuts, logical tab order.
- Performance: UI should load in ~2–3s; test mode helps validate startup.

## AI/Integration Tests (project specifics)
- AI tests are opt-in: require `RUN_AI_TESTS=1` and valid creds (`OPENAI_API_KEY` or config). Provide dummy chat client for offline/test mode to prevent AIChatControl crashes.
- Live smoke/integration tests are env-gated; push disabled by default unless explicitly enabled.
- Never commit secrets; use env vars/user secrets locally and CI secrets in pipelines.

## Optional/Feature Opt-ins
- Build adapter is optional; hosts call `AddDotnetBuildAdapter()` (do not bake into core orchestrator to avoid cycles).
- UI→orchestrator integration is opt-in; wiring requires `ISessionManager` and config.
