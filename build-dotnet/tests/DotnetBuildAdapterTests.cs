using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JuniorDev.Build.Dotnet;
using JuniorDev.Contracts;
using JuniorDev.Orchestrator;
using Moq;
using Xunit;

namespace JuniorDev.Build.Dotnet.Tests;

public class DotnetBuildAdapterTests
{
    private readonly BuildConfig _config;
    private readonly DotnetBuildAdapter _adapter;

    public DotnetBuildAdapterTests()
    {
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