using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using JuniorDev.Contracts;
using Xunit;

namespace JuniorDev.Contracts.Tests;

public class ContractSerializationTests
{
    private static readonly Guid TestSessionId = Guid.Parse("12345678-1234-1234-1234-123456789abc");
    private static readonly Guid TestCommandId = Guid.Parse("87654321-4321-4321-4321-cba987654321");

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    [Fact]
    public void WorkItemRef_SerializesCorrectly()
    {
        var item = new WorkItemRef("JIRA-123", "jira");
        var json = JsonSerializer.Serialize(item, Options);
        var expected = File.ReadAllText("Fixtures/WorkItemRef.json");
        Assert.Equal(expected.Trim(), json.Trim());
    }

    [Fact]
    public void RepoRef_SerializesCorrectly()
    {
        var repo = new RepoRef("my-repo", "/path/to/repo");
        var json = JsonSerializer.Serialize(repo, Options);
        var expected = File.ReadAllText("Fixtures/RepoRef.json");
        Assert.Equal(expected.Trim(), json.Trim());
    }

    [Fact]
    public void WorkspaceRef_SerializesCorrectly()
    {
        var workspace = new WorkspaceRef("/path/to/workspace");
        var json = JsonSerializer.Serialize(workspace, Options);
        var expected = File.ReadAllText("Fixtures/WorkspaceRef.json");
        Assert.Equal(expected.Trim(), json.Trim());
    }

    [Fact]
    public void Correlation_SerializesCorrectly()
    {
        var correlation = new Correlation(TestSessionId, TestCommandId, TestCommandId, "node1");
        var json = JsonSerializer.Serialize(correlation, Options);
        var expected = File.ReadAllText("Fixtures/Correlation.json");
        Assert.Equal(expected.Trim(), json.Trim());
    }

    [Fact]
    public void CreateBranchCommand_SerializesCorrectly()
    {
        var correlation = new Correlation(TestSessionId);
        var command = new CreateBranch(TestCommandId, correlation, new RepoRef("repo", "path"), "feature-branch", "main");
        var json = JsonSerializer.Serialize(command, Options);
        var expected = File.ReadAllText("Fixtures/CreateBranch.json");
        Assert.Equal(expected.Trim(), json.Trim());
    }

    [Fact]
    public void RunTestsCommand_SerializesCorrectly()
    {
        var correlation = new Correlation(TestSessionId);
        var command = new RunTests(TestCommandId, correlation, new RepoRef("repo", "path"), "UnitTests", TimeSpan.FromMinutes(5));
        var json = JsonSerializer.Serialize(command, Options);
        var expected = File.ReadAllText("Fixtures/RunTests.json");
        Assert.Equal(expected.Trim(), json.Trim());
    }

    [Fact]
    public void CommitCommand_SerializesCorrectly()
    {
        var correlation = new Correlation(TestSessionId);
        var command = new Commit(TestCommandId, correlation, new RepoRef("repo", "path"), "Initial commit", new[] { "file1.cs", "file2.cs" }, false);
        var json = JsonSerializer.Serialize(command, Options);
        var expected = File.ReadAllText("Fixtures/Commit.json");
        Assert.Equal(expected.Trim(), json.Trim());
    }

    [Fact]
    public void CommandCompletedEvent_SerializesCorrectly()
    {
        var correlation = new Correlation(TestSessionId);
        var @event = new CommandCompleted(TestCommandId, correlation, TestCommandId, CommandOutcome.Success, "Completed successfully");
        var json = JsonSerializer.Serialize(@event, Options);
        var expected = File.ReadAllText("Fixtures/CommandCompleted.json");
        Assert.Equal(expected.Trim(), json.Trim());
    }

    [Fact]
    public void ArtifactAvailableEvent_SerializesCorrectly()
    {
        var correlation = new Correlation(TestSessionId);
        var artifact = new Artifact("Diff", "changes.diff", InlineText: "diff content");
        var @event = new ArtifactAvailable(TestCommandId, correlation, artifact);
        var json = JsonSerializer.Serialize(@event, Options);
        var expected = File.ReadAllText("Fixtures/ArtifactAvailable.json");
        Assert.Equal(expected.Trim(), json.Trim());
    }

    [Fact]
    public void TaskPlan_SerializesCorrectly()
    {
        var nodes = new[]
        {
            new TaskNode("1", "Implement feature", new[] { "2" }, new WorkItemRef("JIRA-123"), "executor", "feature-branch", new[] { "backend" })
        };
        var plan = new TaskPlan(nodes);
        var json = JsonSerializer.Serialize(plan, Options);
        var expected = File.ReadAllText("Fixtures/TaskPlan.json");
        Assert.Equal(expected.Trim(), json.Trim());
    }

    [Fact]
    public void PolicyProfile_SerializesCorrectly()
    {
        var limits = new RateLimits(100, 10, new Dictionary<string, int> { ["CreateBranch"] = 5 });
        var profile = new PolicyProfile("Default", new[] { "CreateBranch" }, null, new[] { "main" }, 10, true, false, new[] { "To Do->In Progress" }, limits);
        var json = JsonSerializer.Serialize(profile, Options);
        var expected = File.ReadAllText("Fixtures/PolicyProfile.json");
        Assert.Equal(expected.Trim(), json.Trim());
    }

    [Fact]
    public void SessionConfig_SerializesCorrectly()
    {
        var policy = new PolicyProfile("Default", null, null, new[] { "main" }, null, false, false, null, null);
        var config = new SessionConfig(TestSessionId, null, "node1", policy, new RepoRef("repo", "path"), new WorkspaceRef("workspace"), new WorkItemRef("JIRA-123"), "planner");
        var json = JsonSerializer.Serialize(config, Options);
        var expected = File.ReadAllText("Fixtures/SessionConfig.json");
        Assert.Equal(expected.Trim(), json.Trim());
    }

    // Round-trip tests
    [Fact]
    public void WorkItemRef_RoundTrip()
    {
        var original = new WorkItemRef("JIRA-123", "jira");
        var json = JsonSerializer.Serialize(original, Options);
        var deserialized = JsonSerializer.Deserialize<WorkItemRef>(json, Options);
        Assert.Equal(original, deserialized);
    }

    [Fact]
    public void CreateBranchCommand_RoundTrip()
    {
        var correlation = new Correlation(TestSessionId);
        var original = new CreateBranch(TestCommandId, correlation, new RepoRef("repo", "path"), "branch", "main");
        var json = JsonSerializer.Serialize(original, Options);
        var deserialized = JsonSerializer.Deserialize<CreateBranch>(json, Options);
        Assert.Equal(original, deserialized);
    }
}