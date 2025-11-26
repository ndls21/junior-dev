using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using JuniorDev.Contracts;
using JuniorDev.Orchestrator;
using JuniorDev.VcsGit;
using Xunit;

public class RealVcsAdapterTests
{
    [Fact]
    public async Task CreateBranch_Real_Succeeds()
    {
        // Skip if not integration test
        if (Environment.GetEnvironmentVariable("RUN_INTEGRATION_TESTS") != "true")
        {
            return;
        }

        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            // Init git repo
            RunGitCommand("init", tempDir);
            RunGitCommand("config user.name \"Test\"", tempDir);
            RunGitCommand("config user.email \"test@test.com\"", tempDir);
            File.WriteAllText(Path.Combine(tempDir, "test.txt"), "content");
            RunGitCommand("add test.txt", tempDir);
            RunGitCommand("commit -m \"initial\"", tempDir);

            var config = new VcsConfig { RepoPath = tempDir, AllowPush = true, IsIntegrationTest = true };
            var adapter = new VcsGitAdapter(config, isFake: false);
            var session = new SessionState(new SessionConfig(
                Guid.NewGuid(),
                null,
                null,
                new PolicyProfile("test", null, null, Array.Empty<string>(), null, false, false, null, null),
                new RepoRef("test", tempDir),
                new WorkspaceRef(tempDir),
                null,
                "test"), tempDir);

            var command = new CreateBranch(Guid.NewGuid(), new Correlation(session.Config.SessionId), new RepoRef("test", tempDir), "feature");

            await adapter.HandleCommand(command, session);

            var events = session.Events;
            Assert.Contains(events, e => e is CommandAccepted ca && ca.CommandId == command.Id);
            Assert.Contains(events, e => e is CommandCompleted cc && cc.CommandId == command.Id && cc.Outcome == CommandOutcome.Success);

            // Check branch exists
            var branches = RunGitCommand("branch", tempDir);
            Assert.Contains("feature", branches);
        }
        finally
        {
            try
            {
                Directory.Delete(tempDir, true);
            }
            catch
            {
                // Ignore
            }
        }
    }

    [Fact]
    public async Task ApplyPatch_Conflict_EmitsConflictDetected()
    {
        // Skip if not integration test
        if (Environment.GetEnvironmentVariable("RUN_INTEGRATION_TESTS") != "true")
        {
            return;
        }

        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            // Init git repo
            RunGitCommand("init", tempDir);
            RunGitCommand("config user.name \"Test\"", tempDir);
            RunGitCommand("config user.email \"test@test.com\"", tempDir);
            File.WriteAllText(Path.Combine(tempDir, "test.txt"), "line1\nline2\n");
            RunGitCommand("add test.txt", tempDir);
            RunGitCommand("commit -m \"initial\"", tempDir);

            var config = new VcsConfig { RepoPath = tempDir, IsIntegrationTest = true };
            var adapter = new VcsGitAdapter(config, isFake: false);
            var session = new SessionState(new SessionConfig(
                Guid.NewGuid(),
                null,
                null,
                new PolicyProfile("test", null, null, Array.Empty<string>(), null, false, false, null, null),
                new RepoRef("test", tempDir),
                new WorkspaceRef(tempDir),
                null,
                "test"), tempDir);

            // Create conflicting patch
            var patchContent = @"diff --git a/test.txt b/test.txt
index 1234567..abcdef0 100644
--- a/test.txt
+++ b/test.txt
@@ -1,2 +1,2 @@
-line1
+changed line1
 line2
";

            var command = new ApplyPatch(Guid.NewGuid(), new Correlation(session.Config.SessionId), new RepoRef("test", tempDir), patchContent);

            await adapter.HandleCommand(command, session);

            var events = session.Events;
            Assert.Contains(events, e => e is ConflictDetected);
            Assert.Contains(events, e => e is ArtifactAvailable aa && aa.Artifact.Kind == "Conflict");
            Assert.Contains(events, e => e is CommandCompleted cc && cc.CommandId == command.Id && cc.Outcome == CommandOutcome.Failure);
        }
        finally
        {
            try
            {
                Directory.Delete(tempDir, true);
            }
            catch
            {
                // Ignore
            }
        }
    }

    [Fact]
    public async Task DryRun_NoFsChanges()
    {
        // Skip if not integration test
        if (Environment.GetEnvironmentVariable("RUN_INTEGRATION_TESTS") != "true")
        {
            return;
        }

        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            // Init git repo
            RunGitCommand("init", tempDir);
            RunGitCommand("config user.name \"Test\"", tempDir);
            RunGitCommand("config user.email \"test@test.com\"", tempDir);
            File.WriteAllText(Path.Combine(tempDir, "test.txt"), "content");
            RunGitCommand("add test.txt", tempDir);
            RunGitCommand("commit -m \"initial\"", tempDir);

            var initialStatus = RunGitCommand("status --porcelain", tempDir);

            var config = new VcsConfig { RepoPath = tempDir, DryRun = true };
            var adapter = new VcsGitAdapter(config, isFake: false);
            var session = new SessionState(new SessionConfig(
                Guid.NewGuid(),
                null,
                null,
                new PolicyProfile("test", null, null, Array.Empty<string>(), null, false, false, null, null),
                new RepoRef("test", tempDir),
                new WorkspaceRef(tempDir),
                null,
                "test"), tempDir);

            var command = new ApplyPatch(Guid.NewGuid(), new Correlation(session.Config.SessionId), new RepoRef("test", tempDir), "dummy patch");

            await adapter.HandleCommand(command, session);

            var finalStatus = RunGitCommand("status --porcelain", tempDir);
            Assert.Equal(initialStatus, finalStatus); // No changes
        }
        finally
        {
            try
            {
                Directory.Delete(tempDir, true);
            }
            catch
            {
                // Ignore
            }
        }
    }

    private string RunGitCommand(string args, string workingDirectory)
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
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Git command failed: {error}");
        }

        return output;
    }
}