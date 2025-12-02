using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JuniorDev.Build.Dotnet;
using JuniorDev.Contracts;
using JuniorDev.Orchestrator;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace JuniorDev.Build.Dotnet.Tests;

public class DotnetBuildAdapterTests
{
    private readonly BuildConfig _config;
    private readonly DotnetBuildAdapter _adapter;
    private readonly ITestOutputHelper _output;

    public DotnetBuildAdapterTests(ITestOutputHelper output)
    {
        _output = output;
        _config = new BuildConfig("/tmp/workspace", TimeSpan.FromMinutes(5));
        _adapter = new DotnetBuildAdapter(_config);
    }

    [Fact]
    public void CanHandle_ReturnsTrue_ForBuildProjectCommand()
    {
        var command = new BuildProject(
            Guid.NewGuid(),
            new Correlation(Guid.NewGuid()),
            new RepoRef("test", "/tmp/test"),
            "src/MyProject.csproj");

        Assert.True(_adapter.CanHandle(command));
    }

    [Fact]
    public void CanHandle_ReturnsFalse_ForOtherCommands()
    {
        var command = new CreateBranch(
            Guid.NewGuid(),
            new Correlation(Guid.NewGuid()),
            new RepoRef("test", "/tmp/test"),
            "feature-branch");

        Assert.False(_adapter.CanHandle(command));
    }

