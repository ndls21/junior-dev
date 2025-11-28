using System;
using System.Linq;
using System.Threading.Tasks;
using JuniorDev.Contracts;
using JuniorDev.Orchestrator;
using JuniorDev.VcsGit;
using Xunit;

public class FakeVcsAdapterTests
{
    [Fact]
    public async Task CreateBranch_Fake_Succeeds()
    {
        var adapter = new VcsGitAdapter(new VcsConfig(), isFake: true);
        var session = new SessionState(new SessionConfig(
            Guid.NewGuid(),
            null,
            null,
            new PolicyProfile { Name = "test", ProtectedBranches = new HashSet<string> { "main" } }, null, false, false, null, null),
            new RepoRef("test", "/tmp/repo"),
            new WorkspaceRef("/tmp/ws"),
            null,
            "test"), "/tmp/ws");

        var command = new CreateBranch(Guid.NewGuid(), new Correlation(session.Config.SessionId), new RepoRef("test", "/tmp/repo"), "feature");

        await adapter.HandleCommand(command, session);

        var events = session.Events;
        Assert.Contains(events, e => e is CommandAccepted ca && ca.CommandId == command.Id);
        Assert.Contains(events, e => e is CommandCompleted cc && cc.CommandId == command.Id && cc.Outcome == CommandOutcome.Success);
    }

    [Fact]
    public async Task GetDiff_DryRun_EmitsArtifact()
    {
        var adapter = new VcsGitAdapter(new VcsConfig { DryRun = true }, isFake: false);
        var session = new SessionState(new SessionConfig(
            Guid.NewGuid(),
            null,
            null,
            new PolicyProfile { Name = "test", ProtectedBranches = new HashSet<string> { "main" } }, null, false, false, null, null),
            new RepoRef("test", "/tmp/repo"),
            new WorkspaceRef("/tmp/ws"),
            null,
            "test"), "/tmp/ws");

        var command = new GetDiff(Guid.NewGuid(), new Correlation(session.Config.SessionId), new RepoRef("test", "/tmp/repo"));

        await adapter.HandleCommand(command, session);

        var events = session.Events;
        Assert.Contains(events, e => e is ArtifactAvailable aa && aa.Artifact.Kind == "Diff");
    }

    [Fact]
    public async Task DryRun_ApplyPatch_EmitsArtifact()
    {
        var adapter = new VcsGitAdapter(new VcsConfig { DryRun = true }, isFake: false);
        var session = new SessionState(new SessionConfig(
            Guid.NewGuid(),
            null,
            null,
            new PolicyProfile { Name = "test", ProtectedBranches = new HashSet<string> { "main" } }, null, false, false, null, null),
            new RepoRef("test", "/tmp/repo"),
            new WorkspaceRef("/tmp/ws"),
            null,
            "test"), "/tmp/ws");

        var patchContent = "dummy patch";
        var command = new ApplyPatch(Guid.NewGuid(), new Correlation(session.Config.SessionId), new RepoRef("test", "/tmp/repo"), patchContent);

        await adapter.HandleCommand(command, session);

        var events = session.Events;
        Assert.Contains(events, e => e is ArtifactAvailable aa && aa.Artifact.Kind == "Patch" && aa.Artifact.InlineText == patchContent);
    }
}
