using System;
using Xunit;

namespace JuniorDev.Agents.Sk.Tests;

/// <summary>
/// Test collection for AI integration tests that require real AI services.
/// These tests are skipped unless RUN_AI_TESTS=1 and valid credentials are configured.
/// </summary>
[CollectionDefinition("AI Integration Tests")]
public class AiIntegrationTestCollection : ICollectionFixture<AiTestFixture>
{
    // This class has no code, but serves as an anchor for the collection definition
}

/// <summary>
/// Fixture for AI integration tests that ensures proper setup and skipping when not configured.
/// </summary>
public class AiTestFixture : IDisposable
{
    public bool ShouldRunAiTests { get; }

    public AiTestFixture()
    {
        // Check if AI tests should run
        var runAiTests = Environment.GetEnvironmentVariable("RUN_AI_TESTS") == "1";

        // Check if we have valid OpenAI credentials
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ??
                    Environment.GetEnvironmentVariable("JUNIORDEV__AppConfig__Auth__OpenAI__ApiKey");

        var hasValidCredentials = !string.IsNullOrEmpty(apiKey) &&
                                 !apiKey.Contains("your-") &&
                                 apiKey != "your-openai-key";

        ShouldRunAiTests = runAiTests && hasValidCredentials;

        if (!ShouldRunAiTests)
        {
            Console.WriteLine("AI integration tests are disabled. Set RUN_AI_TESTS=1 and provide valid OpenAI credentials to enable.");
            if (!runAiTests)
            {
                Console.WriteLine("  - Set environment variable: RUN_AI_TESTS=1");
            }
            if (!hasValidCredentials)
            {
                Console.WriteLine("  - Set environment variable: OPENAI_API_KEY=<your-key>");
                Console.WriteLine("    Or: JUNIORDEV__AppConfig__Auth__OpenAI__ApiKey=<your-key>");
            }
        }
        else
        {
            Console.WriteLine("AI integration tests are enabled with valid credentials.");
        }
    }

    public void Dispose()
    {
        // Cleanup if needed
    }
}