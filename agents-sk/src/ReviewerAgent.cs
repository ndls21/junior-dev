using System.ComponentModel;
using JuniorDev.Agents;
using JuniorDev.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace JuniorDev.Agents.Sk;

/// <summary>
/// Reviewer agent that consumes artifacts and provides review feedback to work items.
/// Never emits VCS commands, only work-item operations.
/// </summary>
///
/// TODOs / Known Gaps:
/// - Replace deterministic diff/log/test heuristics with Semantic Kernel / LLM analysis (issues #8/#9).
/// - Event filtering is currently session-only; consider cross-session artifact routing or queued review processing (issue #6).
/// - Reviewer currently assumes a work item is present; add queueing or deferred review when work item is linked.
/// - Several methods are `async` but lack `await` (warning CS1998). Consider making them synchronous or adding awaited async work.
/// - ExecutorAgent has nullable dereference warnings; follow-up refactor needed to tighten nullability (see `ExecutorAgent.cs`).

public class ReviewerAgent : AgentBase
{
    private readonly Kernel _kernel;
    private OrchestratorFunctionBindings? _functionBindings;

    // Track reviewed artifacts to avoid duplicate reviews
    private readonly HashSet<string> _reviewedArtifacts = new();

    public override string AgentType => "reviewer";

    public ReviewerAgent(Kernel kernel)
    {
        _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
    }

    // Centralized review transition states so they are not hardcoded in multiple places
    private static class ReviewStates
    {
        public const string ReadyForQA = "Ready for QA";
        public const string NeedsReview = "Needs Review";
    }

    protected override Task OnStartedAsync()
    {
        // Initialize SK function bindings now that Context is available
        _functionBindings = new OrchestratorFunctionBindings(Context!);
        _functionBindings.RegisterFunctions(_kernel);

        // Register this agent's functions as SK functions
        RegisterAgentFunctions();

        Logger.LogInformation("Reviewer agent started for session {SessionId}", Context!.Config.SessionId);

        // Reviewer agents are reactive - they wait for artifacts to review
        Logger.LogInformation("Reviewer agent ready to process artifacts");

        return Task.CompletedTask;
    }

