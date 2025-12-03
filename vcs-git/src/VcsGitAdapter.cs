using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using JuniorDev.Contracts;
using JuniorDev.Orchestrator;
using Microsoft.Extensions.Logging;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Options;
using System.Linq;

namespace JuniorDev.VcsGit;

public class VcsGitAdapter : IAdapter
{
    private readonly bool _isFake;
    private readonly VcsConfig _config;
    private readonly AppConfig _appConfig;
    private readonly Dictionary<string, List<string>> _fakeBranches = new();
    private readonly Dictionary<string, List<string>> _fakeCommits = new();
    private readonly ILogger<VcsGitAdapter> _logger;
    private readonly Meter _meter;
    private readonly Counter<long> _commandsProcessed;
    private readonly Counter<long> _commandsSucceeded;
    private readonly Counter<long> _commandsFailed;
    private readonly Counter<long> _gitOperations;
    private readonly Counter<long> _gitErrors;
    private readonly IOptionsMonitor<LivePolicyConfig> _livePolicyMonitor;

    public VcsGitAdapter(VcsConfig config, bool isFake = false, AppConfig? appConfig = null, ILogger<VcsGitAdapter>? logger = null, IOptionsMonitor<LivePolicyConfig>? livePolicyMonitor = null)
    {
        _config = config;
        _isFake = isFake;
        _appConfig = appConfig ?? new AppConfig();
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<VcsGitAdapter>.Instance;
        _livePolicyMonitor = livePolicyMonitor ?? new StaticOptionsMonitor<LivePolicyConfig>();

        // Initialize metrics
        _meter = new Meter("JuniorDev.VcsGit", "1.0.0");
        _commandsProcessed = _meter.CreateCounter<long>("commands_processed", "commands", "Number of commands processed");
        _commandsSucceeded = _meter.CreateCounter<long>("commands_succeeded", "commands", "Number of commands that succeeded");
        _commandsFailed = _meter.CreateCounter<long>("commands_failed", "commands", "Number of commands that failed");
        _gitOperations = _meter.CreateCounter<long>("git_operations", "operations", "Number of git operations performed");
        _gitErrors = _meter.CreateCounter<long>("git_errors", "errors", "Number of git operation errors");
    }

    public bool CanHandle(ICommand command)
    {
        return command is CreateBranch or ApplyPatch or Commit or Push or GetDiff;
    }

    public async Task HandleCommand(ICommand command, SessionState session)
    {
        // Emit CommandAccepted
        var acceptedEvent = new CommandAccepted(
            Guid.NewGuid(),
            command.Correlation,
            command.Id);

        await session.AddEvent(acceptedEvent);

        // Record command processed metric
        _commandsProcessed.Add(1, new KeyValuePair<string, object?>("command_type", command.Kind));

        try
        {
            if (_isFake)
            {
                await HandleFakeCommand(command, session);
            }
            else
            {
                await HandleRealCommand(command, session);
            }

            // Emit CommandCompleted
            var completedEvent = new CommandCompleted(
                Guid.NewGuid(),
                command.Correlation,
                command.Id,
                CommandOutcome.Success);

            await session.AddEvent(completedEvent);
            _commandsSucceeded.Add(1, new KeyValuePair<string, object?>("command_type", command.Kind));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute command {CommandType}", command.Kind);
            var completedEvent = new CommandCompleted(
                Guid.NewGuid(),
                command.Correlation,
                command.Id,
                CommandOutcome.Failure,
                ex.Message);

            await session.AddEvent(completedEvent);
            _commandsFailed.Add(1, new KeyValuePair<string, object?>("command_type", command.Kind));
        }
    }

    private async Task HandleFakeCommand(ICommand command, SessionState session)
    {
        switch (command)
        {
            case CreateBranch cb:
                var repoKey = cb.Repo.Path;
                if (!_fakeBranches.ContainsKey(repoKey))
                    _fakeBranches[repoKey] = new List<string>();
                _fakeBranches[repoKey].Add(cb.BranchName);
                break;
            case ApplyPatch ap:
                // Fake apply patch, just store or something
                break;
            case Commit c:
                repoKey = c.Repo.Path;
                if (!_fakeCommits.ContainsKey(repoKey))
                    _fakeCommits[repoKey] = new List<string>();
                _fakeCommits[repoKey].Add(c.Message);
                break;
            case Push p:
                // Fake push
                break;
            case GetDiff gd:
                // Emit fake diff artifact
                var diffArtifact = new Artifact("Diff", "diff.txt", InlineText: "fake diff content");
                var artifactEvent = new ArtifactAvailable(
                    Guid.NewGuid(),
                    command.Correlation,
                    diffArtifact);
                await session.AddEvent(artifactEvent);
                break;
        }
    }

