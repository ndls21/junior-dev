using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using JuniorDev.Agents;
using JuniorDev.Agents.Sk;
using JuniorDev.Build.Dotnet;
using JuniorDev.Contracts;
using JuniorDev.Orchestrator;
using JuniorDev.VcsGit;
using JuniorDev.WorkItems.Jira;
using JuniorDev.WorkItems.GitHub;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Gauntlet.E2E;

/// <summary>
/// End-to-end smoke test harness for the Junior Dev platform.
/// Tests the full pipeline: orchestrator + adapters (fakes) + agents.
/// </summary>
public class GauntletSmokeTest
{
    private readonly ITestOutputHelper _output;

    public GauntletSmokeTest(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    [Trait("Category", "Smoke")]
    public async Task FakeModeSmokeTest_QueriesBacklog_ProcessesWorkItem_ExecutesVcsOperations()
    {
        _output.WriteLine("=== GAUNTLET E2E SMOKE TEST (FAKE MODE) STARTED ===");
        _output.WriteLine("Purpose: Test full pipeline from backlog query through VCS operations using fakes only");

        await RunSmokeTest(useLiveAdapters: false);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task LiveModeSmokeTest_QueriesBacklog_ProcessesWorkItem_ExecutesVcsOperations()
    {
        // Load configuration using ConfigBuilder (appsettings + env + user-secrets)
        var config = ConfigBuilder.Build("Development", Path.GetFullPath("../../.."));
        var appConfig = ConfigBuilder.GetAppConfig(config);

        // Check if live mode is enabled
        var runLive = Environment.GetEnvironmentVariable("RUN_LIVE") == "1";
        if (!runLive)
        {
            _output.WriteLine("=== LIVE MODE SMOKE TEST SKIPPED ===");
            _output.WriteLine("Set RUN_LIVE=1 to enable live testing");
            return;
        }

        // Validate required configuration
        var hasJiraConfig = appConfig.Auth?.Jira != null &&
                           !string.IsNullOrEmpty(appConfig.Auth.Jira.BaseUrl) &&
                           !string.IsNullOrEmpty(appConfig.Auth.Jira.Username) &&
                           !string.IsNullOrEmpty(appConfig.Auth.Jira.ApiToken);

        var hasGitConfig = appConfig.Auth?.Git != null &&
                          (!string.IsNullOrEmpty(appConfig.Auth.Git.PersonalAccessToken) ||
                           !string.IsNullOrEmpty(appConfig.Auth.Git.SshKeyPath));

        var hasGitHubConfig = appConfig.Auth?.GitHub != null &&
                             !string.IsNullOrEmpty(appConfig.Auth.GitHub.Token) &&
                             (!string.IsNullOrEmpty(appConfig.Auth.GitHub.DefaultOrg) ||
                              !string.IsNullOrEmpty(appConfig.Auth.GitHub.DefaultRepo));

        if (!hasJiraConfig)
        {
            _output.WriteLine("=== LIVE MODE SMOKE TEST SKIPPED ===");
            _output.WriteLine("Missing required Jira configuration in appsettings.json, environment variables, or user-secrets");
            _output.WriteLine("Required: Auth.Jira.BaseUrl, Auth.Jira.Username, Auth.Jira.ApiToken");
            return;
        }

        if (!hasGitConfig)
        {
            _output.WriteLine("=== LIVE MODE SMOKE TEST SKIPPED ===");
            _output.WriteLine("Missing required Git configuration in appsettings.json, environment variables, or user-secrets");
            _output.WriteLine("Required: Auth.Git.PersonalAccessToken or Auth.Git.SshKeyPath");
            return;
        }

        if (!hasGitHubConfig)
        {
            _output.WriteLine("=== LIVE MODE SMOKE TEST SKIPPED ===");
            _output.WriteLine("Missing required GitHub configuration in appsettings.json, environment variables, or user-secrets");
            _output.WriteLine("Required: Auth.GitHub.Token and (Auth.GitHub.DefaultOrg or Auth.GitHub.DefaultRepo)");
            return;
        }

        _output.WriteLine("=== GAUNTLET E2E SMOKE TEST (LIVE MODE) STARTED ===");
        _output.WriteLine("Purpose: Test full pipeline with real Jira/Git adapters");
        _output.WriteLine("WARNING: This test interacts with real external services!");
        _output.WriteLine($"Jira: {appConfig.Auth!.Jira!.BaseUrl}");
        _output.WriteLine($"Git: Configured with {(appConfig.Auth!.Git!.PersonalAccessToken != null ? "PAT" : "SSH key")}");

        await RunSmokeTest(useLiveAdapters: true, appConfig: appConfig);
    }

    private async Task RunSmokeTest(bool useLiveAdapters, AppConfig? appConfig = null)
    {
        // Setup DI container with all services
        var services = new ServiceCollection();
        services.AddOrchestrator();           // Core orchestrator with fake adapters
        services.AddDotnetBuildAdapter();     // Optional: adds real build functionality
        services.AddAgentSdk();
        services.AddAgent<PlannerAgent>();
        services.AddAgent<ExecutorAgent>();
        services.AddAgent<ReviewerAgent>();

        // Configure adapters based on mode
        if (useLiveAdapters && appConfig != null)
        {
            // Set environment variables from configuration for live adapters
            if (appConfig.Auth?.Jira != null)
            {
                Environment.SetEnvironmentVariable("JIRA_URL", appConfig.Auth.Jira.BaseUrl);
                Environment.SetEnvironmentVariable("JIRA_USER", appConfig.Auth.Jira.Username);
                Environment.SetEnvironmentVariable("JIRA_TOKEN", appConfig.Auth.Jira.ApiToken);
                // Note: JIRA_PROJECT is not in the config, so we'll use a default or skip project-specific operations
                Environment.SetEnvironmentVariable("JIRA_PROJECT", "TEST"); // Default for testing
            }

            if (appConfig.Auth?.GitHub != null)
            {
                Environment.SetEnvironmentVariable("GITHUB_TOKEN", appConfig.Auth.GitHub.Token);
                // Set repo from config or use environment variable
                var repo = !string.IsNullOrEmpty(appConfig.Auth.GitHub.DefaultRepo) 
                    ? $"{appConfig.Auth.GitHub.DefaultOrg ?? "owner"}/{appConfig.Auth.GitHub.DefaultRepo}"
                    : Environment.GetEnvironmentVariable("GITHUB_REPO") ?? "owner/repo";
                Environment.SetEnvironmentVariable("GITHUB_REPO", repo);
            }

            // Override with real adapters for live mode
            services.AddSingleton<IAdapter>(new JuniorDev.WorkItems.Jira.JiraAdapter());
            services.AddSingleton<IAdapter>(new JuniorDev.VcsGit.VcsGitAdapter(new JuniorDev.VcsGit.VcsConfig
            {
                RepoPath = "/tmp/live-test-repo",
                AllowPush = Environment.GetEnvironmentVariable("RUN_LIVE_PUSH") == "1",
                IsIntegrationTest = true
            }, isFake: false));
            services.AddSingleton<IAdapter>(new JuniorDev.WorkItems.GitHub.GitHubAdapter());
            _output.WriteLine("Using real Jira, Git, and GitHub adapters");
        }
        else
        {
            // Use fake adapters for smoke mode (default)
            // Fakes are registered automatically by AddOrchestrator
            _output.WriteLine("Using fake adapters");
        }

        // Override with test-specific config
        services.AddSingleton(new AgentConfig
        {
            DryRun = false,
            EnableDetailedLogging = true,
            EnableMetrics = true,
            AgentProfile = "test-agent"
        });

        await using var provider = services.BuildServiceProvider();
        var sessionManager = provider.GetRequiredService<ISessionManager>();

        // Create session config
        var sessionId = Guid.NewGuid();
        var sessionConfig = new SessionConfig(
            sessionId,
            null,
            null,
            new PolicyProfile
            {
                Name = useLiveAdapters ? "live-smoke-test" : "smoke-test",
                ProtectedBranches = new HashSet<string> { "main", "master" },
                RequireTestsBeforePush = false,
                RequireApprovalForPush = !useLiveAdapters // Only allow push in fake mode by default
            },
            new RepoRef("test-repo", useLiveAdapters ? "/tmp/live-test-repo" : "/tmp/test-repo"),
            new WorkspaceRef(""), // Empty means temp workspace
            null,
            "test-agent"
        );

        _output.WriteLine($"Created session {sessionId} with temp workspace");

        // Start the session
        await sessionManager.CreateSession(sessionConfig);
        _output.WriteLine("Session created successfully");

        // Subscribe to events for monitoring
        var events = new List<IEvent>();
        var subscription = sessionManager.Subscribe(sessionId);
        var eventCollectionTask = Task.Run(async () =>
        {
            await foreach (var @event in subscription)
            {
                events.Add(@event);
                _output.WriteLine($"Event: {@event.Kind}");
            }
        });

        // Execute all commands
        _output.WriteLine("--- Executing commands ---");

        // Query backlog
        var queryBacklogCmd = new QueryBacklog(Guid.NewGuid(), new Correlation(sessionId), null);
        await sessionManager.PublishCommand(queryBacklogCmd);
        _output.WriteLine("Backlog query published");

        // Query work item
        var queryWorkItemCmd = new QueryWorkItem(Guid.NewGuid(), new Correlation(sessionId), new WorkItemRef("PROJ-123"));
        await sessionManager.PublishCommand(queryWorkItemCmd);
        _output.WriteLine("Work item query published");

        // Create branch
        var createBranchCmd = new CreateBranch(Guid.NewGuid(), new Correlation(sessionId), new RepoRef("test-repo", "/tmp/test-repo"), "feature/proj-123");
        await sessionManager.PublishCommand(createBranchCmd);
        _output.WriteLine("Create branch published");

        // Commit
        var commitCmd = new Commit(Guid.NewGuid(), new Correlation(sessionId), new RepoRef("test-repo", "/tmp/test-repo"), "Implement PROJ-123", new List<string> { "." }, false);
        await sessionManager.PublishCommand(commitCmd);
        _output.WriteLine("Commit published");

        // Run tests
        var testCmd = new RunTests(Guid.NewGuid(), new Correlation(sessionId), new RepoRef("test-repo", "/tmp/test-repo"), null, TimeSpan.FromMinutes(2));
        await sessionManager.PublishCommand(testCmd);
        _output.WriteLine("Run tests published");

        // Build project (demonstrates build adapter integration)
        var buildCmd = new BuildProject(Guid.NewGuid(), new Correlation(sessionId), new RepoRef("test-repo", "/tmp/test-repo"), "src/TestProject.csproj", "Release", "net8.0", new[] { "Build" }, TimeSpan.FromMinutes(5));
        await sessionManager.PublishCommand(buildCmd);
        _output.WriteLine("Build project published");

        // Push - only in fake mode by default for safety
        if (!useLiveAdapters)
        {
            var pushCmd = new Push(Guid.NewGuid(), new Correlation(sessionId), new RepoRef("test-repo", "/tmp/test-repo"), "feature/proj-123");
            await sessionManager.PublishCommand(pushCmd);
            _output.WriteLine("Push published (fake mode only)");
        }
        else
        {
            _output.WriteLine("Push SKIPPED in live mode for safety (set RUN_LIVE_PUSH=1 to override)");
        }

        // Complete the session
        ((SessionManager)sessionManager).CompleteSession(sessionId);
        _output.WriteLine("Session completed");

        // Wait for all events (longer for live services)
        await Task.Delay(useLiveAdapters ? 5000 : 3000); // Longer wait for live services

        // Analyze results
        var acceptedCommands = events.Count(e => e.Kind == nameof(CommandAccepted));
        var completedCommands = events.Count(e => e.Kind == nameof(CommandCompleted));
        var successfulCommands = events.Count(e => e.Kind == nameof(CommandCompleted) && ((CommandCompleted)e).Outcome == CommandOutcome.Success);
        var artifactEvents = events.Count(e => e.Kind == nameof(ArtifactAvailable));

        _output.WriteLine("=== RESULTS ===");
        _output.WriteLine($"Commands accepted: {acceptedCommands}");
        _output.WriteLine($"Commands completed: {completedCommands}");
        _output.WriteLine($"Commands successful: {successfulCommands}");
        _output.WriteLine($"Artifacts generated: {artifactEvents}");

        // Assertions - allow for concurrent execution and session completion timing
        Assert.True(acceptedCommands >= 6, $"Expected at least 6 accepted commands, got {acceptedCommands}");
        Assert.True(completedCommands >= 6, $"Expected at least 6 completed commands, got {completedCommands}");
        Assert.True(successfulCommands >= 5, $"Expected at least 5 successful commands (build may fail in test env), got {successfulCommands}");
        Assert.True(artifactEvents > 0);

        _output.WriteLine($"=== GAUNTLET E2E SMOKE TEST ({(useLiveAdapters ? "LIVE" : "FAKE")}) PASSED ===");

        // Generate reports and collect artifacts
        await GenerateSmokeTestReport(sessionId, events, useLiveAdapters, sessionConfig);
    }

    private async Task GenerateSmokeTestReport(Guid sessionId, List<IEvent> events, bool useLiveAdapters, SessionConfig sessionConfig)
    {
        // Create output directory
        var outputDir = Path.Combine(Path.GetTempPath(), $"junior-dev-smoke-{sessionId}");
        Directory.CreateDirectory(outputDir);
        _output.WriteLine($"Artifacts output directory: {outputDir}");

        // Collect artifacts from events
        var artifacts = events.Where(e => e.Kind == nameof(ArtifactAvailable))
                             .Select(e => (ArtifactAvailable)e)
                             .ToList();

        // Generate detailed JSON report
        var report = new
        {
            TestRun = new
            {
                SessionId = sessionId,
                Mode = useLiveAdapters ? "live" : "fake",
                Timestamp = DateTime.UtcNow,
                Duration = "N/A", // Could be enhanced with timing
                PolicyProfile = sessionConfig.Policy.Name
            },
            Commands = new
            {
                Total = events.Count(e => e.Kind == nameof(CommandAccepted)),
                Completed = events.Count(e => e.Kind == nameof(CommandCompleted)),
                Successful = events.Count(e => e.Kind == nameof(CommandCompleted) &&
                                             ((CommandCompleted)e).Outcome == CommandOutcome.Success),
                Failed = events.Count(e => e.Kind == nameof(CommandCompleted) &&
                                         ((CommandCompleted)e).Outcome != CommandOutcome.Success)
            },
            Events = new
            {
                Total = events.Count,
                ByType = events.GroupBy(e => e.Kind)
                              .ToDictionary(g => g.Key, g => g.Count())
            },
            Artifacts = new
            {
                Total = artifacts.Count,
                Types = artifacts.GroupBy(a => a.Artifact.Kind)
                                .ToDictionary(g => g.Key, g => g.Count()),
                Details = artifacts.Select(a => new
                {
                    Id = a.Id,
                    Type = a.Artifact.Kind,
                    Name = a.Artifact.Name,
                    CorrelationId = a.Correlation?.CommandId
                }).ToList()
            },
            Breadcrumbs = events.Select(e => new
            {
                Timestamp = DateTime.UtcNow, // Could use actual event timestamp if available
                EventType = e.Kind,
                CorrelationId = e.Correlation?.CommandId,
                Details = e switch
                {
                    CommandAccepted ca => $"Command {ca.CommandId} accepted",
                    CommandCompleted cc => $"Command {cc.CommandId} completed with outcome {cc.Outcome}",
                    CommandRejected cr => $"Command {cr.CommandId} rejected: {cr.Reason}",
                    ArtifactAvailable aa => $"Artifact {aa.Id} ({aa.Artifact.Kind}) available: {aa.Artifact.Name}",
                    _ => e.Kind
                }
            }).ToList()
        };

        // Save JSON report
        var jsonReportPath = Path.Combine(outputDir, "smoke-test-report.json");
        await File.WriteAllTextAsync(jsonReportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions
        {
            WriteIndented = true
        }));

        // Generate markdown summary
        var markdownSummary = $@"# Junior Dev Smoke Test Report

## Test Run Summary
- **Session ID**: {sessionId}
- **Mode**: {(useLiveAdapters ? "Live" : "Fake")}
- **Timestamp**: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss UTC}
- **Policy Profile**: {sessionConfig.Policy.Name}

## Command Results
- **Total Commands**: {report.Commands.Total}
- **Completed**: {report.Commands.Completed}
- **Successful**: {report.Commands.Successful}
- **Failed**: {report.Commands.Failed}

## Events Summary
- **Total Events**: {report.Events.Total}
{string.Join("\n", report.Events.ByType.Select(kvp => $"- **{kvp.Key}**: {kvp.Value}"))}

## Artifacts Generated
- **Total Artifacts**: {report.Artifacts.Total}
{string.Join("\n", report.Artifacts.Types.Select(kvp => $"- **{kvp.Key}**: {kvp.Value}"))}

## Detailed Artifacts
{string.Join("\n", report.Artifacts.Details.Select(a => $"- {a.Type}: {a.Name} (ID: {a.Id})"))}

## Execution Breadcrumbs
{string.Join("\n", report.Breadcrumbs.Select((b, i) => $"{i + 1:00}. {b.Details}"))}

---
*Report generated by Gauntlet E2E Smoke Test*
";

        // Save markdown summary
        var markdownPath = Path.Combine(outputDir, "README.md");
        await File.WriteAllTextAsync(markdownPath, markdownSummary);

        // Save raw event log
        var eventLogPath = Path.Combine(outputDir, "event-log.json");
        await File.WriteAllTextAsync(eventLogPath, JsonSerializer.Serialize(events.Select(e => new
        {
            e.Kind,
            CorrelationId = e.Correlation?.CommandId,
            Timestamp = DateTime.UtcNow,
            Details = e.ToString()
        }), new JsonSerializerOptions { WriteIndented = true }));

        // Collect and save artifact contents (if available)
        foreach (var artifact in artifacts)
        {
            try
            {
                // Note: In a real implementation, you'd need to access the actual artifact content
                // For now, we'll just log the artifact metadata
                var artifactPath = Path.Combine(outputDir, $"artifact-{artifact.Id}.json");
                await File.WriteAllTextAsync(artifactPath, JsonSerializer.Serialize(new
                {
                    artifact.Id,
                    artifact.Artifact.Kind,
                    artifact.Artifact.Name,
                    artifact.Correlation
                }, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Warning: Failed to save artifact {artifact.Id}: {ex.Message}");
            }
        }

        _output.WriteLine("=== REPORTS GENERATED ===");
        _output.WriteLine($"JSON Report: {jsonReportPath}");
        _output.WriteLine($"Markdown Summary: {markdownPath}");
        _output.WriteLine($"Event Log: {eventLogPath}");
        _output.WriteLine($"Artifacts saved: {artifacts.Count}");

        // In CI, these would be uploaded as build artifacts
        if (Environment.GetEnvironmentVariable("CI") == "true")
        {
            _output.WriteLine("CI detected - artifacts would be uploaded here");
        }
    }
}