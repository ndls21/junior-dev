using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using JuniorDev.Contracts;
using Xunit;
using Microsoft.Extensions.Configuration;
using System.IO;

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
        var correlation = new Correlation(TestSessionId, TestCommandId, TestCommandId, "node1", null);
        var json = JsonSerializer.Serialize(correlation, Options);
        var expected = File.ReadAllText("Fixtures/Correlation.json");
        Assert.Equal(expected.Trim(), json.Trim());
    }

    [Fact]
    public void ApplyPatchCommand_SerializesCorrectly()
    {
        var correlation = new Correlation(TestSessionId);
        var command = new ApplyPatch(TestCommandId, correlation, new RepoRef("repo", "path"), "patch content here");
        var json = JsonSerializer.Serialize(command, Options);
        var expected = File.ReadAllText("Fixtures/ApplyPatch.json");
        Assert.Equal(expected.Trim(), json.Trim());
    }

    [Fact]
    public void PushCommand_SerializesCorrectly()
    {
        var correlation = new Correlation(TestSessionId);
        var command = new Push(TestCommandId, correlation, new RepoRef("repo", "path"), "main");
        var json = JsonSerializer.Serialize(command, Options);
        var expected = File.ReadAllText("Fixtures/Push.json");
        Assert.Equal(expected.Trim(), json.Trim());
    }

    [Fact]
    public void GetDiffCommand_SerializesCorrectly()
    {
        var correlation = new Correlation(TestSessionId);
        var command = new GetDiff(TestCommandId, correlation, new RepoRef("repo", "path"), "HEAD");
        var json = JsonSerializer.Serialize(command, Options);
        var expected = File.ReadAllText("Fixtures/GetDiff.json");
        Assert.Equal(expected.Trim(), json.Trim());
    }

    [Fact]
    public void TransitionTicketCommand_SerializesCorrectly()
    {
        var correlation = new Correlation(TestSessionId);
        var command = new TransitionTicket(TestCommandId, correlation, new WorkItemRef("JIRA-123"), "In Progress");
        var json = JsonSerializer.Serialize(command, Options);
        var expected = File.ReadAllText("Fixtures/TransitionTicket.json");
        Assert.Equal(expected.Trim(), json.Trim());
    }

    [Fact]
    public void CommentCommand_SerializesCorrectly()
    {
        var correlation = new Correlation(TestSessionId);
        var command = new Comment(TestCommandId, correlation, new WorkItemRef("JIRA-123"), "This is a comment");
        var json = JsonSerializer.Serialize(command, Options);
        var expected = File.ReadAllText("Fixtures/Comment.json");
        Assert.Equal(expected.Trim(), json.Trim());
    }

    [Fact]
    public void SetAssigneeCommand_SerializesCorrectly()
    {
        var correlation = new Correlation(TestSessionId);
        var command = new SetAssignee(TestCommandId, correlation, new WorkItemRef("JIRA-123"), "user@example.com");
        var json = JsonSerializer.Serialize(command, Options);
        var expected = File.ReadAllText("Fixtures/SetAssignee.json");
        Assert.Equal(expected.Trim(), json.Trim());
    }

    [Fact]
    public void UploadArtifactCommand_SerializesCorrectly()
    {
        var correlation = new Correlation(TestSessionId);
        var content = new byte[] { 1, 2, 3 };
        var command = new UploadArtifact(TestCommandId, correlation, "artifact.txt", "text/plain", content, "path/to/file");
        var json = JsonSerializer.Serialize(command, Options);
        var expected = File.ReadAllText("Fixtures/UploadArtifact.json");
        Assert.Equal(expected.Trim(), json.Trim());
    }

    [Fact]
    public void RequestApprovalCommand_SerializesCorrectly()
    {
        var correlation = new Correlation(TestSessionId);
        var actions = new[] { "Review code", "Run tests" };
        var command = new RequestApproval(TestCommandId, correlation, "Please approve this change", actions);
        var json = JsonSerializer.Serialize(command, Options);
        var expected = File.ReadAllText("Fixtures/RequestApproval.json");
        Assert.Equal(expected.Trim(), json.Trim());
    }

    [Fact]
    public void SpawnSessionCommand_SerializesCorrectly()
    {
        var correlation = new Correlation(TestSessionId);
        var config = new SessionConfig(TestSessionId, TestSessionId, "node1", new PolicyProfile { Name = "Default", ProtectedBranches = new HashSet<string> { "main" } }, new RepoRef("repo", "path"), new WorkspaceRef("workspace"), new WorkItemRef("JIRA-123"), "planner");
        var command = new SpawnSession(TestCommandId, correlation, config);
        var json = JsonSerializer.Serialize(command, Options);
        var expected = File.ReadAllText("Fixtures/SpawnSession.json");
        Assert.Equal(expected.Trim(), json.Trim());
    }

    [Fact]
    public void LinkPlanNodeCommand_SerializesCorrectly()
    {
        var correlation = new Correlation(TestSessionId);
        var command = new LinkPlanNode(TestCommandId, correlation, "node1");
        var json = JsonSerializer.Serialize(command, Options);
        var expected = File.ReadAllText("Fixtures/LinkPlanNode.json");
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
    public void BuildProjectCommand_SerializesCorrectly()
    {
        var correlation = new Correlation(TestSessionId);
        var targets = new[] { "Build", "Publish" };
        var command = new BuildProject(TestCommandId, correlation, new RepoRef("repo", "path"), "src/MyProject.csproj", "Release", "net8.0", targets, TimeSpan.FromMinutes(10));
        var json = JsonSerializer.Serialize(command, Options);
        var expected = File.ReadAllText("Fixtures/BuildProject.json");
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
    public void CommandAcceptedEvent_SerializesCorrectly()
    {
        var correlation = new Correlation(TestSessionId);
        var @event = new CommandAccepted(TestCommandId, correlation, TestCommandId);
        var json = JsonSerializer.Serialize(@event, Options);
        var expected = File.ReadAllText("Fixtures/CommandAccepted.json");
        Assert.Equal(expected.Trim(), json.Trim());
    }

    [Fact]
    public void CommandRejectedEvent_SerializesCorrectly()
    {
        var correlation = new Correlation(TestSessionId);
        var @event = new CommandRejected(TestCommandId, correlation, TestCommandId, "Not allowed", "PolicyRule1");
        var json = JsonSerializer.Serialize(@event, Options);
        var expected = File.ReadAllText("Fixtures/CommandRejected.json");
        Assert.Equal(expected.Trim(), json.Trim());
    }

    [Fact]
    public void ThrottledEvent_SerializesCorrectly()
    {
        var correlation = new Correlation(TestSessionId);
        var @event = new Throttled(TestCommandId, correlation, "api", DateTimeOffset.Parse("2025-11-26T12:00:00Z"));
        var json = JsonSerializer.Serialize(@event, Options);
        var expected = File.ReadAllText("Fixtures/Throttled.json");
        Assert.Equal(expected.Trim(), json.Trim());
    }

    [Fact]
    public void ConflictDetectedEvent_SerializesCorrectly()
    {
        var correlation = new Correlation(TestSessionId);
        var @event = new ConflictDetected(TestCommandId, correlation, new RepoRef("repo", "path"), "Merge conflict", "diff content");
        var json = JsonSerializer.Serialize(@event, Options);
        var expected = File.ReadAllText("Fixtures/ConflictDetected.json");
        Assert.Equal(expected.Trim(), json.Trim());
    }

    [Fact]
    public void SessionStatusChangedEvent_SerializesCorrectly()
    {
        var correlation = new Correlation(TestSessionId);
        var @event = new SessionStatusChanged(TestCommandId, correlation, SessionStatus.Running, "Started");
        var json = JsonSerializer.Serialize(@event, Options);
        var expected = File.ReadAllText("Fixtures/SessionStatusChanged.json");
        Assert.Equal(expected.Trim(), json.Trim());
    }

    [Fact]
    public void PlanUpdatedEvent_SerializesCorrectly()
    {
        var correlation = new Correlation(TestSessionId);
        var plan = new TaskPlan(new[] { new TaskNode("1", "Task", new string[0], null, null, null, new string[0]) });
        var @event = new PlanUpdated(TestCommandId, correlation, plan);
        var json = JsonSerializer.Serialize(@event, Options);
        var expected = File.ReadAllText("Fixtures/PlanUpdated.json");
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
        var limits = new RateLimits { CallsPerMinute = 100, Burst = 10, PerCommandCaps = new Dictionary<string, int> { ["CreateBranch"] = 5 } };
        var profile = new PolicyProfile
        {
            Name = "Default",
            ProtectedBranches = new HashSet<string> { "main" },
            RequireTestsBeforePush = true,
            RequireApprovalForPush = false,
            CommandWhitelist = new List<string> { "CreateBranch" },
            MaxFilesPerCommit = 10,
            AllowedWorkItemTransitions = new List<string> { "To Do->In Progress" },
            Limits = limits
        };
        var json = JsonSerializer.Serialize(profile, Options);
        var expected = File.ReadAllText("Fixtures/PolicyProfile.json");
        Assert.Equal(expected.Trim(), json.Trim());
    }

    [Fact]
    public void SessionConfig_SerializesCorrectly()
    {
        var policy = new PolicyProfile
        {
            Name = "Default",
            ProtectedBranches = new HashSet<string> { "main" }
        };
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

    [Fact]
    public void CreateBranchCommand_WithNullFromRef_RoundTrip()
    {
        var correlation = new Correlation(TestSessionId);
        var original = new CreateBranch(TestCommandId, correlation, new RepoRef("repo", "path"), "branch", null);
        var json = JsonSerializer.Serialize(original, Options);
        var deserialized = JsonSerializer.Deserialize<CreateBranch>(json, Options);
        Assert.Equal(original, deserialized);
    }

    [Fact]
    public void BuildProjectCommand_RoundTrip()
    {
        var correlation = new Correlation(TestSessionId);
        var targets = new[] { "Build", "Publish" };
        var original = new BuildProject(TestCommandId, correlation, new RepoRef("repo", "path"), "src/MyProject.csproj", "Release", "net8.0", targets, TimeSpan.FromMinutes(10));
        var json = JsonSerializer.Serialize(original, Options);
        var deserialized = JsonSerializer.Deserialize<BuildProject>(json, Options);
        Assert.Equal(original.Id, deserialized.Id);
        Assert.Equal(original.Correlation, deserialized.Correlation);
        Assert.Equal(original.Kind, deserialized.Kind);
        Assert.Equal(original.Repo, deserialized.Repo);
        Assert.Equal(original.ProjectPath, deserialized.ProjectPath);
        Assert.Equal(original.Configuration, deserialized.Configuration);
        Assert.Equal(original.TargetFramework, deserialized.TargetFramework);
        Assert.Equal(original.Timeout, deserialized.Timeout);
        // Check targets individually since JSON deserializes arrays as lists
        Assert.Equal(original.Targets.Count, deserialized.Targets.Count);
        for (int i = 0; i < original.Targets.Count; i++)
        {
            Assert.Equal(original.Targets[i], deserialized.Targets[i]);
        }
    }

    [Fact]
    public void QueryBacklogCommand_SerializesCorrectly()
    {
        var correlation = new Correlation(TestSessionId);
        var command = new QueryBacklog(TestCommandId, correlation, "urgent");
        var json = JsonSerializer.Serialize(command, Options);
        var expected = File.ReadAllText("Fixtures/QueryBacklog.json");
        Assert.Equal(expected.Trim(), json.Trim());
    }

    [Fact]
    public void QueryWorkItemCommand_SerializesCorrectly()
    {
        var correlation = new Correlation(TestSessionId);
        var command = new QueryWorkItem(TestCommandId, correlation, new WorkItemRef("JIRA-123"));
        var json = JsonSerializer.Serialize(command, Options);
        var expected = File.ReadAllText("Fixtures/QueryWorkItem.json");
        Assert.Equal(expected.Trim(), json.Trim());
    }

    [Fact]
    public void BacklogQueriedEvent_SerializesCorrectly()
    {
        var correlation = new Correlation(TestSessionId);
        var items = new[]
        {
            new WorkItemSummary("JIRA-123", "Implement feature", "Open", "user1"),
            new WorkItemSummary("JIRA-124", "Fix bug", "In Progress", "user2")
        };
        var @event = new BacklogQueried(TestCommandId, correlation, items);
        var json = JsonSerializer.Serialize(@event, Options);
        var expected = File.ReadAllText("Fixtures/BacklogQueried.json");
        Assert.Equal(expected.Trim(), json.Trim());
    }

    [Fact]
    public void WorkItemQueriedEvent_SerializesCorrectly()
    {
        var correlation = new Correlation(TestSessionId);
        var details = new WorkItemDetails("JIRA-123", "Implement feature", "Detailed description", "Open", "user1", new[] { "backend", "urgent" });
        var @event = new WorkItemQueried(TestCommandId, correlation, details);
        var json = JsonSerializer.Serialize(@event, Options);
        var expected = File.ReadAllText("Fixtures/WorkItemQueried.json");
        Assert.Equal(expected.Trim(), json.Trim());
    }    // Round-trip tests
// Round-trip tests
[Fact]
public void QueryBacklogCommand_RoundTrip()
{
    var correlation = new Correlation(TestSessionId);
    var original = new QueryBacklog(TestCommandId, correlation, "urgent");
    var json = JsonSerializer.Serialize(original, Options);
    var deserialized = JsonSerializer.Deserialize<QueryBacklog>(json, Options);
    Assert.Equal(original, deserialized);
}

[Fact]
public void QueryWorkItemCommand_RoundTrip()
{
    var correlation = new Correlation(TestSessionId);
    var original = new QueryWorkItem(TestCommandId, correlation, new WorkItemRef("JIRA-123"));
    var json = JsonSerializer.Serialize(original, Options);
    var deserialized = JsonSerializer.Deserialize<QueryWorkItem>(json, Options);
    Assert.Equal(original, deserialized);
}

[Fact]
public void BacklogQueriedEvent_RoundTrip()
{
    var correlation = new Correlation(TestSessionId);
    var items = new[]
    {
        new WorkItemSummary("JIRA-123", "Implement feature", "Open", "user1"),
        new WorkItemSummary("JIRA-124", "Fix bug", "In Progress", "user2")
    };
    var original = new BacklogQueried(TestCommandId, correlation, items);
    var json = JsonSerializer.Serialize(original, Options);
    var deserialized = JsonSerializer.Deserialize<BacklogQueried>(json, Options);
    // Check individual properties since collection types may differ (array vs List)
    Assert.Equal(original.Id, deserialized.Id);
    Assert.Equal(original.Correlation, deserialized.Correlation);
    Assert.Equal(original.Kind, deserialized.Kind);
    Assert.Equal(original.Items.Count, deserialized.Items.Count);
    for (int i = 0; i < original.Items.Count; i++)
    {
        Assert.Equal(original.Items[i], deserialized.Items[i]);
    }
}

[Fact]
public void WorkItemQueriedEvent_RoundTrip()
{
    var correlation = new Correlation(TestSessionId);
    var details = new WorkItemDetails("JIRA-123", "Implement feature", "Detailed description", "Open", "user1", new[] { "backend", "urgent" });
    var original = new WorkItemQueried(TestCommandId, correlation, details);
    var json = JsonSerializer.Serialize(original, Options);
    var deserialized = JsonSerializer.Deserialize<WorkItemQueried>(json, Options);
    // Check individual properties since collection types may differ (array vs List)
    Assert.Equal(original.Id, deserialized.Id);
    Assert.Equal(original.Correlation, deserialized.Correlation);
    Assert.Equal(original.Kind, deserialized.Kind);
    Assert.Equal(original.Details.Id, deserialized.Details.Id);
    Assert.Equal(original.Details.Title, deserialized.Details.Title);
    Assert.Equal(original.Details.Description, deserialized.Details.Description);
    Assert.Equal(original.Details.Status, deserialized.Details.Status);
    Assert.Equal(original.Details.Assignee, deserialized.Details.Assignee);
    Assert.Equal(original.Details.Tags.Count, deserialized.Details.Tags.Count);
    for (int i = 0; i < original.Details.Tags.Count; i++)
    {
        Assert.Equal(original.Details.Tags[i], deserialized.Details.Tags[i]);
    }
}

// Configuration Tests

public class ConfigurationTests
{
    [Fact]
    public void AppConfig_BindsFromJsonConfiguration()
    {
        // Arrange
        var json = @"
        {
          ""AppConfig"": {
            ""Adapters"": {
              ""WorkItemsAdapter"": ""jira"",
              ""VcsAdapter"": ""git"",
              ""TerminalAdapter"": ""powershell""
            },
            ""SemanticKernel"": {
              ""DefaultProvider"": ""openai"",
              ""DefaultModel"": ""gpt-4"",
              ""MaxTokens"": 4096,
              ""Temperature"": 0.7,
              ""Timeout"": ""00:05:00""
            },
            ""Ui"": {
              ""Settings"": {
                ""Theme"": ""Dark"",
                ""FontSize"": 10,
                ""ShowStatusChips"": true,
                ""AutoScrollEvents"": false,
                ""ShowTimestamps"": true,
                ""MaxEventHistory"": 500
              }
            },
            ""Workspace"": {
              ""BasePath"": ""./workspaces"",
              ""AutoCreateDirectories"": true
            },
            ""Policy"": {
              ""Profiles"": {
                ""default"": {
                  ""Name"": ""Default Policy"",
                  ""ProtectedBranches"": [""master"", ""main""],
                  ""MaxFilesPerCommit"": 50,
                  ""RequireTestsBeforePush"": true,
                  ""RequireApprovalForPush"": false,
                  ""Limits"": {
                    ""CallsPerMinute"": 60,
                    ""Burst"": 10
                  }
                }
              },
              ""DefaultProfile"": ""default"",
              ""GlobalLimits"": {
                ""CallsPerMinute"": 120,
                ""Burst"": 20
              }
            }
          }
        }";

        var configuration = new ConfigurationBuilder()
            .AddJsonStream(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json)))
            .Build();

        Console.WriteLine("Diagnostic: configuration.AsEnumerable():");
        foreach (var kv in configuration.AsEnumerable())
        {
            Console.WriteLine($"  {kv.Key} = {kv.Value}");
        }

        // Act
        var appConfig = ConfigBuilder.GetAppConfig(configuration);

        // Assert
        Assert.NotNull(appConfig);
        Assert.Equal("jira", appConfig.Adapters.WorkItemsAdapter);
        Assert.Equal("git", appConfig.Adapters.VcsAdapter);
        Assert.Equal("powershell", appConfig.Adapters.TerminalAdapter);
        Assert.Equal("openai", appConfig.SemanticKernel.DefaultProvider);
        Assert.Equal("gpt-4", appConfig.SemanticKernel.DefaultModel);
        Assert.Equal(4096, appConfig.SemanticKernel.MaxTokens);
        Assert.Equal(0.7, appConfig.SemanticKernel.Temperature);
        Assert.Equal("Dark", appConfig.Ui.Settings.Theme);
        Assert.Equal(10, appConfig.Ui.Settings.FontSize);
        Assert.True(appConfig.Ui.Settings.ShowStatusChips);
        Assert.False(appConfig.Ui.Settings.AutoScrollEvents);
        Assert.Equal("./workspaces", appConfig.Workspace.BasePath);
        Assert.True(appConfig.Workspace.AutoCreateDirectories);
        Assert.Equal("default", appConfig.Policy.DefaultProfile);
        Assert.Single(appConfig.Policy.Profiles);
        Assert.Contains("default", appConfig.Policy.Profiles.Keys);
        Assert.Equal(120, appConfig.Policy.GlobalLimits.CallsPerMinute);
    }

    [Fact]
    public void AuthConfig_BindsJiraCredentials()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new KeyValuePair<string, string>("AppConfig:Auth:Jira:BaseUrl", "https://company.atlassian.net"),
                new KeyValuePair<string, string>("AppConfig:Auth:Jira:Username", "user@company.com"),
                new KeyValuePair<string, string>("AppConfig:Auth:Jira:ApiToken", "token123")
            })
            .Build();

        // Act
        var appConfig = ConfigBuilder.GetAppConfig(configuration);

        // Assert
        Assert.NotNull(appConfig.Auth.Jira);
        Assert.Equal("https://company.atlassian.net", appConfig.Auth.Jira.BaseUrl);
        Assert.Equal("user@company.com", appConfig.Auth.Jira.Username);
        Assert.Equal("token123", appConfig.Auth.Jira.ApiToken);
    }

    [Fact]
    public void AuthConfig_BindsGitHubCredentials()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new KeyValuePair<string, string>("AppConfig:Auth:GitHub:Token", "gh_token123"),
                new KeyValuePair<string, string>("AppConfig:Auth:GitHub:DefaultOrg", "myorg"),
                new KeyValuePair<string, string>("AppConfig:Auth:GitHub:DefaultRepo", "myrepo")
            })
            .Build();

        // Act
        var appConfig = ConfigBuilder.GetAppConfig(configuration);

        // Assert
        Assert.NotNull(appConfig.Auth.GitHub);
        Assert.Equal("gh_token123", appConfig.Auth.GitHub.Token);
        Assert.Equal("myorg", appConfig.Auth.GitHub.DefaultOrg);
        Assert.Equal("myrepo", appConfig.Auth.GitHub.DefaultRepo);
    }

    [Fact]
    public void AuthConfig_BindsOpenAICredentials()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new KeyValuePair<string, string>("AppConfig:Auth:OpenAI:ApiKey", "openai_key123"),
                new KeyValuePair<string, string>("AppConfig:Auth:OpenAI:OrganizationId", "org123")
            })
            .Build();

        // Act
        var appConfig = ConfigBuilder.GetAppConfig(configuration);

        // Assert
        Assert.NotNull(appConfig.Auth.OpenAI);
        Assert.Equal("openai_key123", appConfig.Auth.OpenAI.ApiKey);
        Assert.Equal("org123", appConfig.Auth.OpenAI.OrganizationId);
    }

    [Fact]
    public void AuthConfig_BindsAzureOpenAICredentials()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new KeyValuePair<string, string>("AppConfig:Auth:AzureOpenAI:Endpoint", "https://resource.openai.azure.com"),
                new KeyValuePair<string, string>("AppConfig:Auth:AzureOpenAI:ApiKey", "azure_key123"),
                new KeyValuePair<string, string>("AppConfig:Auth:AzureOpenAI:DeploymentName", "gpt-4-deployment")
            })
            .Build();

        // Act
        var appConfig = ConfigBuilder.GetAppConfig(configuration);

        // Assert
        Assert.NotNull(appConfig.Auth.AzureOpenAI);
        Assert.Equal("https://resource.openai.azure.com", appConfig.Auth.AzureOpenAI.Endpoint);
        Assert.Equal("azure_key123", appConfig.Auth.AzureOpenAI.ApiKey);
        Assert.Equal("gpt-4-deployment", appConfig.Auth.AzureOpenAI.DeploymentName);
    }

    [Fact]
    public void AuthConfig_BindsGitCredentials()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new KeyValuePair<string, string>("AppConfig:Auth:Git:SshKeyPath", "/home/user/.ssh/id_rsa"),
                new KeyValuePair<string, string>("AppConfig:Auth:Git:PersonalAccessToken", "git_token123"),
                new KeyValuePair<string, string>("AppConfig:Auth:Git:UserName", "John Doe"),
                new KeyValuePair<string, string>("AppConfig:Auth:Git:UserEmail", "john@example.com"),
                new KeyValuePair<string, string>("AppConfig:Auth:Git:DefaultRemote", "upstream")
            })
            .Build();

        // Act
        var appConfig = ConfigBuilder.GetAppConfig(configuration);

        // Assert
        Assert.NotNull(appConfig.Auth.Git);
        Assert.Equal("/home/user/.ssh/id_rsa", appConfig.Auth.Git.SshKeyPath);
        Assert.Equal("git_token123", appConfig.Auth.Git.PersonalAccessToken);
        Assert.Equal("John Doe", appConfig.Auth.Git.UserName);
        Assert.Equal("john@example.com", appConfig.Auth.Git.UserEmail);
        Assert.Equal("upstream", appConfig.Auth.Git.DefaultRemote);
    }

    [Fact]
    public void PolicyProfile_BindsWithRateLimits()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new KeyValuePair<string, string>("AppConfig:Policy:Profiles:test:Name", "Test Policy"),
                new KeyValuePair<string, string>("AppConfig:Policy:Profiles:test:ProtectedBranches:0", "master"),
                new KeyValuePair<string, string>("AppConfig:Policy:Profiles:test:ProtectedBranches:1", "main"),
                new KeyValuePair<string, string>("AppConfig:Policy:Profiles:test:MaxFilesPerCommit", "25"),
                new KeyValuePair<string, string>("AppConfig:Policy:Profiles:test:RequireTestsBeforePush", "True"),
                new KeyValuePair<string, string>("AppConfig:Policy:Profiles:test:RequireApprovalForPush", "True"),
                new KeyValuePair<string, string>("AppConfig:Policy:Profiles:test:Limits:CallsPerMinute", "30"),
                new KeyValuePair<string, string>("AppConfig:Policy:Profiles:test:Limits:Burst", "5"),
                new KeyValuePair<string, string>("AppConfig:Policy:Profiles:test:Limits:PerCommandCaps:RunTests", "2"),
                new KeyValuePair<string, string>("AppConfig:Policy:Profiles:test:Limits:PerCommandCaps:Push", "1"),
                new KeyValuePair<string, string>("AppConfig:Policy:DefaultProfile", "test"),
                new KeyValuePair<string, string>("AppConfig:Policy:GlobalLimits:CallsPerMinute", "100"),
                new KeyValuePair<string, string>("AppConfig:Policy:GlobalLimits:Burst", "10")
            })
            .Build();

        Console.WriteLine("Diagnostic: configuration.AsEnumerable():");
        foreach (var kv in configuration.AsEnumerable())
        {
            Console.WriteLine($"  {kv.Key} = {kv.Value}");
        }

        Console.WriteLine("Configuration providers:");
        foreach (var provider in configuration.Providers)
        {
            Console.WriteLine($"Provider: {provider.GetType().Name}");
        }

        Console.WriteLine("AppConfig section:");
        var appConfigSection = configuration.GetSection("AppConfig");
        Console.WriteLine($"AppConfig exists: {appConfigSection.Exists()}");

        var policySection = appConfigSection.GetSection("Policy");
        Console.WriteLine($"Policy exists: {policySection.Exists()}");

        var profilesSection = policySection.GetSection("Profiles");
        Console.WriteLine($"Profiles exists: {profilesSection.Exists()}");

        var testProfileSection = profilesSection.GetSection("test");
        Console.WriteLine($"test profile exists: {testProfileSection.Exists()}");

        var limitsSection = testProfileSection.GetSection("Limits");
        Console.WriteLine($"Limits exists: {limitsSection.Exists()}");

        var perCommandCapsSection = limitsSection.GetSection("PerCommandCaps");
        Console.WriteLine($"PerCommandCaps exists: {perCommandCapsSection.Exists()}");

        Console.WriteLine("PerCommandCaps children:");
        foreach (var child in perCommandCapsSection.GetChildren())
        {
            Console.WriteLine($"{child.Key} = {child.Value}");
        }

        // Act
        var appConfig = ConfigBuilder.GetAppConfig(configuration);

        // Assert
        Assert.NotNull(appConfig.Policy.Profiles);
        Assert.Contains("test", appConfig.Policy.Profiles.Keys);
        var profile = appConfig.Policy.Profiles["test"];
        Assert.Equal("Test Policy", profile.Name);
        Assert.Equal(2, profile.ProtectedBranches.Count);
        Assert.Contains("master", profile.ProtectedBranches);
        Assert.Contains("main", profile.ProtectedBranches);
        Assert.Equal(25, profile.MaxFilesPerCommit);
        Assert.True(profile.RequireTestsBeforePush);
        Assert.True(profile.RequireApprovalForPush);
        Assert.NotNull(profile.Limits);
        Assert.Equal(30, profile.Limits.CallsPerMinute);
        Assert.Equal(5, profile.Limits.Burst);
        Assert.Equal(2, profile.Limits.PerCommandCaps["RunTests"]);
        Assert.Equal(1, profile.Limits.PerCommandCaps["Push"]);
        Assert.Equal("test", appConfig.Policy.DefaultProfile);
        Assert.NotNull(appConfig.Policy.GlobalLimits);
        Assert.Equal(100, appConfig.Policy.GlobalLimits.CallsPerMinute);
        Assert.Equal(10, appConfig.Policy.GlobalLimits.Burst);
    }

    [Fact]
    public void ConfigBuilder_BuildsConfigurationWithEnvironmentVariables()
    {
        // Arrange
        Environment.SetEnvironmentVariable("JUNIORDEV__AppConfig__Adapters__WorkItemsAdapter", "github");
        Environment.SetEnvironmentVariable("JUNIORDEV__AppConfig__Adapters__VcsAdapter", "git");
        Environment.SetEnvironmentVariable("JUNIORDEV__AppConfig__Adapters__TerminalAdapter", "powershell");
        Environment.SetEnvironmentVariable("JUNIORDEV__AppConfig__SemanticKernel__DefaultProvider", "openai");
        Environment.SetEnvironmentVariable("JUNIORDEV__AppConfig__SemanticKernel__DefaultModel", "gpt-3.5-turbo");

        try
        {
            // Act
            var basePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
            var config = ConfigBuilder.Build(basePath: basePath, skipDefaults: true); // Skip defaults to test env vars only
            var appConfig = ConfigBuilder.GetAppConfig(config);

            // Assert
            Assert.Equal("github", appConfig.Adapters?.WorkItemsAdapter);
            Assert.Equal("git", appConfig.Adapters?.VcsAdapter);
            Assert.Equal("powershell", appConfig.Adapters?.TerminalAdapter);
            Assert.Equal("openai", appConfig.SemanticKernel?.DefaultProvider);
            Assert.Equal("gpt-3.5-turbo", appConfig.SemanticKernel?.DefaultModel);
        }
        finally
        {
            // Cleanup
            Environment.SetEnvironmentVariable("JUNIORDEV__AppConfig__Adapters__WorkItemsAdapter", null);
            Environment.SetEnvironmentVariable("JUNIORDEV__AppConfig__Adapters__VcsAdapter", null);
            Environment.SetEnvironmentVariable("JUNIORDEV__AppConfig__Adapters__TerminalAdapter", null);
            Environment.SetEnvironmentVariable("JUNIORDEV__AppConfig__SemanticKernel__DefaultProvider", null);
            Environment.SetEnvironmentVariable("JUNIORDEV__AppConfig__SemanticKernel__DefaultModel", null);
        }
    }

    [Fact]
    public void ValidateLiveAdapterCredentials_SucceedsWithValidConfig()
    {
        // Arrange
        var appConfig = new AppConfig
        {
            Adapters = new AdaptersConfig("github", "git", "powershell"),
            Auth = new AuthConfig
            {
                GitHub = new GitHubAuthConfig("ghp_1234567890abcdef")
            }
        };

        // Act & Assert - Should not throw exception
        var exception = Record.Exception(() => ConfigBuilder.ValidateLiveAdapterCredentials(appConfig));
        Assert.Null(exception);
    }

    [Fact]
    public void ValidateLiveAdapterCredentials_FailsWithMissingGitHubConfig()
    {
        // Arrange
        var appConfig = new AppConfig
        {
            Adapters = new AdaptersConfig("github", "git", "powershell")
        };

        // Act & Assert - Should throw with specific error
        var exception = Record.Exception(() => ConfigBuilder.ValidateLiveAdapterCredentials(appConfig));
        Assert.NotNull(exception);
        Assert.Contains("GitHub authentication not configured", exception.Message);
    }

    [Fact]
    public void ValidateLiveAdapterCredentials_FailsWithIncompleteJiraConfig()
    {
        // Arrange
        var appConfig = new AppConfig
        {
            Adapters = new AdaptersConfig("jira", "git", "powershell"),
            Auth = new AuthConfig
            {
                Jira = new JiraAuthConfig("", "user@company.com", "api-token-123")
            }
        };

        // Act & Assert - Should throw with specific error
        var exception = Record.Exception(() => ConfigBuilder.ValidateLiveAdapterCredentials(appConfig));
        Assert.NotNull(exception);
        Assert.Contains("Jira BaseUrl is required", exception.Message);
    }

    [Fact]
    public void ValidateLiveAdapters_SucceedsWithFakeAdapters()
    {
        // Arrange
        var appConfig = new AppConfig
        {
            Adapters = new AdaptersConfig("fake", "fake", "powershell"),
            LivePolicy = new LivePolicyConfig { RequireCredentialsValidation = true }
        };

        // Act & Assert - Should not throw exception
        var exception = Record.Exception(() => ConfigBuilder.ValidateLiveAdapters(appConfig));
        Assert.Null(exception);
    }

    [Fact]
    public void ValidateLiveAdapters_SucceedsWithLiveAdaptersAndValidCreds()
    {
        // Arrange
        var appConfig = new AppConfig
        {
            Adapters = new AdaptersConfig("github", "git", "powershell"),
            Auth = new AuthConfig
            {
                GitHub = new GitHubAuthConfig("ghp_1234567890abcdef")
            },
            LivePolicy = new LivePolicyConfig { RequireCredentialsValidation = true }
        };

        // Act & Assert - Should not throw exception
        var exception = Record.Exception(() => ConfigBuilder.ValidateLiveAdapters(appConfig));
        Assert.Null(exception);
    }

    [Fact]
    public void ValidateLiveAdapters_SucceedsWithLiveAdaptersAndValidationDisabled()
    {
        // Arrange
        var appConfig = new AppConfig
        {
            Adapters = new AdaptersConfig("github", "git", "powershell"),
            LivePolicy = new LivePolicyConfig { RequireCredentialsValidation = false }
        };

        // Act & Assert - Should not throw exception even without creds
        var exception = Record.Exception(() => ConfigBuilder.ValidateLiveAdapters(appConfig));
        Assert.Null(exception);
    }

    [Fact]
    public void ValidateLiveAdapters_FailsWithLiveAdaptersAndMissingCreds()
    {
        // Arrange
        var appConfig = new AppConfig
        {
            Adapters = new AdaptersConfig("github", "git", "powershell"),
            LivePolicy = new LivePolicyConfig { RequireCredentialsValidation = true }
        };

        // Act & Assert - Should throw with specific error
        var exception = Record.Exception(() => ConfigBuilder.ValidateLiveAdapters(appConfig));
        Assert.NotNull(exception);
        Assert.Contains("Live adapter credentials validation failed", exception.Message);
    }

    [Fact]
    public void AppConfig_DefaultsToNullAdapters_WhenNotConfigured()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new KeyValuePair<string, string>("AppConfig:SemanticKernel:DefaultProvider", "openai"),
                new KeyValuePair<string, string>("AppConfig:SemanticKernel:DefaultModel", "gpt-4")
            })
            .Build();

        // Act
        var appConfig = ConfigBuilder.GetAppConfig(configuration);

        // Assert
        Assert.NotNull(appConfig);
        Assert.Null(appConfig.Adapters); // Should be null when not configured
    }

    [Fact]
    public void LivePolicyConfig_HasSafeDefaults()
    {
        // Arrange & Act
        var livePolicy = new LivePolicyConfig();

        // Assert
        Assert.False(livePolicy.PushEnabled);
        Assert.True(livePolicy.DryRun);
        Assert.True(livePolicy.RequireCredentialsValidation);
    }
}
}
