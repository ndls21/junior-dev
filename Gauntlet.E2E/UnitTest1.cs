using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JuniorDev.Agents;
using JuniorDev.Agents.Sk;
using JuniorDev.Contracts;
using JuniorDev.Orchestrator;
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
    public async Task FullPipelineSmokeTest_QueriesBacklog_ProcessesWorkItem_ExecutesVcsOperations()
    {
        _output.WriteLine("=== GAUNTLET E2E SMOKE TEST STARTED ===");
        _output.WriteLine("Purpose: Test full pipeline from backlog query through VCS operations using fakes only");

        // Setup DI container with all services
        var services = new ServiceCollection();
        services.AddOrchestrator();
        services.AddAgentSdk();
        services.AddAgent<PlannerAgent>();
        services.AddAgent<ExecutorAgent>();
        services.AddAgent<ReviewerAgent>();

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
                Name = "smoke-test",
                ProtectedBranches = new HashSet<string> { "main", "master" },
                RequireTestsBeforePush = false,
                RequireApprovalForPush = false
            },
            new RepoRef("test-repo", "/tmp/test-repo"),
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

        // Push
        var pushCmd = new Push(Guid.NewGuid(), new Correlation(sessionId), new RepoRef("test-repo", "/tmp/test-repo"), "feature/proj-123");
        await sessionManager.PublishCommand(pushCmd);
        _output.WriteLine("Push published");

        // Complete the session
        ((SessionManager)sessionManager).CompleteSession(sessionId);
        _output.WriteLine("Session completed");

        // Wait for all events
        await Task.Delay(2000);

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

        // Assertions - allow for concurrent execution
        Assert.True(acceptedCommands >= 6, $"Expected at least 6 accepted commands, got {acceptedCommands}");
        Assert.Equal(6, completedCommands);
        Assert.Equal(6, successfulCommands);
        Assert.True(artifactEvents > 0);

        _output.WriteLine("=== GAUNTLET E2E SMOKE TEST PASSED ===");
    }
}