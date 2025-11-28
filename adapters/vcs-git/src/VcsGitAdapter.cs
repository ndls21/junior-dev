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
                if (session.Config.Policy.ProtectedBranches.Contains(cb.BranchName))
                {
                    throw new InvalidOperationException($"Cannot create protected branch: {cb.BranchName}");
                }
                await RunGitCommand($"checkout -b {cb.BranchName}", repoPath);
                break;
            case ApplyPatch ap:
                // Apply patch
                await RunGitCommand($"apply", repoPath, input: ap.PatchContent);
                break;
            case Commit c:
                // Check max files
                if (session.Config.Policy.MaxFilesPerCommit.HasValue && c.IncludePaths.Count > session.Config.Policy.MaxFilesPerCommit.Value)
                {
                    throw new InvalidOperationException($"Too many files in commit: {c.IncludePaths.Count} > {session.Config.Policy.MaxFilesPerCommit.Value}");
                }
                await RunGitCommand($"add {string.Join(" ", c.IncludePaths)}", repoPath);
                await RunGitCommand($"commit -m \"{c.Message}\"", repoPath);
                break;
            case Push p:
                if (!_config.AllowPush)
                {
                    throw new InvalidOperationException("Push is disabled");
                }
                if (session.Config.Policy.ProtectedBranches.Contains(p.BranchName))
                {
                    throw new InvalidOperationException($"Cannot push to protected branch: {p.BranchName}");
                }
                await RunGitCommand($"push origin {p.BranchName}", repoPath);
                break;
            case GetDiff gd:
                var diffOutput = await RunGitCommand($"diff {gd.Ref}", repoPath);
                var diffArtifact = new Artifact("Diff", "diff.patch", InlineText: diffOutput);
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

    private async Task<string> RunGitCommand(string args, string workingDirectory, string? input = null)
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

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Git command failed: {error}");
        }

        return output;
    }
}
