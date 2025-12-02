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
    [Trait("Category", "Smoke")]
    public async Task BuildProjectFailCase_InvalidProject_Rejected()
    {
        _output.WriteLine("=== BUILD PROJECT FAIL CASE TEST STARTED ===");
        _output.WriteLine("Purpose: Test that BuildProject properly rejects invalid project paths");

        // Setup DI container with build adapter
        var services = new ServiceCollection();
        services.AddOrchestrator();
        services.AddDotnetBuildAdapter();
        services.AddAgentSdk();

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
                Name = "test",
                ProtectedBranches = new HashSet<string> { "main", "master" },
                RequireTestsBeforePush = false,
                RequireApprovalForPush = false
            },
            new RepoRef("test-repo", "/tmp/test-repo"),
            new WorkspaceRef(""),
            null,
            "test-agent"
        );

        await sessionManager.CreateSession(sessionConfig);

        // Subscribe to events
        var events = new List<IEvent>();
        var subscription = sessionManager.Subscribe(sessionId);
        var eventCollectionTask = Task.Run(async () =>
        {
            await foreach (var @event in subscription)
            {
                events.Add(@event);
            }
        });

        // Try to build with invalid project path
        var buildCmd = new BuildProject(
            Guid.NewGuid(),
            new Correlation(sessionId),
            new RepoRef("test-repo", "/tmp/test-repo"),
            "../../../etc/passwd", // Invalid path
            "Release",
            "net8.0");

        await sessionManager.PublishCommand(buildCmd);

        // Wait for events
        await Task.Delay(3000);

        // Verify the command was rejected
        var rejectedEvents = events.Where(e => e.Kind == nameof(CommandRejected)).ToList();
        Assert.Single(rejectedEvents);

        var rejected = (CommandRejected)rejectedEvents[0];
        Assert.Contains("Invalid project path", rejected.Reason);

        _output.WriteLine("=== BUILD PROJECT FAIL CASE TEST PASSED ===");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task LiveModeSmokeTest_QueriesBacklog_ProcessesWorkItem_ExecutesVcsOperations()
    {
        // Check if live mode is enabled
        var runLive = Environment.GetEnvironmentVariable("RUN_LIVE") == "1";
        if (!runLive)
        {
            _output.WriteLine("=== LIVE MODE SMOKE TEST SKIPPED ===");
            _output.WriteLine("Set RUN_LIVE=1 to enable live testing");
            return;
        }

        // For live mode, we need configuration. Try to load it, but provide defaults if binding fails
        AppConfig? appConfig = null;
        try
        {
            var config = ConfigBuilder.Build("Development", Path.GetFullPath("../../.."));
            appConfig = ConfigBuilder.GetAppConfig(config);
            
            // Validate live adapters before proceeding - this enforces LivePolicy safety
            ConfigBuilder.ValidateLiveAdapters(appConfig);
        }
        catch (Exception ex)
        {
            _output.WriteLine($"Warning: Failed to load full config: {ex.Message}");
            _output.WriteLine("Using environment variables directly for live testing");
            // Don't create minimal config - we'll rely on environment variables
        }

        // Validate required configuration - check both config and environment variables
        var hasJiraConfig = (appConfig?.Auth?.Jira != null &&
                           !string.IsNullOrEmpty(appConfig.Auth.Jira.BaseUrl) &&
                           !string.IsNullOrEmpty(appConfig.Auth.Jira.Username) &&
                           !string.IsNullOrEmpty(appConfig.Auth.Jira.ApiToken)) ||
                           (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("JIRA_URL")) &&
                            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("JIRA_USER")) &&
                            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("JIRA_TOKEN")));

        var hasGitConfig = (appConfig?.Auth?.Git != null &&
                          (!string.IsNullOrEmpty(appConfig.Auth.Git.PersonalAccessToken) ||
                           !string.IsNullOrEmpty(appConfig.Auth.Git.SshKeyPath))) ||
                          !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GIT_TOKEN"));

        var hasGitHubConfig = (appConfig?.Auth?.GitHub != null &&
                             !string.IsNullOrEmpty(appConfig.Auth.GitHub.Token) &&
                             (!string.IsNullOrEmpty(appConfig.Auth.GitHub.DefaultOrg) ||
                              !string.IsNullOrEmpty(appConfig.Auth.GitHub.DefaultRepo))) ||
                             (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_TOKEN")) &&
                              !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_REPO")));

        // Debug output
        _output.WriteLine($"DEBUG: GITHUB_TOKEN env var: {Environment.GetEnvironmentVariable("GITHUB_TOKEN")?.Substring(0, 10)}...");
        _output.WriteLine($"DEBUG: GITHUB_REPO env var: {Environment.GetEnvironmentVariable("GITHUB_REPO")}");
        _output.WriteLine($"DEBUG: hasGitHubConfig: {hasGitHubConfig}");

        // For GitHub-only testing, we need GitHub + Git config, Jira is optional
        if (!hasGitHubConfig)
        {
            _output.WriteLine("=== LIVE MODE SMOKE TEST SKIPPED ===");
            _output.WriteLine("Missing required GitHub configuration in appsettings.json, environment variables, or user-secrets");
            _output.WriteLine("Required: Auth.GitHub.Token and (Auth.GitHub.DefaultOrg or Auth.GitHub.DefaultRepo)");
            return;
        }

        if (!hasGitConfig)
        {
            _output.WriteLine("=== LIVE MODE SMOKE TEST SKIPPED ===");
            _output.WriteLine("Missing required Git configuration in appsettings.json, environment variables, or user-secrets");
            _output.WriteLine("Required: Auth.Git.PersonalAccessToken or Auth.Git.SshKeyPath");
            return;
        }

        if (!hasJiraConfig)
        {
            _output.WriteLine("=== LIVE MODE SMOKE TEST (GITHUB-ONLY) STARTED ===");
            _output.WriteLine("Purpose: Test full pipeline with real GitHub and Git adapters (Jira not configured)");
            _output.WriteLine("WARNING: This test interacts with real external services!");
            _output.WriteLine($"GitHub: Configured with token and repo");
            _output.WriteLine($"Git: Configured with PAT");
        }
        else
        {
            _output.WriteLine("=== GAUNTLET E2E SMOKE TEST (LIVE MODE) STARTED ===");
            _output.WriteLine("Purpose: Test full pipeline with real Jira/Git adapters");
            _output.WriteLine("WARNING: This test interacts with real external services!");
            _output.WriteLine($"Jira: Configured");
            _output.WriteLine($"GitHub: Configured with token and repo");
            _output.WriteLine($"Git: Configured with PAT");
        }

        await RunSmokeTest(useLiveAdapters: true, appConfig: appConfig, hasJiraConfig: hasJiraConfig);
    }

    private async Task RunSmokeTest(bool useLiveAdapters, AppConfig? appConfig = null, bool hasJiraConfig = false)
    {
        // Setup DI container with all services
        var services = new ServiceCollection();
        
        // Register AppConfig if provided (needed for agents in live mode)
        if (appConfig != null)
        {
            services.AddSingleton(appConfig);
        }
        
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
            if (hasJiraConfig)
            {
                services.AddSingleton<IAdapter>(new JuniorDev.WorkItems.Jira.JiraAdapter(appConfig, Microsoft.Extensions.Logging.Abstractions.NullLogger<JuniorDev.WorkItems.Jira.JiraAdapter>.Instance));
                _output.WriteLine("Using real Jira adapter");
            }
            else
            {
                _output.WriteLine("Jira adapter not configured - skipping");
            }
            
            services.AddSingleton<IAdapter>(new JuniorDev.VcsGit.VcsGitAdapter(new JuniorDev.VcsGit.VcsConfig
            {
                RepoPath = "/tmp/live-test-repo",
                AllowPush = Environment.GetEnvironmentVariable("RUN_LIVE_PUSH") == "1",
                IsIntegrationTest = true
            }, isFake: false));
            services.AddSingleton<IAdapter>(new JuniorDev.WorkItems.GitHub.GitHubAdapter(appConfig));
            _output.WriteLine("Using real Git and GitHub adapters");
        }
        else if (useLiveAdapters && appConfig == null)
        {
            // Config failed to load, but we have environment variables - create minimal AppConfig from env vars
            _output.WriteLine("Config failed to load, creating minimal config from environment variables");
            
            // Create minimal AppConfig from environment variables
            var envAppConfig = new AppConfig
            {
                Auth = new AuthConfig
                {
                    GitHub = new GitHubAuthConfig(
                        Environment.GetEnvironmentVariable("JUNIORDEV__APPCONFIG__AUTH__GITHUB__TOKEN") ?? 
                        Environment.GetEnvironmentVariable("GITHUB_TOKEN") ?? "dummy-token",
                        null, // DefaultOrg
                        null  // DefaultRepo
                    ),
                    Jira = hasJiraConfig ? new JiraAuthConfig(
                        Environment.GetEnvironmentVariable("JIRA_URL") ?? "https://dummy.atlassian.net",
                        Environment.GetEnvironmentVariable("JIRA_USER") ?? "dummy-user",
                        Environment.GetEnvironmentVariable("JIRA_TOKEN") ?? "dummy-token"
                    ) : null
                }
            };

            // Register adapters with the constructed config
            if (hasJiraConfig)
            {
                services.AddSingleton<IAdapter>(new JuniorDev.WorkItems.Jira.JiraAdapter(envAppConfig, Microsoft.Extensions.Logging.Abstractions.NullLogger<JuniorDev.WorkItems.Jira.JiraAdapter>.Instance));
                _output.WriteLine("Using real Jira adapter (from env vars)");
            }
            else
            {
                _output.WriteLine("Jira adapter not configured - skipping");
            }
            
            services.AddSingleton<IAdapter>(new JuniorDev.VcsGit.VcsGitAdapter(new JuniorDev.VcsGit.VcsConfig
            {
                RepoPath = "/tmp/live-test-repo",
                AllowPush = Environment.GetEnvironmentVariable("RUN_LIVE_PUSH") == "1",
                IsIntegrationTest = true
            }, isFake: false));
            services.AddSingleton<IAdapter>(new JuniorDev.WorkItems.GitHub.GitHubAdapter(envAppConfig));
            _output.WriteLine("Using real Git and GitHub adapters (from env vars)");
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

        // Create a simple .NET project in the temp workspace for build testing
        var tempWorkspacePath = Path.Combine(Path.GetTempPath(), $"junior-dev-workspace-{sessionId}");
        Directory.CreateDirectory(tempWorkspacePath);
        _output.WriteLine($"Created temp workspace: {tempWorkspacePath}");

        // Create a simple console app project
        var projectPath = Path.Combine(tempWorkspacePath, "TestProject.csproj");
        var programPath = Path.Combine(tempWorkspacePath, "Class1.cs");
        
        await File.WriteAllTextAsync(projectPath, @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>");

        await File.WriteAllTextAsync(programPath, @"namespace TestProject
{
    public class Class1
    {
        public string GetMessage() => ""Hello from smoke test!"";
    }
}");

        _output.WriteLine("Created test .NET project for build verification");

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

        // Build project (now points to real project in temp workspace)
        var buildCmd = new BuildProject(Guid.NewGuid(), new Correlation(sessionId), new RepoRef("test-repo", tempWorkspacePath), "TestProject.csproj", "Release", "net8.0", null, TimeSpan.FromMinutes(5));
        await sessionManager.PublishCommand(buildCmd);
        _output.WriteLine("Build project published (using real .NET project)");

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
        await ((SessionManager)sessionManager).CompleteSession(sessionId);
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
        Assert.True(successfulCommands >= 6, $"Expected at least 6 successful commands (all should succeed with real project), got {successfulCommands}");
        Assert.True(artifactEvents > 0);

        _output.WriteLine($"=== GAUNTLET E2E SMOKE TEST ({(useLiveAdapters ? "LIVE" : "FAKE")}) PASSED ===");

        // Generate reports and collect artifacts
        await GenerateSmokeTestReport(sessionId, events, useLiveAdapters, sessionConfig);
    }

    private async Task GenerateSmokeTestReport(Guid sessionId, List<IEvent> events, bool useLiveAdapters, SessionConfig sessionConfig)
    {
        _output.WriteLine("=== GENERATING SMOKE TEST REPORT ===");
        
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
                var artifactPath = Path.Combine(outputDir, $"{artifact.Artifact.Name}");
                
                // Always save metadata
                await File.WriteAllTextAsync(artifactPath + ".metadata.json", JsonSerializer.Serialize(new
                {
                    artifact.Id,
                    artifact.Artifact.Kind,
                    artifact.Artifact.Name,
                    HasInlineText = !string.IsNullOrEmpty(artifact.Artifact.InlineText),
                    InlineTextLength = artifact.Artifact.InlineText?.Length ?? 0,
                    artifact.Artifact.PathHint,
                    artifact.Artifact.DownloadUri,
                    artifact.Artifact.ContentType
                }, new JsonSerializerOptions { WriteIndented = true }));
                
                if (!string.IsNullOrEmpty(artifact.Artifact.InlineText))
                {
                    // Save inline text content
                    await File.WriteAllTextAsync(artifactPath, artifact.Artifact.InlineText);
                    _output.WriteLine($"Saved inline text content to {artifactPath} ({artifact.Artifact.InlineText.Length} chars)");
                }
                else
                {
                    _output.WriteLine($"Artifact {artifact.Artifact.Name} has no inline text (PathHint: {artifact.Artifact.PathHint})");
                }
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