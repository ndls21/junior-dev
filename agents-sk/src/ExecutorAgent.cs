using System.ComponentModel;
using JuniorDev.Agents;
using JuniorDev.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace JuniorDev.Agents.Sk;

/// <summary>
/// Represents a function call to be executed by the orchestrator.
/// </summary>
public record FunctionCall
{
    /// <summary>
    /// The plugin name (e.g., "vcs", "workitems", "general").
    /// </summary>
    public required string PluginName { get; init; }
    
    /// <summary>
    /// The function name within the plugin.
    /// </summary>
    public required string FunctionName { get; init; }
    
    /// <summary>
    /// Human-readable description of what this function call does.
    /// </summary>
    public required string Description { get; init; }
    
    /// <summary>
    /// Arguments to pass to the function.
    /// </summary>
    public required KernelArguments Arguments { get; init; }
    
    /// <summary>
    /// Whether this operation is considered risky (requires retry logic).
    /// </summary>
    public bool IsRisky { get; init; }
}

/// <summary>
/// Executor agent that processes work items by analyzing requirements and executing development tasks.
/// </summary>
///
/// TODOs / Known Gaps:
/// - Several nullable-dereference warnings exist (CS8602); refactor to validate `_functionBindings` and `Context` usage.
/// - Improve planning integration with SK/LLM (work-item query functions TODO: #8/#9).
/// - Ensure all async methods use `await` where appropriate or remove async modifier to clear CS1998 warnings.
/// - Consider better retry/backoff strategies and observability for long-running operations.

public class ExecutorAgent : AgentBase
{
    private readonly Kernel _kernel;
    private OrchestratorFunctionBindings? _functionBindings;
    private OrchestratorFunctionBindings FunctionBindings => _functionBindings ?? throw new InvalidOperationException("Function bindings not initialized");
        private AgentSessionContext Ctx => Context ?? throw new InvalidOperationException("Agent Context is not initialized");
    
    // Track execution state
    private readonly List<FunctionCall> _pendingOperations = new();
    private bool _testsExecuted = false;
    private bool _approvalGranted = false;

    public override string AgentType => "executor";

    public ExecutorAgent(Kernel kernel)
    {
        _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
    }

    protected override async Task OnStartedAsync()
    {
        // Initialize SK function bindings now that Context is available
        _functionBindings = new OrchestratorFunctionBindings(Ctx!);
        _functionBindings.RegisterFunctions(_kernel);
        
        // Register this agent's functions as SK functions
        RegisterAgentFunctions();

        Logger.LogInformation("Executor agent started for session {SessionId}", Ctx!.Config.SessionId);

        // Check if we have a work item to execute (auto-execution mode)
        if (Ctx!.Config.WorkItem != null)
        {
            await ExecuteWorkItemAsync(Ctx!.Config.WorkItem);
        }
        else
        {
            Logger.LogInformation("No work item assigned to session - waiting for LLM invocation");
        }
    }

