# Copilot Instructions (Generic)

These guidelines are reusable across projects. Project-specific guidance for this repo is in `.github/copilot-project.md` (read that too).

## General Review & Testing Guidelines
- **Tests & coverage**: Prefer over-testing; cover happy paths and edge cases (null/missing inputs, invalid data, timeouts). Don’t skip “unlikely” paths—assert behavior when inputs/config are absent.
- **Input validation & guardrails**: Validate inputs early; fail fast with clear errors. Handle null/missing config gracefully. Avoid silent failures; log/throw with actionable messages.
- **Docs & contracts**: Keep code, tests, and docs in sync. When interfaces/contracts change, update docs/versioning (if applicable) and add serialization/compatibility tests.
- **Secrets & config**: Never commit secrets. Load secrets via env vars/user secrets/CI secrets. Gate integration/AI/live tests with opt-in flags; provide fallbacks/dummy clients for offline/test modes.
- **Dependencies**: Keep dependencies explicit and minimal. Avoid circular refs and hidden global state. Make optional features opt-in to avoid bloating the core.
- **Error handling & logging**: Provide clear, contextual logging for failures. Prefer explicit exceptions over silent catches; surface enough info to debug without leaking secrets.
- **Performance & resources**: Be mindful of long-running/expensive calls; add timeouts/cancellation. Dispose/cleanup deterministically in tests and production.
- **UI/UX (if applicable)**: Avoid blocking UI in tests; provide test modes. Ensure accessibility basics (labels, shortcuts), sensible defaults, and graceful degradation when services are unavailable.
- **CI discipline**: Keep integration/live/AI tests opt-in; default CI to deterministic, offline-safe tests. Use flags to enable heavier suites.
- **Code quality**: Favor readability over cleverness; small, single-responsibility functions; meaningful names; consistent formatting; minimize shared mutable state.
- **Test strata expectations**: Unit tests for core logic and edge cases; integration tests for module interactions (env-gated); smoke tests as minimal E2E (fakes/dry-run by default, live opt-in); AI/external tests opt-in with secrets/flags; UI tests non-blocking with test modes. In reviews, look for tests on error/guard paths, proper gating of external dependencies, and smoke coverage.

## Workflow & Collaboration
- **Claiming work**: Link to or create a GitHub issue when you start. Reference the issue in TODOs (`// TODO: ... - Issue: #123`).
- **Branching**: Do NOT create branches unless you have explicit permission from the user. Work off master incrementally to avoid merge conflicts. Only branch when the user specifically decides to branch off for another body of work.
- **Issue closure**: Don't auto-close issues; wait for explicit go-ahead. Clean up and commit before switching issues.
- **NEXT! ritual**: When completing an issue and ready to move to the next:
  - Run full test suite to ensure no regressions
  - Commit changes with descriptive message including issue reference
  - Push to remote repository
  - Close the completed issue
  - Claim the next issue with a comment
  - Reference issue numbers in all TODOs
  - **ONTO command**: If you say "ONTO <issue_number>" instead of "NEXT!", perform the above ritual but jump to the specified issue number instead of claiming the next sequential issue. Only use this if the specified issue is open and available.
- **Commit messages**: Include issue/stage context if applicable (e.g., `Feat: implement X (#123)`).
- **Test failures**: Investigate code vs. test assumptions; don’t just change tests to make them pass.
- **Stage transitions**: Before moving to a new development stage or completing a feature, run the full test suite (`dotnet test`) to ensure all tests pass and no regressions were introduced. This includes unit tests, integration tests, and UI tests. Never proceed to the next stage with failing tests.
- **Transparency**: Surface major/refactor/reset decisions immediately; get consent for disruptive changes.

## Config/Secrets & AI Tests
- Use env vars or user-secrets for secrets; never commit them. Keep appsettings checked in with placeholders only.
- AI/Live/Integration tests should be opt-in (`RUN_AI_TESTS=1` or similar) and skip when creds are absent. Provide dummy clients for offline modes to avoid crashes.