    private async Task HandleRealCommand(ICommand command, SessionState session)
    {
        var repoPath = _config.RepoPath;
        
        // Read live policy settings dynamically
        var livePolicy = _livePolicyMonitor.CurrentValue;
        if (livePolicy?.DryRun ?? true)
        {
            // In dry run, just emit events without running commands
            await HandleDryRunCommand(command, session);
            return;
        }

        switch (command)
        {
            case CreateBranch cb:
                var (output, exitCode) = await RunGitCommand($"checkout -b {cb.BranchName}", repoPath);
                if (exitCode != 0)
                {
                    await EmitCommandCompletedFailure(session, command, $"Git checkout failed: {output}");
                    return;
                }
                break;
            case ApplyPatch ap:
                (output, exitCode) = await RunGitCommand($"apply", repoPath, input: ap.PatchContent);
                if (exitCode != 0 && IsConflictError(output))
                {
                    // Conflict detected
                    var conflictEvent = new ConflictDetected(
                        Guid.NewGuid(),
                        command.Correlation,
                        ap.Repo,
                        "Patch application failed due to conflicts",
                        ap.PatchContent);
                    await session.AddEvent(conflictEvent);
                // Emit conflict artifact
                    var conflictArtifact = new Artifact("Conflict", "conflict.patch", InlineText: ap.PatchContent);
                    var conflictArtifactEvent = new ArtifactAvailable(
                        Guid.NewGuid(),
                        command.Correlation,
                        conflictArtifact);
                    await session.AddEvent(conflictArtifactEvent);
                    // Emit failure
                    await EmitCommandCompletedFailure(session, command, "Conflict detected");
                    return;
                }
                else if (exitCode != 0)
                {
                    await EmitCommandCompletedFailure(session, command, $"Git apply failed: {output}");
                    return;
                }
                // Emit artifact
                var patchArtifact = new Artifact("Patch", "applied.patch", InlineText: ap.PatchContent);
                var patchEvent = new ArtifactAvailable(
                    Guid.NewGuid(),
                    command.Correlation,
                    patchArtifact);
                await session.AddEvent(patchEvent);
                break;
            case Commit c:
                // Check max files
                if (session.Config.Policy.MaxFilesPerCommit.HasValue && c.IncludePaths.Count > session.Config.Policy.MaxFilesPerCommit.Value)
                {
                    await EmitCommandRejected(session, command, "Too many files in commit", "MaxFilesPerCommit");
                    return;
                }
                (output, exitCode) = await RunGitCommand($"add {string.Join(" ", c.IncludePaths)}", repoPath);
                if (exitCode != 0)
                {
                    await EmitCommandCompletedFailure(session, command, $"Git add failed: {output}");
                    return;
                }
                (output, exitCode) = await RunGitCommand($"commit -m \"{c.Message}\"", repoPath);
                if (exitCode != 0)
                {
                    await EmitCommandCompletedFailure(session, command, $"Git commit failed: {output}");
                    return;
                }
                // Emit artifact: commit diff
                var diffOutput = await RunGitCommand($"show --format= HEAD", repoPath);
                var commitArtifact = new Artifact("Diff", "commit.diff", InlineText: diffOutput.output);
                var commitEvent = new ArtifactAvailable(
                    Guid.NewGuid(),
                    command.Correlation,
                    commitArtifact);
                await session.AddEvent(commitEvent);
                break;
            case Push p:
                if (!(_livePolicyMonitor.CurrentValue?.PushEnabled ?? false))
                {
                    await EmitCommandCompletedFailure(session, command, "Push is disabled");
                    return;
                }
                if (session.Config.Policy.ProtectedBranches.Contains(p.BranchName))
                {
                    await EmitCommandRejected(session, command, $"Cannot push to protected branch: {p.BranchName}", "ProtectedBranches");
                    return;
                }
                (output, exitCode) = await RunGitCommand($"push origin {p.BranchName}", repoPath);
                if (exitCode != 0)
                {
                    await EmitCommandCompletedFailure(session, command, $"Git push failed: {output}");
                    return;
                }
                // Emit artifact: push log
                var pushArtifact = new Artifact("Log", "push.log", InlineText: output);
                var pushEvent = new ArtifactAvailable(
                    Guid.NewGuid(),
                    command.Correlation,
                    pushArtifact);
                await session.AddEvent(pushEvent);
                break;
            case GetDiff gd:
                (output, exitCode) = await RunGitCommand($"diff {gd.Ref}", repoPath);
                if (exitCode != 0)
                {
                    await EmitCommandCompletedFailure(session, command, $"Git diff failed: {output}");
                    return;
                }
                var diffArtifact = new Artifact("Diff", "diff.patch", InlineText: output);
                var artifactEvent = new ArtifactAvailable(
                    Guid.NewGuid(),
                    command.Correlation,
                    diffArtifact);
                await session.AddEvent(artifactEvent);
                break;
        }
    }

