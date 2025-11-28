# Copilot Instructions for Junior Dev

## Project Overview
Junior Dev is a .NET-based platform for AI-assisted software development, featuring modular services (orchestrator, adapters for Jira/git/Semantic Kernel, UI) with typed action protocols, session isolation, and policy enforcement. Key files: `ARCHITECTURE.md`, `CONTRACTS.md`, `docs/module-plan.md`.

## Architecture Patterns
- **Modular Services**: Orchestrator coordinates adapters (workitems-jira, vcs-git, agents-sk, ui-shell). Adapters implement the single `IAdapter` (capability-based routing via `CanHandle/HandleCommand`) using contracts DTOs.
- **Action Protocol**: Use typed commands (e.g., `CreateBranch`, `RunTests`) and events (e.g., `CommandCompleted`, `ArtifactAvailable`) with `Correlation` for traceability. Example: `Commit` command includes `IncludePaths` and `Amend` flag.
- **Session Isolation**: One workspace per session; interactions via git artifacts/events. Avoid shared mutable state.
- **Policy Enforcement**: Central checks in orchestrator against `PolicyProfile` (whitelists, protected branches, rate limits). Reject with `CommandRejected` event.

## Developer Workflows
- **Build/Test**: Use `dotnet build` on `jDev.sln`, `dotnet test` for unit/integration. Contracts changes require version bump in `ContractVersion.Current` and doc updates.
- **UI Testing**: For automated UI inspection during development, use `dotnet run -- --test` to run the application in test mode. The UI will display for 2 seconds then automatically exit, allowing inspection of layout and functionality changes without manual intervention.
- **Debugging**: Event logs are append-only; correlate via `Correlation` IDs. Surface throttling/conflicts in UI.
- **CI Guard**: Any contract/architecture change must update docs with date/reason (e.g., in `CONTRACTS.md`). Enforce in pre-commit hooks.
- **Version Control Discipline**: Always `git commit` and `git push` when shifting gears into the next body of work to maintain a clean, shareable state and enable collaboration. Always `git commit` and `git push` when switching between GitHub issues to maintain a clean state and enable collaboration.
- **Issue Management**: Prefix issue titles with the current stage name (e.g., "Envoy – Feature Name") to indicate the development phase. Create detailed GitHub issues for all TODO items with implementation requirements, technical considerations, and acceptance criteria.
- **TODO Management**: When stubbing out functionality for later implementation, add detailed TODO comments with issue references. Format: `// TODO: [Brief description] - Issue: #[number]`. Create corresponding GitHub issues for tracking. TODOs should include implementation requirements, technical considerations, and acceptance criteria.
- **Dependencies**: When creating issues, note dependencies explicitly (e.g., "Blocked by #X", "Blocks #Y") in the body/comments so ordering is clear for developers.

## Copilot Workflow Rules
- **Claiming Work**: When Copilot (or any automated coding agent) picks up an issue to implement, it MUST open or link to a GitHub issue and add a comment indicating it has started work. The issue number MUST be referenced in any `TODO` comments added to the code (format: `// TODO: ... - Issue: #[number]`).
- **Branching Guidance**: By default prefer creating a feature branch named `agent/<issue-number>-short-desc` when implementing an issue. Working directly on `master` is allowed only when explicitly approved by the repository owner or maintainer and when the change is small, well-tested, and non-breaking. When working on `master`, include the issue number and rationale in the commit message.
- **Issue Closure - STRICT POLICY**: 
  - **NEVER** automatically close GitHub issues without explicit user confirmation.
  - **ALWAYS** wait for the user to say "NEXT!" before closing any issue.
  - When the user says "NEXT!", THEN close the current issue using `gh issue close <number> --reason completed`, merge the feature branch back to master if applicable, and proceed to identify the next priority.
  - Once a ticket is taken up for implementation, do **NOT** move on to another ticket until it has been confirmed closed with "NEXT!".
  - Also perform `git commit` and `git push` when transitioning between issues to maintain a clean, shareable state.
  - **SAFEGUARD**: Before any issue closure action, explicitly ask the user for confirmation and wait for their response.
- **Commit Message Convention**: Include the current stage or codename in the commit message prefix when applicable (e.g., `Envoy – feat: add reviewer agent` or `Dock – fix: vcs adapter`). This helps trace changes to module stages. If a GitHub issue was opened for the work, include the issue number in the commit message (e.g., `Envoy – feat: implement reviewer agent (#3)`).
- **Test Issue Investigation**: When encountering test failures, take the investigative route rather than immediately changing the test. Tests represent the source of truth for expected behavior. Only modify tests after confirming the error is in the test assumptions, not the implementation. If a test fails, first investigate whether the code behavior is incorrect or if the test expectations need updating. This ensures we build robust software where tests validate correct behavior, not accommodate bugs.
- **Transparency for Major Changes**: When making significant changes such as git resets, branch switches, major refactors, or pivoting from the planned approach, immediately inform the user what happened, why it was necessary, and what the impact is. Do not proceed with major changes without user awareness and consent.

## Conventions and Patterns
- **DTOs**: Use sealed records for immutability (e.g., `WorkItemRef`, `SessionConfig`). Interfaces like `ICommand` with `Kind` for polymorphism.
- **Versioning**: Bump `ContractVersion` on schema changes; update `CONTRACTS.md` with rationale.
- **Testing Bias**: Prefer over-testing: unit tests for serialization (golden files), scenario tests for orchestrator, smoke tests gated by env vars.
- **Documentation Discipline**: Changes to contracts/policy/architecture require doc updates (e.g., `ARCHITECTURE.md`, `CONTRACTS.md`) with timestamps.
- **Placeholder Implementation**: When implementing incomplete features, provide clear placeholder implementations that log warnings and return descriptive error messages. Include detailed TODO comments explaining what's needed for full implementation.

## Integration Points
- **External Dependencies**: Jira (workitems), Git CLI (VCS), Semantic Kernel (agents), DevExpress (UI dockable layout).
- **Communication**: Event-driven; artifacts carry diffs/patches/logs. Rate limits via `Throttled` events with backoff.
- **UI Layout**: Columnar dockable: sessions left, conversation center, artifacts right. Persist layout.

## UI Development Guidelines

- **Testing UI Changes**: Always test UI changes using `dotnet run -- --test` to verify layout and functionality without manual intervention. Describe what you observe (panel positions, control states, any visual issues).
- **Layout Verification**: When implementing UI features, verify dockable panels work correctly, layout persists between runs, and reset functionality restores defaults.
- **Error Reporting**: When UI issues occur, provide specific details: which panel/control is affected, what behavior was expected vs observed, any error messages, and current layout state.
- **Mock Data**: Use descriptive mock data that clearly indicates functionality (e.g., "🔄 Session 1 - Running" instead of generic placeholders).
- **Accessibility**: Ensure UI elements have clear labels, keyboard shortcuts where appropriate, and logical tab order.
- **Performance**: UI should load within 2-3 seconds; test mode helps verify startup performance.
- **Automated Testing**: When writing unit tests that create `MainForm` instances, always set `form.IsTestMode = true` to prevent modal dialogs from blocking test execution. Structure automated tests to avoid any modal dialogs or user interactions that would block test execution. This ensures clean automated testing without user interaction requirements.
