# Orchestrator

- Purpose: hub for sessions, policy enforcement, rate limiting, workspace provisioning, event logging, and routing commands to adapters.
- Structure: `src/` for core services (SessionManager, PolicyEnforcer, RateLimiter, WorkspaceProvider, EventLog, ArtifactStore); `tests/` for scenario/unit tests.
- Contracts: consumes `contracts` project DTOs; does not redefine schemas.
- Adapters: depends on interfaces for work items and VCS, injected via DI.
