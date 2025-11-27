using System.Text.RegularExpressions;
using JuniorDev.Contracts;
using JuniorDev.Orchestrator;
using Microsoft.Extensions.Logging;

namespace JuniorDev.Agents;

/// <summary>
/// Result of a work item claim attempt.
/// </summary>
public enum ClaimResult
{
    Success,
    AlreadyClaimed,
    Rejected,
    NetworkError,
    UnknownError
}

/// <summary>
/// Utilities for claiming work items and managing related operations.
/// </summary>
public class ClaimUtilities
{
    private readonly AgentSessionContext _context;

    public ClaimUtilities(AgentSessionContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <summary>
    /// Attempts to claim a work item with retry logic and collision handling.
    /// </summary>
    public async Task<ClaimResult> TryClaimWorkItemAsync(WorkItemRef workItem, string assignee)
    {
        var maxRetries = _context.AgentConfig.MaxRetryAttempts;
        var baseDelay = _context.AgentConfig.RetryBaseDelayMs;

        for (var attempt = 1; attempt <= maxRetries + 1; attempt++)
        {
            var result = await TryClaimWorkItemOnceAsync(workItem, assignee);

            // Don't retry for permanent failures
            if (result == ClaimResult.AlreadyClaimed || result == ClaimResult.Rejected || result == ClaimResult.NetworkError)
            {
                return result;
            }

            // Success
            if (result == ClaimResult.Success)
            {
                return result;
            }

            // For retryable errors, wait before next attempt
            if (attempt <= maxRetries)
            {
                var delay = baseDelay * (int)Math.Pow(2, attempt - 1) + Random.Shared.Next(0, 100);
                _context.Logger.LogInformation("Claim attempt {Attempt} failed for work item {WorkItemId}, retrying in {Delay}ms",
                    attempt, workItem.Id, delay);
                await Task.Delay(delay);
            }
        }

        return ClaimResult.UnknownError;
    }

    private async Task<ClaimResult> TryClaimWorkItemOnceAsync(WorkItemRef workItem, string assignee)
    {
        try
        {
            // First, try to set assignee
            var assignCommand = new SetAssignee(
                Guid.NewGuid(),
                _context.CreateCorrelation(),
                workItem,
                assignee);

            await _context.SessionManager.PublishCommand(assignCommand);

            // Then transition to In Progress
            var transitionCommand = new TransitionTicket(
                Guid.NewGuid(),
                _context.CreateCorrelation(),
                workItem,
                "In Progress");

            await _context.SessionManager.PublishCommand(transitionCommand);

            // Add a comment indicating the claim
            var commentCommand = new Comment(
                Guid.NewGuid(),
                _context.CreateCorrelation(),
                workItem,
                $"Claimed by {assignee}");

            await _context.SessionManager.PublishCommand(commentCommand);

            return ClaimResult.Success;
        }
        catch (Exception ex)
        {
            // Analyze the exception to determine the result type
            var message = ex.Message.ToLowerInvariant();
            if (message.Contains("already") || message.Contains("assigned") || message.Contains("conflict") || message.Contains("claimed"))
            {
                _context.Logger.LogInformation("Work item {WorkItemId} already claimed", workItem.Id);
                return ClaimResult.AlreadyClaimed;
            }
            else if (message.Contains("reject") || message.Contains("denied") || message.Contains("not allowed") || message.Contains("policy"))
            {
                _context.Logger.LogWarning(ex, "Claim rejected for work item {WorkItemId}", workItem.Id);
                return ClaimResult.Rejected;
            }
            else if (message.Contains("network") || message.Contains("timeout") || message.Contains("connection") || message.Contains("unreachable") || message.Contains("Network"))
            {
                _context.Logger.LogWarning(ex, "Network error during claim of work item {WorkItemId}", workItem.Id);
                return ClaimResult.NetworkError;
            }
            else
            {
                _context.Logger.LogWarning(ex, "Unknown error during claim of work item {WorkItemId}: {Message}", workItem.Id, ex.Message);
                return ClaimResult.UnknownError;
            }
        }
    }

    /// <summary>
    /// Generates a branch name for a work item, respecting protected branches.
    /// Uses work item ID and title for better readability.
    /// </summary>
    public string GenerateBranchName(WorkItemRef workItem, IEnumerable<string> protectedBranches, string? title = null)
    {
        // Create a more descriptive branch name using ID and title
        var titleSlug = string.IsNullOrWhiteSpace(title) ? workItem.Id : $"{workItem.Id}-{Slugify(title)}";
        var baseName = $"feature/{titleSlug}";

        // Ensure it doesn't conflict with protected branches and limit length
        var finalName = baseName;
        var counter = 1;

        while (protectedBranches.Contains(finalName) || finalName.Length > 100)
        {
            if (finalName.Length > 100)
            {
                // Truncate and try again
                var truncatedTitle = titleSlug.Substring(0, Math.Max(10, titleSlug.Length - 10));
                finalName = $"feature/{truncatedTitle}";
                if (counter > 1)
                {
                    finalName += $"-{counter}";
                }
            }
            else
            {
                finalName = $"{baseName}-{counter}";
            }

            counter++;
            if (counter > 10) // Prevent infinite loops
            {
                finalName = $"feature/{workItem.Id}-{Guid.NewGuid().ToString("N").Substring(0, 8)}";
                break;
            }
        }

        return finalName;
    }

    /// <summary>
    /// Creates a branch for a work item and sets it up for development.
    /// </summary>
    public async Task<string> CreateWorkItemBranchAsync(WorkItemRef workItem, RepoRef repo)
    {
        var branchName = GenerateBranchName(workItem, _context.Config.Policy.ProtectedBranches ?? Array.Empty<string>());

        var command = new CreateBranch(
            Guid.NewGuid(),
            _context.CreateCorrelation(),
            repo,
            branchName);

        await _context.SessionManager.PublishCommand(command);

        return branchName;
    }

    private static string Slugify(string text)
    {
        // Convert to lowercase and replace non-alphanumeric with hyphens
        var slug = Regex.Replace(text.ToLowerInvariant(), @"[^a-z0-9]+", "-");

        // Remove leading/trailing hyphens
        slug = slug.Trim('-');

        // Limit length
        if (slug.Length > 50)
        {
            slug = slug.Substring(0, 50).TrimEnd('-');
        }

        return slug;
    }
}