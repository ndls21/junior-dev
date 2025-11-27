using System.ComponentModel;
using JuniorDev.Agents;
using JuniorDev.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace JuniorDev.Agents.Sk;

/// <summary>
/// Executor agent that processes work items by analyzing requirements and executing development tasks.
/// </summary>
public class ExecutorAgent : AgentBase
{
    private readonly Kernel _kernel;
    private OrchestratorFunctionBindings? _functionBindings;

    public override string AgentType => "executor";

    public ExecutorAgent(Kernel kernel)
    {
        _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
    }

    protected override async Task OnStartedAsync()
    {
        // Initialize SK function bindings now that Context is available
        _functionBindings = new OrchestratorFunctionBindings(Context!);
        _functionBindings.RegisterFunctions(_kernel);
        
        Logger.LogInformation("Executor agent started for session {SessionId}", Context!.Config.SessionId);

        // Check if we have a work item to execute (auto-execution mode)
        if (Context!.Config.WorkItem != null)
        {
            await ExecuteWorkItemAsync(Context.Config.WorkItem);
        }
        else
        {
            Logger.LogInformation("No work item assigned to session - waiting for LLM invocation");
        }
    }

    protected override Task OnStoppedAsync()
    {
        Logger.LogInformation("Executor agent stopped for session {SessionId}", Context!.Config.SessionId);
        return Task.CompletedTask;
    }

    protected override async Task OnEventAsync(IEvent @event)
    {
        switch (@event)
        {
            case CommandCompleted completed:
                await HandleCommandCompleted(completed);
                break;
            case CommandRejected rejected:
                await HandleCommandRejected(rejected);
                break;
            case Throttled throttled:
                await HandleThrottled(throttled);
                break;
            case ConflictDetected conflict:
                await HandleConflictDetected(conflict);
                break;
            default:
                Logger.LogDebug("Ignoring event {EventType}", @event.Kind);
                break;
        }
    }

    private async Task ExecuteWorkItemAsync(WorkItemRef workItem)
    {
        Logger.LogInformation("Executing work item {WorkItemId}", workItem.Id);

        try
        {
            // Step 1: Claim the work item if not already claimed
            var claimResult = await ClaimWorkItemAsync(workItem);
            if (claimResult != ClaimResult.Success)
            {
                Logger.LogWarning("Failed to claim work item {WorkItemId}: {Result}", workItem.Id, claimResult);
                return;
            }

            // Step 2: Analyze the work item and create execution plan
            var executionPlan = await AnalyzeWorkItemAsync(workItem);
            if (executionPlan == null || !executionPlan.Any())
            {
                Logger.LogWarning("No executable tasks found for work item {WorkItemId}", workItem.Id);
                return;
            }

            // Step 3: Execute the plan
            await ExecutePlanAsync(executionPlan, workItem);

            // Step 4: Mark work item as completed
            await CompleteWorkItemAsync(workItem);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error executing work item {WorkItemId}", workItem.Id);
            await HandleExecutionErrorAsync(workItem, ex);
        }
    }

    private async Task<ClaimResult> ClaimWorkItemAsync(WorkItemRef workItem)
    {
        try
        {
            var claimUtil = new ClaimUtilities(Context!);
            return await claimUtil.TryClaimWorkItemAsync(workItem, Context!.AgentConfig.AgentProfile ?? "executor");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error claiming work item {WorkItemId}", workItem.Id);
            return ClaimResult.NetworkError;
        }
    }

