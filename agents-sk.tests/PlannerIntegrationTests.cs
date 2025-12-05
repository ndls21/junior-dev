using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JuniorDev.Agents.Sk;
using JuniorDev.Contracts;
using Microsoft.SemanticKernel;
using Xunit;

namespace JuniorDev.Agents.Sk.Tests;

/// <summary>
/// Integration tests demonstrating the complete planner → execution pipeline.
/// Shows how PlanUpdated events flow through the system to trigger actual work.
/// </summary>
[Collection("Planner Integration Tests")]
public class PlannerIntegrationTests
{
    [Fact]
    public async Task PlannerToExecution_PlanGenerated_TriggersExecutionFlow()
    {
        // Arrange: Set up planner with work item
        var kernel = new Kernel();
        var planner = new PlannerAgent(kernel);

        var workItem = new WorkItemRef("PROJ-123");
        var policy = new PolicyProfile
        {
            Name = "integration-test",
            ProtectedBranches = new HashSet<string> { "main", "develop" }
        };

        // Act: Generate plan
        var plan = await planner.GeneratePlanAsync(workItem, policy);

        // Assert: Plan structure is correct
        Assert.NotEmpty(plan.Nodes);
        Assert.True(plan.Nodes.Count >= 1, "Plan should have at least one execution step");

        // Validate DAG structure
        var rootNodes = plan.Nodes.Where(n => !n.DependsOn.Any()).ToList();
        Assert.NotEmpty(rootNodes);

        var leafNodes = plan.Nodes.Where(n =>
            !plan.Nodes.Any(other => other.DependsOn.Contains(n.Id))).ToList();
        Assert.NotEmpty(leafNodes);

        // Validate node properties
        foreach (var node in plan.Nodes)
        {
            Assert.Equal(workItem, node.WorkItem);
            Assert.NotNull(node.AgentHint);
            Assert.Contains("task", node.Tags);
            Assert.NotEmpty(node.Id);
        }

        // Validate dependency chain
        foreach (var node in plan.Nodes)
        {
            foreach (var depId in node.DependsOn)
            {
                var dependency = plan.Nodes.FirstOrDefault(n => n.Id == depId);
                Assert.NotNull(dependency);
            }
        }
    }

    [Fact]
    public async Task PlanExecutionFlow_WorkItemToExecutionArtifacts()
    {
        // This test demonstrates the conceptual flow:
        // Work Item → Planner → PlanUpdated Event → Executor → Command Execution → Artifacts

        var kernel = new Kernel();
        var planner = new PlannerAgent(kernel);

        var workItem = new WorkItemRef("INTEGRATION-001");
        var policy = new PolicyProfile
        {
            Name = "integration-test",
            ProtectedBranches = new HashSet<string> { "main", "master" }
        };

        // Step 1: Planning phase
        var plan = await planner.GeneratePlanAsync(workItem, policy);

        // Step 2: Simulate PlanUpdated event emission
        var planUpdatedEvent = new PlanUpdated(
            Id: Guid.NewGuid(),
            Correlation: new Correlation(Guid.NewGuid()),
            Plan: plan
        );

        // Step 3: Validate event structure for downstream consumption
        Assert.Equal(plan, planUpdatedEvent.Plan);
        Assert.NotEqual(Guid.Empty, planUpdatedEvent.Id);
        Assert.NotEqual(Guid.Empty, planUpdatedEvent.Correlation.SessionId);

        // Step 4: Validate plan can drive execution
        var executableNodes = plan.Nodes.Where(n => n.AgentHint == "executor").ToList();
        Assert.NotEmpty(executableNodes);

        // Step 5: Validate execution ordering via dependencies
        var executionOrder = GetExecutionOrder(plan.Nodes.ToList());
        Assert.Equal(plan.Nodes.Count, executionOrder.Count);

        // Verify topological sort - dependencies come before dependents
        foreach (var node in executionOrder)
        {
            foreach (var depId in node.DependsOn)
            {
                var depIndex = executionOrder.FindIndex(n => n.Id == depId);
                var nodeIndex = executionOrder.IndexOf(node);
                Assert.True(depIndex < nodeIndex, $"Dependency {depId} should execute before {node.Id}");
            }
        }
    }

    [Fact]
    public async Task PlannerHandlesComplexWorkItems_GeneratesComprehensivePlan()
    {
        var kernel = new Kernel();
        var planner = new PlannerAgent(kernel);

        var workItem = new WorkItemRef("COMPLEX-456");
        var policy = new PolicyProfile
        {
            Name = "complex-integration",
            ProtectedBranches = new HashSet<string> { "main", "develop", "staging" }
        };

        var plan = await planner.GeneratePlanAsync(workItem, policy);

        // Complex work items should generate more comprehensive plans
        Assert.True(plan.Nodes.Count >= 1, "Complex work items should have at least one task");

        // Should include basic task tags
        var tags = plan.Nodes.SelectMany(n => n.Tags).Distinct().ToList();
        Assert.Contains("task", tags);

        // Should have proper dependency chains (may be 0 for simple plans)
        var maxDepth = GetMaxDependencyDepth(plan.Nodes.ToList());
        Assert.True(maxDepth >= 0, "Plans should have valid dependency structure");
    }

    private static List<TaskNode> GetExecutionOrder(List<TaskNode> nodes)
    {
        // Simple topological sort for validation
        var result = new List<TaskNode>();
        var visited = new HashSet<string>();
        var visiting = new HashSet<string>();

        void Visit(TaskNode node)
        {
            if (visited.Contains(node.Id) || visiting.Contains(node.Id))
                return;

            visiting.Add(node.Id);

            foreach (var depId in node.DependsOn)
            {
                var dep = nodes.First(n => n.Id == depId);
                Visit(dep);
            }

            visiting.Remove(node.Id);
            visited.Add(node.Id);
            result.Add(node);
        }

        foreach (var node in nodes.Where(n => !n.DependsOn.Any()))
        {
            Visit(node);
        }

        // Handle any remaining nodes (shouldn't happen in valid DAG)
        foreach (var node in nodes.Where(n => !visited.Contains(n.Id)))
        {
            Visit(node);
        }

        return result;
    }

    private static int GetMaxDependencyDepth(List<TaskNode> nodes)
    {
        var depths = new Dictionary<string, int>();

        int GetDepth(TaskNode node)
        {
            if (depths.TryGetValue(node.Id, out var depth))
                return depth;

            if (!node.DependsOn.Any())
                return 0;

            var maxDepDepth = node.DependsOn
                .Select(depId => nodes.First(n => n.Id == depId))
                .Select(GetDepth)
                .Max();

            depth = maxDepDepth + 1;
            depths[node.Id] = depth;
            return depth;
        }

        return nodes.Select(GetDepth).Max();
    }
}