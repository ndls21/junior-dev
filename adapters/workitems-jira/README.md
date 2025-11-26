# WorkItems - Jira Adapter

- Purpose: implements work item commands (get/update/comment/transition/assign/attach) against Jira.
- Structure: `src/` for adapter implementation; `tests/` for unit (fakes) and integration (env-var gated) tests.
- Notes: surface errors as `CommandRejected/CommandCompleted`; retry/backoff on 429/5xx; keep dry-run/fake for CI.
