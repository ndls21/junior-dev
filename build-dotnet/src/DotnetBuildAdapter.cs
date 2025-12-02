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
using Microsoft.Extensions.Logging;
using System.Diagnostics.Metrics;

namespace JuniorDev.Build.Dotnet;

/// <summary>
/// Build adapter for .NET projects using dotnet CLI and MSBuild.
/// </summary>
public class DotnetBuildAdapter : IAdapter
{
    private readonly BuildConfig _config;
    private readonly ILogger<DotnetBuildAdapter> _logger;
    private readonly Meter _meter;
    private readonly Counter<long> _buildsStarted;
    private readonly Counter<long> _buildsSucceeded;
    private readonly Counter<long> _buildsFailed;
    private readonly Histogram<double> _buildDuration;

    public DotnetBuildAdapter(BuildConfig config, ILogger<DotnetBuildAdapter>? logger = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<DotnetBuildAdapter>.Instance;

        // Initialize metrics
        _meter = new Meter("JuniorDev.Build.Dotnet", "1.0.0");
        _buildsStarted = _meter.CreateCounter<long>("builds_started", "builds", "Number of builds started");
        _buildsSucceeded = _meter.CreateCounter<long>("builds_succeeded", "builds", "Number of builds that succeeded");
        _buildsFailed = _meter.CreateCounter<long>("builds_failed", "builds", "Number of builds that failed");
        _buildDuration = _meter.CreateHistogram<double>("build_duration_ms", "ms", "Time taken to complete builds");
    }

    public bool CanHandle(ICommand command) => command is BuildProject;

    public async Task HandleCommand(ICommand command, SessionState session)
    {
        if (command is not BuildProject buildCommand)
        {
            throw new ArgumentException($"Command must be {nameof(BuildProject)}", nameof(command));
        }

        _logger.LogInformation("Starting build for project: {ProjectPath}", buildCommand.ProjectPath);
        _buildsStarted.Add(1);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Validate the project path for security
            if (!IsValidProjectPath(buildCommand.ProjectPath))
            {
                _logger.LogWarning("Invalid project path rejected: {ProjectPath}", buildCommand.ProjectPath);
                await session.AddEvent(new CommandRejected(
                    Guid.NewGuid(),
                    command.Correlation,
                    command.Id,
                    "Invalid project path",
                    "Path validation"));
                _buildsFailed.Add(1);
                return;
            }

            // Validate targets if specified
            if (buildCommand.Targets != null && buildCommand.Targets.Any(target => !IsValidTarget(target)))
            {
                _logger.LogWarning("Invalid build target rejected: {Targets}", string.Join(", ", buildCommand.Targets));
                await session.AddEvent(new CommandRejected(
                    Guid.NewGuid(),
                    command.Correlation,
                    command.Id,
                    "Invalid build target",
                    "Target validation"));
                _buildsFailed.Add(1);
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

            if (success)
            {
                _logger.LogInformation("Build completed successfully for project: {ProjectPath}", buildCommand.ProjectPath);
                _buildsSucceeded.Add(1);
            }
            else
            {
                _logger.LogError("Build failed for project: {ProjectPath}", buildCommand.ProjectPath);
                _buildsFailed.Add(1);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Build execution failed for project: {ProjectPath}", buildCommand.ProjectPath);
            await session.AddEvent(new CommandCompleted(
                Guid.NewGuid(),
                command.Correlation,
                command.Id,
                CommandOutcome.Failure,
                $"Build execution failed: {ex.Message}"));
            _buildsFailed.Add(1);
        }
        finally
        {
            stopwatch.Stop();
            _buildDuration.Record(stopwatch.ElapsedMilliseconds);
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
        Console.WriteLine($"DEBUG: BuildAdapter executing 'dotnet {arguments}' in '{command.Repo.Path}'");

        var processStartInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = arguments,
            WorkingDirectory = command.Repo.Path,
            UseShellExecute = false,  // Capture output
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = processStartInfo };
        
        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();
        
        process.OutputDataReceived += (sender, e) => 
        {
            if (e.Data != null) 
            {
                outputBuilder.AppendLine(e.Data);
                Console.WriteLine($"BUILD OUTPUT: {e.Data}");
            }
        };
        
        process.ErrorDataReceived += (sender, e) => 
        {
            if (e.Data != null) 
            {
                errorBuilder.AppendLine(e.Data);
                Console.WriteLine($"BUILD ERROR: {e.Data}");
            }
        };
        
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.WaitForExit();
        
        var success = process.ExitCode == 0;
        var output = outputBuilder.ToString();
        var errorOutput = errorBuilder.ToString();
        
        Console.WriteLine($"DEBUG: Build completed with exit code {process.ExitCode}, success: {success}");
        
        return (success, output, errorOutput);
    }

    private string BuildArguments(BuildProject command)
    {
        var args = new List<string>();

        args.Add("build");
        args.Add(command.ProjectPath);

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