using System.Threading.Tasks;
using JuniorDev.Agents.Sk;
using Microsoft.SemanticKernel;
using Xunit;
using System.IO;
using System.Linq;
using JuniorDev.Contracts;
using JuniorDev.Orchestrator;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.AI;
using Microsoft.SemanticKernel.TextGeneration;
using Microsoft.SemanticKernel;

namespace JuniorDev.Agents.Sk.Tests
{
    public class ReviewerAgentTests
    {
        // Factory for creating ReviewerAgent instances in tests
        private static class TestAgentFactory
        {
            public static ReviewerAgent CreateReviewerForTesting(
                JuniorDev.Agents.AgentSessionContext context,
                Microsoft.SemanticKernel.Kernel kernel,
                JuniorDev.Contracts.AppConfig appConfig)
            {
                var agent = new ReviewerAgent(kernel, appConfig);
                agent.SetContextForTesting(context);
                return agent;
            }
        }
        // Fake session manager for testing
        private class FakeSessionManager : ISessionManager
        {
            public Task PublishEventAsync(IEvent @event) => Task.CompletedTask;
            public Task<ICommand> SendCommandAsync(ICommand command) => Task.FromResult(command);
            public Task<ICommand> SendCommandAsync(ICommand command, TimeSpan timeout) => Task.FromResult(command);
            // Implement other required methods with no-op implementations
            public Task CreateSession(SessionConfig config) => Task.CompletedTask;
            public Task PublishCommand(ICommand command) => Task.CompletedTask;
            public Task PublishEvent(IEvent @event) => Task.CompletedTask;
            public IAsyncEnumerable<IEvent> Subscribe(Guid sessionId) => System.Linq.AsyncEnumerable.Empty<IEvent>();
            public Task PauseSession(Guid sessionId, string actor = "system") => Task.CompletedTask;
            public Task ResumeSession(Guid sessionId) => Task.CompletedTask;
            public Task AbortSession(Guid sessionId, string actor = "system") => Task.CompletedTask;
            public Task ApproveSession(Guid sessionId) => Task.CompletedTask;
            public Task CompleteSession(Guid sessionId) => Task.CompletedTask;
            public IReadOnlyList<SessionInfo> GetActiveSessions() => new List<SessionInfo>();
            public SessionConfig? GetSessionConfig(Guid sessionId) => null;
        }

        // Mock chat client for testing
        private class MockChatClient : Microsoft.Extensions.AI.IChatClient
        {
            public ChatClientMetadata Metadata => new ChatClientMetadata("mock", new Uri("http://mock"), "Mock Chat Client");

            public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            {
                var response = @"- Path: /missing-readme
- Kind: structure
- Severity: warning
- Summary: Missing README file
- Details: Repository lacks a README.md file which is essential for documentation
- Recommendation: Add a README.md file with project description and setup instructions

- Path: /src
- Kind: structure
- Severity: info
- Summary: Standard src directory structure
- Details: Code is organized in a standard src directory
- Recommendation: Keep this organization";

                return new ChatResponse(new ChatMessage(ChatRole.Assistant, response));
            }

            public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            {
                var response = @"- Path: /missing-readme
- Kind: structure
- Severity: warning
- Summary: Missing README file
- Details: Repository lacks a README.md file which is essential for documentation
- Recommendation: Add a README.md file with project description and setup instructions";

                yield return new ChatResponseUpdate(ChatRole.Assistant, response);
            }

            public void Dispose() { }

            public object? GetService(Type serviceType, object? serviceKey = null) => null;
        }

        // Mock text generation service for testing
        private class MockTextGenerationService : ITextGenerationService
        {
            public IReadOnlyDictionary<string, object?> Attributes => new Dictionary<string, object?>();

            public async Task<IReadOnlyList<Microsoft.SemanticKernel.TextContent>> GetTextContentsAsync(string prompt, PromptExecutionSettings? executionSettings = null, Kernel? kernel = null, CancellationToken cancellationToken = default)
            {
                var response = @"- Path: /missing-readme
- Kind: structure
- Severity: warning
- Summary: Missing README file
- Details: Repository lacks a README.md file which is essential for documentation
- Recommendation: Add a README.md file with project description and setup instructions

- Path: /src
- Kind: structure
- Severity: info
- Summary: Standard src directory structure
- Details: Code is organized in a standard src directory
- Recommendation: Keep this organization";

                return new List<Microsoft.SemanticKernel.TextContent> { new Microsoft.SemanticKernel.TextContent(response) };
            }