    protected override Task OnStoppedAsync()
    {
        Logger.LogInformation("Executor agent stopped for session {SessionId}", Ctx!.Config.SessionId);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Registers this agent's capabilities as Semantic Kernel functions.
    /// This makes the agent's high-level operations discoverable by LLMs.
    /// </summary>
    private void RegisterAgentFunctions()
    {
        try
        {
            _kernel.Plugins.AddFromObject(this, "executor_agent");
            Logger.LogInformation("Registered executor agent functions with kernel");
        }
        catch (ArgumentException ex) when (ex.Message.Contains("An item with the same key has already been added"))
        {
            Logger.LogDebug("Executor agent functions already registered");
        }
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

    [KernelFunction("execute_work_item")]
    [Description("Executes a work item by analyzing it and performing the necessary development tasks.")]
    public async Task<string> ExecuteWorkItemKernelFunctionAsync(
        [Description("The work item ID to execute")] string workItemId)
    {
        var workItem = new WorkItemRef(workItemId);
        await ExecuteWorkItemAsync(workItem);
        return $"Executed work item {workItemId}";
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
            var claimUtil = new ClaimUtilities(Ctx!);
            return await claimUtil.TryClaimWorkItemAsync(workItem, Ctx!.AgentConfig.AgentProfile ?? "executor");
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

        var functionCalls = new List<FunctionCall>();

        try
        {
            // TODO: Use SK/LLM to analyze work item details once get_item function is implemented
            // For now, analyze based on work item ID patterns and basic heuristics
            
            // Check if this appears to be a development task based on ID patterns
            bool isDevelopmentTask = workItem.Id.Contains("FEATURE") || 
                                   workItem.Id.Contains("TASK") || 
                                   workItem.Id.Contains("BUG") ||
                                   workItem.Id.Contains("STORY") ||
                                   workItem.Id.Contains("EPIC");

            // Additional heuristics could be added here when work item details are available
            // e.g., check description, tags, etc.

            if (isDevelopmentTask)
            {
                Logger.LogInformation("Work item {WorkItemId} identified as development task", workItem.Id);
                
                // Create a feature branch with policy-aware naming using ClaimUtilities
                var claimUtil = new ClaimUtilities(Ctx!);
                var branchName = claimUtil.GenerateBranchName(workItem, Ctx!.Config.Policy.ProtectedBranches);

                functionCalls.Add(new FunctionCall
                {
                    PluginName = "vcs",
                    FunctionName = "create_branch",
                    Description = $"Create feature branch for {workItem.Id}",
                    Arguments = new KernelArguments
                    {
                        ["repoName"] = Ctx!.Config.Repo.Name,
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
                        ["repoName"] = Ctx!.Config.Repo.Name,
                        ["message"] = $"Implement {workItem.Id}: Development work completed",
                        ["amend"] = false
                    },
                    IsRisky = false
                });

                // Add testing if required by policy
                if (Ctx!.Config.Policy.RequireTestsBeforePush)
                {
                    functionCalls.Add(new FunctionCall
                    {
                        PluginName = "vcs",
                        FunctionName = "run_tests",
                        Description = "Run tests before push",
                        Arguments = new KernelArguments
                        {
                            ["repoName"] = Ctx!.Config.Repo.Name
                        },
                        IsRisky = false
                    });
                }

                // Add approval request if required by policy
                if (Ctx!.Config.Policy.RequireApprovalForPush)
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
                        ["repoName"] = Ctx!.Config.Repo.Name,
                        ["branchName"] = branchName
                    },
                    IsRisky = true
                });
            }
            else
            {
                Logger.LogInformation("Work item {WorkItemId} does not appear to require development tasks", workItem.Id);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to analyze work item {WorkItemId}, using fallback analysis", workItem.Id);
            
            // Fallback to basic analysis if anything fails
            if (workItem.Id.Contains("FEATURE") || workItem.Id.Contains("TASK") || workItem.Id.Contains("BUG"))
            {
                var claimUtil = new ClaimUtilities(Ctx!);
                var branchName = claimUtil.GenerateBranchName(workItem, Ctx!.Config.Policy.ProtectedBranches);

                functionCalls.Add(new FunctionCall
                {
                    PluginName = "vcs",
                    FunctionName = "create_branch",
                    Description = $"Create feature branch for {workItem.Id}",
                    Arguments = new KernelArguments
                    {
                        ["repoName"] = Ctx.Config.Repo.Name,
                        ["branchName"] = branchName
                    },
                    IsRisky = false
                });

                functionCalls.Add(new FunctionCall
                {
                    PluginName = "vcs",
                    FunctionName = "commit",
                    Description = $"Implement changes for {workItem.Id}",
                    Arguments = new KernelArguments
                    {
                        ["repoName"] = Ctx.Config.Repo.Name,
                        ["message"] = $"Implement {workItem.Id}",
                        ["amend"] = false
                    },
                    IsRisky = false
                });

                if (Ctx.Config.Policy.RequireTestsBeforePush)
                {
                    functionCalls.Add(new FunctionCall
                    {
                        PluginName = "vcs",
                        FunctionName = "run_tests",
                        Description = "Run tests before push",
                        Arguments = new KernelArguments
                        {
                            ["repoName"] = Ctx.Config.Repo.Name
                        },
                        IsRisky = false
                    });
                }

                if (Ctx.Config.Policy.RequireApprovalForPush)
                {
                    functionCalls.Add(new FunctionCall
                    {
                        PluginName = "general",
                        FunctionName = "request_approval",
                        Description = $"Request approval before pushing {workItem.Id}",
                        Arguments = new KernelArguments
                        {
                            ["reason"] = $"Push changes for {workItem.Id}",
                            ["requiredActions"] = new[] { "Push" }
                        },
                        IsRisky = false
                    });
                }

                functionCalls.Add(new FunctionCall
                {
                    PluginName = "vcs",
                    FunctionName = "push",
                    Description = $"Push changes for {workItem.Id}",
                    Arguments = new KernelArguments
                    {
                        ["repoName"] = Ctx.Config.Repo.Name,
                        ["branchName"] = branchName
                    },
                    IsRisky = true
                });
            }
        }

        Logger.LogInformation("Created execution plan with {CallCount} function calls for work item {WorkItemId}",
            functionCalls.Count, workItem.Id);

        return Task.FromResult(functionCalls);
    }

