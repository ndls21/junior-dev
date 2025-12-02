using System;
using System.Threading.Tasks;
using JuniorDev.Agents.Sk;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Xunit;
using Xunit.Sdk;

namespace JuniorDev.Agents.Sk.Tests;

/// <summary>
/// AI integration tests that require real AI services to be configured.
/// These tests are only run when RUN_AI_TESTS=1 and valid credentials are provided.
/// </summary>
[Collection("AI Integration Tests")]
public class AiIntegrationTests
{
    private readonly AiTestFixture _fixture;

    public AiIntegrationTests(AiTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ReviewerAgent_CanAnalyzeDiff_WithRealAI()
    {
        // Skip.IfNot(_fixture.ShouldRunAiTests, "AI tests disabled or credentials not available");
        // Temporarily disabled due to Skip method issues

        // Arrange - Set up kernel with OpenAI chat completion
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ??
                    Environment.GetEnvironmentVariable("JUNIORDEV__APPCONFIG__AUTH__OPENAI__APIKEY");
        
        var builder = Kernel.CreateBuilder();
        builder.AddOpenAIChatCompletion("gpt-4", apiKey!);
        var kernel = builder.Build();
        var agent = new ReviewerAgent(kernel);

        var diffContent = @"
diff --git a/src/Main.cs b/src/Main.cs
index 1234567..abcdef0 100644
--- a/src/Main.cs
+++ b/src/Main.cs
@@ -1,5 +1,8 @@
 using System;

+// New feature: Add logging
+Console.WriteLine(""Starting application"");
+
 public class Main {
     public static void Main(string[] args) {
         Console.WriteLine(""Hello World"");
";

        var artifact = new JuniorDev.Contracts.ArtifactAvailable(
            Id: Guid.NewGuid(),
            Correlation: new JuniorDev.Contracts.Correlation(Guid.NewGuid()),
            Artifact: new JuniorDev.Contracts.Artifact(Kind: "Diff", Name: "test-diff", InlineText: diffContent)
        );

        // Act
        var result = await agent.ReviewDiffAsync(artifact);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Summary);
        Assert.True(result.Summary.Length > 10, "AI should provide a meaningful summary");
        Assert.NotNull(result.Issues);
    }

    [Fact]
    public async Task ReviewerAgent_CanAnalyzeLog_WithRealAI()
    {
        // Skip.IfNot(_fixture.ShouldRunAiTests, "AI tests disabled or credentials not available");
        // Temporarily disabled due to Skip method issues

        // Arrange - Set up kernel with OpenAI chat completion
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ??
                    Environment.GetEnvironmentVariable("JUNIORDEV__APPCONFIG__AUTH__OPENAI__APIKEY");
        
        var builder = Kernel.CreateBuilder();
        builder.AddOpenAIChatCompletion("gpt-4", apiKey!);
        var kernel = builder.Build();
        var agent = new ReviewerAgent(kernel);

        var logContent = @"Build started...
Compiling src/Main.cs...
Warning: Unused variable 'x' in Main.cs:15
Error: NullReferenceException in Database.cs:42
Build completed with 1 error, 1 warning.";

        var artifact = new JuniorDev.Contracts.ArtifactAvailable(
            Id: Guid.NewGuid(),
            Correlation: new JuniorDev.Contracts.Correlation(Guid.NewGuid()),
            Artifact: new JuniorDev.Contracts.Artifact(Kind: "Log", Name: "build-log", InlineText: logContent)
        );

        // Act
        var result = await agent.ReviewLogAsync(artifact);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Summary);
        Assert.Contains("error", result.Summary.ToLowerInvariant());
        Assert.Equal(ReviewerAgent.ReviewStatus.NeedsReview, result.Status);
    }
}