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

## Project Structure

- `contracts/`: Shared DTOs and interfaces.
- `contracts.Tests/`: Unit tests for contracts, including golden serialization tests.
- `docs/`: Documentation.
- `.github/`: CI workflows and instructions.

## Tooling

- .NET 8 SDK
- xUnit for testing
- PowerShell for scripts