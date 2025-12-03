using System;
using System.IO;
using Xunit;
using DotNetEnv;

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
        // Load .env.local file if it exists (same as main application)
        LoadEnvironmentFile();

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

    private static void LoadEnvironmentFile()
    {
        // Find workspace root (same logic as Program.cs)
        var workspaceRoot = FindWorkspaceRoot();
        
        // Load .env.local if it exists (highest priority, ignored by git)
        var envFilePath = Path.Combine(workspaceRoot, ".env.local");
        if (File.Exists(envFilePath))
        {
            Console.WriteLine($"Loading .env.local from: {envFilePath}");
            Env.Load(envFilePath);
            Console.WriteLine("Loaded .env.local file");
            
            // Debug: Check if our environment variable was loaded
            var apiKey = Environment.GetEnvironmentVariable("JUNIORDEV__APPCONFIG__AUTH__OPENAI__APIKEY");
            Console.WriteLine($"JUNIORDEV__APPCONFIG__AUTH__OPENAI__APIKEY loaded: {!string.IsNullOrEmpty(apiKey)}");
        }
        else
        {
            Console.WriteLine($".env.local not found at: {envFilePath}");
        }
    }

    private static string FindWorkspaceRoot()
    {
        var currentDir = Directory.GetCurrentDirectory();

        // Look for solution file or common workspace markers
        var directory = new DirectoryInfo(currentDir);
        while (directory != null)
        {
            if (directory.GetFiles("*.sln").Any() ||
                directory.GetFiles("Directory.Packages.props").Any() ||
                directory.GetFiles("global.json").Any())
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }

        // Fallback to current directory
        return currentDir;
    }

    public void Dispose()
    {
        // Cleanup if needed
    }
}