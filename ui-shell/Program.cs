using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using JuniorDev.Agents;
using JuniorDev.Contracts;
using JuniorDev.Orchestrator;
using JuniorDev.WorkItems.GitHub;
using JuniorDev.WorkItems.Jira;
using JuniorDev.VcsGit;
using JuniorDev.Build.Dotnet;
using DotNetEnv;

namespace Ui.Shell;

/// <summary>
/// Dummy chat client for test/offline mode that doesn't require external API keys
/// </summary>
public class DummyChatClient : Microsoft.Extensions.AI.IChatClient
{
    public async Task<Microsoft.Extensions.AI.ChatResponse> GetResponseAsync(IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages, Microsoft.Extensions.AI.ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        // Return a simple dummy response
        var response = new Microsoft.Extensions.AI.ChatResponse(new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.Assistant, "This is a dummy response for test mode. AI features are disabled."));
        return response;
    }

    public async IAsyncEnumerable<Microsoft.Extensions.AI.ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages, Microsoft.Extensions.AI.ChatOptions? options = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Return a simple dummy streaming response
        var update = new Microsoft.Extensions.AI.ChatResponseUpdate(Microsoft.Extensions.AI.ChatRole.Assistant, "This is a dummy streaming response for test mode. AI features are disabled.");
        yield return update;
    }

    public void Dispose()
    {
        // No resources to dispose
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        return null;
    }
}

static class Program
{
    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        try
        {
            // Load configuration
            var configuration = LoadConfiguration();

            // Setup DI container
            var services = new ServiceCollection();
            ConfigureServices(services, configuration);

            var serviceProvider = services.BuildServiceProvider();

            // Configure AI client (needs service provider for factory)
            ConfigureAIClient(services, configuration, serviceProvider);

            // Rebuild service provider with updated services
            serviceProvider = services.BuildServiceProvider();

            // Configure AI client (needs service provider for factory)
            ConfigureAIClient(services, configuration, serviceProvider);

            // Rebuild service provider with updated services
            serviceProvider = services.BuildServiceProvider();

            // Get required services
            var sessionManager = serviceProvider.GetRequiredService<ISessionManager>();
            var config = serviceProvider.GetRequiredService<IConfiguration>();
            var chatClient = serviceProvider.GetRequiredService<Microsoft.Extensions.AI.IChatClient>();

            // Check for test mode
            var isTestMode = Environment.GetCommandLineArgs().Contains("--test") || Environment.GetCommandLineArgs().Contains("-t");

            // Run the application
            var mainForm = new MainForm(sessionManager, config, chatClient, serviceProvider, isTestMode);
            Application.Run(mainForm);
            
            // Dispose the service provider when the application exits
            (serviceProvider as IDisposable)?.Dispose();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Application failed to start: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            throw;
        }
    }

    private static IConfiguration LoadConfiguration()
    {
        var workspaceRoot = FindWorkspaceRoot();
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";

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

        var builder = new ConfigurationBuilder()
            .SetBasePath(workspaceRoot)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddJsonFile($"appsettings.{environment}.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables("JUNIORDEV__")
            .AddUserSecrets(typeof(Program).Assembly, optional: true);

        return builder.Build();
    }

    private static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // Add configuration
        services.AddSingleton<IConfiguration>(configuration);

        // Debug: Check configuration values
        var openaiKey = configuration["AppConfig:Auth:OpenAI:ApiKey"];
        var envVar = Environment.GetEnvironmentVariable("JUNIORDEV__APPCONFIG__AUTH__OPENAI__APIKEY");
        Console.WriteLine($"Configuration AppConfig:Auth:OpenAI:ApiKey: {!string.IsNullOrEmpty(openaiKey)}");
        Console.WriteLine($"Environment variable: {!string.IsNullOrEmpty(envVar)}");

        // Add AppConfig from configuration
        var appConfig = JuniorDev.Contracts.ConfigBuilder.GetAppConfig(configuration);
        services.AddSingleton<AppConfig>(appConfig);

        // Add logging
        services.AddLogging(configure => configure.AddConsole());

        // Add orchestrator
        services.AddOrchestrator();

        // Add agents
        services.AddAgentSdk();

        // Register adapters based on configuration
        RegisterAdapters(services, configuration);

        // Add UI services
        // services.AddTransient<MainForm>(); // No longer needed - created manually
    }

    private static void ConfigureAIClient(IServiceCollection services, IConfiguration configuration, IServiceProvider serviceProvider)
    {
        // Try to register a real AI client, fall back to dummy if needed
        var aiClient = RegisterClientViaFallbackMethods(serviceProvider, configuration);
        services.AddSingleton<Microsoft.Extensions.AI.IChatClient>(aiClient);
    }

    private static Microsoft.Extensions.AI.IChatClient RegisterClientViaFallbackMethods(IServiceProvider services, IConfiguration configuration)
    {
        try
        {
            // Try to get a real chat client factory
            var chatClientFactory = services.GetRequiredService<IChatClientFactory>();

            // Get the underlying client for the default agent profile
            var underlyingClient = chatClientFactory.GetUnderlyingClientFor("default");

            if (underlyingClient is Microsoft.Extensions.AI.IChatClient aiClient)
            {
                Console.WriteLine("Using real AI client from ChatClientFactory");
                return aiClient;
            }
            else
            {
                Console.WriteLine("No real AI client available, falling back to dummy client");
                return new DummyChatClient();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to create AI client: {ex.Message}, falling back to dummy client");
            return new DummyChatClient();
        }
    }

    private static void RegisterAdapters(IServiceCollection services, IConfiguration configuration)
    {
        // Get the unified AppConfig
        var appConfig = ConfigBuilder.GetAppConfig(configuration);

        // Validate live adapter configuration if any live adapters are selected
        ConfigBuilder.ValidateLiveAdapters(appConfig);

        // Register adapters based on the Adapters configuration
        var adapters = appConfig.Adapters ?? new AdaptersConfig("fake", "fake", "powershell");

        // Register work items adapter
        if (adapters.WorkItemsAdapter?.ToLower() == "jira")
        {
            services.AddWorkItemAdapters(appConfig);
        }
        else if (adapters.WorkItemsAdapter?.ToLower() == "github")
        {
            services.AddGitHubWorkItemAdapter(appConfig);
        }
        else
        {
            // Default to fake adapters
            services.AddSingleton<IAdapter, FakeWorkItemsAdapter>();
        }

        // Register VCS adapter
        if (adapters.VcsAdapter?.ToLower() == "git")
        {
            services.AddVcsGitAdapter(appConfig);
        }
        else
        {
            // Default to fake VCS adapter
            services.AddSingleton<IAdapter, FakeVcsAdapter>();
        }

        // Register build adapter
        if (adapters.BuildAdapter?.ToLower() == "dotnet")
        {
            services.AddDotnetBuildAdapter();
        }
        else
        {
            // Default to fake build adapter
            services.AddSingleton<IAdapter, FakeBuildAdapter>();
        }

        // Terminal adapter is always fake for now
        // Could be extended to support different terminal types
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
}
