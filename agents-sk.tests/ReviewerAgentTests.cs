using System.Threading.Tasks;
using JuniorDev.Agents.Sk;
using Microsoft.SemanticKernel;
using Xunit;

namespace JuniorDev.Agents.Sk.Tests
{
    public class ReviewerAgentTests
    {
        [Fact]
        public async Task ReviewDiff_WithTestsAndDocs_ReturnsReadyForQA()
        {
            var kernel = new Kernel();
            var appConfig = new JuniorDev.Contracts.AppConfig();
            var agent = new ReviewerAgent(kernel, appConfig);

            var diffContent = "+ public void Test() { }\n+ // updated docs\n";
            var artifact = new JuniorDev.Contracts.ArtifactAvailable(
                Id: System.Guid.NewGuid(),
                Correlation: new JuniorDev.Contracts.Correlation(System.Guid.NewGuid()),
                Artifact: new JuniorDev.Contracts.Artifact(Kind: "Diff", Name: "d1", InlineText: diffContent)
            );

            var result = await agent.ReviewDiffAsync(artifact);

            Assert.Empty(result.Issues);
            Assert.Equal(ReviewerAgent.ReviewStatus.ReadyForQA, result.Status);
        }

        [Fact]
        public async Task ReviewLog_WithErrors_ReturnsNeedsReview()
        {
            var kernel = new Kernel();
            var appConfig = new JuniorDev.Contracts.AppConfig();
            var agent = new ReviewerAgent(kernel, appConfig);

            var logContent = "Error: NullReferenceException\nWarning: something odd";
            var artifact = new JuniorDev.Contracts.ArtifactAvailable(
                Id: System.Guid.NewGuid(),
                Correlation: new JuniorDev.Contracts.Correlation(System.Guid.NewGuid()),
                Artifact: new JuniorDev.Contracts.Artifact(Kind: "Log", Name: "l1", InlineText: logContent)
            );

            var result = await agent.ReviewLogAsync(artifact);

            Assert.Contains("Errors found", result.Summary);
            Assert.Equal(ReviewerAgent.ReviewStatus.NeedsReview, result.Status);
        }

        [Fact]
        public async Task GenerateReview_UnknownType_ReturnsNeedsReview()
        {
            var kernel = new Kernel();
            var appConfig = new JuniorDev.Contracts.AppConfig();
            var agent = new ReviewerAgent(kernel, appConfig);

            var artifact = new JuniorDev.Contracts.ArtifactAvailable(
                Id: System.Guid.NewGuid(),
                Correlation: new JuniorDev.Contracts.Correlation(System.Guid.NewGuid()),
                Artifact: new JuniorDev.Contracts.Artifact(Kind: "Unknown", Name: "u1", InlineText: "")
            );

            var result = await agent.GenerateReviewAsync(artifact);
            Assert.Equal(ReviewerAgent.ReviewStatus.NeedsReview, result.Status);
        }
    }
}
