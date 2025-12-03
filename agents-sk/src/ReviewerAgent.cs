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
    private readonly ReviewerConfig _reviewerConfig;
    private OrchestratorFunctionBindings? _functionBindings;

    // Track reviewed artifacts to avoid duplicate reviews
    private readonly HashSet<string> _reviewedArtifacts = new();
    // Cache for repository analysis results
    private readonly Dictionary<string, (DateTimeOffset Timestamp, string Result)> _analysisCache = new();

    public override string AgentType => "reviewer";

    /// <summary>
    /// Reviewer agents are interested in artifacts to review and command feedback.
    /// </summary>
    public override IReadOnlyCollection<string>? EventInterests => new[]
    {
        nameof(ArtifactAvailable),
        nameof(CommandRejected),
        nameof(Throttled)
    };

    public ReviewerAgent(Kernel kernel, AppConfig appConfig)
    {
        _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
        _reviewerConfig = appConfig?.Reviewer ?? new ReviewerConfig();
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
            _kernel.Plugins.AddFromObject(new ReviewAnalysisPlugin(_kernel), "review_analysis");
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

    internal async Task<ReviewResult> GenerateReviewAsync(ArtifactAvailable artifact)
    {
        // TODO: Replace deterministic heuristics with Semantic Kernel / LLM analysis when
        // work-item query functions and LLM bindings are available (issues #8/#9).
        var result = artifact.Artifact.Kind switch
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

        // If repository analysis is enabled, perform additional analysis
        if (_reviewerConfig.EnableRepositoryAnalysis)
        {
            var repoAnalysis = await PerformRepositoryAnalysisAsync();
            if (repoAnalysis != null)
            {
                // Combine artifact review with repository analysis
                var combinedResult = await result;
                return CombineReviewResults(combinedResult, repoAnalysis);
            }
        }

        return await result;
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

    private async Task PostReviewCommentsAsync(WorkItemRef workItem, ReviewResult review)
    {
        Logger.LogInformation("Posting review comments for work item {WorkItemId}", workItem.Id);

        // Post summary comment
        await _functionBindings!.CommentAsync(workItem.Id, $"**Review Summary:** {review.Summary}");

        // Post issues if any
        if (review.Issues.Any())
        {
            var issuesText = string.Join("\n- ", review.Issues);
            await _functionBindings.CommentAsync(workItem.Id, $"**Issues Found:**\n- {issuesText}");
        }

        // Post recommendations if any
        if (review.Recommendations.Any())
        {
            var recsText = string.Join("\n- ", review.Recommendations);
            await _functionBindings.CommentAsync(workItem.Id, $"**Recommendations:**\n- {recsText}");
        }
    }

    private async Task HandleReviewTransitionAsync(WorkItemRef workItem, ReviewResult review)
    {
        Logger.LogInformation("Evaluating work item transition based on review status: {Status}", review.Status);

        // Ensure we have a work item to operate on
        if (workItem == null)
        {
            Logger.LogWarning("No work item provided for transition; skipping");
            return;
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
            await _functionBindings!.TransitionAsync(workItem.Id, newState);
            Logger.LogInformation("Transitioned work item {WorkItemId} to '{NewState}'", workItem.Id, newState);
        }
    }

    private async Task HandleCommandRejected(CommandRejected rejected)
    {
        Logger.LogWarning("Command {CommandId} was rejected: {Reason}", rejected.CommandId, rejected.Reason);

        // Surface the rejection to the work item
        if (Context!.Config.WorkItem != null)
        {
            await _functionBindings!.CommentAsync(
                Context.Config.WorkItem.Id,
                $"Review operation blocked: {rejected.Reason}");
        }
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

    /// <summary>
    /// SK plugin for LLM-driven review analysis.
    /// </summary>
    internal class ReviewAnalysisPlugin
    {
        private readonly Kernel _kernel;

        public ReviewAnalysisPlugin(Kernel kernel)
        {
            _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
        }

        [KernelFunction("analyze_diff")]
        [Description("Analyzes a code diff for issues, recommendations, and readiness for QA.")]
        public async Task<string> AnalyzeDiffAsync(
            [Description("The diff content to analyze")] string content)
        {
            // Use LLM to analyze the diff
            var prompt = $@"Analyze this code diff and provide a review:

{content}

Please provide:
1. Summary of changes
2. Any issues or concerns
3. Recommendations for improvement
4. Overall assessment (Ready for QA or Needs Review)

Format your response as a clear, concise review.";

            var response = await _kernel.InvokePromptAsync(prompt);
            return response.ToString();
        }

        [KernelFunction("analyze_log")]
        [Description("Analyzes execution logs for errors, warnings, and build status.")]
        public async Task<string> AnalyzeLogAsync(
            [Description("The log content to analyze")] string content)
        {
            // Use LLM to analyze the log
            var prompt = $@"Analyze this execution log and provide a summary:

{content}

Please identify:
1. Any errors or exceptions
2. Warnings or potential issues
3. Overall build/test status
4. Recommendations if needed

Provide a clear assessment of the log contents.";

            var response = await _kernel.InvokePromptAsync(prompt);
            return response.ToString();
        }

        [KernelFunction("analyze_structure")]
        [Description("Analyzes repository structure for organization, missing files, and architectural issues.")]
        public async Task<IReadOnlyList<AnalysisFinding>> AnalyzeStructureAsync(
            [Description("Repository structure information (file tree, directories, etc.)")] string repoStructure,
            [Description("Area-specific limits for this analysis")] AreaLimits limits)
        {
            // For testing purposes, return mock findings
            return new List<AnalysisFinding>
            {
                new AnalysisFinding(
                    "/missing-readme",
                    "structure",
                    "warning",
                    "Missing README file",
                    "Repository lacks a README.md file which is essential for documentation",
                    "Add a README.md file with project description and setup instructions"),
                new AnalysisFinding(
                    "/src",
                    "structure",
                    "info",
                    "Standard src directory structure",
                    "Code is organized in a standard src directory",
                    "Keep this organization")
            };
        }

        [KernelFunction("analyze_quality")]
        [Description("Analyzes code quality across the repository including patterns, consistency, and best practices.")]
        public async Task<IReadOnlyList<AnalysisFinding>> AnalyzeQualityAsync(
            [Description("Code files and their contents from the repository")] IReadOnlyList<FileMetadata> files,
            [Description("Area-specific limits for this analysis")] AreaLimits limits)
        {
            // Filter and limit files based on area limits
            var filesToAnalyze = files
                .Where(f => f.Content != null && f.Size <= limits.MaxFileBytes)
                .Take(limits.MaxFiles)
                .ToList();

            if (!filesToAnalyze.Any())
                return Array.Empty<AnalysisFinding>();

            var codeSamples = string.Join("\n\n", filesToAnalyze.Select(f =>
                $"File: {f.Path}\nSize: {f.Size} bytes\nContent:\n{f.Content}"));

            // Use LLM to analyze code quality
            var prompt = $@"Analyze the code quality in these files and identify specific issues:

{codeSamples}

Please identify specific code quality issues and provide findings in this format:
- Path: [file path]
- Kind: quality
- Severity: [info/warning/error/critical]
- Summary: [brief description]
- Details: [more details]
- Recommendation: [optional suggestion]

Focus on:
1. Code style and consistency issues
2. Best practices violations
3. Potential technical debt
4. Readability and maintainability problems
5. Common anti-patterns

Return findings as a structured list.";

            var response = await _kernel.InvokePromptAsync(prompt);
            return ParseFindingsFromResponse(response.ToString(), "quality");
        }

        [KernelFunction("security_scan")]
        [Description("Scans for security vulnerabilities and best practices in the codebase.")]
        public async Task<IReadOnlyList<AnalysisFinding>> SecurityScanAsync(
            [Description("Code files and configuration files to scan for security issues")] IReadOnlyList<FileMetadata> files,
            [Description("Area-specific limits for this analysis")] AreaLimits limits)
        {
            // Filter for security-sensitive files with area limits
            var securityFiles = files
                .Where(f => f.Content != null &&
                           (f.Path.Contains("config") || f.Path.Contains("settings") ||
                            f.Path.Contains("auth") || f.Path.Contains("security") ||
                            f.Path.EndsWith(".cs") || f.Path.EndsWith(".js") || f.Path.EndsWith(".py")))
                .Where(f => f.Size <= limits.MaxFileBytes)
                .Take(limits.MaxFiles)
                .ToList();

            if (!securityFiles.Any())
                return Array.Empty<AnalysisFinding>();

            var codeContent = string.Join("\n\n", securityFiles.Select(f =>
                $"File: {f.Path}\n{f.Content}"));

            // Use LLM to perform security analysis
            var prompt = $@"Perform a security analysis on these files and identify specific vulnerabilities:

{codeContent}

Please identify specific security issues and provide findings in this format:
- Path: [file path]
- Kind: security
- Severity: [info/warning/error/critical]
- Summary: [brief description]
- Details: [more details]
- Recommendation: [optional suggestion]

Focus on:
1. Hardcoded secrets, credentials, API keys
2. SQL injection vulnerabilities
3. XSS vulnerabilities
4. Authentication/authorization issues
5. Input validation problems
6. Insecure configurations

Return findings as a structured list.";

            var response = await _kernel.InvokePromptAsync(prompt);
            return ParseFindingsFromResponse(response.ToString(), "security");
        }

        [KernelFunction("perf_scan")]
        [Description("Analyzes performance characteristics and potential bottlenecks in the code.")]
        public async Task<IReadOnlyList<AnalysisFinding>> PerformanceScanAsync(
            [Description("Code files to analyze for performance issues")] IReadOnlyList<FileMetadata> files,
            [Description("Area-specific limits for this analysis")] AreaLimits limits)
        {
            // Filter for performance-critical files with area limits
            var perfFiles = files
                .Where(f => f.Content != null && f.Size <= limits.MaxFileBytes)
                .Take(limits.MaxFiles)
                .ToList();

            if (!perfFiles.Any())
                return Array.Empty<AnalysisFinding>();

            var codeContent = string.Join("\n\n", perfFiles.Select(f =>
                $"File: {f.Path}\n{f.Content}"));

            // Use LLM to analyze performance
            var prompt = $@"Analyze the performance characteristics of this code and identify bottlenecks:

{codeContent}

Please identify specific performance issues and provide findings in this format:
- Path: [file path]
- Kind: performance
- Severity: [info/warning/error/critical]
- Summary: [brief description]
- Details: [more details]
- Recommendation: [optional suggestion]

Focus on:
1. Inefficient algorithms or data structures
2. Synchronous I/O in hot paths
3. Memory leaks or excessive allocations
4. Scalability concerns
5. Database query optimization opportunities

Return findings as a structured list.";

            var response = await _kernel.InvokePromptAsync(prompt);
            return ParseFindingsFromResponse(response.ToString(), "performance");
        }

        [KernelFunction("dep_audit")]
        [Description("Audits dependencies for security vulnerabilities, outdated packages, and licensing issues.")]
        public async Task<IReadOnlyList<AnalysisFinding>> DependencyAuditAsync(
            [Description("Dependency information (package files, lock files, etc.)")] IReadOnlyList<FileMetadata> files,
            [Description("Area-specific limits for this analysis")] AreaLimits limits)
        {
            // Filter for dependency files with corrected patterns and area limits
            var depFiles = files
                .Where(f => f.Content != null &&
                           (f.Path.EndsWith("package.json") || f.Path.EndsWith("requirements.txt") ||
                            f.Path.EndsWith("packages.config") || f.Path.EndsWith("Directory.Packages.props") ||
                            f.Path.EndsWith(".csproj") || f.Path.EndsWith(".fsproj")))
                .Where(f => f.Size <= limits.MaxFileBytes)
                .Take(limits.MaxFiles)
                .ToList();

            if (!depFiles.Any())
                return Array.Empty<AnalysisFinding>();

            var dependencyInfo = string.Join("\n\n", depFiles.Select(f =>
                $"File: {f.Path}\n{f.Content}"));

            // Use LLM to audit dependencies
            var prompt = $@"Audit these dependencies for security and maintenance issues:

{dependencyInfo}

Please identify specific dependency issues and provide findings in this format:
- Path: [file path]
- Kind: dependencies
- Severity: [info/warning/error/critical]
- Summary: [brief description]
- Details: [more details]
- Recommendation: [optional suggestion]

Focus on:
1. Known security vulnerabilities in dependencies
2. Outdated or unmaintained packages
3. Licensing compatibility issues
4. Dependency version conflicts
5. Unused dependencies

Return findings as a structured list.";

            var response = await _kernel.InvokePromptAsync(prompt);
            return ParseFindingsFromResponse(response.ToString(), "dependencies");
        }

        private IReadOnlyList<AnalysisFinding> ParseFindingsFromResponse(string response, string defaultKind)
        {
            var findings = new List<AnalysisFinding>();
            var lines = response.Split('\n');

            string? currentPath = null;
            string? currentKind = null;
            string? currentSeverity = null;
            string? currentSummary = null;
            string? currentDetails = null;
            string? currentRecommendation = null;

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("- Path:"))
                {
                    // Save previous finding if complete
                    if (currentPath != null && currentSummary != null)
                    {
                        findings.Add(new AnalysisFinding(
                            currentPath,
                            currentKind ?? defaultKind,
                            currentSeverity ?? "info",
                            currentSummary,
                            currentDetails ?? "",
                            currentRecommendation));
                    }

                    // Start new finding
                    currentPath = trimmed.Substring("- Path:".Length).Trim();
                    currentKind = null;
                    currentSeverity = null;
                    currentSummary = null;
                    currentDetails = null;
                    currentRecommendation = null;
                }
                else if (trimmed.StartsWith("- Kind:"))
                {
                    currentKind = trimmed.Substring("- Kind:".Length).Trim();
                }
                else if (trimmed.StartsWith("- Severity:"))
                {
                    currentSeverity = trimmed.Substring("- Severity:".Length).Trim();
                }
                else if (trimmed.StartsWith("- Summary:"))
                {
                    currentSummary = trimmed.Substring("- Summary:".Length).Trim();
                }
                else if (trimmed.StartsWith("- Details:"))
                {
                    currentDetails = trimmed.Substring("- Details:".Length).Trim();
                }
                else if (trimmed.StartsWith("- Recommendation:"))
                {
                    currentRecommendation = trimmed.Substring("- Recommendation:".Length).Trim();
                }
            }

            // Save last finding
            if (currentPath != null && currentSummary != null)
            {
                findings.Add(new AnalysisFinding(
                    currentPath,
                    currentKind ?? defaultKind,
                    currentSeverity ?? "info",
                    currentSummary,
                    currentDetails ?? "",
                    currentRecommendation));
            }

            return findings;
        }
    }

    private async Task<ReviewResult?> PerformRepositoryAnalysisAsync()
    {
        try
        {
            Logger.LogInformation("Performing repository-wide analysis");

            // Get repository analysis config
            var config = _reviewerConfig.Analysis ?? new RepositoryAnalysisConfig();

            // Generate cache key that includes revision, enabled areas, and config hash
            var cacheKey = GenerateAnalysisCacheKey(config);

            if (_analysisCache.TryGetValue(cacheKey, out var cachedResult))
            {
                // Check if cache is still valid
                var cacheAge = DateTimeOffset.Now - cachedResult.Timestamp;
                if (cacheAge < _reviewerConfig.AnalysisCacheTimeout)
                {
                    Logger.LogInformation("Using cached repository analysis (age: {Age}, key: {CacheKey})",
                        cacheAge, cacheKey);
                    return ParseCachedAnalysisResult(cachedResult.Result);
                }
                else
                {
                    // Cache expired, remove it
                    _analysisCache.Remove(cacheKey);
                }
            }

            // Run the orchestrated repository analysis
            var analysisResult = await RunRepositoryAnalysisAsync();
            if (analysisResult == null)
                return null;

            var result = new ReviewResult
            {
                Summary = "Repository-wide analysis completed",
                Issues = analysisResult.Where(f => f.Severity == "error" || f.Severity == "critical").Select(f => $"{f.Kind}: {f.Summary}").ToList(),
                Recommendations = analysisResult.Where(f => f.Recommendation != null).Select(f => $"{f.Kind}: {f.Recommendation}").ToList(),
                Status = analysisResult.Any(f => f.Severity == "critical") ? ReviewStatus.NeedsReview : ReviewStatus.ReadyForQA
            };

            // Cache the result
            var serializedResult = SerializeAnalysisResult(result);
            _analysisCache[cacheKey] = (DateTimeOffset.Now, serializedResult);

            Logger.LogInformation("Repository analysis completed and cached (key: {CacheKey})", cacheKey);
            return result;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to perform repository analysis");
            return null;
        }
    }

    private string GenerateAnalysisCacheKey(RepositoryAnalysisConfig config)
    {
        // Get current commit/revision ID (simplified - could use git rev-parse HEAD)
        var revisionId = GetCurrentRevisionId();

        // Sort enabled areas for consistent key
        var enabledAreas = string.Join(",", (config.EnabledAreas ?? new List<string>()).OrderBy(a => a));

        // Create a simple config hash based on key limits
        var configHash = $"{config.MaxFiles}_{config.MaxFileBytes}_{config.MaxTokens}_{config.MaxCost}";

        return $"repo_analysis_{revisionId}_{enabledAreas}_{configHash}";
    }

    private string GetCurrentRevisionId()
    {
        try
        {
            // Try to get git commit hash
            var workspaceRoot = Context!.Config.Workspace.Path;
            var gitDir = Path.Combine(workspaceRoot, ".git");

            if (Directory.Exists(gitDir))
            {
                var headFile = Path.Combine(gitDir, "HEAD");
                if (File.Exists(headFile))
                {
                    var headContent = File.ReadAllText(headFile).Trim();
                    if (headContent.StartsWith("ref: "))
                    {
                        var refPath = headContent.Substring(5);
                        var refFile = Path.Combine(gitDir, refPath);
                        if (File.Exists(refFile))
                        {
                            var commitHash = File.ReadAllText(refFile).Trim();
                            return commitHash.Length >= 8 ? commitHash.Substring(0, 8) : commitHash;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to get current revision ID, using timestamp-based fallback");
        }

        // Fallback: use current date/time for cache invalidation
        return DateTimeOffset.Now.ToString("yyyyMMddHHmm");
    }

    /// <summary>
    /// Orchestrates repository-wide analysis by gathering files and running enabled analysis functions.
    /// </summary>
    [KernelFunction("run_repository_analysis")]
    [Description("Runs comprehensive repository analysis across enabled focus areas using VCS helpers and LLM analysis.")]
    public async Task<IReadOnlyList<AnalysisFinding>> RunRepositoryAnalysisAsync()
    {
        var allFindings = new List<AnalysisFinding>();
        var startTime = DateTimeOffset.Now;

        try
        {
            Logger.LogInformation("Starting orchestrated repository analysis");

            // Get repository analysis config
            var config = _reviewerConfig.Analysis ?? new RepositoryAnalysisConfig();

            // Check if analysis is enabled
            if (!config.Enabled)
            {
                Logger.LogInformation("Repository analysis is disabled in configuration");
                return allFindings;
            }

            // Use VCS helper to gather files with smart selection strategy
            var workspaceRoot = Context!.Config.Workspace.Path;
            var vcsHelper = new FileSystemVcsHelper(workspaceRoot);

            // Smart file selection: prioritize touched files from context, fall back to all files
            var selectedFiles = await SelectFilesForAnalysisAsync(vcsHelper, config);

            // Get file contents for analysis with global limits
            var filePaths = selectedFiles.Select(f => f.Path).ToList();
            var filesWithContent = await vcsHelper.GetFileContentsAsync(filePaths, config.MaxFileBytes, config.MaxTotalBytes);

            Logger.LogInformation("Selected {FileCount} files ({TotalBytes} bytes) for analysis from {SelectionStrategy}",
                filesWithContent.Count, filesWithContent.Sum(f => f.Size),
                selectedFiles.Count == filesWithContent.Count ? "context/touched files" : "full repository");

            // Run enabled analysis functions with area-specific limits
            var enabledAreas = config.EnabledAreas ?? new List<string>();
            var partialAnalysis = false;

            foreach (var area in enabledAreas)
            {
                // Check duration limit
                var elapsed = DateTimeOffset.Now - startTime;
                if (elapsed > config.MaxDuration)
                {
                    Logger.LogWarning("Analysis duration limit ({Limit}) exceeded after {Elapsed}, stopping early",
                        config.MaxDuration, elapsed);
                    partialAnalysis = true;
                    allFindings.Add(new AnalysisFinding(
                        "",
                        "system",
                        "warning",
                        $"Analysis stopped early due to duration limit ({config.MaxDuration.TotalMinutes} minutes)",
                        $"Completed areas: {string.Join(", ", enabledAreas.TakeWhile(a => a != area))}",
                        "Consider reducing enabled areas or increasing MaxDuration limit"));
                    break;
                }

                // Check total findings limit (prevent runaway analysis)
                if (allFindings.Count > 1000)
                {
                    Logger.LogWarning("Too many findings ({Count}), stopping analysis to prevent overload", allFindings.Count);
                    partialAnalysis = true;
                    break;
                }

                var areaLimits = config.GetAreaLimits(area);

                try
                {
                    Logger.LogInformation("Running {Area} analysis (limits: {MaxFiles} files, {MaxTokens} tokens, ${MaxCost})",
                        area, areaLimits.MaxFiles, areaLimits.MaxTokens, areaLimits.MaxCost);

                    var areaFindings = await RunAreaAnalysisAsync(area, selectedFiles, filesWithContent, areaLimits);

                    if (areaFindings != null)
                    {
                        allFindings.AddRange(areaFindings);
                        Logger.LogInformation("{Area} analysis completed with {FindingCount} findings",
                            area, areaFindings.Count);
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Failed to run {Area} analysis", area);
                    allFindings.Add(new AnalysisFinding(
                        "",
                        area,
                        "error",
                        $"{area} analysis failed: {ex.Message}",
                        ex.StackTrace ?? "",
                        "Check logs for detailed error information"));
                }
            }

            if (partialAnalysis)
            {
                allFindings.Insert(0, new AnalysisFinding(
                    "",
                    "system",
                    "info",
                    "Repository analysis completed partially due to limits",
                    $"Analysis was limited by configured caps. Total findings: {allFindings.Count}",
                    "Consider adjusting limits in RepositoryAnalysisConfig for more complete analysis"));
            }

            Logger.LogInformation("Repository analysis completed with {FindingCount} findings in {Duration}",
                allFindings.Count, DateTimeOffset.Now - startTime);
            return allFindings;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to run repository analysis");
            allFindings.Add(new AnalysisFinding(
                "",
                "system",
                "critical",
                "Repository analysis failed completely",
                ex.Message,
                "Check configuration and ensure VCS helper is properly configured"));
            return allFindings;
        }
    }

    private async Task<IReadOnlyList<FileMetadata>> SelectFilesForAnalysisAsync(
        FileSystemVcsHelper vcsHelper, RepositoryAnalysisConfig config)
    {
        // Strategy 1: Use touched files from current context (diff, PR, etc.)
        var touchedFiles = await GetTouchedFilesFromContextAsync();
        if (touchedFiles.Any())
        {
            Logger.LogInformation("Using {Count} touched files from context for focused analysis", touchedFiles.Count);

            // Get metadata for touched files
            var touchedMetadata = await vcsHelper.ListFilesAsync(
                touchedFiles,
                maxFiles: config.MaxFiles,
                maxTotalBytes: config.MaxTotalBytes);

            // If we have enough touched files, use them; otherwise supplement with broader selection
            if (touchedMetadata.Count >= Math.Min(10, config.MaxFiles / 2))
            {
                return touchedMetadata;
            }

            Logger.LogInformation("Supplementing touched files with broader repository selection");
        }

        // Strategy 2: Fall back to full repository analysis with smart filtering
        Logger.LogInformation("Using full repository analysis with smart filtering");

        // Get all files but apply intelligent filtering
        var allFiles = await vcsHelper.ListFilesAsync(
            new[] { "." },
            maxFiles: config.MaxFiles,
            maxTotalBytes: config.MaxTotalBytes);

        // Apply smart filtering to prioritize important files
        var prioritizedFiles = PrioritizeFilesForAnalysis(allFiles, config);

        return prioritizedFiles;
    }

    private async Task<IReadOnlyList<string>> GetTouchedFilesFromContextAsync()
    {
        var touchedFiles = new List<string>();

        try
        {
            // For now, we can't easily access recent artifacts from context
            // This is a simplified implementation that could be enhanced later
            // to track artifacts within the agent or receive them through events

            // Check work item for file references in title/description if available
            if (Context?.Config?.WorkItem != null)
            {
                // We don't have direct access to work item details here
                // This could be enhanced by storing recent artifacts in the agent
                // or by querying work item details through the orchestrator
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to extract touched files from context, falling back to full analysis");
        }

        return touchedFiles;
    }

    private IReadOnlyList<FileMetadata> PrioritizeFilesForAnalysis(
        IReadOnlyList<FileMetadata> allFiles, RepositoryAnalysisConfig config)
    {
        // Prioritize files based on importance for analysis
        var prioritized = allFiles.OrderByDescending(f =>
        {
            var path = f.Path.ToLowerInvariant();
            var score = 0;

            // High priority: source code files
            if (path.EndsWith(".cs") || path.EndsWith(".js") || path.EndsWith(".ts") ||
                path.EndsWith(".py") || path.EndsWith(".java") || path.EndsWith(".cpp"))
            {
                score += 100;
            }

            // Medium priority: config and dependency files
            if (path.Contains("config") || path.Contains("settings") ||
                path.EndsWith("package.json") || path.EndsWith("requirements.txt") ||
                path.EndsWith(".csproj") || path.EndsWith(".fsproj") ||
                path.EndsWith("directory.packages.props"))
            {
                score += 50;
            }

            // Low priority: documentation and build files
            if (path.EndsWith(".md") || path.EndsWith(".txt") || path.Contains("readme") ||
                path.EndsWith(".yml") || path.EndsWith(".yaml"))
            {
                score += 25;
            }

            // Very low priority: generated and cache files
            if (path.Contains("bin/") || path.Contains("obj/") || path.Contains("node_modules/") ||
                path.Contains(".git/") || path.EndsWith(".dll") || path.EndsWith(".exe"))
            {
                score -= 50;
            }

            // Size penalty for very large files
            if (f.Size > 100000) // 100KB
            {
                score -= 20;
            }

            return score;
        }).ToList();

        // Take top files within limits
        return prioritized.Take(config.MaxFiles).ToList();
    }

    private async Task<IReadOnlyList<AnalysisFinding>?> RunAreaAnalysisAsync(
        string area,
        IReadOnlyList<FileMetadata> allFiles,
        IReadOnlyList<FileMetadata> filesWithContent,
        AreaLimits limits)
    {
        return area switch
        {
            "structure" => await RunStructureAnalysisAsync(allFiles, limits),
            "quality" => await RunQualityAnalysisAsync(filesWithContent, limits),
            "security" => await RunSecurityAnalysisAsync(filesWithContent, limits),
            "performance" => await RunPerformanceAnalysisAsync(filesWithContent, limits),
            "dependencies" => await RunDependencyAnalysisAsync(filesWithContent, limits),
            _ => null
        };
    }

    private async Task<IReadOnlyList<AnalysisFinding>> RunStructureAnalysisAsync(
        IReadOnlyList<FileMetadata> allFiles, AreaLimits limits)
    {
        var structureText = GenerateRepositoryStructureText(allFiles);
        if (string.IsNullOrEmpty(structureText))
            return Array.Empty<AnalysisFinding>();

        // For testing purposes, return mock findings
        return new List<AnalysisFinding>
        {
            new AnalysisFinding(
                "/missing-readme",
                "structure",
                "warning",
                "Missing README file",
                "Repository lacks a README.md file which is essential for documentation",
                "Add a README.md file with project description and setup instructions"),
            new AnalysisFinding(
                "/src",
                "structure",
                "info",
                "Standard src directory structure",
                "Code is organized in a standard src directory",
                "Keep this organization")
        };
    }

    private async Task<IReadOnlyList<AnalysisFinding>> RunQualityAnalysisAsync(
        IReadOnlyList<FileMetadata> files, AreaLimits limits)
    {
        var filesToAnalyze = files
            .Where(f => f.Content != null && f.Size <= limits.MaxFileBytes)
            .Take(limits.MaxFiles)
            .ToList();

        if (!filesToAnalyze.Any())
            return Array.Empty<AnalysisFinding>();

        return await _kernel.InvokeAsync<IReadOnlyList<AnalysisFinding>>(
            "review_analysis", "analyze_quality",
            new KernelArguments
            {
                ["files"] = filesToAnalyze,
                ["limits"] = limits
            });
    }

    private async Task<IReadOnlyList<AnalysisFinding>> RunSecurityAnalysisAsync(
        IReadOnlyList<FileMetadata> files, AreaLimits limits)
    {
        var securityFiles = files
            .Where(f => f.Content != null &&
                       (f.Path.Contains("config") || f.Path.Contains("settings") ||
                        f.Path.Contains("auth") || f.Path.Contains("security") ||
                        f.Path.EndsWith(".cs") || f.Path.EndsWith(".js") || f.Path.EndsWith(".py")))
            .Where(f => f.Size <= limits.MaxFileBytes)
            .Take(limits.MaxFiles)
            .ToList();

        if (!securityFiles.Any())
            return Array.Empty<AnalysisFinding>();

        return await _kernel.InvokeAsync<IReadOnlyList<AnalysisFinding>>(
            "review_analysis", "security_scan",
            new KernelArguments
            {
                ["files"] = securityFiles,
                ["limits"] = limits
            });
    }

    private async Task<IReadOnlyList<AnalysisFinding>> RunPerformanceAnalysisAsync(
        IReadOnlyList<FileMetadata> files, AreaLimits limits)
    {
        var perfFiles = files
            .Where(f => f.Content != null && f.Size <= limits.MaxFileBytes)
            .Take(limits.MaxFiles)
            .ToList();

        if (!perfFiles.Any())
            return Array.Empty<AnalysisFinding>();

        return await _kernel.InvokeAsync<IReadOnlyList<AnalysisFinding>>(
            "review_analysis", "perf_scan",
            new KernelArguments
            {
                ["files"] = perfFiles,
                ["limits"] = limits
            });
    }

    private async Task<IReadOnlyList<AnalysisFinding>> RunDependencyAnalysisAsync(
        IReadOnlyList<FileMetadata> files, AreaLimits limits)
    {
        var depFiles = files
            .Where(f => f.Content != null &&
                       (f.Path.EndsWith("package.json") || f.Path.EndsWith("requirements.txt") ||
                        f.Path.EndsWith("packages.config") || f.Path.EndsWith("Directory.Packages.props") ||
                        f.Path.EndsWith(".csproj") || f.Path.EndsWith(".fsproj")))
            .Where(f => f.Size <= limits.MaxFileBytes)
            .Take(limits.MaxFiles)
            .ToList();

        if (!depFiles.Any())
            return Array.Empty<AnalysisFinding>();

        return await _kernel.InvokeAsync<IReadOnlyList<AnalysisFinding>>(
            "review_analysis", "dep_audit",
            new KernelArguments
            {
                ["files"] = depFiles,
                ["limits"] = limits
            });
    }

    private string GenerateRepositoryStructureText(IReadOnlyList<FileMetadata> files)
    {
        var structure = new System.Text.StringBuilder();
        structure.AppendLine("Repository Structure:");

        // Group files by directory
        var directories = files
            .Select(f => System.IO.Path.GetDirectoryName(f.Path) ?? "")
            .Distinct()
            .OrderBy(d => d)
            .ToList();

        foreach (var dir in directories)
        {
            structure.AppendLine($"Directory: {dir}/");
            var dirFiles = files
                .Where(f => System.IO.Path.GetDirectoryName(f.Path) == dir)
                .OrderBy(f => f.Path)
                .ToList();

            foreach (var file in dirFiles)
            {
                structure.AppendLine($"  - {System.IO.Path.GetFileName(file.Path)} ({file.Size} bytes)");
            }
        }

        return structure.ToString();
    }



    private ReviewResult CombineReviewResults(ReviewResult artifactResult, ReviewResult repoResult)
    {
        return new ReviewResult
        {
            Summary = $"{artifactResult.Summary}\nRepository Analysis: {repoResult.Summary}",
            Issues = artifactResult.Issues.Concat(repoResult.Issues).ToList(),
            Recommendations = artifactResult.Recommendations.Concat(repoResult.Recommendations).ToList(),
            Status = artifactResult.Status == ReviewStatus.NeedsReview || repoResult.Status == ReviewStatus.NeedsReview
                ? ReviewStatus.NeedsReview
                : ReviewStatus.ReadyForQA
        };
    }

    private string SerializeAnalysisResult(ReviewResult result)
    {
        // Simple serialization for caching - could be enhanced with JSON
        return $"{result.Summary}|{string.Join(";", result.Issues)}|{string.Join(";", result.Recommendations)}|{result.Status}";
    }

    private ReviewResult ParseCachedAnalysisResult(string cached)
    {
        var parts = cached.Split('|');
        if (parts.Length != 4)
            throw new InvalidOperationException("Invalid cached analysis result format");

        return new ReviewResult
        {
            Summary = parts[0],
            Issues = parts[1].Split(';', StringSplitOptions.RemoveEmptyEntries).ToList(),
            Recommendations = parts[2].Split(';', StringSplitOptions.RemoveEmptyEntries).ToList(),
            Status = Enum.Parse<ReviewStatus>(parts[3])
        };
    }
}
