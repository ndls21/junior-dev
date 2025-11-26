# Copilot Instructions for Junior Dev

## Project Overview
Junior Dev is a .NET-based platform for AI-assisted software development, featuring modular services (orchestrator, adapters for Jira/git/Semantic Kernel, UI) with typed action protocols, session isolation, and policy enforcement. Key files: `ARCHITECTURE.md`, `CONTRACTS.md`, `docs/module-plan.md`.

## Architecture Patterns
- **Modular Services**: Orchestrator coordinates adapters (workitems-jira, vcs-git, agents-sk, ui-shell). Each adapter implements interfaces from `contracts/src/Contracts/Contracts.cs`.
- **Action Protocol**: Use typed commands (e.g., `CreateBranch`, `RunTests`) and events (e.g., `CommandCompleted`, `ArtifactAvailable`) with `Correlation` for traceability. Example: `Commit` command includes `IncludePaths` and `Amend` flag.
- **Session Isolation**: One workspace per session; interactions via git artifacts/events. Avoid shared mutable state.
- **Policy Enforcement**: Central checks in orchestrator against `PolicyProfile` (whitelists, protected branches, rate limits). Reject with `CommandRejected` event.

## Developer Workflows
- **Build/Test**: Use `dotnet build` on `jDev.sln`, `dotnet test` for unit/integration. Contracts changes require version bump in `ContractVersion.Current` and doc updates.
- **Debugging**: Event logs are append-only; correlate via `Correlation` IDs. Surface throttling/conflicts in UI.
- **CI Guard**: Any contract/architecture change must update docs with date/reason (e.g., in `CONTRACTS.md`). Enforce in pre-commit hooks.
- **Version Control Discipline**: Always `git commit` and `git push` when shifting gears into the next body of work to maintain a clean, shareable state and enable collaboration.

## Conventions and Patterns
- **DTOs**: Use sealed records for immutability (e.g., `WorkItemRef`, `SessionConfig`). Interfaces like `ICommand` with `Kind` for polymorphism.
- **Versioning**: Bump `ContractVersion` on schema changes; update `CONTRACTS.md` with rationale.
- **Testing Bias**: Prefer over-testing: unit tests for serialization (golden files), scenario tests for orchestrator, smoke tests gated by env vars.
- **Documentation Discipline**: Changes to contracts/policy/architecture require doc updates (e.g., `ARCHITECTURE.md`, `CONTRACTS.md`) with timestamps.

## Integration Points
- **External Dependencies**: Jira (workitems), Git CLI (VCS), Semantic Kernel (agents), DevExpress (UI dockable layout).
- **Communication**: Event-driven; artifacts carry diffs/patches/logs. Rate limits via `Throttled` events with backoff.
- **UI Layout**: Columnar dockable: sessions left, conversation center, artifacts right. Persist layout.

## Key Files
- `contracts/src/Contracts/Contracts.cs`: Core DTOs/commands/events.
- `ARCHITECTURE.md`: Principles, modules, protocols.
- `CONTRACTS.md`: Schema details, versioning rules.
- `docs/module-plan.md`: MVP deliverables per module.