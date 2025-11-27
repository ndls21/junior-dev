# Semantic Kernel Function Discovery in ExecutorAgent

## Overview

The ExecutorAgent now properly exposes its capabilities as Semantic Kernel functions, making them discoverable and invokable by LLMs and other SK components.

## Exposed Functions

### Plugin: `executor_agent`

#### `execute_work_item`
**Description**: Analyzes and executes a complete work item, handling branch creation, implementation, testing, and completion.

**Parameters**:
- `workItemId` (string): The work item ID to execute

**Returns**: String describing the execution result

**Example Usage**:
```csharp
var result = await kernel.InvokeAsync("executor_agent", "execute_work_item", new KernelArguments
{
    ["workItemId"] = "JIRA-123"
});
```

## Plugin Architecture

The agent registers three types of plugins with the kernel:

1. **VCS Plugin** (`vcs`): Version control operations
   - `create_branch`: Create a new git branch
   - `apply_patch`: Apply a patch to the repository
   - `run_tests`: Run tests in the repository
   - `commit`: Commit changes
   - `push`: Push changes to remote
   - `get_diff`: Get repository diff

2. **Work Items Plugin** (`workitems`): Work item operations
   - `list_backlog`: List work items in backlog (placeholder)
   - `get_item`: Get work item details (placeholder)
   - `claim_item`: Claim a work item
   - `comment`: Add comment to work item
   - `transition`: Transition work item state

3. **General Plugin** (`general`): General orchestrator operations
   - `upload_artifact`: Upload an artifact
   - `request_approval`: Request approval for an action

4. **Executor Agent Plugin** (`executor_agent`): High-level agent operations
   - `execute_work_item`: Complete work item execution workflow

## LLM Discovery

When an LLM is connected to the kernel, it can:

1. **Discover available functions**:
   ```csharp
   var plugins = kernel.Plugins;
   foreach (var plugin in plugins)
   {
       Console.WriteLine($"Plugin: {plugin.Name}");
       foreach (var function in plugin)
       {
           Console.WriteLine($"  - {function.Name}: {function.Description}");
       }
   }
   ```

2. **Invoke functions based on context**:
   ```csharp
   var llmResponse = await kernel.InvokeAsync("chat", "plan_and_execute", new KernelArguments
   {
       ["userRequest"] = "Implement the login feature for JIRA-456"
   });
   // LLM can choose to call: executor_agent.execute_work_item(workItemId: "JIRA-456")
   ```

3. **Compose workflows**:
   ```csharp
   // LLM can reason about and create multi-step plans:
   // 1. workitems.get_item(itemId: "JIRA-456") - to understand requirements
   // 2. executor_agent.execute_work_item(workItemId: "JIRA-456") - to implement
   // 3. workitems.comment(itemId: "JIRA-456", comment: "Implementation complete") - to notify
   ```

## Policy Enforcement

All functions go through the orchestrator's policy enforcement:
- **Protected branches**: Checked before branch creation
- **Approval requirements**: Enforced before risky operations (push)
- **Test requirements**: Enforced before push if configured
- **Dry-run mode**: All functions respect dry-run configuration
- **Rate limits**: Applied via orchestrator's rate limiting

## Function Invocation Flow

```
LLM/Caller
    ↓
kernel.InvokeAsync("plugin", "function", args)
    ↓
OrchestratorFunctionBindings (if vcs/workitems/general)
    ↓
SessionManager.PublishCommand
    ↓
Orchestrator (policy validation)
    ↓
Adapter (execution)
```

OR

```
LLM/Caller
    ↓
kernel.InvokeAsync("executor_agent", "execute_work_item", args)
    ↓
ExecutorAgent.ExecuteWorkItemAsync
    ↓
Creates plan as FunctionCall list
    ↓
kernel.InvokeAsync for each step
    ↓
[follows flow above]
```

## Benefits

1. **LLM-Ready**: Functions are discoverable with descriptions
2. **Composable**: LLM can chain functions into workflows
3. **Type-Safe**: SK validates parameters against descriptions
4. **Policy-Aware**: All operations validated by orchestrator
5. **Extensible**: New functions automatically available to LLM
6. **Testable**: Functions testable in isolation

## Future: Full LLM Planning

With functions properly exposed, we can replace static planning:

```csharp
// Current: Static planning in AnalyzeWorkItemAsync
var functionCalls = new List<FunctionCall>
{
    new() { PluginName = "vcs", FunctionName = "create_branch", ... },
    new() { PluginName = "vcs", FunctionName = "commit", ... },
    // ...
};

// Future: LLM-driven planning
var planningPrompt = $"""
    You are a software development agent. Analyze work item {workItem.Id} and create a plan.
    
    Available functions:
    {GetAvailableFunctionsDescription()}
    
    Policy constraints:
    - Protected branches: {string.Join(", ", Context.Config.Policy.ProtectedBranches)}
    - Require tests before push: {Context.Config.Policy.RequireTestsBeforePush}
    - Require approval for push: {Context.Config.Policy.RequireApprovalForPush}
    
    Return a JSON array of function calls to execute.
    """;

var planJson = await kernel.InvokeAsync("planning", "create_plan", new KernelArguments
{
    ["prompt"] = planningPrompt,
    ["workItemId"] = workItem.Id
});

var functionCalls = JsonSerializer.Deserialize<List<FunctionCall>>(planJson);
```

The LLM would output function calls that respect policy constraints, and execution proceeds unchanged.
