using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using JuniorDev.Contracts;
using JuniorDev.Orchestrator;

namespace JuniorDev.VcsGit;

public class VcsGitAdapter : IAdapter
{
    private readonly bool _isFake;
    private readonly VcsConfig _config;
    private readonly Dictionary<string, List<string>> _fakeBranches = new();
    private readonly Dictionary<string, List<string>> _fakeCommits = new();

    public VcsGitAdapter(VcsConfig config, bool isFake = false)
    {
        _config = config;
        _isFake = isFake;
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
        }
        catch (Exception ex)
        {
            var completedEvent = new CommandCompleted(
                Guid.NewGuid(),
                command.Correlation,
                command.Id,
                CommandOutcome.Failure,
                ex.Message);

            await session.AddEvent(completedEvent);
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
        if (_config.DryRun)
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
                if (!_config.AllowPush)
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