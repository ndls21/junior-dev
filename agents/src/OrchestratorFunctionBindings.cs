using System.ComponentModel;
using System.Text.Json;
using JuniorDev.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace JuniorDev.Agents;

/// <summary>
/// Semantic Kernel function bindings for orchestrator commands.
/// </summary>
public class OrchestratorFunctionBindings
{
    private readonly AgentSessionContext _context;

    public OrchestratorFunctionBindings(AgentSessionContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <summary>
    /// Helper method to handle dry-run mode for commands.
    /// </summary>
    private async Task<string> ExecuteOrDryRunAsync(ICommand command, string successMessage, string? dryRunMessage = null)
    {
        if (_context.AgentConfig.DryRun)
        {
            var message = dryRunMessage ?? $"[DRY RUN] Would execute {command.Kind}";
            _context.Logger.LogInformation("Dry run: {Message}", message);
            return message;
        }

        await _context.SessionManager.PublishCommand(command);
        return successMessage;
    }

    /// <summary>
    /// Registers all orchestrator command functions with the kernel.
    /// </summary>
    public void RegisterFunctions(Kernel kernel)
    {
        if (kernel == null)
        {
            throw new ArgumentNullException(nameof(kernel));
        }

        // Register plugins, ignoring if already registered
        try
        {
            kernel.Plugins.AddFromObject(this, "vcs");
        }
        catch (ArgumentException ex) when (ex.Message.Contains("An item with the same key has already been added"))
        {
            // Plugin already registered, ignore
        }

        try
        {
            kernel.Plugins.AddFromObject(this, "workitems");
        }
        catch (ArgumentException ex) when (ex.Message.Contains("An item with the same key has already been added"))
        {
            // Plugin already registered, ignore
        }

        try
        {
            kernel.Plugins.AddFromObject(this, "general");
        }
        catch (ArgumentException ex) when (ex.Message.Contains("An item with the same key has already been added"))
        {
            // Plugin already registered, ignore
        }
    }

    #region VCS Functions

    [KernelFunction("create_branch")]
    [Description("Creates a new git branch from the specified reference.")]
    public async Task<string> CreateBranchAsync(
        [Description("The repository name")] string repoName,
        [Description("The branch name to create")] string branchName,
        [Description("The reference to branch from (optional)")] string? fromRef = null)
    {
        var repo = new RepoRef(repoName, $"/repos/{repoName}");
        var command = new CreateBranch(
            Guid.NewGuid(),
            _context.CreateCorrelation(),
            repo,
            branchName,
            fromRef);

        return await ExecuteOrDryRunAsync(
            command,
            $"Created branch '{branchName}' command issued",
            $"[DRY RUN] Would create branch '{branchName}' from '{fromRef ?? "HEAD"}' in repository '{repoName}'");
    }

    [KernelFunction("apply_patch")]
    [Description("Applies a patch to the repository.")]
    public async Task<string> ApplyPatchAsync(
        [Description("The repository name")] string repoName,
        [Description("The patch content to apply")] string patchContent)
    {
        var repo = new RepoRef(repoName, $"/repos/{repoName}");
        var command = new ApplyPatch(
            Guid.NewGuid(),
            _context.CreateCorrelation(),
            repo,
            patchContent);

        return await ExecuteOrDryRunAsync(
            command,
            "Patch application command issued",
            $"[DRY RUN] Would apply patch to repository '{repoName}'");
    }

    [KernelFunction("run_tests")]
    [Description("Runs tests in the repository.")]
    public async Task<string> RunTestsAsync(
        [Description("The repository name")] string repoName,
        [Description("Test filter (optional)")] string? filter = null,
        [Description("Timeout in seconds (optional)")] int? timeoutSeconds = null)
    {
        var repo = new RepoRef(repoName, $"/repos/{repoName}");
        var timeout = timeoutSeconds.HasValue ? (TimeSpan?)TimeSpan.FromSeconds(timeoutSeconds.Value) : null;

        var command = new RunTests(
            Guid.NewGuid(),
            _context.CreateCorrelation(),
            repo,
            filter,
            timeout);

        return await ExecuteOrDryRunAsync(
            command,
            "Test execution command issued",
            $"[DRY RUN] Would run tests{(string.IsNullOrEmpty(filter) ? "" : $" with filter '{filter}'")} in repository '{repoName}'{(timeout.HasValue ? $" (timeout: {timeout.Value.TotalSeconds}s)" : "")}");
    }

    [KernelFunction("build_project")]
    [Description("Builds a .NET project or solution.")]
    public async Task<string> BuildProjectAsync(
        [Description("The repository name")] string repoName,
        [Description("The project/solution path")] string projectPath,
        [Description("Build configuration (optional)")] string? configuration = null,
        [Description("Target framework (optional)")] string? targetFramework = null,
        [Description("Build targets (optional, comma-separated)")] string? targets = null,
        [Description("Timeout in seconds (optional)")] int? timeoutSeconds = null)
    {
        var repo = new RepoRef(repoName, $"/repos/{repoName}");
        var targetList = string.IsNullOrEmpty(targets) ? null : targets.Split(',').Select(t => t.Trim()).ToArray();
        var timeout = timeoutSeconds.HasValue ? (TimeSpan?)TimeSpan.FromSeconds(timeoutSeconds.Value) : null;

        var command = new BuildProject(
            Guid.NewGuid(),
            _context.CreateCorrelation(),
            repo,
            projectPath,
            configuration,
            targetFramework,
            targetList,
            timeout);

        return await ExecuteOrDryRunAsync(
            command,
            "Build command issued",
            $"[DRY RUN] Would build '{projectPath}'{(string.IsNullOrEmpty(configuration) ? "" : $" with configuration '{configuration}'")}{(string.IsNullOrEmpty(targetFramework) ? "" : $" for framework '{targetFramework}'")}{(string.IsNullOrEmpty(targets) ? "" : $" with targets '{targets}'")} in repository '{repoName}'{(timeout.HasValue ? $" (timeout: {timeout.Value.TotalSeconds}s)" : "")}");
    }

    [KernelFunction("commit")]
    [Description("Commits changes to the repository.")]
    public async Task<string> CommitAsync(
        [Description("The repository name")] string repoName,
        [Description("The commit message")] string message,
        [Description("Whether to amend the last commit")] bool amend = false)
    {
        var repo = new RepoRef(repoName, $"/repos/{repoName}");
        var command = new Commit(
            Guid.NewGuid(),
            _context.CreateCorrelation(),
            repo,
            message,
            Array.Empty<string>(),
            amend);

        return await ExecuteOrDryRunAsync(
            command,
            "Commit command issued",
            $"[DRY RUN] Would commit changes with message '{message}'{(amend ? " (amending)" : "")} in repository '{repoName}'");
    }

    [KernelFunction("push")]
    [Description("Pushes changes to the remote repository.")]
    public async Task<string> PushAsync(
        [Description("The repository name")] string repoName,
        [Description("The branch to push")] string branchName)
    {
        var repo = new RepoRef(repoName, $"/repos/{repoName}");
        var command = new Push(
            Guid.NewGuid(),
            _context.CreateCorrelation(),
            repo,
            branchName);

        return await ExecuteOrDryRunAsync(
            command,
            "Push command issued",
            $"[DRY RUN] Would push branch '{branchName}' to remote in repository '{repoName}'");
    }

    [KernelFunction("get_diff")]
    [Description("Gets the diff for the repository.")]
    public async Task<string> GetDiffAsync(
        [Description("The repository name")] string repoName,
        [Description("The reference to diff against (optional)")] string? refName = null)
    {
        var repo = new RepoRef(repoName, $"/repos/{repoName}");
        var command = new GetDiff(
            Guid.NewGuid(),
            _context.CreateCorrelation(),
            repo,
            refName ?? "HEAD");

        return await ExecuteOrDryRunAsync(
            command,
            "Diff retrieval command issued",
            $"[DRY RUN] Would get diff against '{refName ?? "HEAD"}' in repository '{repoName}'");
    }

    #endregion

    #region Work Item Functions

    [KernelFunction("list_backlog")]
    [Description("Lists work items in the backlog.")]
    public async Task<string> ListBacklogAsync(
        [Description("Optional filter for work items")] string? filter = null)
    {
        var command = new QueryBacklog(
            Guid.NewGuid(),
            _context.CreateCorrelation(),
            filter);

        return await ExecuteOrDryRunAsync(
            command,
            "Backlog query initiated - results will be emitted as BacklogQueried event",
            $"[DRY RUN] Would query backlog{(string.IsNullOrEmpty(filter) ? "" : $" with filter '{filter}'")}");
    }

    [KernelFunction("get_item")]
    [Description("Gets details of a specific work item.")]
    public async Task<string> GetItemAsync(
        [Description("The work item ID")] string itemId)
    {
        var workItem = new WorkItemRef(itemId);
        var command = new QueryWorkItem(
            Guid.NewGuid(),
            _context.CreateCorrelation(),
            workItem);

        return await ExecuteOrDryRunAsync(
            command,
            $"Work item {itemId} query initiated - results will be emitted as WorkItemQueried event",
            $"[DRY RUN] Would query work item '{itemId}'");
    }

    [KernelFunction("claim_item")]
    [Description("Attempts to claim a work item by setting assignee and transitioning to In Progress.")]
    public async Task<string> ClaimItemAsync(
        [Description("The work item ID")] string itemId)
    {
        var workItem = new WorkItemRef(itemId);
        var assignee = _context.AgentConfig.AgentProfile ?? "agent";

        var claimUtil = new ClaimUtilities(_context);
        var result = await claimUtil.TryClaimWorkItemAsync(workItem, assignee);

        return result switch
        {
            ClaimResult.Success => $"Successfully claimed work item {itemId}",
            ClaimResult.AlreadyClaimed => $"Work item {itemId} is already claimed by another agent",
            ClaimResult.Rejected => $"Claim rejected for work item {itemId}",
            _ => $"Failed to claim work item {itemId}"
        };
    }

    [KernelFunction("comment")]
    [Description("Adds a comment to a work item.")]
    public async Task<string> CommentAsync(
        [Description("The work item ID")] string itemId,
        [Description("The comment text")] string comment)
    {
        var workItem = new WorkItemRef(itemId);
        var command = new Comment(
            Guid.NewGuid(),
            _context.CreateCorrelation(),
            workItem,
            comment);

        return await ExecuteOrDryRunAsync(
            command,
            $"Comment added to work item {itemId}",
            $"[DRY RUN] Would add comment '{comment}' to work item {itemId}");
    }

    [KernelFunction("transition")]
    [Description("Transitions a work item to a new state.")]
    public async Task<string> TransitionAsync(
        [Description("The work item ID")] string itemId,
        [Description("The new state")] string newState)
    {
        var workItem = new WorkItemRef(itemId);
        var command = new TransitionTicket(
            Guid.NewGuid(),
            _context.CreateCorrelation(),
            workItem,
            newState);

        return await ExecuteOrDryRunAsync(
            command,
            $"Transitioned work item {itemId} to {newState}",
            $"[DRY RUN] Would transition work item {itemId} to state '{newState}'");
    }

    #endregion

    #region General Functions

    [KernelFunction("upload_artifact")]
    [Description("Uploads an artifact.")]
    public async Task<string> UploadArtifactAsync(
        [Description("The artifact name")] string name,
        [Description("The content type")] string contentType,
        [Description("The content as base64")] string contentBase64)
    {
        var content = Convert.FromBase64String(contentBase64);
        var command = new UploadArtifact(
            Guid.NewGuid(),
            _context.CreateCorrelation(),
            name,
            contentType,
            content);

        return await ExecuteOrDryRunAsync(
            command,
            $"Uploaded artifact {name}",
            $"[DRY RUN] Would upload artifact '{name}' ({contentType})");
    }

    [KernelFunction("request_approval")]
    [Description("Requests approval for an action.")]
    public async Task<string> RequestApprovalAsync(
        [Description("The reason for approval")] string reason,
        [Description("Required actions")] string[] requiredActions)
    {
        var command = new RequestApproval(
            Guid.NewGuid(),
            _context.CreateCorrelation(),
            reason,
            requiredActions);

        return await ExecuteOrDryRunAsync(
            command,
            "Approval requested",
            $"[DRY RUN] Would request approval for: {reason} (actions: {string.Join(", ", requiredActions)})");
    }

    #endregion

}
