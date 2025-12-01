# Junior Dev

A .NET-based platform for AI-assisted software development.

## Setup

1. Ensure .NET 8 is installed (pinned in `global.json`).
2. Clone the repository.
3. Run `dotnet restore` to restore dependencies.
4. Run `dotnet build` to build the solution.
5. Run `dotnet test` to run unit tests (integration tests are skipped by default).

## Running CI Locally

- Build: `dotnet build`
- Unit tests: `dotnet test --filter "TestCategory!=Integration"`
- Contract guard: `pwsh scripts/check-contracts.ps1`

## Smoke Tests

End-to-end smoke tests validate the complete development pipeline:

- **Fake mode** (default): `dotnet test --filter "Category=Smoke"` - tests with mock adapters
- **Live mode**: `dotnet test --filter "Category=Integration"` with `RUN_LIVE=1` - tests with real GitHub/Jira APIs

### Smoke Test Artifacts

Smoke tests generate comprehensive artifact bundles containing:
- `README.md`: Executive summary with test results and metrics
- `smoke-test-report.json`: Detailed JSON report with full execution data
- `event-log.json`: Complete event stream for debugging
- Artifact files: Build logs, test results, and generated content
- Metadata files: JSON descriptions for each artifact

**Local runs**: Artifacts saved to `%TEMP%\junior-dev-smoke-*`  
**CI runs**: Available as "smoke-test-artifacts" in GitHub Actions artifacts

## Project Structure

- `contracts/`: Shared DTOs and interfaces.
- `contracts.Tests/`: Unit tests for contracts, including golden serialization tests.
- `docs/`: Documentation.
- `.github/`: CI workflows and instructions.

## UI Development

The UI shell (`ui-shell/`) provides a DevExpress-based Windows Forms application for managing AI-assisted development sessions.

### Testing UI Changes

- **Normal mode**: `dotnet run` (from `ui-shell/` directory) - runs the full application
- **Test mode**: `dotnet run -- --test` - displays UI for 2 seconds then auto-exits for automated testing
- **Layout**: UI uses dockable panels (sessions left, AI chat center-top, event stream center-bottom, artifacts right)
- **Persistence**: Layout is saved to `%APPDATA%\JuniorDev\layout.xml`

### UI Architecture

- DevExpress WinForms 24.2.9 for dockable panels and controls
- **Four-panel layout**:
  - **Sessions Panel (Left)**: Active development sessions with status indicators and filters
  - **AI Chat Panel (Center-Top)**: Interactive conversations with AI assistants for task instructions
  - **Event Stream Panel (Center-Bottom)**: Real-time system events and command results
  - **Artifacts Panel (Right)**: Build results, test outputs, diffs, and logs
- Session filtering with status chips (All/Running/Paused/Error)
- Layout persistence with reset functionality (View → Reset Layout or Ctrl+R)

## Tooling

- .NET 8 SDK
- xUnit for testing
- PowerShell for scripts