    protected override Task OnStoppedAsync()
    {
        Logger.LogInformation("Reviewer agent stopped for session {SessionId}", Context!.Config.SessionId);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Registers this agent's capabilities as Semantic Kernel functions.
    /// This makes the agent's review operations discoverable by LLMs.
    /// </summary>
    private void RegisterAgentFunctions()
    {
        try
        {
            _kernel.Plugins.AddFromObject(this, "reviewer_agent");
            Logger.LogInformation("Registered reviewer agent functions with kernel");
        }
        catch (ArgumentException ex) when (ex.Message.Contains("An item with the same key has already been added"))
        {
            Logger.LogDebug("Reviewer agent functions already registered");
        }

        // SK/LLM scaffolding: Register LLM-driven analysis functions
        try
        {
            _kernel.Plugins.AddFromObject(new ReviewAnalysisPlugin(), "review_analysis");
            Logger.LogInformation("Registered review analysis plugin with kernel");
        }
        catch (ArgumentException ex) when (ex.Message.Contains("An item with the same key has already been added"))
        {
            Logger.LogDebug("Review analysis plugin already registered");
        }
    }

    protected override async Task OnEventAsync(IEvent @event)
    {
        switch (@event)
        {
            case ArtifactAvailable artifact:
                // Gate reviewer invocation: only process artifacts when a work item is associated with the session.
                // TODO: consider queueing artifacts for later review when work item is linked (issue #6).
                if (Context?.Config?.WorkItem == null)
                {
                    Logger.LogInformation("Skipping artifact review because no work item is associated with the session");
                    return;
                }

                await HandleArtifactAvailable(artifact);
                break;
            case CommandRejected rejected:
                await HandleCommandRejected(rejected);
                break;
            case Throttled throttled:
                await HandleThrottled(throttled);
                break;
            default:
                Logger.LogDebug("Ignoring event {EventType}", @event.Kind);
                break;
        }
    }

    [KernelFunction("review_artifact")]
    [Description("Reviews an artifact and provides feedback to the associated work item.")]
    public async Task<string> ReviewArtifactKernelFunctionAsync(
        [Description("The artifact name to review")] string artifactName,
        [Description("The artifact type (Diff/Log/TestResults)")] string artifactType,
        [Description("The artifact content")] string content)
    {
        var artifact = new ArtifactAvailable(
            Id: Guid.NewGuid(),
            Correlation: new Correlation(Context!.Config.SessionId),
            Artifact: new Artifact(
                Kind: artifactType,
                Name: artifactName,
                InlineText: content));

        await HandleArtifactAvailable(artifact);
        return $"Reviewed artifact {artifactName}";
    }

    private async Task HandleArtifactAvailable(ArtifactAvailable artifact)
    {
        // Avoid reviewing the same artifact multiple times
        var artifactKey = $"{artifact.Artifact.Kind}:{artifact.Artifact.Name}";
        if (_reviewedArtifacts.Contains(artifactKey))
        {
            Logger.LogDebug("Artifact {ArtifactKey} already reviewed, skipping", artifactKey);
            return;
        }

        Logger.LogInformation("Reviewing artifact {ArtifactKey}", artifactKey);

        try
        {
            // Generate review feedback based on artifact type
            var reviewResult = await GenerateReviewAsync(artifact);

            // Only proceed if we have a work item to comment on
            if (Context!.Config.WorkItem != null)
            {
                // Post review comments to the work item
                await PostReviewCommentsAsync(Context.Config.WorkItem, reviewResult);

                // Optionally transition the work item based on review findings
                await HandleReviewTransitionAsync(Context.Config.WorkItem, reviewResult);
            }
            else
            {
                Logger.LogWarning("No work item associated with session - cannot post review comments");
            }

            // Mark artifact as reviewed
            _reviewedArtifacts.Add(artifactKey);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to review artifact {ArtifactKey}", artifactKey);

            // Surface the review failure to the work item if possible
            if (Context!.Config.WorkItem != null)
            {
                await _functionBindings!.CommentAsync(
                    Context.Config.WorkItem.Id,
                    $"Review failed for artifact {artifact.Artifact.Name}: {ex.Message}");
            }
        }
    }

    internal Task<ReviewResult> GenerateReviewAsync(ArtifactAvailable artifact)
    {
        // TODO: Replace deterministic heuristics with Semantic Kernel / LLM analysis when
        // work-item query functions and LLM bindings are available (issues #8/#9).
        return artifact.Artifact.Kind switch
        {
            "Diff" => ReviewDiffAsync(artifact),
            "Log" => ReviewLogAsync(artifact),
            "TestResults" => ReviewTestResultsAsync(artifact),
            _ => Task.FromResult(new ReviewResult
            {
                Summary = $"Unknown artifact type: {artifact.Artifact.Kind}",
                Issues = new List<string> { "Cannot review unknown artifact type" },
                Recommendations = new List<string>(),
                Status = ReviewStatus.NeedsReview
            })
        };
    }

    internal async Task<ReviewResult> ReviewDiffAsync(ArtifactAvailable artifact)
    {
        // Logger?.LogInformation("Reviewing diff artifact");

        try
        {
            // SK/LLM scaffolding: Use LLM for diff analysis
            var analysisResult = await _kernel.InvokeAsync<string>(
                "review_analysis", "analyze_diff",
                new KernelArguments { ["content"] = artifact.Artifact.InlineText ?? "" });

            // Parse the LLM response into ReviewResult
            // TODO: Implement proper parsing of LLM response
            // For scaffolding, fall back to deterministic if LLM fails
            if (!string.IsNullOrEmpty(analysisResult) && !analysisResult.Contains("placeholder"))
            {
                return ParseAnalysisToReviewResult(analysisResult, "Diff");
            }
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "LLM analysis failed, falling back to deterministic heuristics");
        }

        // Fallback to deterministic heuristics
        return await ReviewDiffDeterministicAsync(artifact);
    }