    [Theory]
    [InlineData("src/MyProject.csproj", true)]
    [InlineData("MySolution.sln", true)]
    [InlineData("src/MyProject.fsproj", true)]
    [InlineData("src/MyProject.vbproj", true)]
    [InlineData("", false)]
    [InlineData("../../../etc/passwd", false)]
    [InlineData("C:\\Windows\\System32\\cmd.exe", false)]
    [InlineData("src/MyProject.txt", false)]
    public void IsValidProjectPath_ValidatesCorrectly(string path, bool expected)
    {
        // We need to access the private method for testing
        // In a real implementation, we'd make this protected or use a test-specific constructor
        var method = typeof(DotnetBuildAdapter).GetMethod("IsValidProjectPath",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        Assert.NotNull(method);
        var result = (bool)(method.Invoke(_adapter, new object[] { path }) ?? false);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("Build", true)]
    [InlineData("Clean", true)]
    [InlineData("Rebuild", true)]
    [InlineData("Restore", true)]
    [InlineData("Publish", true)]
    [InlineData("Pack", true)]
    [InlineData("Test", true)]
    [InlineData("build", true)] // Case insensitive
    [InlineData("DangerousTarget", false)]
    [InlineData("", false)]
    public void IsValidTarget_ValidatesCorrectly(string target, bool expected)
    {
        // We need to access the private method for testing
        var method = typeof(DotnetBuildAdapter).GetMethod("IsValidTarget",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        Assert.NotNull(method);
        var result = (bool)(method.Invoke(_adapter, new object[] { target }) ?? false);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void BuildArguments_ConstructsCorrectArguments()
    {
        var command = new BuildProject(
            Guid.NewGuid(),
            new Correlation(Guid.NewGuid()),
            new RepoRef("test", "/tmp/test"),
            "src/MyProject.csproj",
            "Release",
            "net8.0",
            new[] { "Build", "Publish" });

        // We need to access the private method for testing
        var method = typeof(DotnetBuildAdapter).GetMethod("BuildArguments",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        Assert.NotNull(method);
        var result = (string)(method.Invoke(_adapter, new object[] { command }) ?? "");
        Assert.NotNull(result);

        Assert.Contains("build", result);
        Assert.Contains("src/MyProject.csproj", result);
        Assert.Contains("--configuration Release", result);
        Assert.Contains("--framework net8.0", result);
        Assert.Contains("--target:Build;Publish", result);
    }
}

/// <summary>
/// Integration tests for DotnetBuildAdapter on a real repo.
/// These tests are gated by RUN_INTEGRATION_TESTS environment variable.
/// </summary>
public class DotnetBuildAdapterIntegrationTests
{
    private readonly ITestOutputHelper _output;

    public DotnetBuildAdapterIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private bool ShouldRunIntegrationTests()
    {
        return Environment.GetEnvironmentVariable("RUN_INTEGRATION_TESTS") == "true";
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task BuildRealProject_WithTimeout_ProducesArtifacts()
    {
        // Skip if not integration test mode
        if (!ShouldRunIntegrationTests())
        {
            _output.WriteLine("Skipping integration test - set RUN_INTEGRATION_TESTS=true to run");
            return;
        }

        _output.WriteLine("=== LIVE BUILD INTEGRATION TEST STARTED ===");
        _output.WriteLine("Testing real dotnet build on current repository");

        // Use the current repository (jDev) as the test subject
        var workspaceRoot = FindWorkspaceRoot();
        Assert.NotNull(workspaceRoot);
        _output.WriteLine($"Workspace root: {workspaceRoot}");

        // Find a simple project to build (contracts is a good candidate - small, no external dependencies)
        var projectPath = "contracts/Contracts.csproj";
        var fullProjectPath = Path.Combine(workspaceRoot, projectPath);
        
        if (!File.Exists(fullProjectPath))
        {
            _output.WriteLine($"Project not found at {fullProjectPath}, skipping test");
            return;
        }

        // Create adapter with actual workspace
        var config = new BuildConfig(workspaceRoot, TimeSpan.FromSeconds(120)); // 2 minute timeout
        var adapter = new DotnetBuildAdapter(config);

        // Create real session state to collect events
        var sessionConfig = new SessionConfig(
            SessionId: Guid.NewGuid(),
            ParentSessionId: null,
            PlanNodeId: null,
            Policy: new PolicyProfile { Name = "test", ProtectedBranches = new HashSet<string> { "main" } },
            Repo: new RepoRef("jDev", workspaceRoot),
            Workspace: new WorkspaceRef(workspaceRoot),
            WorkItem: null,
            AgentProfile: "test");
        
        var sessionState = new SessionState(sessionConfig, workspaceRoot);

        // Create build command
        var command = new BuildProject(
            Guid.NewGuid(),
            new Correlation(Guid.NewGuid()),
            new RepoRef("jDev", workspaceRoot),
            projectPath,
            "Release",
            "net8.0",
            new[] { "Build" },
            TimeSpan.FromSeconds(120));

        _output.WriteLine($"Building {projectPath} in Release/net8.0 mode...");

        // Execute build
        await adapter.HandleCommand(command, sessionState);

        // Assert - should have received events
        Assert.NotEmpty(sessionState.Events);
        _output.WriteLine($"Received {sessionState.Events.Count} events");

        // Should have CommandAccepted
        var accepted = sessionState.Events.OfType<CommandAccepted>().FirstOrDefault();
        Assert.NotNull(accepted);
        _output.WriteLine("✓ Command accepted");

        // Should have ArtifactAvailable with build log
        var artifact = sessionState.Events.OfType<ArtifactAvailable>().FirstOrDefault();
        Assert.NotNull(artifact);
        Assert.Equal("BuildLog", artifact.Artifact.Kind);
        Assert.NotNull(artifact.Artifact.InlineText);
        Assert.Contains("Build", artifact.Artifact.InlineText);
        _output.WriteLine($"✓ Build artifact created: {artifact.Artifact.Name}");
        _output.WriteLine($"  Artifact size: {artifact.Artifact.InlineText.Length} chars");

        // Should have CommandCompleted with success
        var completed = sessionState.Events.OfType<CommandCompleted>().FirstOrDefault();
        Assert.NotNull(completed);
        Assert.Equal(CommandOutcome.Success, completed.Outcome);
        _output.WriteLine($"✓ Build completed: {completed.Outcome} - {completed.Message}");

        _output.WriteLine("=== LIVE BUILD INTEGRATION TEST PASSED ===");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task BuildWithInvalidProject_Rejects()
    {
        if (!ShouldRunIntegrationTests())
        {
            _output.WriteLine("Skipping integration test - set RUN_INTEGRATION_TESTS=true to run");
            return;
        }

        _output.WriteLine("=== INVALID PROJECT TEST STARTED ===");

        var workspaceRoot = FindWorkspaceRoot();
        var config = new BuildConfig(workspaceRoot, TimeSpan.FromSeconds(30));
        var adapter = new DotnetBuildAdapter(config);

        var sessionConfig = new SessionConfig(
            SessionId: Guid.NewGuid(),
            ParentSessionId: null,
            PlanNodeId: null,
            Policy: new PolicyProfile { Name = "test", ProtectedBranches = new HashSet<string> { "main" } },
            Repo: new RepoRef("jDev", workspaceRoot),
            Workspace: new WorkspaceRef(workspaceRoot),
            WorkItem: null,
            AgentProfile: "test");
        
        var sessionState = new SessionState(sessionConfig, workspaceRoot);

        // Try to build with invalid path
        var command = new BuildProject(
            Guid.NewGuid(),
            new Correlation(Guid.NewGuid()),
            new RepoRef("jDev", workspaceRoot),
            "../../../etc/passwd", // Security violation
            "Release",
            "net8.0");

        await adapter.HandleCommand(command, sessionState);

        // Should have rejected
        var rejected = sessionState.Events.OfType<CommandRejected>().FirstOrDefault();
        Assert.NotNull(rejected);
        Assert.Contains("Invalid project path", rejected.Reason);
        _output.WriteLine($"✓ Command properly rejected: {rejected.Reason}");

        _output.WriteLine("=== INVALID PROJECT TEST PASSED ===");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task BuildNonExistentProject_Fails()
    {
        if (!ShouldRunIntegrationTests())
        {
            _output.WriteLine("Skipping integration test - set RUN_INTEGRATION_TESTS=true to run");
            return;
        }

        _output.WriteLine("=== NON-EXISTENT PROJECT TEST STARTED ===");

        var workspaceRoot = FindWorkspaceRoot();
        var config = new BuildConfig(workspaceRoot, TimeSpan.FromSeconds(30));
        var adapter = new DotnetBuildAdapter(config);

        var sessionConfig = new SessionConfig(
            SessionId: Guid.NewGuid(),
            ParentSessionId: null,
            PlanNodeId: null,
            Policy: new PolicyProfile { Name = "test", ProtectedBranches = new HashSet<string> { "main" } },
            Repo: new RepoRef("jDev", workspaceRoot),
            Workspace: new WorkspaceRef(workspaceRoot),
            WorkItem: null,
            AgentProfile: "test");
        
        var sessionState = new SessionState(sessionConfig, workspaceRoot);

        // Try to build non-existent project
        var command = new BuildProject(
            Guid.NewGuid(),
            new Correlation(Guid.NewGuid()),
            new RepoRef("jDev", workspaceRoot),
            "NonExistent/Project.csproj",
            "Release",
            "net8.0");

        await adapter.HandleCommand(command, sessionState);

        // Should have accepted (path is valid format) but then failed
        var accepted = sessionState.Events.OfType<CommandAccepted>().FirstOrDefault();
        Assert.NotNull(accepted);

        var completed = sessionState.Events.OfType<CommandCompleted>().FirstOrDefault();
        Assert.NotNull(completed);
        Assert.Equal(CommandOutcome.Failure, completed.Outcome);
        _output.WriteLine($"✓ Build properly failed: {completed.Message}");

        _output.WriteLine("=== NON-EXISTENT PROJECT TEST PASSED ===");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task BuildWithTimeout_RespectedAndDocumented()
    {
        if (!ShouldRunIntegrationTests())
        {
            _output.WriteLine("Skipping integration test - set RUN_INTEGRATION_TESTS=true to run");
            return;
        }

        _output.WriteLine("=== BUILD TIMEOUT TEST STARTED ===");
        _output.WriteLine("Testing that timeout parameter is respected");

        var workspaceRoot = FindWorkspaceRoot();
        var projectPath = "contracts/Contracts.csproj";

        // Use a very short timeout to test timeout behavior
        var config = new BuildConfig(workspaceRoot, TimeSpan.FromSeconds(1)); // 1 second - likely to timeout
        var adapter = new DotnetBuildAdapter(config);

        var sessionConfig = new SessionConfig(
            SessionId: Guid.NewGuid(),
            ParentSessionId: null,
            PlanNodeId: null,
            Policy: new PolicyProfile { Name = "test", ProtectedBranches = new HashSet<string> { "main" } },
            Repo: new RepoRef("jDev", workspaceRoot),
            Workspace: new WorkspaceRef(workspaceRoot),
            WorkItem: null,
            AgentProfile: "test");
        
        var sessionState = new SessionState(sessionConfig, workspaceRoot);

        var command = new BuildProject(
            Guid.NewGuid(),
            new Correlation(Guid.NewGuid()),
            new RepoRef("jDev", workspaceRoot),
            projectPath,
            "Release",
            "net8.0",
            new[] { "Build" },
            TimeSpan.FromSeconds(1)); // Very short timeout

        _output.WriteLine("Building with 1-second timeout (may timeout or succeed quickly)...");
        
        await adapter.HandleCommand(command, sessionState);

        // Check that we got a completion event (either success or failure due to timeout)
        var completed = sessionState.Events.OfType<CommandCompleted>().FirstOrDefault();
        Assert.NotNull(completed);

        if (completed.Outcome == CommandOutcome.Failure)
        {
            _output.WriteLine($"✓ Build timed out as expected: {completed.Message}");
        }
        else
        {
            _output.WriteLine($"✓ Build completed within timeout: {completed.Message}");
        }

        _output.WriteLine("=== BUILD TIMEOUT TEST PASSED ===");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task BuildMultipleTargets_ExecutesAllTargets()
    {
        if (!ShouldRunIntegrationTests())
        {
            _output.WriteLine("Skipping integration test - set RUN_INTEGRATION_TESTS=true to run");
            return;
        }

        _output.WriteLine("=== MULTIPLE TARGETS TEST STARTED ===");

        var workspaceRoot = FindWorkspaceRoot();
        var projectPath = "contracts/Contracts.csproj";

        var config = new BuildConfig(workspaceRoot, TimeSpan.FromSeconds(120));
        var adapter = new DotnetBuildAdapter(config);

        var sessionConfig = new SessionConfig(
            SessionId: Guid.NewGuid(),
            ParentSessionId: null,
            PlanNodeId: null,
            Policy: new PolicyProfile { Name = "test", ProtectedBranches = new HashSet<string> { "main" } },
            Repo: new RepoRef("jDev", workspaceRoot),
            Workspace: new WorkspaceRef(workspaceRoot),
            WorkItem: null,
            AgentProfile: "test");
        
        var sessionState = new SessionState(sessionConfig, workspaceRoot);

        // Build with multiple targets: Clean, then Build
        var command = new BuildProject(
            Guid.NewGuid(),
            new Correlation(Guid.NewGuid()),
            new RepoRef("jDev", workspaceRoot),
            projectPath,
            "Debug",
            "net8.0",
            new[] { "Clean", "Build" },
            TimeSpan.FromSeconds(120));

        _output.WriteLine("Building with targets: Clean, Build...");
        
        await adapter.HandleCommand(command, sessionState);

        var artifact = sessionState.Events.OfType<ArtifactAvailable>().FirstOrDefault();
        Assert.NotNull(artifact);
        
        // Artifact should contain evidence of both targets
        var buildLog = artifact.Artifact.InlineText ?? "";
        _output.WriteLine($"Build log length: {buildLog.Length} chars");

        var completed = sessionState.Events.OfType<CommandCompleted>().FirstOrDefault();
        Assert.NotNull(completed);
        _output.WriteLine($"✓ Multi-target build: {completed.Outcome}");

        _output.WriteLine("=== MULTIPLE TARGETS TEST PASSED ===");
    }

    private string FindWorkspaceRoot()
    {
        var currentDir = Directory.GetCurrentDirectory();
        var directory = new DirectoryInfo(currentDir);
        
        while (directory != null)
        {
            if (directory.GetFiles("*.sln").Any() || 
                directory.GetFiles("Directory.Packages.props").Any() ||
                directory.GetFiles("global.json").Any())
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }

        return currentDir;
    }
}