    private Task<List<FunctionCall>> AnalyzeWorkItemAsync(WorkItemRef workItem)
    {
        Logger.LogInformation("Analyzing work item {WorkItemId} to create execution plan", workItem.Id);

        // Use SK functions to analyze the work item and determine what needs to be done
        // For now, we'll use a simple analysis, but this could be enhanced with LLM analysis
        var analysisPrompt = $"Analyze this work item: {workItem.Id}. Determine what development tasks need to be performed.";

        // TODO: Replace with SK/LLM-driven analysis that directly outputs function calls
        // The LLM would:
        // 1. Query work item details via get_item function
        // 2. Analyze requirements and constraints
        // 3. Generate a sequence of function calls to accomplish the task
        // 4. Consider policy constraints in its planning
        // For now, generate function calls programmatically

        var functionCalls = new List<FunctionCall>();

        // For now, keep the basic logic but express as SK function calls
        if (workItem.Id.Contains("FEATURE") || workItem.Id.Contains("TASK") || workItem.Id.Contains("BUG"))
        {
            // Create a feature branch with policy-aware naming using ClaimUtilities
            var claimUtil = new ClaimUtilities(Context!);
            var branchName = claimUtil.GenerateBranchName(workItem, Context!.Config.Policy.ProtectedBranches);

            functionCalls.Add(new FunctionCall
            {
                PluginName = "vcs",
                FunctionName = "create_branch",
                Description = $"Create feature branch for {workItem.Id}",
                Arguments = new KernelArguments
                {
                    ["repoName"] = Context.Config.Repo.Name,
                    ["branchName"] = branchName,
                    ["fromRef"] = "main"
                },
                IsRisky = false
            });

            // Add implementation step (placeholder - real implementation would apply patches)
            functionCalls.Add(new FunctionCall
            {
                PluginName = "vcs",
                FunctionName = "commit",
                Description = $"Implement changes for {workItem.Id}",
                Arguments = new KernelArguments
                {
                    ["repoName"] = Context.Config.Repo.Name,
                    ["message"] = $"Implement {workItem.Id}: Development work completed",
                    ["amend"] = false
                },
                IsRisky = false
            });

            // Add testing if required by policy
            if (Context!.Config.Policy.RequireTestsBeforePush)
            {
                functionCalls.Add(new FunctionCall
                {
                    PluginName = "vcs",
                    FunctionName = "run_tests",
                    Description = "Run tests before push",
                    Arguments = new KernelArguments
                    {
                        ["repoName"] = Context.Config.Repo.Name
                    },
                    IsRisky = false
                });
            }

            // Add approval request if required by policy
            if (Context.Config.Policy.RequireApprovalForPush)
            {
                functionCalls.Add(new FunctionCall
                {
                    PluginName = "general",
                    FunctionName = "request_approval",
                    Description = $"Request approval before pushing {workItem.Id}",
                    Arguments = new KernelArguments
                    {
                        ["reason"] = $"Push changes for {workItem.Id} to branch {branchName}",
                        ["requiredActions"] = new[] { "Push" }
                    },
                    IsRisky = false
                });
            }

            // Add push (marked as risky for dry-run handling)
            functionCalls.Add(new FunctionCall
            {
                PluginName = "vcs",
                FunctionName = "push",
                Description = $"Push changes for {workItem.Id}",
                Arguments = new KernelArguments
                {
                    ["repoName"] = Context.Config.Repo.Name,
                    ["branchName"] = branchName
                },
                IsRisky = true
            });
        }

        Logger.LogInformation("Created execution plan with {CallCount} function calls for work item {WorkItemId}",
            functionCalls.Count, workItem.Id);

        return Task.FromResult(functionCalls);
    }

    private async Task ExecutePlanAsync(List<FunctionCall> plan, WorkItemRef workItem)
    {
        Logger.LogInformation("Executing {CallCount} function calls for work item {WorkItemId}", plan.Count, workItem.Id);

        foreach (var call in plan)
        {
            // Dry-run mode skips risky operations (already handled by OrchestratorFunctionBindings)
            // but we log them here for visibility
            if (Context!.AgentConfig.DryRun && call.IsRisky)
            {
                Logger.LogInformation("[DRY RUN] Would execute: {Description}", call.Description);
                // Still invoke - the function binding will handle dry-run mode
            }

            try
            {
                await ExecuteFunctionCallAsync(call);
                Logger.LogInformation("Completed function call: {Description}", call.Description);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to execute function call: {Description}", call.Description);
                throw;
            }
        }
    }

    private async Task ExecuteFunctionCallAsync(FunctionCall call)
    {
        // Invoke the SK function via the kernel
        // This delegates all policy enforcement to the orchestrator through OrchestratorFunctionBindings
        var result = await _kernel.InvokeAsync(
            call.PluginName,
            call.FunctionName,
            call.Arguments);

        Logger.LogDebug("Function {Plugin}.{Function} returned: {Result}",
            call.PluginName, call.FunctionName, result.ToString());
    }