    private Task<ReviewResult> ReviewDiffDeterministicAsync(ArtifactAvailable artifact)
    {
        // NOTE: Current heuristics are simplistic and intended for golden tests.
        // TODO: Improve with SK/LLM analysis and richer parsing (issue #8).

        var issues = new List<string>();
        var recommendations = new List<string>();
        var status = ReviewStatus.ReadyForQA;

        try
        {
            var content = artifact.Artifact.InlineText ?? "";
            var lines = content.Split('\n');
            var addedLines = 0;
            var deletedLines = 0;
            var hasTests = false;
            var hasDocumentation = false;

            foreach (var line in lines)
            {
                if (line.StartsWith('+') && !line.StartsWith("+++"))
                {
                    addedLines++;
                    if (line.Contains("test", StringComparison.OrdinalIgnoreCase) ||
                        line.Contains("spec", StringComparison.OrdinalIgnoreCase))
                    {
                        hasTests = true;
                    }
                    if (line.Contains("readme", StringComparison.OrdinalIgnoreCase) ||
                        line.Contains("doc", StringComparison.OrdinalIgnoreCase) ||
                        line.Contains("///") || line.Contains("/*"))
                    {
                        hasDocumentation = true;
                    }
                }
                else if (line.StartsWith('-') && !line.StartsWith("---"))
                {
                    deletedLines++;
                }
            }

            // Basic heuristics for review
            if (addedLines == 0)
            {
                issues.Add("Diff contains no added lines - appears to be empty");
                status = ReviewStatus.NeedsReview;
            }

            if (addedLines > 100)
            {
                recommendations.Add("Large diff detected - consider breaking into smaller commits");
            }

            if (!hasTests && addedLines > 10)
            {
                issues.Add("No test changes detected in diff - consider adding tests");
                status = ReviewStatus.NeedsReview;
            }

            if (!hasDocumentation && addedLines > 50)
            {
                recommendations.Add("Consider adding documentation for significant changes");
            }

            var summary = $"Diff review: +{addedLines} -{deletedLines} lines. " +
                         $"{(hasTests ? "Tests included" : "No tests detected")}. " +
                         $"{(hasDocumentation ? "Documentation updated" : "No documentation changes")}.";

            return Task.FromResult(new ReviewResult
            {
                Summary = summary,
                Issues = issues,
                Recommendations = recommendations,
                Status = status
            });
        }
        catch (Exception ex)
        {
            // Logger?.LogError(ex, "Error analyzing diff");
            return Task.FromResult(new ReviewResult
            {
                Summary = "Failed to analyze diff content",
                Issues = new List<string> { $"Diff analysis error: {ex.Message}" },
                Recommendations = new List<string>(),
                Status = ReviewStatus.NeedsReview
            });
        }
    }

    internal async Task<ReviewResult> ReviewLogAsync(ArtifactAvailable artifact)
    {
        // Logger?.LogInformation("Reviewing log artifact");

        try
        {
            // SK/LLM scaffolding: Use LLM for log analysis
            var analysisResult = await _kernel.InvokeAsync<string>(
                "review_analysis", "analyze_log",
                new KernelArguments { ["content"] = artifact.Artifact.InlineText ?? "" });

            // Parse the LLM response into ReviewResult
            if (!string.IsNullOrEmpty(analysisResult) && !analysisResult.Contains("placeholder"))
            {
                return ParseAnalysisToReviewResult(analysisResult, "Log");
            }
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "LLM analysis failed, falling back to deterministic heuristics");
        }