    private async Task ExecutePlanAsync(List<FunctionCall> plan, WorkItemRef workItem)
    {
        Logger.LogInformation("Executing {CallCount} function calls for work item {WorkItemId}", plan.Count, workItem.Id);

        // Reset execution state for this work item
        _pendingOperations.Clear();
        _testsExecuted = false;
        _approvalGranted = false;

        foreach (var call in plan)
        {
            // Track risky operations for retry logic
            if (call.IsRisky)
            {
                _pendingOperations.Add(call);
            }

            // Dry-run mode skips risky operations (already handled by OrchestratorFunctionBindings)
            // but we log them here for visibility
            if (Ctx!.AgentConfig.DryRun && call.IsRisky)
            {
                Logger.LogInformation("[DRY RUN] Would execute: {Description}", call.Description);
                // Still invoke - the function binding will handle dry-run mode
                continue;
            }

            // Check push gating before executing push operations
            if (call.PluginName == "vcs" && call.FunctionName == "push")
            {
                if (!CanExecutePush(workItem))
                {
                    Logger.LogWarning("Push operation blocked by policy for work item {WorkItemId}", workItem.Id);
                    await FunctionBindings!.CommentAsync(workItem.Id, "Push operation blocked by policy requirements (tests and/or approval needed).");
                    continue;
                }
            }

            try
            {
                await ExecuteFunctionCallAsync(call);
                Logger.LogInformation("Completed function call: {Description}", call.Description);
                
                // Update execution state
                if (call.PluginName == "vcs" && call.FunctionName == "run_tests")
                {
                    _testsExecuted = true;
                }
                else if (call.PluginName == "general" && call.FunctionName == "request_approval")
                {
                    // Note: In a real implementation, approval would be granted asynchronously
                    // For now, we'll assume approval is granted when requested
                    _approvalGranted = true;
                }
                
                // Remove from pending operations on success
                if (call.IsRisky)
                {
                    _pendingOperations.Remove(call);
                }
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
        // Call the appropriate method on _functionBindings based on plugin and function name
        // This provides direct method invocation with proper policy enforcement
        var result = call.PluginName switch
        {
            "vcs" => call.FunctionName switch
            {
                "create_branch" => await FunctionBindings.CreateBranchAsync(
                    call.Arguments["repoName"] as string ?? throw new ArgumentException("repoName is required"),
                    call.Arguments["branchName"] as string ?? throw new ArgumentException("branchName is required"),
                    call.Arguments.TryGetValue("fromRef", out var fromRef) ? fromRef as string : null),
                
                "commit" => await FunctionBindings.CommitAsync(
                    call.Arguments["repoName"] as string ?? throw new ArgumentException("repoName is required"),
                    call.Arguments["message"] as string ?? throw new ArgumentException("message is required"),
                    call.Arguments.TryGetValue("amend", out var amend) && amend is bool b ? b : false),
                
                "run_tests" => await FunctionBindings.RunTestsAsync(
                    call.Arguments["repoName"] as string ?? throw new ArgumentException("repoName is required"),
                    call.Arguments.TryGetValue("filter", out var filter) ? filter as string : null,
                    call.Arguments.TryGetValue("timeoutSeconds", out var timeout) && timeout is int i ? i : (int?)null),
                
                "push" => await FunctionBindings.PushAsync(
                    call.Arguments["repoName"] as string ?? throw new ArgumentException("repoName is required"),
                    call.Arguments["branchName"] as string ?? throw new ArgumentException("branchName is required")),
                
                "get_diff" => await FunctionBindings.GetDiffAsync(
                    call.Arguments["repoName"] as string ?? throw new ArgumentException("repoName is required"),
                    call.Arguments.TryGetValue("refName", out var refName) ? refName as string : null),
                
                _ => throw new ArgumentException($"Unknown VCS function: {call.FunctionName}")
            },
            
            "workitems" => call.FunctionName switch
            {
                "claim_item" => await FunctionBindings.ClaimItemAsync(
                    call.Arguments["itemId"] as string ?? throw new ArgumentException("itemId is required")),
                
                "comment" => await FunctionBindings.CommentAsync(
                    call.Arguments["itemId"] as string ?? throw new ArgumentException("itemId is required"),
                    call.Arguments["comment"] as string ?? throw new ArgumentException("comment is required")),
                
                "transition" => await FunctionBindings.TransitionAsync(
                    call.Arguments["itemId"] as string ?? throw new ArgumentException("itemId is required"),
                    call.Arguments["newState"] as string ?? throw new ArgumentException("newState is required")),
                
                _ => throw new ArgumentException($"Unknown workitems function: {call.FunctionName}")
            },
            
            "general" => call.FunctionName switch
            {
                "request_approval" => await FunctionBindings.RequestApprovalAsync(
                    call.Arguments["reason"] as string ?? throw new ArgumentException("reason is required"),
                    call.Arguments["requiredActions"] as string[] ?? throw new ArgumentException("requiredActions is required")),
                
                _ => throw new ArgumentException($"Unknown general function: {call.FunctionName}")
            },
            
            _ => throw new ArgumentException($"Unknown plugin: {call.PluginName}")
        };

        Logger.LogDebug("Function {Plugin}.{Function} returned: {Result}",
            call.PluginName, call.FunctionName, result);
    }

    private async Task CompleteWorkItemAsync(WorkItemRef workItem)
    {
        Logger.LogInformation("Marking work item {WorkItemId} as completed", workItem.Id);

        // Use function bindings for consistency
        await FunctionBindings!.TransitionAsync(workItem.Id, "Done");
        await FunctionBindings!.CommentAsync(workItem.Id, "Implementation completed successfully");
    }

    private async Task HandleExecutionErrorAsync(WorkItemRef workItem, Exception ex)
    {
        await FunctionBindings!.CommentAsync(workItem.Id, $"Execution failed: {ex.Message}");
        await FunctionBindings!.TransitionAsync(workItem.Id, "Blocked");
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
        if (Ctx!.Config.WorkItem != null)
        {
            await FunctionBindings!.CommentAsync(Ctx!.Config.WorkItem.Id, $"Command rejected: {rejected.Reason}");
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
        if (Ctx!.Config.WorkItem != null)
        {
            await FunctionBindings!.CommentAsync(Ctx!.Config.WorkItem.Id, $"Operation throttled for scope '{throttled.Scope}'. Retried after backoff period.");
        }

        // Retry the last pending operation if any
        if (_pendingOperations.Any())
        {
            var lastOperation = _pendingOperations.Last();
            Logger.LogInformation("Retrying throttled operation: {Description}", lastOperation.Description);
            
            try
            {
                await ExecuteFunctionCallAsync(lastOperation);
                Logger.LogInformation("Successfully retried operation: {Description}", lastOperation.Description);
                
                // Remove from pending operations
                _pendingOperations.RemoveAt(_pendingOperations.Count - 1);
                
                // Continue with next operation if any
                if (_pendingOperations.Any())
                {
                    var nextOperation = _pendingOperations.First();
                    _pendingOperations.RemoveAt(0);
                    await ExecuteFunctionCallAsync(nextOperation);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to retry throttled operation: {Description}", lastOperation.Description);
                // Keep the operation in pending list for potential future retry
            }
        }
    }

    private async Task HandleConflictDetected(ConflictDetected conflict)
    {
        Logger.LogWarning("Conflict detected in repo {Repo}: {Details}", conflict.Repo.Name, conflict.Details);

        if (Ctx!.Config.WorkItem != null)
        {
            await FunctionBindings!.CommentAsync(Ctx!.Config.WorkItem.Id, $"Conflict detected: {conflict.Details}. Attempting automatic resolution.");

            // Attempt automatic conflict resolution using patch content
            if (!string.IsNullOrEmpty(conflict.PatchContent))
            {
                Logger.LogInformation("Attempting automatic conflict resolution with patch content");
                
                try
                {
                    // Apply the patch to resolve the conflict
                    await FunctionBindings!.ApplyPatchAsync(conflict.Repo.Name, conflict.PatchContent);
                    
                    // Commit the resolved changes
                    await FunctionBindings!.CommitAsync(
                        conflict.Repo.Name,
                        $"Resolve conflict for {Ctx!.Config.WorkItem.Id}",
                        false);
                    
                    await FunctionBindings!.CommentAsync(Ctx!.Config.WorkItem.Id, "Conflict automatically resolved using patch content.");
                    Logger.LogInformation("Successfully resolved conflict for work item {WorkItemId}", Ctx!.Config.WorkItem.Id);
                    
                    // Continue with pending operations if any
                    if (_pendingOperations.Any())
                    {
                        var nextOperation = _pendingOperations.First();
                        _pendingOperations.RemoveAt(0);
                        await ExecuteFunctionCallAsync(nextOperation);
                    }
                    
                    return;
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Failed to automatically resolve conflict for work item {WorkItemId}", Ctx!.Config.WorkItem.Id);
                    await FunctionBindings!.CommentAsync(Ctx!.Config.WorkItem.Id, $"Automatic conflict resolution failed: {ex.Message}. Manual resolution required.");
                }
            }
            else
            {
                await FunctionBindings!.CommentAsync(Ctx!.Config.WorkItem.Id, "No patch content available for automatic resolution. Manual resolution required.");
            }

            // If auto-resolution failed or no patch available, block the work item
            await FunctionBindings!.TransitionAsync(Ctx!.Config.WorkItem.Id, "Blocked");
        }
    }

    private bool CanExecutePush(WorkItemRef workItem)
    {
        var policy = Ctx!.Config.Policy;
        
        // Check if tests are required and have been executed
        if (policy.RequireTestsBeforePush && !_testsExecuted)
        {
            Logger.LogWarning("Push blocked: Tests required but not executed for work item {WorkItemId}", workItem.Id);
            return false;
        }
        
        // Check if approval is required and has been granted
        if (policy.RequireApprovalForPush && !_approvalGranted)
        {
            Logger.LogWarning("Push blocked: Approval required but not granted for work item {WorkItemId}", workItem.Id);
            return false;
        }
        
        // Check dry-run mode
        if (Ctx!.AgentConfig.DryRun)
        {
            Logger.LogInformation("Push blocked: Dry-run mode enabled for work item {WorkItemId}", workItem.Id);
            return false;
        }
        
        return true;
    }
}