    private async Task CompleteWorkItemAsync(WorkItemRef workItem)
    {
        Logger.LogInformation("Marking work item {WorkItemId} as completed", workItem.Id);

        // Use kernel invocations for consistency
        await _kernel.InvokeAsync("workitems", "transition", new KernelArguments
        {
            ["itemId"] = workItem.Id,
            ["newState"] = "Done"
        });

        await _kernel.InvokeAsync("workitems", "comment", new KernelArguments
        {
            ["itemId"] = workItem.Id,
            ["comment"] = "Implementation completed successfully"
        });
    }

    private async Task HandleExecutionErrorAsync(WorkItemRef workItem, Exception ex)
    {
        await _kernel.InvokeAsync("workitems", "comment", new KernelArguments
        {
            ["itemId"] = workItem.Id,
            ["comment"] = $"Execution failed: {ex.Message}"
        });

        await _kernel.InvokeAsync("workitems", "transition", new KernelArguments
        {
            ["itemId"] = workItem.Id,
            ["newState"] = "Blocked"
        });
    }

    private Task HandleCommandCompleted(CommandCompleted completed)
    {
        Logger.LogInformation("Command {CommandId} completed successfully", completed.CommandId);
        // Could implement follow-up actions here
        return Task.CompletedTask;
    }

    private async Task HandleCommandRejected(CommandRejected rejected)
    {
        Logger.LogWarning("Command {CommandId} was rejected: {Reason}", rejected.CommandId, rejected.Reason);

        // Surface the rejection to the work item
        if (Context!.Config.WorkItem != null)
        {
            await _kernel.InvokeAsync("workitems", "comment", new KernelArguments
            {
                ["itemId"] = Context.Config.WorkItem.Id,
                ["comment"] = $"Command rejected: {rejected.Reason}"
            });
        }
    }

    private async Task HandleThrottled(Throttled throttled)
    {
        Logger.LogWarning("Operation was throttled for scope '{Scope}': retry after {RetryAfter}", throttled.Scope, throttled.RetryAfter);

        // Implement backoff logic - wait until retry time
        var delay = throttled.RetryAfter - DateTimeOffset.Now;
        if (delay > TimeSpan.Zero)
        {
            Logger.LogInformation("Waiting {Delay} before retrying throttled operation", delay);
            await Task.Delay(delay);
        }

        // Surface the throttling to the work item
        if (Context!.Config.WorkItem != null)
        {
            await _kernel.InvokeAsync("workitems", "comment", new KernelArguments
            {
                ["itemId"] = Context.Config.WorkItem.Id,
                ["comment"] = $"Operation throttled for scope '{throttled.Scope}'. Retried after backoff period."
            });
        }

        // TODO: Implement retry logic for the specific operation that was throttled
        // This would require tracking pending operations and retrying them
    }

    private async Task HandleConflictDetected(ConflictDetected conflict)
    {
        Logger.LogWarning("Conflict detected in repo {Repo}: {Details}", conflict.Repo.Name, conflict.Details);

        if (Context!.Config.WorkItem != null)
        {
            await _kernel.InvokeAsync("workitems", "comment", new KernelArguments
            {
                ["itemId"] = Context.Config.WorkItem.Id,
                ["comment"] = $"Conflict detected: {conflict.Details}. Manual resolution required."
            });

            // If we have patch content, we could try to apply it, but for now just block
            if (!string.IsNullOrEmpty(conflict.PatchContent))
            {
                Logger.LogInformation("Conflict includes patch content - could attempt automatic resolution");
                // TODO: Implement automatic conflict resolution using the patch
            }

            await _kernel.InvokeAsync("workitems", "transition", new KernelArguments
            {
                ["itemId"] = Context.Config.WorkItem.Id,
                ["newState"] = "Blocked"
            });
        }
    }

    /// <summary>
    /// Represents a planned function call to be executed via Semantic Kernel.
    /// </summary>
    private class FunctionCall
    {
        public string PluginName { get; set; } = "";
        public string FunctionName { get; set; } = "";
        public string Description { get; set; } = "";
        public KernelArguments Arguments { get; set; } = new();
        public bool IsRisky { get; set; }
    }
}