        // Fallback to deterministic heuristics
        return await ReviewLogDeterministicAsync(artifact);
    }

    private Task<ReviewResult> ReviewLogDeterministicAsync(ArtifactAvailable artifact)
    {
        var issues = new List<string>();
        var recommendations = new List<string>();
        var status = ReviewStatus.ReadyForQA;

        try
        {
            // TODO: Use SK/LLM for log analysis
            // Deterministic analysis for golden tests

            var content = (artifact.Artifact.InlineText ?? "").ToLowerInvariant();
            var hasErrors = content.Contains("error") || content.Contains("exception") || content.Contains("fail");
            var hasWarnings = content.Contains("warn") || content.Contains("warning");
            var hasBuildSuccess = content.Contains("build succeeded") || content.Contains("successful");

            if (hasErrors)
            {
                issues.Add("Errors detected in execution logs");
                status = ReviewStatus.NeedsReview;
            }

            if (hasWarnings)
            {
                recommendations.Add("Warnings detected in logs - review for potential issues");
            }

            if (!hasBuildSuccess && !hasErrors)
            {
                recommendations.Add("Build completion status unclear from logs");
            }

            var summary = $"Log review: {(hasErrors ? "Errors found" : "No errors detected")}. " +
                         $"{(hasWarnings ? "Warnings present" : "No warnings")}. " +
                         $"{(hasBuildSuccess ? "Build successful" : "Build status unclear")}.";

            return Task.FromResult(new ReviewResult
            {
                Summary = summary,
                Issues = issues,
                Recommendations = recommendations,
                Status = status
            });
        }
        catch (Exception ex)
        {
            // Logger?.LogError(ex, "Error analyzing log");
            return Task.FromResult(new ReviewResult
            {
                Summary = "Failed to analyze log content",
                Issues = new List<string> { $"Log analysis error: {ex.Message}" },
                Recommendations = new List<string>(),
                Status = ReviewStatus.NeedsReview
            });
        }
    }

    internal async Task<ReviewResult> ReviewTestResultsAsync(ArtifactAvailable artifact)
    {
        // Logger?.LogInformation("Reviewing test results artifact");

        try
        {
            // SK/LLM scaffolding: Use LLM for test results analysis
            var analysisResult = await _kernel.InvokeAsync<string>(
                "review_analysis", "analyze_test_results",
                new KernelArguments { ["content"] = artifact.Artifact.InlineText ?? "" });

            // Parse the LLM response into ReviewResult
            if (!string.IsNullOrEmpty(analysisResult) && !analysisResult.Contains("placeholder"))
            {
                return ParseAnalysisToReviewResult(analysisResult, "TestResults");
            }
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "LLM analysis failed, falling back to deterministic heuristics");
        }

        // Fallback to deterministic heuristics
        return await ReviewTestResultsDeterministicAsync(artifact);
    }

    private Task<ReviewResult> ReviewTestResultsDeterministicAsync(ArtifactAvailable artifact)
    {
        var issues = new List<string>();
        var recommendations = new List<string>();
        var status = ReviewStatus.ReadyForQA;

        try
        {
            // TODO: Use SK/LLM for test result analysis
            // Deterministic analysis for golden tests

            var content = (artifact.Artifact.InlineText ?? "").ToLowerInvariant();
            var hasFailures = content.Contains("fail") || content.Contains("error");
            var hasSuccess = content.Contains("pass") || content.Contains("success") || content.Contains("ok");
            var totalTests = 0;
            var passedTests = 0;

            // Simple parsing for test counts (this would be more sophisticated with real test output)
            var lines = (artifact.Artifact.InlineText ?? "").Split('\n');
            foreach (var line in lines)
            {
                if (line.Contains("tests run", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("test results", StringComparison.OrdinalIgnoreCase))
                {
                    // Extract numbers - simplified for demo
                    var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var word in words)
                    {
                        if (int.TryParse(word, out var num))
                        {
                            if (totalTests == 0)
                                totalTests = num;
                            else if (passedTests == 0)
                                passedTests = num;
                        }
                    }
                }
            }

            if (hasFailures)
            {
                issues.Add("Test failures detected");
                status = ReviewStatus.NeedsReview;
            }

            if (!hasSuccess && !hasFailures)
            {
                recommendations.Add("Test execution status unclear");
            }

            if (totalTests > 0 && passedTests > 0 && passedTests < totalTests)
            {
                var failureRate = ((double)(totalTests - passedTests) / totalTests) * 100;
                issues.Add($"Test failure rate: {failureRate:F1}% ({totalTests - passedTests}/{totalTests} failed)");
                status = ReviewStatus.NeedsReview;
            }

            var summary = $"Test review: {(hasFailures ? "Failures detected" : "All tests passed")}. " +
                         $"{(totalTests > 0 ? $"{passedTests}/{totalTests} tests passed" : "Test counts unclear")}.";

            return Task.FromResult(new ReviewResult
            {
                Summary = summary,
                Issues = issues,
                Recommendations = recommendations,
                Status = status
            });
        }
        catch (Exception ex)
        {
            // Logger?.LogError(ex, "Error analyzing test results");
            return Task.FromResult(new ReviewResult
            {
                Summary = "Failed to analyze test results",
                Issues = new List<string> { $"Test analysis error: {ex.Message}" },
                Recommendations = new List<string>(),
                Status = ReviewStatus.NeedsReview
            });
        }
    }

    private Task PostReviewCommentsAsync(WorkItemRef workItem, ReviewResult review)
    {
        Logger.LogInformation("Posting review comments for work item {WorkItemId}", workItem.Id);

        // Post summary comment
        _functionBindings!.CommentAsync(workItem.Id, $"**Review Summary:** {review.Summary}");

        // Post issues if any
        if (review.Issues.Any())
        {
            var issuesText = string.Join("\n- ", review.Issues);
            _functionBindings.CommentAsync(workItem.Id, $"**Issues Found:**\n- {issuesText}");
        }

        // Post recommendations if any
        if (review.Recommendations.Any())
        {
            var recsText = string.Join("\n- ", review.Recommendations);
            _functionBindings.CommentAsync(workItem.Id, $"**Recommendations:**\n- {recsText}");
        }

        return Task.CompletedTask;
    }

    private Task HandleReviewTransitionAsync(WorkItemRef workItem, ReviewResult review)
    {
        Logger.LogInformation("Evaluating work item transition based on review status: {Status}", review.Status);

        // Ensure we have a work item to operate on
        if (workItem == null)
        {
            Logger.LogWarning("No work item provided for transition; skipping");
            return Task.CompletedTask;
        }

        // Transition based on review findings using centralized constants
        var newState = review.Status switch
        {
            ReviewStatus.ReadyForQA => ReviewStates.ReadyForQA,
            ReviewStatus.NeedsReview => ReviewStates.NeedsReview,
            _ => null
        };

        if (newState != null)
        {
            // Use the function bindings to transition the work item (Reviewer is read-only for VCS)
            Logger.LogDebug("Reviewer agent maintaining read-only policy - only work item operations allowed, no VCS commands");
            _functionBindings!.TransitionAsync(workItem.Id, newState);
            Logger.LogInformation("Transitioned work item {WorkItemId} to '{NewState}'", workItem.Id, newState);
        }

        return Task.CompletedTask;
    }

    private Task HandleCommandRejected(CommandRejected rejected)
    {
        Logger.LogWarning("Command {CommandId} was rejected: {Reason}", rejected.CommandId, rejected.Reason);

        // Surface the rejection to the work item
        if (Context!.Config.WorkItem != null)
        {
            _functionBindings!.CommentAsync(
                Context.Config.WorkItem.Id,
                $"Review operation blocked: {rejected.Reason}");
        }

        return Task.CompletedTask;
    }

    private async Task HandleThrottled(Throttled throttled)
    {
        Logger.LogWarning("Review operation throttled for scope '{Scope}': retry after {RetryAfter}",
            throttled.Scope, throttled.RetryAfter);

        // Wait for backoff period
        var delay = throttled.RetryAfter - DateTimeOffset.Now;
        if (delay > TimeSpan.Zero)
        {
            Logger.LogInformation("Waiting {Delay} before retrying review operation", delay);
            await Task.Delay(delay);
        }

        // Surface throttling to work item
        if (Context!.Config.WorkItem != null)
        {
            await _functionBindings!.CommentAsync(
                Context.Config.WorkItem.Id,
                $"Review operation throttled for scope '{throttled.Scope}'. Will retry after backoff.");
        }
    }

    /// <summary>
    /// Internal result of a review operation.
    /// </summary>
    internal record ReviewResult
    {
        public required string Summary { get; init; }
        public required List<string> Issues { get; init; }
        public required List<string> Recommendations { get; init; }
        public required ReviewStatus Status { get; init; }
    }

    /// <summary>
    /// Status resulting from a review.
    /// </summary>
    public enum ReviewStatus
    {
        ReadyForQA,
        NeedsReview
    }

    /// <summary>
    /// SK plugin for LLM-driven review analysis.
    /// </summary>
    private class ReviewAnalysisPlugin
    {
        [KernelFunction("analyze_diff")]
        [Description("Analyzes a code diff for issues, recommendations, and readiness for QA.")]
        public async Task<string> AnalyzeDiffAsync(
            [Description("The diff content to analyze")] string content)
        {
            // SK/LLM scaffolding: This would call an LLM to analyze the diff
            // For now, return a placeholder response
            // TODO: Implement actual LLM call with prompt engineering
            return await Task.FromResult("Diff analysis: No issues found, ready for QA.");
        }

        [KernelFunction("analyze_log")]
        [Description("Analyzes execution logs for errors, warnings, and build status.")]
        public async Task<string> AnalyzeLogAsync(
            [Description("The log content to analyze")] string content)
        {
            // SK/LLM scaffolding: This would call an LLM to analyze the log
            // For now, return a placeholder response
            // TODO: Implement actual LLM call with prompt engineering
            return await Task.FromResult("Log analysis: No errors detected, build successful.");
        }

        [KernelFunction("analyze_test_results")]
        [Description("Analyzes test results for failures, success rates, and recommendations.")]
        public async Task<string> AnalyzeTestResultsAsync(
            [Description("The test results content to analyze")] string content)
        {
            // SK/LLM scaffolding: This would call an LLM to analyze the test results
            // For now, return a placeholder response
            // TODO: Implement actual LLM call with prompt engineering
            return await Task.FromResult("Test analysis: All tests passed.");
        }
    }

    /// <summary>
    /// Parses LLM analysis response into ReviewResult.
    /// </summary>
    private ReviewResult ParseAnalysisToReviewResult(string analysis, string artifactType)
    {
        // SK/LLM scaffolding: Parse the LLM response
        // TODO: Implement proper parsing based on expected LLM output format
        // For now, simple heuristics on the response string

        var summary = $"{artifactType} analysis: {analysis}";
        var issues = new List<string>();
        var recommendations = new List<string>();
        var status = ReviewStatus.ReadyForQA;

        if (analysis.Contains("error", StringComparison.OrdinalIgnoreCase) ||
            analysis.Contains("fail", StringComparison.OrdinalIgnoreCase) ||
            analysis.Contains("issue", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add("Issues detected in analysis");
            status = ReviewStatus.NeedsReview;
        }

        if (analysis.Contains("recommend", StringComparison.OrdinalIgnoreCase) ||
            analysis.Contains("consider", StringComparison.OrdinalIgnoreCase))
        {
            recommendations.Add("Review recommendations provided");
        }

        return new ReviewResult
        {
            Summary = summary,
            Issues = issues,
            Recommendations = recommendations,
            Status = status
        };
    }
}