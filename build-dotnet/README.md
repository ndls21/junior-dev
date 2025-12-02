# Build.Dotnet Adapter

## Overview

The `Build.Dotnet` adapter provides integration with the .NET build system (MSBuild) for building, cleaning, restoring, and testing .NET projects. It handles `BuildProject` commands by invoking `dotnet build` with appropriate parameters.

## Features

- **MSBuild Integration**: Executes dotnet CLI commands with support for multiple targets (Build, Clean, Rebuild, Restore, Publish, Pack, Test)
- **Security Validation**: Validates project paths to prevent directory traversal attacks and restricts to supported project types (.csproj, .fsproj, .vbproj, .sln)
- **Timeout Support**: Configurable timeouts for build operations to prevent hanging processes
- **Artifact Generation**: Captures build output as artifacts (BuildLog kind) for inspection and debugging
- **Event-Driven**: Emits CommandAccepted, ArtifactAvailable, and CommandCompleted events for orchestration

## Configuration

The adapter requires a `BuildConfig` with:
- `WorkspaceRoot`: Base directory for resolving project paths
- `DefaultTimeout`: Maximum time allowed for build operations (default: 5 minutes)

Example:
```csharp
var config = new BuildConfig("/path/to/workspace", TimeSpan.FromMinutes(5));
var adapter = new DotnetBuildAdapter(config);
```

## BuildProject Command

```csharp
var command = new BuildProject(
    Id: Guid.NewGuid(),
    Correlation: new Correlation(sessionId),
    Repo: new RepoRef("my-repo", "/repo/path"),
    ProjectPath: "src/MyProject.csproj",
    Configuration: "Release",      // Optional: Debug/Release
    Framework: "net8.0",            // Optional: Target framework
    Targets: new[] { "Build" },     // Optional: Build targets
    Timeout: TimeSpan.FromMinutes(5) // Optional: Override default timeout
);
```

## Testing

### Unit Tests

Standard unit tests run by default:
```bash
dotnet test build-dotnet/tests
```

### Integration Tests

Integration tests validate the adapter on real repositories and are gated by an environment variable to avoid unnecessary builds during regular test runs.

**Enable integration tests:**
```bash
# PowerShell
$env:RUN_INTEGRATION_TESTS="true"; dotnet test build-dotnet/tests --filter "Category=Integration"

# Bash
RUN_INTEGRATION_TESTS=true dotnet test build-dotnet/tests --filter "Category=Integration"
```

**Integration test scenarios:**
1. **BuildRealProject_WithTimeout_ProducesArtifacts**: Validates successful build on contracts/Contracts.csproj with timeout and artifact generation
2. **BuildWithInvalidProject_Rejects**: Validates security by rejecting path traversal attempts (../../../etc/passwd)
3. **BuildNonExistentProject_Fails**: Validates proper failure handling when project doesn't exist
4. **BuildWithTimeout_RespectedAndDocumented**: Validates timeout behavior (1-second timeout)
5. **BuildMultipleTargets_ExecutesAllTargets**: Validates multi-target execution (Clean, Build)

**Test Requirements:**
- Integration tests use the jDev repository itself as the test subject
- Tests automatically locate the workspace root by searching for .sln, Directory.Packages.props, or global.json files
- The contracts/Contracts.csproj project is used as a small, fast-building test case
- Tests validate events (CommandAccepted, ArtifactAvailable, CommandCompleted) and artifact content

**Skipping integration tests:**

By default, integration tests are skipped when `RUN_INTEGRATION_TESTS` is not set to `"true"`. This prevents long-running builds during regular development test runs.

## Security

The adapter includes multiple security validations:

1. **Path Validation**: Rejects paths containing `..` or absolute paths outside the workspace root
2. **Target Validation**: Restricts targets to safe MSBuild targets (Build, Clean, Rebuild, Restore, Publish, Pack, Test)
3. **Project Type Validation**: Only allows supported project types (.csproj, .fsproj, .vbproj, .sln)

## Events

The adapter emits the following events:

- **CommandAccepted**: Build command was validated and accepted
- **CommandRejected**: Build command failed validation (security/path issues)
- **ArtifactAvailable**: Build output log is available as an artifact (Kind: "BuildLog")
- **CommandCompleted**: Build process finished (Success/Failure outcome)

## Architecture

The adapter follows the standard adapter pattern:
- Implements `IAdapter` interface for registration with orchestrator
- Uses `SessionState` for event emission
- Executes builds via `System.Diagnostics.Process` with proper timeout handling
- Captures stdout/stderr for artifact generation