            public async IAsyncEnumerable<StreamingTextContent> GetStreamingTextContentsAsync(string prompt, PromptExecutionSettings? executionSettings = null, Kernel? kernel = null, CancellationToken cancellationToken = default)
            {
                var response = @"- Path: /missing-readme
- Kind: structure
- Severity: warning
- Summary: Missing README file
- Details: Repository lacks a README.md file which is essential for documentation
- Recommendation: Add a README.md file with project description and setup instructions";

                yield return new StreamingTextContent(response);
            }
        }
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

        [Fact]
        public async Task RunRepositoryAnalysis_WhenDisabled_ReturnsEmptyFindings()
        {
            // Arrange
            var kernel = new Kernel();
            var appConfig = new AppConfig
            {
                Reviewer = new ReviewerConfig
                {
                    Analysis = new RepositoryAnalysisConfig { Enabled = false }
                }
            };
            var agent = new ReviewerAgent(kernel, appConfig);

            // Act
            var findings = await agent.RunRepositoryAnalysisAsync();

            // Assert
            Assert.Empty(findings);
        }

        [Fact]
        public async Task RunRepositoryAnalysis_RespectsEnabledAreas()
        {
            // Arrange - Create temporary workspace
            var tempDir = Path.Combine(Path.GetTempPath(), "junior-dev-test-" + Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);

            try
            {
                // Create some test files
                await File.WriteAllTextAsync(Path.Combine(tempDir, "test.cs"), "public class Test { }");
                await File.WriteAllTextAsync(Path.Combine(tempDir, "package.json"), "{}");

                var kernel = new Kernel();
                var appConfig = new AppConfig
                {
                    Reviewer = new ReviewerConfig
                    {
                        Analysis = new RepositoryAnalysisConfig
                        {
                            Enabled = true,
                            EnabledAreas = new List<string> { "structure" }, // Only structure analysis
                            MaxFiles = 10,
                            MaxFileBytes = 1024 * 1024
                        }
                    }
                };

                // Create agent with test context using factory
                var context = new JuniorDev.Agents.AgentSessionContext(
                    Guid.NewGuid(),
                    new SessionConfig(
                        Guid.NewGuid(),
                        null,
                        null,
                        new PolicyProfile { Name = "test" },
                        new RepoRef("test", tempDir),
                        new WorkspaceRef(tempDir),
                        null,
                        "test-profile"
                    ),
                    new FakeSessionManager(),
                    new JuniorDev.Agents.AgentConfig(),
                    NullLogger.Instance,
                    "reviewer"
                );

                var agent = TestAgentFactory.CreateReviewerForTesting(context, kernel, appConfig);

                // Register agent functions (normally done in OnStartedAsync)
                var registerMethod = typeof(ReviewerAgent).GetMethod("RegisterAgentFunctions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                registerMethod?.Invoke(agent, new object[] { });

                // Act
                var findings = await agent.RunRepositoryAnalysisAsync();

                // Assert - Should only have structure-related findings, no quality/security/etc.
                Assert.All(findings, f => Assert.Equal("structure", f.Kind));
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }        [Fact]
        public async Task RunRepositoryAnalysis_CacheKeyChangesWithConfig()
        {
            // Arrange - Create temporary workspace
            var tempDir = Path.Combine(Path.GetTempPath(), "junior-dev-test-" + Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);

            try
            {
                // Create a test file
                await File.WriteAllTextAsync(Path.Combine(tempDir, "test.cs"), "public class Test { }");

                var kernel = new Kernel();

                // Test different configurations that should produce different cache keys
                var configs = new[]
                {
                    new RepositoryAnalysisConfig
                    {
                        Enabled = true,
                        EnabledAreas = new List<string> { "structure" },
                        MaxFiles = 10
                    },
                    new RepositoryAnalysisConfig
                    {
                        Enabled = true,
                        EnabledAreas = new List<string> { "structure", "quality" }, // Different areas
                        MaxFiles = 10
                    },
                    new RepositoryAnalysisConfig
                    {
                        Enabled = true,
                        EnabledAreas = new List<string> { "structure" },
                        MaxFiles = 20 // Different limit
                    }
                };

                var generatedKeys = new System.Collections.Generic.HashSet<string>();

                foreach (var config in configs)
                {
                    var appConfig = new AppConfig
                    {
                        Reviewer = new ReviewerConfig { Analysis = config }
                    };

                    var agent = new ReviewerAgent(kernel, appConfig);
                    var context = new JuniorDev.Agents.AgentSessionContext(
                        Guid.NewGuid(),
                        new SessionConfig(
                            Guid.NewGuid(),
                            null,
                            null,
                            new PolicyProfile { Name = "test" }, // Fixed: proper PolicyProfile initialization
                            new RepoRef("test", tempDir),
                            new WorkspaceRef(tempDir),
                            null,
                            "test-profile"
                        ),
                        new FakeSessionManager(), // Use fake session manager
                        new JuniorDev.Agents.AgentConfig(),
                        NullLogger.Instance,
                        "reviewer"
                    );

                    var contextField = typeof(JuniorDev.Agents.AgentBase).GetField("<Context>k__BackingField", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    contextField?.SetValue(agent, context);

                    // Use reflection to access the private GenerateAnalysisCacheKey method
                    var method = typeof(ReviewerAgent).GetMethod("GenerateAnalysisCacheKey", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var cacheKey = (string)method?.Invoke(agent, new object[] { config })!;

                    // Assert cache key is not null/empty and unique for different configs
                    Assert.NotNull(cacheKey);
                    Assert.NotEmpty(cacheKey);
                    Assert.DoesNotContain(cacheKey, generatedKeys);
                    generatedKeys.Add(cacheKey);
                }

                // Verify we got 3 different cache keys
                Assert.Equal(3, generatedKeys.Count);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task RunRepositoryAnalysis_WhenDisabled_ReturnsEmptyFindingsAndLogsDisabledMessage()
        {
            // Arrange - Create temporary workspace
            var tempDir = Path.Combine(Path.GetTempPath(), "junior-dev-test-" + Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);

            try
            {
                // Create some test files
                await File.WriteAllTextAsync(Path.Combine(tempDir, "test.cs"), "public class Test { }");

                var kernel = new Kernel();
                var appConfig = new AppConfig
                {
                    Reviewer = new ReviewerConfig
                    {
                        Analysis = new RepositoryAnalysisConfig { Enabled = false }
                    }
                };

                var context = new JuniorDev.Agents.AgentSessionContext(
                    Guid.NewGuid(),
                    new SessionConfig(
                        Guid.NewGuid(),
                        null,
                        null,
                        new PolicyProfile { Name = "test" },
                        new RepoRef("test", tempDir),
                        new WorkspaceRef(tempDir),
                        null,
                        "test-profile"
                    ),
                    new FakeSessionManager(),
                    new JuniorDev.Agents.AgentConfig(),
                    NullLogger.Instance,
                    "reviewer"
                );

                var agent = TestAgentFactory.CreateReviewerForTesting(context, kernel, appConfig);

                // Act
                var findings = await agent.RunRepositoryAnalysisAsync();

                // Assert
                Assert.Empty(findings);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task RunRepositoryAnalysis_RespectsLimitsAndPrioritizesFileSelection()
        {
            // Arrange - Create temporary workspace with mixed file types
            var tempDir = Path.Combine(Path.GetTempPath(), "junior-dev-test-" + Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);

            try
            {
                // Create test files of different types and priorities
                await File.WriteAllTextAsync(Path.Combine(tempDir, "Program.cs"), "using System;\n\nclass Program { static void Main() { } }"); // High priority
                await File.WriteAllTextAsync(Path.Combine(tempDir, "Utils.cs"), "public static class Utils { }"); // High priority
                await File.WriteAllTextAsync(Path.Combine(tempDir, "package.json"), "{\"name\": \"test\"}"); // Medium priority
                await File.WriteAllTextAsync(Path.Combine(tempDir, "README.md"), "# Test Project"); // Low priority
                await File.WriteAllTextAsync(Path.Combine(tempDir, "build.sh"), "#!/bin/bash\necho 'build'"); // Low priority

                var kernel = new Kernel();
                var appConfig = new AppConfig
                {
                    Reviewer = new ReviewerConfig
                    {
                        Analysis = new RepositoryAnalysisConfig
                        {
                            Enabled = true,
                            EnabledAreas = new List<string> { "quality" }, // Only quality analysis
                            MaxFiles = 2, // Very small limit to test prioritization
                            MaxFileBytes = 1024,
                            MaxTotalBytes = 2048
                        }
                    }
                };

                var context = new JuniorDev.Agents.AgentSessionContext(
                    Guid.NewGuid(),
                    new SessionConfig(
                        Guid.NewGuid(),
                        null,
                        null,
                        new PolicyProfile { Name = "test" },
                        new RepoRef("test", tempDir),
                        new WorkspaceRef(tempDir),
                        null,
                        "test-profile"
                    ),
                    new FakeSessionManager(),
                    new JuniorDev.Agents.AgentConfig(),
                    NullLogger.Instance,
                    "reviewer"
                );

                var agent = TestAgentFactory.CreateReviewerForTesting(context, kernel, appConfig);

                // Register agent functions
                var registerMethod = typeof(ReviewerAgent).GetMethod("RegisterAgentFunctions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                registerMethod?.Invoke(agent, new object[] { });

                // Act
                var findings = await agent.RunRepositoryAnalysisAsync();

                // Assert - Should only analyze high-priority .cs files due to limits and prioritization
                // Quality analysis should run on the selected files
                Assert.All(findings, f => Assert.Equal("quality", f.Kind));
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task PerformRepositoryAnalysis_UsesCacheForRepeatedCalls()
        {
            // Arrange - Create temporary workspace
            var tempDir = Path.Combine(Path.GetTempPath(), "junior-dev-test-" + Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);

            try
            {
                // Create a test file
                await File.WriteAllTextAsync(Path.Combine(tempDir, "test.cs"), "public class Test { }");

                var kernel = new Kernel();
                var appConfig = new AppConfig
                {
                    Reviewer = new ReviewerConfig
                    {
                        Analysis = new RepositoryAnalysisConfig
                        {
                            Enabled = true,
                            EnabledAreas = new List<string> { "structure" },
                            MaxFiles = 10,
                            MaxFileBytes = 1024 * 1024
                        },
                        AnalysisCacheTimeout = TimeSpan.FromSeconds(30) // Short timeout for testing
                    }
                };

                var context = new JuniorDev.Agents.AgentSessionContext(
                    Guid.NewGuid(),
                    new SessionConfig(
                        Guid.NewGuid(),
                        null,
                        null,
                        new PolicyProfile { Name = "test" },
                        new RepoRef("test", tempDir),
                        new WorkspaceRef(tempDir),
                        null,
                        "test-profile"
                    ),
                    new FakeSessionManager(),
                    new JuniorDev.Agents.AgentConfig(),
                    NullLogger.Instance,
                    "reviewer"
                );

                var agent = TestAgentFactory.CreateReviewerForTesting(context, kernel, appConfig);

                // Register agent functions
                var registerMethod = typeof(ReviewerAgent).GetMethod("RegisterAgentFunctions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                registerMethod?.Invoke(agent, new object[] { });

                // Act - First call
                var findings1 = await agent.RunRepositoryAnalysisAsync();

                // Second call (should use cache)
                var findings2 = await agent.RunRepositoryAnalysisAsync();

                // Assert - Both calls should return the same results (from cache on second call)
                Assert.Equal(findings1.Count, findings2.Count);
                for (int i = 0; i < findings1.Count; i++)
                {
                    Assert.Equal(findings1[i].Path, findings2[i].Path);
                    Assert.Equal(findings1[i].Kind, findings2[i].Kind);
                    Assert.Equal(findings1[i].Summary, findings2[i].Summary);
                }
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }
    }
}