    private async Task HandleDryRunCommand(ICommand command, SessionState session)
    {
        // Emit artifacts as if commands succeeded
        switch (command)
        {
            case CreateBranch:
                // No artifact
                break;
            case ApplyPatch ap:
                var patchArtifact = new Artifact("Patch", "applied.patch", InlineText: ap.PatchContent);
                var patchEvent = new ArtifactAvailable(
                    Guid.NewGuid(),
                    command.Correlation,
                    patchArtifact);
                await session.AddEvent(patchEvent);
                break;
            case Commit:
                var commitArtifact = new Artifact("Diff", "commit.diff", InlineText: "dry run commit diff");
                var commitEvent = new ArtifactAvailable(
                    Guid.NewGuid(),
                    command.Correlation,
                    commitArtifact);
                await session.AddEvent(commitEvent);
                break;
            case Push:
                var pushArtifact = new Artifact("Log", "push.log", InlineText: "dry run push log");
                var pushEvent = new ArtifactAvailable(
                    Guid.NewGuid(),
                    command.Correlation,
                    pushArtifact);
                await session.AddEvent(pushEvent);
                break;
            case GetDiff:
                var diffArtifact = new Artifact("Diff", "diff.patch", InlineText: "dry run diff");
                var artifactEvent = new ArtifactAvailable(
                    Guid.NewGuid(),
                    command.Correlation,
                    diffArtifact);
                await session.AddEvent(artifactEvent);
                break;
        }
    }

    private async Task<(string output, int exitCode)> RunGitCommand(string args, string workingDirectory, string? input = null)
    {
        _logger.LogDebug("Executing git command: git {Args} in {WorkingDirectory}", args, workingDirectory);
        _gitOperations.Add(1);

        var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "git",
                Arguments = args,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = input != null,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        if (input != null)
        {
            await process.StandardInput.WriteAsync(input);
            process.StandardInput.Close();
        }

        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            _logger.LogWarning("Git command failed with exit code {ExitCode}: git {Args}", process.ExitCode, args);
            _gitErrors.Add(1);
        }
        else
        {
            _logger.LogDebug("Git command succeeded: git {Args}", args);
        }

        return (output + error, process.ExitCode);
    }

    private bool IsConflictError(string output)
    {
        return output.Contains("patch does not apply") || output.Contains("error: patch failed") || output.Contains("Merge conflict");
    }

    private async Task EmitCommandCompletedFailure(SessionState session, ICommand command, string message)
    {
        var completedEvent = new CommandCompleted(
            Guid.NewGuid(),
            command.Correlation,
            command.Id,
            CommandOutcome.Failure,
            message);
        await session.AddEvent(completedEvent);
    }

    private async Task EmitCommandRejected(SessionState session, ICommand command, string reason, string? policyRule = null)
    {
        var rejectedEvent = new CommandRejected(
            Guid.NewGuid(),
            command.Correlation,
            command.Id,
            reason,
            policyRule);
        await session.AddEvent(rejectedEvent);
    }
}
