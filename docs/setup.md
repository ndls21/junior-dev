# Setup & CI Skeleton

## Tooling lock-in
- .NET SDK: 8.x (pin via global.json). Require git CLI available on PATH.
- Semantic Kernel: pin via Directory.Packages.props once selected (document date/reason on change).
- DevExpress: pin major/minor release (e.g., v24.x); update docs on change; ensure licenses configured.
- Keep versions noted here and in onboarding tickets; any change updates docs with date/reason.

## Onboarding steps (Day 1)
1) Install .NET 8 SDK and git CLI; confirm `dotnet --info` and `git --version`.
2) Restore/build contracts project: `dotnet build jDev.sln`.
3) Read `ARCHITECTURE.md`, `CONTRACTS.md`, `docs/prompt-guidelines.md`, `docs/dev-plan.md`.
4) Configure DevExpress (license/sign-in) when working on UI.
5) For live Jira/git tests, set env vars (see below); otherwise tests run in fake/dry-run mode.

## CI skeleton (initial pipeline)
1) Restore + build: `dotnet build`.
2) Unit tests (exclude `[Category=Integration]` by default).
3) Contract guard: fail if contracts changed without docs/timestamp and golden refresh.
4) Optional nightly job: run integration/smoke when env vars present; push disabled by default.
Add a matrix later if multiple OS/SDK versions are needed.

## Baseline mirror (optional optimization)
- Purpose: speed workspace provisioning by cloning from a local read-only mirror.
- Create once: `git clone --mirror <repo-url> path/to/mirror` (on CI agent or shared dev machine).
- Use: when creating a session workspace, clone from mirror (or use `--reference`/`--dissociate`) then checkout the needed branch/ref. Each session still gets its own writable clone.
- If a ticket requires a specific branch or ref not in the mirror, fall back to cloning from origin directly. Document whether mirror is enabled or skipped on your environment.

## Env vars for live integration (examples)
- Jira: `JIRA_URL`, `JIRA_USER`, `JIRA_TOKEN`, `JIRA_PROJECT`
- Git: `GIT_REMOTE_URL` (read-only preferred), `GIT_PUSH_ENABLED` (default `false`)
- Optional: `SK_ENDPOINT`, `SK_API_KEY` for live LLM tests (not needed for fakes/goldens)
Tests should skip or run in fake mode when these are absent.

## Prompt discipline
- Use `docs/prompt-guidelines.md` as preprompt; include rule: any contract/architecture deviation requires doc update with date/reason before merge.

## Acceptance checklists
- See `docs/dev-plan.md` for stage-by-stage checklists; copy relevant items into Jira tickets to define “Done”.