## Where to find project-specific instructions
- See `.github/copilot-project.md` for this repo’s architecture, staging, UI patterns, and process specifics.

## Review Expectations (for code reviewers and agents)
- **Tests coverage**: Check for adequate unit/interaction tests for both happy paths and edge cases (null/missing config, invalid inputs, timeouts, policy rejections). Ask “what happens when this value is null/missing/empty?” and ensure there’s a test.
- **Guardrails**: Validate inputs and handle null/config-not-set gracefully; prefer explicit rejections/events over silent failure. Ensure new commands/events have serialization tests.
- **Doc alignment**: Verify contracts/architecture/policy changes are mirrored in docs with date/reason. Confirm CI guards (contract guard) remain green.
- **Secrets and gating**: Live/AI/integration paths must be env-gated; no secrets in code/appsettings. Ensure dummy/fallbacks exist for offline/test modes.
- **Dependency clarity**: New services/adapters should be opt-in if they introduce cross-module dependencies; avoid hidden circular refs.

## General Review & Testing Guidelines (project-agnostic)
- **Tests & coverage**: Prefer over-testing; cover happy paths and edge cases (null/missing inputs, invalid data, timeouts). Don’t skip “unlikely” paths—assert behavior when inputs/config are absent.
- **Input validation & guardrails**: Validate inputs early; fail fast with clear errors. Handle null/missing config gracefully. Avoid silent failures; log/throw with actionable messages.
- **Docs & contracts**: Keep code, tests, and docs in sync. When interfaces/contracts change, update docs/versioning (if applicable) and add serialization/compatibility tests.
- **Secrets & config**: Never commit secrets. Load secrets via env vars/user secrets/CI secrets. Gate integration/AI/live tests with opt-in flags; provide fallbacks/dummy clients for offline/test modes.
- **Dependencies**: Keep dependencies explicit and minimal. Avoid circular refs and hidden global state. Make optional features opt-in to avoid bloating the core.
- **Error handling & logging**: Provide clear, contextual logging for failures. Prefer explicit exceptions over silent catches; surface enough info to debug without leaking secrets.
- **Performance & resources**: Be mindful of long-running/expensive calls; add timeouts/cancellation. Dispose/cleanup deterministically in tests and production.
- **UI/UX (if applicable)**: Avoid blocking UI in tests; provide test modes. Ensure accessibility basics (labels, shortcuts), sensible defaults, and graceful degradation when services are unavailable.
- **CI discipline**: Keep integration/live/AI tests opt-in; default CI to deterministic, offline-safe tests. Use flags to enable heavier suites.
- **Code quality**: Favor readability over cleverness; small, single-responsibility functions; meaningful names; consistent formatting; minimize shared mutable state.

## UI Development Guidelines

- **Error Handling**: Prefer throwing exceptions over showing modal dialogs for error conditions. Modal dialogs block automated testing and don't provide debuggable information to LLMs or automated systems. Use `isTestMode` checks to conditionally show dialogs only in interactive user sessions.
- **Testing UI Changes**: Always test UI changes using `dotnet run -- --test` to verify layout and functionality without manual intervention. Describe what you observe (panel positions, control states, any visual issues).
- **Layout Verification**: When implementing UI features, verify dockable panels work correctly, layout persists between runs, and reset functionality restores defaults.
- **Error Reporting**: When UI issues occur, provide specific details: which panel/control is affected, what behavior was expected vs observed, any error messages, and current layout state.
- **Mock Data**: Use descriptive mock data that clearly indicates functionality (e.g., "🔄 Session 1 - Running" instead of generic placeholders).
- **Accessibility**: Ensure UI elements have clear labels, keyboard shortcuts where appropriate, and logical tab order.
- **Performance**: UI should load within 2-3 seconds; test mode helps verify startup performance.
- **Automated Testing**: When writing unit tests that create `MainForm` instances, always set `form.IsTestMode = true` to prevent modal dialogs from blocking test execution. Structure automated tests to avoid any modal dialogs or user interactions that would block test execution. This ensures clean automated testing without user interaction requirements.
