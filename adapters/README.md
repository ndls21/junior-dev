# Adapters

- WorkItems: Jira adapter first (`adapters/workitems-jira`), swappable for other providers.
- VCS: Git CLI adapter first (`adapters/vcs-git`), swappable for GitHub/ADO later.
- Each adapter directory has `src/` and `tests/` and should ship fakes for CI and integration tests gated by env vars.
