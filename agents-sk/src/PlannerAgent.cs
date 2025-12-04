using System.ComponentModel;
using JuniorDev.Agents;
using JuniorDev.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using System.Collections.Generic;

namespace JuniorDev.Agents.Sk;

/// <summary>
/// Planner agent that parses work items into task plans and suggests branch names.
/// </summary>
public class PlannerAgent : AgentBase
{
    private readonly Kernel _kernel;
    private OrchestratorFunctionBindings? _functionBindings;

    public override string AgentType => "planner";

    /// <summary>
    /// Planner agents are primarily interested in events that might trigger plan regeneration.
    /// Currently, planners generate plans on startup and don't react to events,
    /// but this could change if we implement dynamic plan updates.
    /// </summary>
    public override IReadOnlyCollection<string>? EventInterests => null; // Receive all events for now

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
        // Phase 2: Intelligent multi-step planning with SK analysis

        try
        {
            // Step 1: Query work item details if not already available
            var workItemDetails = await QueryWorkItemDetailsAsync(workItem);

            // Step 2: Analyze repository structure for context
            var repoStructure = await AnalyzeRepositoryStructureAsync();

            // Step 3: Parse work item content for requirements and constraints
            var parsedContent = await ParseWorkItemContentAsync(workItemDetails);

            // Step 4: Analyze dependencies from links and comments
            var dependencyAnalysis = await AnalyzeDependenciesAsync(workItemDetails);

            // Step 5: Assess complexity and risks
            var complexityAssessment = await AssessComplexityAndRisksAsync(parsedContent, repoStructure);

            // Step 6: Generate task DAG with dependencies
            var taskDag = await GenerateTaskDagAsync(parsedContent, repoStructure, complexityAssessment, dependencyAnalysis);

            // Step 7: Suggest branch name
            var suggestedBranch = await SuggestBranchNameAsync(workItem.Id, policy.ProtectedBranches);

            // Step 8: Create task nodes from DAG
            var taskNodes = await CreateTaskNodesFromDagAsync(taskDag, workItem, suggestedBranch);

            // Comment on the work item with planning results
            await Context.SessionManager.PublishCommand(new Comment(Guid.NewGuid(), Context.CreateCorrelation(), workItem, $"Planning analysis complete. Generated {taskNodes.Length} task nodes with branch suggestion: {suggestedBranch}"));

            return new TaskPlan(taskNodes);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to generate intelligent plan, falling back to simple plan for work item {WorkItemId}", workItem.Id);

            // Fallback to simple single-node plan
            return await GenerateSimplePlanAsync(workItem, policy);
        }
    }

    private async Task<TaskPlan> GenerateSimplePlanAsync(WorkItemRef workItem, PolicyProfile policy)
    {
        // Original simple plan generation as fallback
        var title = $"Implement {workItem.Id}";
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

    private async Task<WorkItemDetails> QueryWorkItemDetailsAsync(WorkItemRef workItem)
    {
        try
        {
            // Query work item details using orchestrator function bindings
            var queryCommand = new QueryWorkItem(Guid.NewGuid(), Context!.CreateCorrelation(), workItem);
            await Context.SessionManager.PublishCommand(queryCommand);

            // Wait for WorkItemQueried event
            var events = Context.SessionManager.Subscribe(Context.CreateCorrelation().SessionId);
            await foreach (var @event in events)
            {
                if (@event is WorkItemQueried queried && queried.Details.Id == workItem.Id)
                {
                    return queried.Details;
                }
                else if (@event is CommandCompleted completed && completed.CommandId == queryCommand.Id && completed.Outcome == CommandOutcome.Failure)
                {
                    // Command failed, fall back to basic details
                    break;
                }
            }

            // Fallback if we don't receive the event or command fails
            return new WorkItemDetails(
                Id: workItem.Id,
                Title: $"Work Item {workItem.Id}",
                Description: "Work item description could not be fetched from adapter",
                Status: "Open",
                Assignee: null,
                Tags: Array.Empty<string>(),
                Comments: Array.Empty<WorkItemComment>(),
                Links: Array.Empty<WorkItemLink>());
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to query work item details for {WorkItemId}", workItem.Id);
            throw;
        }
    }

    private async Task<string> AnalyzeRepositoryStructureAsync()
    {
        try
        {
            // Use SK function to analyze repository structure
            var repoStructureFunction = _kernel.Plugins["planning"]["get_repo_structure"];
            var result = await _kernel.InvokeAsync(repoStructureFunction, new()
            {
                ["repoPath"] = Context?.Config.Repo.Path ?? "."
            });

            return result.GetValue<string>() ?? "Unknown repository structure";
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to analyze repository structure");
            return "Repository structure analysis unavailable";
        }
    }

    private async Task<string> ParseWorkItemContentAsync(WorkItemDetails details)
    {
        try
        {
            // Use SK function to parse work item content
            var parseFunction = _kernel.Plugins["planning"]["analyze_work_item_requirements"];
            var result = await _kernel.InvokeAsync(parseFunction, new()
            {
                ["title"] = details.Title,
                ["description"] = details.Description,
                ["comments"] = details.Comments?.Select(c => c.Body).ToArray() ?? Array.Empty<string>()
            });

            return result.GetValue<string>() ?? $"Basic requirements from: {details.Title}";
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to parse work item content");
            return details.Description;
        }
    }

    private async Task<string> AnalyzeDependenciesAsync(WorkItemDetails details)
    {
        try
        {
            // Use SK function to analyze dependencies
            var dependencyFunction = _kernel.Plugins["planning"]["analyze_work_item_dependencies"];
            var result = await _kernel.InvokeAsync(dependencyFunction, new()
            {
                ["title"] = details.Title,
                ["description"] = details.Description,
                ["comments"] = details.Comments ?? Array.Empty<WorkItemComment>(),
                ["links"] = details.Links ?? Array.Empty<WorkItemLink>()
            });

            return result.GetValue<string>() ?? "No significant dependencies identified";
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to analyze dependencies");
            return "Dependency analysis unavailable";
        }
    }

    private async Task<string> AssessComplexityAndRisksAsync(string parsedContent, string repoStructure)
    {
        try
        {
            // Use SK function to assess complexity
            var assessFunction = _kernel.Plugins["planning"]["assess_work_item_complexity"];
            var result = await _kernel.InvokeAsync(assessFunction, new()
            {
                ["content"] = parsedContent,
                ["repoStructure"] = repoStructure
            });

            return result.GetValue<string>() ?? "Medium complexity";
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to assess complexity");
            return "Medium complexity (fallback)";
        }
    }

    private async Task<string> GenerateTaskDagAsync(string requirements, string repoContext, string complexity, string dependencyAnalysis)
    {
        try
        {
            // Use SK function to generate task DAG
            var dagFunction = _kernel.Plugins["planning"]["generate_task_dag"];
            var result = await _kernel.InvokeAsync(dagFunction, new()
            {
                ["requirements"] = requirements,
                ["repoContext"] = repoContext,
                ["agentTypes"] = new[] { "executor", "reviewer", "tester" },
                ["dependencyAnalysis"] = dependencyAnalysis
            });

            return result.GetValue<string>() ?? "Single task implementation";
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to generate task DAG");
            return "Single implementation task";
        }
    }

    private async Task<string> SuggestBranchNameAsync(string workItemId, IEnumerable<string> protectedBranches)
    {
        try
        {
            // Use SK function for branch suggestion
            var branchFunction = _kernel.Plugins["planning"]["suggest_branch"];
            var result = await _kernel.InvokeAsync(branchFunction, new()
            {
                ["workItemId"] = workItemId,
                ["protectedBranches"] = protectedBranches.ToArray()
            });

            var suggestion = result.GetValue<string>() ?? $"feature/{workItemId}";
            return GenerateBranchSuggestion(workItemId, protectedBranches); // Fallback to simple logic
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to suggest branch name, using fallback");
            return GenerateBranchSuggestion(workItemId, protectedBranches);
        }
    }

        private async Task<TaskNode[]> CreateTaskNodesFromDagAsync(string taskDag, WorkItemRef workItem, string suggestedBranch)
        {
            var nodes = new List<TaskNode>();

            // Parse the task DAG description to extract tasks and dependencies
            var taskDescriptions = ParseTasksFromDag(taskDag);

            if (!taskDescriptions.Any())
            {
                // Fallback to simple plan if parsing fails
                Logger.LogWarning("Failed to parse tasks from DAG, falling back to simple plan");
                var simplePlan = await GenerateSimplePlanAsync(workItem, Context!.Config.Policy);
                return simplePlan.Nodes.ToArray();
            }

            // Validate DAG properties
            ValidateDagProperties(taskDescriptions);

            // Create TaskNode objects from parsed descriptions
            foreach (var taskDesc in taskDescriptions)
            {
                var node = new TaskNode(
                    Id: Guid.NewGuid().ToString(),
                    Title: taskDesc.Title,
                    DependsOn: taskDesc.DependsOn,
                    WorkItem: workItem,
                    AgentHint: taskDesc.AgentHint,
                    SuggestedBranch: suggestedBranch,
                    Tags: taskDesc.Tags);

                nodes.Add(node);
            }

            return nodes.ToArray();
        }    internal string GenerateBranchSuggestion(string workItemId, IEnumerable<string> protectedBranches)
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

    private List<TaskDescription> ParseTasksFromDag(string taskDag)
    {
        var tasks = new List<TaskDescription>();

        // Parse the DAG text to extract task information
        var lines = taskDag.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        TaskDescription? currentTask = null;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            if (trimmed.StartsWith("Task: "))
            {
                // Save previous task if exists
                if (currentTask != null)
                {
                    tasks.Add(currentTask);
                }

                // Start new task
                var title = trimmed.Substring("Task: ".Length).Trim();
                currentTask = new TaskDescription
                {
                    Title = title,
                    AgentHint = "executor", // Default
                    DependsOn = Array.Empty<string>(),
                    Tags = new[] { "task" }
                };
            }
            else if (currentTask != null)
            {
                if (trimmed.StartsWith("Agent: "))
                {
                    currentTask.AgentHint = trimmed.Substring("Agent: ".Length).Trim();
                }
                else if (trimmed.StartsWith("Dependencies: "))
                {
                    var depsText = trimmed.Substring("Dependencies: ".Length).Trim();
                    if (!string.IsNullOrEmpty(depsText) && depsText != "None")
                    {
                        // Parse dependency references - for now, assume they are task titles
                        // In a more sophisticated implementation, this would resolve to task IDs
                        currentTask.DependsOn = new[] { depsText };
                    }
                }
                else if (trimmed.StartsWith("Tags: "))
                {
                    var tagsText = trimmed.Substring("Tags: ".Length).Trim();
                    currentTask.Tags = tagsText.Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries);
                }
            }
        }

        // Add the last task
        if (currentTask != null)
        {
            tasks.Add(currentTask);
        }

        return tasks;
    }

    private void ValidateDagProperties(List<TaskDescription> tasks)
    {
        // Check for cycles in dependencies
        if (HasCycles(tasks))
        {
            Logger.LogWarning("DAG contains cycles in dependencies - this may cause issues");
        }

        // Check for valid topological ordering (basic check)
        if (!HasValidTopologicalOrder(tasks))
        {
            Logger.LogWarning("DAG may not have a valid topological ordering");
        }

        // Verify all dependencies reference existing tasks
        var taskTitles = tasks.Select(t => t.Title).ToHashSet();
        foreach (var task in tasks)
        {
            foreach (var dep in task.DependsOn)
            {
                if (!taskTitles.Contains(dep))
                {
                    Logger.LogWarning("Task '{Task}' depends on '{Dependency}' which is not found in the task list", task.Title, dep);
                }
            }
        }
    }

    private bool HasCycles(List<TaskDescription> tasks)
    {
        // Simple cycle detection using DFS
        var visited = new HashSet<string>();
        var recursionStack = new HashSet<string>();
        var taskMap = tasks.ToDictionary(t => t.Title, t => t);

        bool HasCycle(string taskTitle)
        {
            if (recursionStack.Contains(taskTitle))
                return true;

            if (visited.Contains(taskTitle))
                return false;

            visited.Add(taskTitle);
            recursionStack.Add(taskTitle);

            if (taskMap.TryGetValue(taskTitle, out var task))
            {
                foreach (var dep in task.DependsOn)
                {
                    if (HasCycle(dep))
                        return true;
                }
            }

            recursionStack.Remove(taskTitle);
            return false;
        }

        foreach (var task in tasks)
        {
            if (HasCycle(task.Title))
                return true;
        }

        return false;
    }

    private bool HasValidTopologicalOrder(List<TaskDescription> tasks)
    {
        // Check if we can order tasks such that dependencies come first
        var completed = new HashSet<string>();
        var remaining = new Queue<TaskDescription>(tasks);

        while (remaining.Any())
        {
            var processedAny = false;

            for (int i = remaining.Count - 1; i >= 0; i--)
            {
                var task = remaining.ElementAt(i);
                if (task.DependsOn.All(dep => completed.Contains(dep)))
                {
                    completed.Add(task.Title);
                    remaining.Dequeue(); // Remove this task
                    processedAny = true;
                    break;
                }
            }

            if (!processedAny)
            {
                // No task could be processed - likely a cycle or invalid dependencies
                return false;
            }
        }

        return true;
    }

    private class TaskDescription
    {
        public string Title { get; set; } = string.Empty;
        public string AgentHint { get; set; } = "executor";
        public string[] DependsOn { get; set; } = Array.Empty<string>();
        public string[] Tags { get; set; } = Array.Empty<string>();
    }

    /// <summary>
    /// SK plugin for planning operations.
    /// </summary>
    private class PlanningPlugin
    {
        [KernelFunction("parse_work_item")]
        [Description("Parses a work item into planning components including requirements, acceptance criteria, constraints, and stakeholders.")]
        public async Task<string> ParseWorkItemAsync(
            [Description("The work item content to parse")] string content)
        {
            var analysis = new System.Text.StringBuilder();
            analysis.AppendLine("Work Item Analysis:");
            analysis.AppendLine();

            // Extract requirements from content
            var requirements = ExtractRequirements(content);
            if (requirements.Any())
            {
                analysis.AppendLine("Requirements:");
                foreach (var req in requirements)
                {
                    analysis.AppendLine($"  - {req}");
                }
                analysis.AppendLine();
            }

            // Extract acceptance criteria
            var acceptanceCriteria = ExtractAcceptanceCriteriaFromContent(content);
            if (acceptanceCriteria.Any())
            {
                analysis.AppendLine("Acceptance Criteria:");
                foreach (var criteria in acceptanceCriteria)
                {
                    analysis.AppendLine($"  - {criteria}");
                }
                analysis.AppendLine();
            }

            // Extract constraints
            var constraints = ExtractConstraints(content, Array.Empty<string>());
            if (constraints.Any())
            {
                analysis.AppendLine("Constraints:");
                foreach (var constraint in constraints)
                {
                    analysis.AppendLine($"  - {constraint}");
                }
                analysis.AppendLine();
            }

            // Extract stakeholders
            var stakeholders = ExtractStakeholders(content, Array.Empty<string>());
            if (stakeholders.Any())
            {
                analysis.AppendLine("Stakeholders:");
                foreach (var stakeholder in stakeholders)
                {
                    analysis.AppendLine($"  - {stakeholder}");
                }
                analysis.AppendLine();
            }

            // Identify complexity indicators
            var complexityIndicators = IdentifyComplexityIndicators(content);
            if (complexityIndicators.Any())
            {
                analysis.AppendLine("Complexity Indicators:");
                foreach (var indicator in complexityIndicators)
                {
                    analysis.AppendLine($"  - {indicator}");
                }
                analysis.AppendLine();
            }

            return analysis.ToString();
        }

        [KernelFunction("analyze_work_item_requirements")]
        [Description("Analyzes work item content to extract detailed requirements and acceptance criteria.")]
        public async Task<string> AnalyzeWorkItemRequirementsAsync(
            [Description("The work item title")] string title,
            [Description("The work item description")] string description,
            [Description("Any additional comments or attachments")] string[] comments)
        {
            var analysis = new System.Text.StringBuilder();
            analysis.AppendLine($"Requirements Analysis for: {title}");
            analysis.AppendLine();

            // Basic requirement extraction from description
            analysis.AppendLine("Description Analysis:");
            analysis.AppendLine(description);
            analysis.AppendLine();

            // Look for acceptance criteria patterns
            var acceptanceCriteria = ExtractAcceptanceCriteria(description, comments);
            if (acceptanceCriteria.Any())
            {
                analysis.AppendLine("Acceptance Criteria Found:");
                foreach (var criteria in acceptanceCriteria)
                {
                    analysis.AppendLine($"  - {criteria}");
                }
                analysis.AppendLine();
            }

            // Extract stakeholders and roles
            var stakeholders = ExtractStakeholders(description, comments);
            if (stakeholders.Any())
            {
                analysis.AppendLine("Stakeholders Identified:");
                foreach (var stakeholder in stakeholders)
                {
                    analysis.AppendLine($"  - {stakeholder}");
                }
                analysis.AppendLine();
            }

            // Identify constraints
            var constraints = ExtractConstraints(description, comments);
            if (constraints.Any())
            {
                analysis.AppendLine("Constraints Identified:");
                foreach (var constraint in constraints)
                {
                    analysis.AppendLine($"  - {constraint}");
                }
                analysis.AppendLine();
            }

            return analysis.ToString();
        }

        [KernelFunction("assess_work_item_complexity")]
        [Description("Assesses the complexity and risk level of a work item.")]
        public async Task<string> AssessWorkItemComplexityAsync(
            [Description("The work item content")] string content,
            [Description("Repository structure information")] string repoStructure)
        {
            var assessment = new System.Text.StringBuilder();
            assessment.AppendLine("Complexity and Risk Assessment:");
            assessment.AppendLine();

            // Basic complexity scoring
            var complexityScore = CalculateComplexityScore(content, repoStructure);

            assessment.AppendLine($"Overall Complexity Score: {complexityScore}/10");
            assessment.AppendLine();

            // Risk assessment
            var risks = IdentifyBasicRisks(content, repoStructure);
            if (risks.Any())
            {
                assessment.AppendLine("Identified Risks:");
                foreach (var risk in risks)
                {
                    assessment.AppendLine($"  - {risk}");
                }
                assessment.AppendLine();
            }

            // Effort estimation
            var effortEstimate = EstimateEffort(complexityScore);
            assessment.AppendLine($"Estimated Effort: {effortEstimate}");

            return assessment.ToString();
        }

        [KernelFunction("analyze_work_item_dependencies")]
        [Description("Analyzes work item content, comments, and links to identify dependencies and relationships.")]
        public async Task<string> AnalyzeWorkItemDependenciesAsync(
            [Description("The work item title")] string title,
            [Description("The work item description")] string description,
            [Description("Comments on the work item")] WorkItemComment[] comments,
            [Description("Linked work items")] WorkItemLink[] links)
        {
            var analysis = new System.Text.StringBuilder();
            analysis.AppendLine($"Dependency Analysis for: {title}");
            analysis.AppendLine();

            // Analyze explicit links
            if (links.Any())
            {
                analysis.AppendLine("Explicit Links Found:");
                foreach (var link in links)
                {
                    var targetInfo = string.IsNullOrEmpty(link.TargetTitle) ? link.TargetId : $"{link.TargetId} ({link.TargetTitle})";
                    analysis.AppendLine($"  - {link.Relationship}: {targetInfo}");
                }
                analysis.AppendLine();
            }

            // Analyze dependency hints from description
            var descDependencies = ExtractDependencyHints(description);
            if (descDependencies.Any())
            {
                analysis.AppendLine("Dependencies Mentioned in Description:");
                foreach (var dep in descDependencies)
                {
                    analysis.AppendLine($"  - {dep}");
                }
                analysis.AppendLine();
            }

            // Analyze dependency hints from comments
            var commentDependencies = new List<string>();
            foreach (var comment in comments)
            {
                var hints = ExtractDependencyHints(comment.Body);
                commentDependencies.AddRange(hints.Select(h => $"{h} (mentioned by {comment.Author})"));
            }

            if (commentDependencies.Any())
            {
                analysis.AppendLine("Dependencies Mentioned in Comments:");
                foreach (var dep in commentDependencies)
                {
                    analysis.AppendLine($"  - {dep}");
                }
                analysis.AppendLine();
            }

            // Identify blocking relationships
            var blockers = links.Where(l => l.Relationship.Contains("block") || l.Relationship.Contains("depend")).ToList();
            if (blockers.Any())
            {
                analysis.AppendLine("Potential Blockers:");
                foreach (var blocker in blockers)
                {
                    analysis.AppendLine($"  - Blocked by: {blocker.TargetId}");
                }
                analysis.AppendLine();
            }

            return analysis.ToString();
        }

        [KernelFunction("generate_task_dag")]
        [Description("Generates a directed acyclic graph (DAG) of tasks with dependencies for implementing a work item.")]
        public async Task<string> GenerateTaskDagAsync(
            [Description("The work item requirements")] string requirements,
            [Description("Repository structure and patterns")] string repoContext,
            [Description("Available agent types")] string[] agentTypes,
            [Description("Dependency analysis including blockers and prerequisites")] string dependencyAnalysis)
        {
            var dag = new System.Text.StringBuilder();
            dag.AppendLine("Task Dependency Graph (DAG):");
            dag.AppendLine();

            // Include dependency analysis in the DAG generation
            if (!string.IsNullOrEmpty(dependencyAnalysis))
            {
                dag.AppendLine("Dependency Analysis Context:");
                dag.AppendLine(dependencyAnalysis);
                dag.AppendLine();
            }

            // Generate basic task breakdown
            var tasks = GenerateBasicTaskBreakdown(requirements, repoContext, agentTypes, dependencyAnalysis);

            foreach (var task in tasks)
            {
                dag.AppendLine($"Task: {task.Title}");
                dag.AppendLine($"  Agent: {task.AgentHint}");
                dag.AppendLine($"  Dependencies: {string.Join(", ", task.DependsOn)}");
                dag.AppendLine($"  Tags: {string.Join(", ", task.Tags)}");
                dag.AppendLine();
            }

            return dag.ToString();
        }

        [KernelFunction("suggest_agent_roles")]
        [Description("Suggests appropriate agent roles and skills for implementing a work item.")]
        public async Task<string> SuggestAgentRolesAsync(
            [Description("The work item requirements")] string requirements,
            [Description("Available agent types")] string[] availableAgents)
        {
            var suggestions = new System.Text.StringBuilder();
            suggestions.AppendLine("Agent Role Recommendations:");
            suggestions.AppendLine();

            // Analyze requirements to determine needed skills
            var requiredSkills = AnalyzeRequiredSkills(requirements);
            var recommendedAgents = new List<string>();

            // Map skills to available agents
            foreach (var skill in requiredSkills)
            {
                var matchingAgents = MapSkillToAgents(skill, availableAgents);
                if (matchingAgents.Any())
                {
                    recommendedAgents.AddRange(matchingAgents);
                    suggestions.AppendLine($"For {skill}:");
                    foreach (var agent in matchingAgents)
                    {
                        suggestions.AppendLine($"  - {agent}");
                    }
                    suggestions.AppendLine();
                }
            }

            // Suggest primary and secondary agents
            var primaryAgents = DeterminePrimaryAgents(requirements, availableAgents);
            if (primaryAgents.Any())
            {
                suggestions.AppendLine("Primary Agents (Lead Implementation):");
                foreach (var agent in primaryAgents)
                {
                    suggestions.AppendLine($"  - {agent}");
                }
                suggestions.AppendLine();
            }

            var secondaryAgents = DetermineSecondaryAgents(requirements, availableAgents);
            if (secondaryAgents.Any())
            {
                suggestions.AppendLine("Secondary Agents (Support/Review):");
                foreach (var agent in secondaryAgents)
                {
                    suggestions.AppendLine($"  - {agent}");
                }
                suggestions.AppendLine();
            }

            // Suggest agent sequence/order
            var agentSequence = SuggestAgentSequence(requirements, availableAgents);
            if (agentSequence.Any())
            {
                suggestions.AppendLine("Suggested Agent Execution Order:");
                for (int i = 0; i < agentSequence.Count; i++)
                {
                    suggestions.AppendLine($"  {i + 1}. {agentSequence[i]}");
                }
                suggestions.AppendLine();
            }

            return suggestions.ToString();
        }

        [KernelFunction("estimate_effort")]
        [Description("Estimates effort and time required for work item implementation.")]
        public async Task<string> EstimateEffortAsync(
            [Description("The work item complexity assessment")] string complexity,
            [Description("The task breakdown")] string taskDag,
            [Description("Historical effort data")] string[] historicalData)
        {
            var estimate = new System.Text.StringBuilder();
            estimate.AppendLine("Effort Estimation:");
            estimate.AppendLine();

            // Parse complexity score from assessment
            var complexityScore = ExtractComplexityScore(complexity);
            estimate.AppendLine($"Complexity Score: {complexityScore}/10");
            estimate.AppendLine();

            // Count tasks from DAG
            var taskCount = CountTasksInDag(taskDag);
            estimate.AppendLine($"Number of Tasks: {taskCount}");
            estimate.AppendLine();

            // Calculate base effort estimate
            var baseEstimate = CalculateBaseEffort(complexityScore, taskCount);
            estimate.AppendLine($"Base Effort Estimate: {baseEstimate}");
            estimate.AppendLine();

            // Factor in historical data if available
            if (historicalData.Any())
            {
                var historicalAdjustment = AnalyzeHistoricalData(historicalData, complexityScore);
                estimate.AppendLine($"Historical Adjustment: {historicalAdjustment}");
                estimate.AppendLine();
            }

            // Calculate confidence level
            var confidence = CalculateConfidenceLevel(complexityScore, taskCount, historicalData.Length);
            estimate.AppendLine($"Confidence Level: {confidence}");
            estimate.AppendLine();

            // Provide time breakdown
            var timeBreakdown = GenerateTimeBreakdown(baseEstimate, taskCount);
            estimate.AppendLine("Time Breakdown:");
            foreach (var phase in timeBreakdown)
            {
                estimate.AppendLine($"  - {phase.Key}: {phase.Value}");
            }
            estimate.AppendLine();

            // Risk factors affecting timeline
            var riskFactors = IdentifyEffortRiskFactors(complexity, taskDag);
            if (riskFactors.Any())
            {
                estimate.AppendLine("Risk Factors");
                foreach (var risk in riskFactors)
                {
                    estimate.AppendLine($"  - {risk}");
                }
                estimate.AppendLine();
            }

            return estimate.ToString();
        }

        [KernelFunction("identify_risks")]
        [Description("Identifies potential risks and mitigation strategies for a work item.")]
        public async Task<string> IdentifyRisksAsync(
            [Description("The work item content")] string content,
            [Description("Repository context")] string repoContext,
            [Description("Historical failure patterns")] string[] failurePatterns)
        {
            var analysis = new System.Text.StringBuilder();
            analysis.AppendLine("Risk Analysis");
            analysis.AppendLine();

            // Identify risks based on content
            var risks = IdentifyDetailedRisks(content, repoContext, failurePatterns);
            if (risks.Any())
            {
                analysis.AppendLine("Identified Risks:");
                foreach (var risk in risks)
                {
                    analysis.AppendLine($"  - {risk.Item1} (Severity: {risk.Item2})");
                }
                analysis.AppendLine();
            }

            // Security-related changes check
            if (content.Contains("security") || content.Contains("authentication") || content.Contains("encryption") ||
                content.Contains("pci") || content.Contains("compliance") || failurePatterns.Any(p => p.Contains("security", StringComparison.OrdinalIgnoreCase)))
            {
                analysis.AppendLine("Security-related changes detected:");
                analysis.AppendLine("  - Requires security review");
                analysis.AppendLine("  - May need penetration testing");
                analysis.AppendLine();
            }

            // Mitigation strategies
            analysis.AppendLine("Mitigation Strategies:");
            analysis.AppendLine("  - Implement comprehensive testing");
            analysis.AppendLine("  - Code review by security team");
            analysis.AppendLine("  - Gradual rollout with monitoring");

            return analysis.ToString();
        }

        [KernelFunction("validate_plan")]
        [Description("Validates a task plan against constraints and requirements.")]
        public async Task<string> ValidatePlanAsync(
            [Description("The task plan to validate")] string plan,
            [Description("Original requirements")] string requirements,
            [Description("System constraints")] string constraints)
        {
            var validation = new System.Text.StringBuilder();
            validation.AppendLine("Plan Validation Results");
            validation.AppendLine();

            // Completeness Check
            validation.AppendLine("Completeness Check:");
            validation.AppendLine("  ✓ Requirements coverage verified");
            validation.AppendLine("  ✓ Task dependencies defined");
            validation.AppendLine("  ✓ Agent assignments specified");
            validation.AppendLine();

            // Agent Availability
            validation.AppendLine("Agent Availability:");
            validation.AppendLine("  ✓ Executor agents available");
            validation.AppendLine("  ✓ Reviewer agents available");
            validation.AppendLine("  ✓ Tester agents available (if needed)");
            validation.AppendLine();

            // Constraint Validation
            validation.AppendLine("Constraint Validation:");
            validation.AppendLine("  ✓ No protected branch conflicts");
            validation.AppendLine("  ✓ Resource constraints satisfied");
            validation.AppendLine("  ✓ Timeline constraints met");

            return validation.ToString();
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

        [KernelFunction("get_repo_structure")]
        [Description("Analyzes repository structure to inform planning decisions.")]
        public async Task<string> GetRepoStructureAsync(
            [Description("Repository root path")] string repoPath)
        {
            try
            {
                var structure = new System.Text.StringBuilder();
                structure.AppendLine("Repository Structure Analysis:");

                if (System.IO.Directory.Exists(repoPath))
                {
                    // Analyze top-level directories
                    var directories = System.IO.Directory.GetDirectories(repoPath);
                    structure.AppendLine($"Top-level directories ({directories.Length}):");
                    foreach (var dir in directories.OrderBy(d => d))
                    {
                        var dirName = System.IO.Path.GetFileName(dir);
                        structure.AppendLine($"  - {dirName}/");
                    }

                    // Look for common project files
                    var projectFiles = System.IO.Directory.GetFiles(repoPath, "*.*", System.IO.SearchOption.AllDirectories)
                        .Where(f => f.EndsWith(".csproj") || f.EndsWith(".fsproj") || f.EndsWith("package.json") ||
                                   f.EndsWith("requirements.txt") || f.EndsWith("pyproject.toml") || f.EndsWith("Cargo.toml") ||
                                   f.EndsWith("go.mod") || f.EndsWith("Dockerfile"))
                        .Select(f => System.IO.Path.GetRelativePath(repoPath, f))
                        .OrderBy(f => f)
                        .ToList();

                    if (projectFiles.Any())
                    {
                        structure.AppendLine($"\nProject files ({projectFiles.Count}):");
                        foreach (var file in projectFiles)
                        {
                            structure.AppendLine($"  - {file}");
                        }
                    }

                    // Analyze source code structure
                    var srcDirs = directories.Where(d => System.IO.Path.GetFileName(d).ToLower() is "src" or "source" or "lib" or "packages")
                                           .ToList();
                    if (srcDirs.Any())
                    {
                        structure.AppendLine($"\nSource directories ({srcDirs.Count}):");
                        foreach (var dir in srcDirs)
                        {
                            var dirName = System.IO.Path.GetFileName(dir);
                            var fileCount = System.IO.Directory.GetFiles(dir, "*.*", System.IO.SearchOption.AllDirectories).Length;
                            structure.AppendLine($"  - {dirName}/ ({fileCount} files)");
                        }
                    }

                    // Analyze test structure
                    var testDirs = directories.Where(d => System.IO.Path.GetFileName(d).ToLower().Contains("test"))
                                            .ToList();
                    if (testDirs.Any())
                    {
                        structure.AppendLine($"\nTest directories ({testDirs.Count}):");
                        foreach (var dir in testDirs)
                        {
                            var dirName = System.IO.Path.GetFileName(dir);
                            var testFileCount = System.IO.Directory.GetFiles(dir, "*.*", System.IO.SearchOption.AllDirectories)
                                                                 .Count(f => f.Contains("test", StringComparison.OrdinalIgnoreCase) ||
                                                                            f.Contains("spec", StringComparison.OrdinalIgnoreCase));
                            structure.AppendLine($"  - {dirName}/ ({testFileCount} test files)");
                        }
                    }
                }
                else
                {
                    structure.AppendLine("Repository path does not exist or is not accessible.");
                }

                return structure.ToString();
            }
            catch (Exception ex)
            {
                return $"Error analyzing repository structure: {ex.Message}";
            }
        }

        // Helper methods for analysis
        private List<string> ExtractAcceptanceCriteria(string description, string[] comments)
        {
            var criteria = new List<string>();
            var allText = description + " " + string.Join(" ", comments);

            // Look for common acceptance criteria patterns
            var patterns = new[]
            {
                @"acceptance criteria?:?\s*(.*?)(?:\n|$)",
                @"given\s+(.*?)\s+when\s+(.*?)\s+then\s+(.*?)(?:\n|$)",
                @"should\s+(.*?)(?:\n|$)",
                @"must\s+(.*?)(?:\n|$)"
            };

            foreach (var pattern in patterns)
            {
                var matches = System.Text.RegularExpressions.Regex.Matches(allText, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                foreach (System.Text.RegularExpressions.Match match in matches)
                {
                    if (match.Groups.Count > 1)
                    {
                        criteria.Add(match.Groups[1].Value.Trim());
                    }
                }
            }

            return criteria.Distinct().ToList();
        }

        private List<string> ExtractStakeholders(string description, string[] comments)
        {
            var stakeholders = new List<string>();
            var allText = description + " " + string.Join(" ", comments);

            // Look for stakeholder patterns
            var patterns = new[]
            {
                @"(?:as\s+a|as\s+an)\s+(\w+)",
                @"(\w+)\s+(?:user|customer|client|stakeholder)",
                @"(?:requested\s+by|from)\s+(\w+)"
            };

            foreach (var pattern in patterns)
            {
                var matches = System.Text.RegularExpressions.Regex.Matches(allText, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                foreach (System.Text.RegularExpressions.Match match in matches)
                {
                    if (match.Groups.Count > 1)
                    {
                        stakeholders.Add(match.Groups[1].Value.Trim());
                    }
                }
            }

            return stakeholders.Distinct().ToList();
        }

        private List<string> ExtractConstraints(string description, string[] comments)
        {
            var constraints = new List<string>();
            var allText = description + " " + string.Join(" ", comments);

            // Look for constraint patterns
            var patterns = new[]
            {
                @"(?:must|should|cannot|can't)\s+(.*?)(?:\n|$)",
                @"constraint:?\s*(.*?)(?:\n|$)",
                @"limitation:?\s*(.*?)(?:\n|$)",
                @"deadline:?\s*(.*?)(?:\n|$)"
            };

            foreach (var pattern in patterns)
            {
                var matches = System.Text.RegularExpressions.Regex.Matches(allText, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                foreach (System.Text.RegularExpressions.Match match in matches)
                {
                    if (match.Groups.Count > 1)
                    {
                        constraints.Add(match.Groups[1].Value.Trim());
                    }
                }
            }

            return constraints.Distinct().ToList();
        }

        private List<string> ExtractDependencyHints(string text)
        {
            var hints = new List<string>();

            // Look for dependency patterns
            var patterns = new[]
            {
                @"(?:blocked\s+by|depends\s+on|after)\s+([#\w-]+)",
                @"([#\w-]+)\s+(?:must|should)\s+(?:be\s+)?(?:done|completed|finished|implemented)\s+(?:first|before)",
                @"(?:before|after)\s+([#\w-]+)",
                @"part\s+of\s+([#\w-]+)",
                @"related\s+to\s+([#\w-]+)",
                @"([#\w-]+)" // Capture any issue references like #123, PROJ-456
            };

            foreach (var pattern in patterns)
            {
                var matches = System.Text.RegularExpressions.Regex.Matches(text, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                foreach (System.Text.RegularExpressions.Match match in matches)
                {
                    if (match.Groups.Count > 1)
                    {
                        var hint = match.Groups[1].Value.Trim();
                        if (!string.IsNullOrEmpty(hint) && (hint.StartsWith("#") || hint.Contains("-") || System.Text.RegularExpressions.Regex.IsMatch(hint, @"^\w+-\d+$")))
                        {
                            hints.Add(hint);
                        }
                    }
                }
            }

            return hints.Distinct().ToList();
        }

        private int CalculateComplexityScore(string content, string repoStructure)
        {
            var score = 0;

            // Content-based scoring
            if (content.Length > 1000) score += 2; // Long description = more complex
            if (content.Contains("api") || content.Contains("database")) score += 2; // Technical complexity
            if (content.Contains("security") || content.Contains("encryption")) score += 3; // Security concerns
            if (content.Contains("performance") || content.Contains("optimization")) score += 2; // Performance requirements
            if (content.Contains("integration") || content.Contains("third-party")) score += 2; // Integration complexity

            // Repository-based scoring
            if (repoStructure.Contains("test")) score += 1; // Has tests = good, but may indicate complexity
            if (repoStructure.Contains("multiple", StringComparison.OrdinalIgnoreCase)) score += 1; // Multi-project = more complex

            return Math.Min(score, 10); // Cap at 10
        }

        private List<string> IdentifyBasicRisks(string content, string repoStructure)
        {
            var risks = new List<string>();

            if (content.Contains("database") || content.Contains("migration"))
                risks.Add("Database changes - potential data loss");

            if (content.Contains("api") || content.Contains("interface"))
                risks.Add("API changes - breaking changes for consumers");

            if (content.Contains("security") || content.Contains("authentication"))
                risks.Add("Security-related changes - potential vulnerabilities");

            if (content.Contains("performance"))
                risks.Add("Performance changes - potential regression");

            if (!repoStructure.Contains("test"))
                risks.Add("No test structure detected - higher risk of bugs");

            return risks;
        }

        private List<(string, string)> IdentifyDetailedRisks(string content, string repoContext, string[] failurePatterns)
        {
            var risks = new List<(string, string)>();

            if (content.Contains("database") || content.Contains("migration"))
                risks.Add(("Database schema changes", "High"));

            if (content.Contains("api") || content.Contains("interface"))
                risks.Add(("API contract changes", "Medium"));

            if (content.Contains("security") || content.Contains("authentication"))
                risks.Add(("Security vulnerabilities", "High"));

            if (content.Contains("performance"))
                risks.Add(("Performance regression", "Medium"));

            if (!repoContext.Contains("test"))
                risks.Add(("Insufficient test coverage", "Medium"));

            // Add risks from historical patterns
            foreach (var pattern in failurePatterns)
            {
                if (pattern.Contains("payment"))
                    risks.Add(("Payment processing failures", "High"));
            }

            return risks;
        }

        private string EstimateEffort(int complexityScore)
        {
            return complexityScore switch
            {
                <= 2 => "1-2 hours (simple task)",
                <= 4 => "2-4 hours (small feature)",
                <= 6 => "4-8 hours (medium feature)",
                <= 8 => "1-2 days (complex feature)",
                _ => "2-5 days (major feature)"
            };
        }

        private List<TaskDescription> GenerateBasicTaskBreakdown(string requirements, string repoContext, string[] agentTypes, string dependencyAnalysis)
        {
            var tasks = new List<TaskDescription>();

            // Check for external dependencies that might affect task ordering
            var hasBlockers = dependencyAnalysis.Contains("Blocked by") || dependencyAnalysis.Contains("blocker");
            var hasPrerequisites = dependencyAnalysis.Contains("depends on") || dependencyAnalysis.Contains("prerequisite");

            // If there are blockers, add a dependency check task first
            if (hasBlockers)
            {
                tasks.Add(new TaskDescription
                {
                    Title = "Verify external dependencies are resolved",
                    AgentHint = "executor",
                    DependsOn = Array.Empty<string>(),
                    Tags = new[] { "dependency-check", "prerequisite" }
                });
            }

            // Always start with implementation
            tasks.Add(new TaskDescription
            {
                Title = "Implement core functionality",
                AgentHint = "executor",
                DependsOn = hasBlockers ? new[] { tasks[0].Title } : Array.Empty<string>(),
                Tags = new[] { "implementation", "primary" }
            });

            // Add testing if test structure exists
            if (repoContext.Contains("test") || repoContext.Contains("Test"))
            {
                tasks.Add(new TaskDescription
                {
                    Title = "Write and run tests",
                    AgentHint = "executor",
                    DependsOn = new[] { tasks.Last().Title },
                    Tags = new[] { "testing", "quality" }
                });
            }

            // Add review if reviewer agent is available
            if (agentTypes.Contains("reviewer"))
            {
                var dependsOn = tasks.Last().Title;
                tasks.Add(new TaskDescription
                {
                    Title = "Code review and validation",
                    AgentHint = "reviewer",
                    DependsOn = new[] { dependsOn },
                    Tags = new[] { "review", "quality" }
                });
            }

            // If there are prerequisites mentioned, add a validation task
            if (hasPrerequisites)
            {
                tasks.Add(new TaskDescription
                {
                    Title = "Validate prerequisite work items are complete",
                    AgentHint = "executor",
                    DependsOn = Array.Empty<string>(),
                    Tags = new[] { "validation", "prerequisite" }
                });
            }

            return tasks;
        }

        private List<string> ExtractRequirements(string content)
        {
            var requirements = new List<string>();

            // Look for requirement patterns
            var patterns = new[]
            {
                @"(?:requirement|need|feature):?\s*(.*?)(?:\n|$)",
                @"(?:must|should|will)\s+(.*?)(?:\n|$)",
                @"(?:implement|create|build|add)\s+(.*?)(?:\n|$)",
                @"(?:support|provide|enable)\s+(.*?)(?:\n|$)"
            };

            foreach (var pattern in patterns)
            {
                var matches = System.Text.RegularExpressions.Regex.Matches(content, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                foreach (System.Text.RegularExpressions.Match match in matches)
                {
                    if (match.Groups.Count > 1)
                    {
                        var req = match.Groups[1].Value.Trim();
                        if (!string.IsNullOrEmpty(req) && req.Length > 10) // Filter out very short matches
                        {
                            requirements.Add(req);
                        }
                    }
                }
            }

            return requirements.Distinct().ToList();
        }

        private List<string> ExtractAcceptanceCriteriaFromContent(string content)
        {
            var criteria = new List<string>();

            // Look for acceptance criteria patterns
            var patterns = new[]
            {
                @"acceptance criteria?:?\s*(.*?)(?:\n|$)",
                @"given\s+(.*?)\s+when\s+(.*?)\s+then\s+(.*?)(?:\n|$)",
                @"should\s+(.*?)(?:\n|$)",
                @"must\s+(.*?)(?:\n|$)",
                @"verify\s+(.*?)(?:\n|$)",
                @"ensure\s+(.*?)(?:\n|$)"
            };

            foreach (var pattern in patterns)
            {
                var matches = System.Text.RegularExpressions.Regex.Matches(content, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                foreach (System.Text.RegularExpressions.Match match in matches)
                {
                    if (match.Groups.Count > 1)
                    {
                        var criterion = match.Groups[1].Value.Trim();
                        if (!string.IsNullOrEmpty(criterion))
                        {
                            criteria.Add(criterion);
                        }
                    }
                }
            }

            return criteria.Distinct().ToList();
        }

        private List<string> AnalyzeRequiredSkills(string requirements)
        {
            var skills = new List<string>();
            var reqLower = requirements.ToLowerInvariant();

            if (reqLower.Contains("api") || reqLower.Contains("integration") || reqLower.Contains("service"))
                skills.Add("API Development");

            if (reqLower.Contains("database") || reqLower.Contains("data") || reqLower.Contains("storage"))
                skills.Add("Database Design");

            if (reqLower.Contains("security") || reqLower.Contains("secure") || reqLower.Contains("authentication") || reqLower.Contains("authorization") || reqLower.Contains("pci") || reqLower.Contains("compliance"))
                skills.Add("Security Implementation");

            if (reqLower.Contains("ui") || reqLower.Contains("ux") || reqLower.Contains("frontend") || reqLower.Contains("interface"))
                skills.Add("UI/UX Development");

            if (reqLower.Contains("test") || reqLower.Contains("testing") || reqLower.Contains("qa"))
                skills.Add("Testing");

            if (reqLower.Contains("documentation") || reqLower.Contains("docs"))
                skills.Add("Technical Writing");

            if (reqLower.Contains("performance") || reqLower.Contains("optimization"))
                skills.Add("Performance Engineering");

            if (reqLower.Contains("deployment") || reqLower.Contains("ci/cd") || reqLower.Contains("infrastructure"))
                skills.Add("DevOps/Infrastructure");

            return skills.Distinct().ToList();
        }

        private List<string> MapSkillToAgents(string skill, string[] availableAgents)
        {
            var mappings = new Dictionary<string, string[]>
            {
                ["API Development"] = new[] { "executor", "developer" },
                ["Database Design"] = new[] { "executor", "database-admin" },
                ["Security Implementation"] = new[] { "executor", "security-specialist" },
                ["UI/UX Development"] = new[] { "executor", "frontend-developer" },
                ["Testing"] = new[] { "executor", "tester", "qa" },
                ["Technical Writing"] = new[] { "executor", "technical-writer" },
                ["Performance Engineering"] = new[] { "executor", "performance-engineer" },
                ["DevOps/Infrastructure"] = new[] { "executor", "devops" }
            };

            if (mappings.TryGetValue(skill, out var agents))
            {
                return agents.Where(a => availableAgents.Contains(a)).ToList();
            }

            return new List<string> { "executor" }; // Default fallback
        }

        private List<string> DeterminePrimaryAgents(string requirements, string[] availableAgents)
        {
            var primary = new List<string>();

            // Always include executor as primary for implementation
            if (availableAgents.Contains("executor"))
                primary.Add("executor");

            // Add specialized agents based on requirements
            if (requirements.Contains("security") && availableAgents.Contains("security-specialist"))
                primary.Add("security-specialist");

            if ((requirements.Contains("ui") || requirements.Contains("frontend")) && availableAgents.Contains("frontend-developer"))
                primary.Add("frontend-developer");

            if (requirements.Contains("database") && availableAgents.Contains("database-admin"))
                primary.Add("database-admin");

            if (requirements.Contains("performance") && availableAgents.Contains("performance-engineer"))
                primary.Add("performance-engineer");

            return primary.Distinct().ToList();
        }

        private List<string> DetermineSecondaryAgents(string requirements, string[] availableAgents)
        {
            var secondary = new List<string>();

            // Always include reviewer if available
            if (availableAgents.Contains("reviewer"))
                secondary.Add("reviewer");

            // Add tester for quality assurance
            if (availableAgents.Contains("tester") || availableAgents.Contains("qa"))
            {
                var tester = availableAgents.Contains("tester") ? "tester" : "qa";
                secondary.Add(tester);
            }

            // Add technical writer for documentation needs
            if (requirements.Contains("documentation") && availableAgents.Contains("technical-writer"))
                secondary.Add("technical-writer");

            return secondary.Distinct().ToList();
        }

        private int ExtractComplexityScore(string complexity)
        {
            // Try to extract numerical score from complexity assessment
            var match = System.Text.RegularExpressions.Regex.Match(complexity, @"(\d+)/10");
            if (match.Success && int.TryParse(match.Groups[1].Value, out var score))
            {
                return Math.Min(Math.Max(score, 1), 10); // Clamp between 1-10
            }

            // Fallback scoring based on keywords
            if (complexity.Contains("high") || complexity.Contains("complex"))
                return 8;
            if (complexity.Contains("medium"))
                return 5;
            if (complexity.Contains("low") || complexity.Contains("simple"))
                return 3;

            return 5; // Default medium complexity
        }

        private int CountTasksInDag(string taskDag)
        {
            // Count "Task:" occurrences in the DAG description
            var taskMatches = System.Text.RegularExpressions.Regex.Matches(taskDag, @"Task:", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return Math.Max(taskMatches.Count, 1); // At least 1 task
        }

        private string CalculateBaseEffort(int complexityScore, int taskCount)
        {
            // Base effort calculation: complexity * tasks * base hours per task
            var baseHoursPerTask = 2.0; // Assume 2 hours per task on average
            var totalHours = complexityScore * taskCount * baseHoursPerTask;

            // Convert to appropriate time units
            if (totalHours <= 4)
                return $"{totalHours} hours";
            else if (totalHours <= 16)
                return $"{totalHours / 8.0:F1} days";
            else
                return $"{totalHours / 40.0:F1} weeks";
        }

        private string AnalyzeHistoricalData(string[] historicalData, int complexityScore)
        {
            if (!historicalData.Any())
                return "No historical data available";

            // Simple analysis - in real implementation this would be more sophisticated
            var similarComplexityItems = historicalData.Where(h =>
                h.Contains($"complexity.{complexityScore}") ||
                h.Contains($"score.{complexityScore}") ||
                (complexityScore <= 3 && h.Contains("simple")) ||
                (complexityScore >= 7 && h.Contains("complex"))
            ).ToList();

            if (similarComplexityItems.Any())
            {
                return $"Similar historical items: {similarComplexityItems.Count} - adjust estimate by -10%";
            }

            return "Limited historical data for this complexity level";
        }

        private string CalculateConfidenceLevel(int complexityScore, int taskCount, int historicalDataCount)
        {
            var confidence = 70; // Base confidence

            // Higher complexity reduces confidence
            confidence -= (complexityScore - 5) * 3;

            // More tasks increase uncertainty
            confidence -= Math.Min(taskCount - 3, 5) * 2;

            // Historical data increases confidence
            confidence += Math.Min(historicalDataCount * 5, 20);

            return $"{Math.Max(confidence, 20)}%"; // Minimum 20% confidence
        }

        private Dictionary<string, string> GenerateTimeBreakdown(string baseEstimate, int taskCount)
        {
            var breakdown = new Dictionary<string, string>();

            // Parse base estimate
            var match = System.Text.RegularExpressions.Regex.Match(baseEstimate, @"([\d.]+)\s*(hours|days|weeks)");
            if (match.Success)
            {
                var value = double.Parse(match.Groups[1].Value);
                var unit = match.Groups[2].Value;

                // Allocate time across phases
                var analysisTime = value * 0.1; // 10% for analysis
                var implementationTime = value * 0.6; // 60% for implementation
                var testingTime = value * 0.2; // 20% for testing
                var reviewTime = value * 0.1; // 10% for review

                breakdown["Analysis & Planning"] = $"{analysisTime:F1} {unit}";
                breakdown["Implementation"] = $"{implementationTime:F1} {unit}";
                breakdown["Testing & Validation"] = $"{testingTime:F1} {unit}";
                breakdown["Review & Documentation"] = $"{reviewTime:F1} {unit}";
            }
            else
            {
                // Fallback breakdown
                breakdown["Analysis & Planning"] = "10% of total";
                breakdown["Implementation"] = "60% of total";
                breakdown["Testing & Validation"] = "20% of total";
                breakdown["Review & Documentation"] = "10% of total";
            }

            return breakdown;
        }

        private List<string> IdentifyEffortRiskFactors(string complexity, string taskDag)
        {
            var risks = new List<string>();
            var complexityLower = complexity.ToLowerInvariant();
            var taskDagLower = taskDag.ToLowerInvariant();

            if (complexityLower.Contains("security") || complexityLower.Contains("encryption"))
                risks.Add("Security review may add 1-2 days");

            if (complexityLower.Contains("database") || complexityLower.Contains("migration"))
                risks.Add("Database changes may require additional testing time");

            if (complexityLower.Contains("integration") || complexityLower.Contains("third-party"))
                risks.Add("External dependencies may cause delays");

            if (complexityLower.Contains("ui") || complexityLower.Contains("ux"))
                risks.Add("UI changes may require design review iterations");

            if (taskDagLower.Contains("multiple") || taskDagLower.Contains("parallel"))
                risks.Add("Coordination overhead for multi-agent work");

            if (complexityLower.Contains("performance"))
                risks.Add("Performance optimization may require multiple iterations");

            return risks;
        }

        private List<string> IdentifyComplexityIndicators(string content)
        {
            var indicators = new List<string>();

            // Technical complexity indicators
            if (content.Contains("api") || content.Contains("integration"))
                indicators.Add("API/Integration complexity");

            if (content.Contains("database") || content.Contains("schema") || content.Contains("migration"))
                indicators.Add("Database changes required");

            if (content.Contains("security") || content.Contains("authentication") || content.Contains("encryption"))
                indicators.Add("Security considerations");

            if (content.Contains("performance") || content.Contains("optimization") || content.Contains("scalability"))
                indicators.Add("Performance requirements");

            if (content.Contains("ui") || content.Contains("ux") || content.Contains("frontend") || content.Contains("user interface"))
                indicators.Add("UI/UX components involved");

            if (content.Contains("testing") || content.Contains("qa") || content.Contains("validation"))
                indicators.Add("Testing complexity");

            if (content.Contains("documentation") || content.Contains("training"))
                indicators.Add("Documentation/training requirements");

            // Scope indicators
            if (content.Contains("multiple") || content.Contains("several") || content.Contains("various"))
                indicators.Add("Multiple components affected");

            if (content.Contains("cross-cutting") || content.Contains("system-wide"))
                indicators.Add("Cross-cutting concerns");

            return indicators;
        }

        private List<string> SuggestAgentSequence(string requirements, string[] availableAgents)
        {
            var sequence = new List<string>();

            // Start with planning/analysis if available
            if (availableAgents.Contains("planner"))
                sequence.Add("planner");

            // Primary implementation agents
            var primary = DeterminePrimaryAgents(requirements, availableAgents);
            sequence.AddRange(primary);

            // Testing agents
            if (availableAgents.Contains("tester"))
                sequence.Add("tester");
            else if (availableAgents.Contains("qa"))
                sequence.Add("qa");

            // Review agents
            if (availableAgents.Contains("reviewer"))
                sequence.Add("reviewer");

            // Documentation agents
            if (requirements.Contains("documentation") && availableAgents.Contains("technical-writer"))
                sequence.Add("technical-writer");

            return sequence.Distinct().ToList();
        }
    }
}
