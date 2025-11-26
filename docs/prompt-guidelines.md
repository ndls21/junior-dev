# Prompt/Preprompt Guidelines

- To understand the project, check prompt-guidelines.md, ARCHITECTURE.md, module-plan.md, and devplan.
- Assume contracts/architecture as defined in `ARCHITECTURE.md` and `CONTRACTS.md`. Deviations must be documented with date/reason and reflected in those docs (and ADRs if used) before merging.
- Default behavior: one session = one isolated workspace; interact with other sessions via git artifacts and session events.
- Use the action protocol: emit typed commands (e.g., `CreateBranch`, `ApplyPatch`, `RunTests`, `Commit`, `Push`, `TransitionTicket`, `Comment`, `UploadArtifact`, `RequestApproval`) and consume events/results; include correlation IDs.
- Respect policy profiles: check allowed commands, protected branches, required tests-before-push, approvals. If blocked, emit a rejection reason; do not bypass.
- Rate limits are centrally enforced; back off on `Throttled` events and surface retry timing.
- UI assumptions: columnar/dockable layout with session list (left), conversation/log (center), artifacts (right); surface blocking items (conflicts, approvals, throttling).
- Testing bias: prefer over-testing (unit, serialization/golden, scenario) and smoke tests gated by environment; avoid making assumptions about live services unless configured.
- Any change to contracts/policy/architecture without a doc/timestamp update is considered a violation; raise it in review.
