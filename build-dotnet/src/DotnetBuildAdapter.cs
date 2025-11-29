using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using JuniorDev.Contracts;
using JuniorDev.Orchestrator;

namespace JuniorDev.Build.Dotnet;

/// <summary>
/// Build adapter for .NET projects using dotnet CLI and MSBuild.
/// </summary>
public class DotnetBuildAdapter : IAdapter
{
    private readonly BuildConfig _config;

    public DotnetBuildAdapter(BuildConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    public bool CanHandle(ICommand command) => command is BuildProject;

    public async Task HandleCommand(ICommand command, SessionState session)
    {
        if (command is not BuildProject buildCommand)
        {
            throw new ArgumentException($"Command must be {nameof(BuildProject)}", nameof(command));
        }

        try
        {
            // Validate the project path for security
            if (!IsValidProjectPath(buildCommand.ProjectPath))
            {
                await session.AddEvent(new CommandRejected(
                    Guid.NewGuid(),
                    command.Correlation,
                    command.Id,
                    "Invalid project path",
                    "Path validation"));
                return;
            }

            // Validate targets if specified
            if (buildCommand.Targets != null && buildCommand.Targets.Any(target => !IsValidTarget(target)))
            {
                await session.AddEvent(new CommandRejected(
                    Guid.NewGuid(),
                    command.Correlation,
                    command.Id,
                    "Invalid build target",
                    "Target validation"));
                return;
            }

            // Accept the command
            await session.AddEvent(new CommandAccepted(
                Guid.NewGuid(),
                command.Correlation,
                command.Id));

            // Execute the build
            var (success, output, errorOutput) = await ExecuteBuildAsync(buildCommand);

            // Create artifact with build output
            var artifactContent = $"Build Output:\n{output}\n\nErrors:\n{errorOutput}";
            var artifact = new Artifact(
                "BuildLog",
                $"build-{Path.GetFileNameWithoutExtension(buildCommand.ProjectPath)}.log",
                InlineText: artifactContent);

            await session.AddEvent(new ArtifactAvailable(
                Guid.NewGuid(),
                command.Correlation,
                artifact));

            // Complete the command
            await session.AddEvent(new CommandCompleted(
                Guid.NewGuid(),
                command.Correlation,
                command.Id,
                success ? CommandOutcome.Success : CommandOutcome.Failure,
                success ? "Build completed successfully" : "Build failed"));
        }
        catch (Exception ex)
        {
            await session.AddEvent(new CommandCompleted(
                Guid.NewGuid(),
                command.Correlation,
                command.Id,
                CommandOutcome.Failure,
                $"Build execution failed: {ex.Message}"));
        }
    }

    private bool IsValidProjectPath(string projectPath)
    {
        // Basic security validation - ensure path doesn't contain dangerous elements
        if (string.IsNullOrWhiteSpace(projectPath))
            return false;

        // Check for directory traversal attempts
        if (projectPath.Contains("..") || projectPath.Contains("\\..") || projectPath.Contains("../"))
            return false;

        // Check for absolute paths that might escape the workspace
        if (Path.IsPathRooted(projectPath) && !projectPath.StartsWith(_config.WorkspaceRoot))
            return false;

        // Only allow .csproj, .fsproj, .vbproj, .sln files
        var extension = Path.GetExtension(projectPath).ToLowerInvariant();
        return extension is ".csproj" or ".fsproj" or ".vbproj" or ".sln";
    }

    private bool IsValidTarget(string target)
    {
        // Allow common MSBuild targets
        var allowedTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Build", "Clean", "Rebuild", "Restore", "Publish", "Pack", "Test"
        };

        return allowedTargets.Contains(target);
    }

    private async Task<(bool success, string output, string errorOutput)> ExecuteBuildAsync(BuildProject command)
    {
        var arguments = BuildArguments(command);

        var processStartInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = arguments,
            WorkingDirectory = _config.WorkspaceRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = processStartInfo };
        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();

        process.OutputDataReceived += (sender, e) =>
        {
            if (e.Data != null)
                outputBuilder.AppendLine(e.Data);
        };

        process.ErrorDataReceived += (sender, e) =>
        {
            if (e.Data != null)
                errorBuilder.AppendLine(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // Wait for completion with timeout
        var timeout = command.Timeout ?? _config.DefaultTimeout;
        var completed = process.WaitForExit((int)timeout.TotalMilliseconds);

        if (!completed)
        {
            process.Kill();
            return (false, outputBuilder.ToString(), $"Build timed out after {timeout.TotalSeconds} seconds");
        }

        var success = process.ExitCode == 0;
        return (success, outputBuilder.ToString(), errorBuilder.ToString());
    }

    private string BuildArguments(BuildProject command)
    {
        var args = new List<string>();

        if (Path.GetExtension(command.ProjectPath).ToLowerInvariant() == ".sln")
        {
            args.Add("build");
            args.Add(command.ProjectPath);
        }
        else
        {
            args.Add("build");
            args.Add(command.ProjectPath);
        }

        if (!string.IsNullOrEmpty(command.Configuration))
        {
            args.Add($"--configuration");
            args.Add(command.Configuration);
        }

        if (!string.IsNullOrEmpty(command.TargetFramework))
        {
            args.Add($"--framework");
            args.Add(command.TargetFramework);
        }

        if (command.Targets != null && command.Targets.Any())
        {
            args.Add($"--target:{string.Join(";", command.Targets)}");
        }

        return string.Join(" ", args.Select(arg => arg.Contains(" ") ? $"\"{arg}\"" : arg));
    }
}

/// <summary>
/// Configuration for the build adapter.
/// </summary>
public sealed record BuildConfig(
    string WorkspaceRoot,
    TimeSpan DefaultTimeout = default)
{
    public BuildConfig() : this(".", TimeSpan.FromMinutes(5)) { }
}