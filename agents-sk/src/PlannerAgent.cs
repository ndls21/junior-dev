using System.ComponentModel;
using JuniorDev.Agents;
using JuniorDev.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace JuniorDev.Agents.Sk;

/// <summary>
/// Planner agent that parses work items into task plans and suggests branch names.
/// </summary>
public class PlannerAgent : AgentBase
{
    private readonly Kernel _kernel;
    private OrchestratorFunctionBindings? _functionBindings;

    public override string AgentType => "planner";

    public PlannerAgent(Kernel kernel)
    {
        _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
    }

    protected override async Task OnStartedAsync()
    {
        // Initialize SK function bindings now that Context is available
        _functionBindings = new OrchestratorFunctionBindings(Context!);
        _functionBindings.RegisterFunctions(_kernel);

        // Register this agent's functions as SK functions
        RegisterAgentFunctions();

        Logger.LogInformation("Planner agent started for session {SessionId}", Context!.Config.SessionId);

        // Generate initial plan if work item is available
        if (Context.Config.WorkItem != null)
        {
            await GenerateAndEmitPlanAsync();
        }
        else
        {
            Logger.LogInformation("No work item associated with session - planner will wait for work item linkage");
        }
    }

    protected override Task OnStoppedAsync()
    {
        Logger.LogInformation("Planner agent stopped for session {SessionId}", Context!.Config.SessionId);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Registers this agent's capabilities as Semantic Kernel functions.
    /// </summary>
    private void RegisterAgentFunctions()
    {
        try
        {
            _kernel.Plugins.AddFromObject(this, "planner_agent");
            Logger.LogInformation("Registered planner agent functions with kernel");
        }
        catch (ArgumentException ex) when (ex.Message.Contains("An item with the same key has already been added"))
        {
            Logger.LogDebug("Planner agent functions already registered");
        }

        // SK/LLM scaffolding: Register planning functions
        try
        {
            _kernel.Plugins.AddFromObject(new PlanningPlugin(), "planning");
            Logger.LogInformation("Registered planning plugin with kernel");
        }
        catch (ArgumentException ex) when (ex.Message.Contains("An item with the same key has already been added"))
        {
            Logger.LogDebug("Planning plugin already registered");
        }
    }

    protected override async Task OnEventAsync(IEvent @event)
    {
        // For now, planner only generates plan on start
        // TODO: Regenerate plan on work item updates when query is available
        Logger.LogDebug("Ignoring event {EventType}", @event.Kind);
    }

    [KernelFunction("generate_plan")]
    [Description("Generates a task plan for the current work item.")]
    public async Task<string> GeneratePlanKernelFunctionAsync()
    {
        await GenerateAndEmitPlanAsync();
        return "Plan generated and emitted";
    }

    private async Task GenerateAndEmitPlanAsync()
    {
        if (Context?.Config.WorkItem == null)
        {
            Logger.LogWarning("Cannot generate plan - no work item associated with session");
            return;
        }

        try
        {
            var plan = await GeneratePlanAsync(Context.Config.WorkItem, Context.Config.Policy);
            var planUpdated = new PlanUpdated(
                Id: Guid.NewGuid(),
                Correlation: Context.CreateCorrelation(),
                Plan: plan);

            // Emit the plan updated event
            await PublishEventAsync(planUpdated);

            Logger.LogInformation("Emitted PlanUpdated with {NodeCount} nodes for work item {WorkItemId}",
                plan.Nodes.Count, Context.Config.WorkItem.Id);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to generate and emit plan for work item {WorkItemId}",
                Context.Config.WorkItem.Id);
        }
    }

    internal async Task<TaskPlan> GeneratePlanAsync(WorkItemRef workItem, PolicyProfile policy)
    {
        // For now, generate a single-node plan deterministically
        // TODO: Use SK/LLM for plan generation when work item query is available (issue #9)

        var title = $"Implement {workItem.Id}"; // Placeholder title based on ID
        var suggestedBranch = GenerateBranchSuggestion(workItem.Id, policy.ProtectedBranches);
        var node = new TaskNode(
            Id: Guid.NewGuid().ToString(),
            Title: title,
            DependsOn: Array.Empty<string>(),
            WorkItem: workItem,
            AgentHint: "executor",
            SuggestedBranch: suggestedBranch,
            Tags: new[] { "task" });

        return new TaskPlan(new[] { node });
    }

    internal string GenerateBranchSuggestion(string workItemId, IReadOnlyList<string> protectedBranches)
    {
        // Generate a branch name based on work item ID, ensuring it's not protected
        var baseName = $"feature/{workItemId.ToLowerInvariant().Replace(" ", "-").Replace("_", "-")}";
        var candidate = baseName;
        var counter = 1;

        while (protectedBranches.Contains(candidate))
        {
            candidate = $"{baseName}-{counter}";
            counter++;
        }

        return candidate;
    }

    /// <summary>
    /// SK plugin for planning operations.
    /// </summary>
    private class PlanningPlugin
    {
        [KernelFunction("parse_work_item")]
        [Description("Parses a work item into planning components.")]
        public async Task<string> ParseWorkItemAsync(
            [Description("The work item content to parse")] string content)
        {
            // SK/LLM scaffolding: This would parse work item content
            // For now, return a placeholder response
            // TODO: Implement actual LLM parsing
            return await Task.FromResult($"Parsed work item: {content}");
        }

        [KernelFunction("suggest_branch")]
        [Description("Suggests a branch name for a work item, respecting protected branches.")]
        public async Task<string> SuggestBranchAsync(
            [Description("The work item identifier")] string workItemId,
            [Description("List of protected branches")] string[] protectedBranches)
        {
            // SK/LLM scaffolding: This would use LLM for branch suggestion
            // For now, return a simple suggestion
            // TODO: Implement actual LLM suggestion
            var suggestion = $"feature/{workItemId}";
            return await Task.FromResult(suggestion);
        }
    }
}