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

namespace Ui.Shell;

/// <summary>
/// Dummy chat client for test/offline mode that doesn't require external API keys
/// </summary>
public class DummyChatClient : Microsoft.Extensions.AI.IChatClient
{
    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        // Return a simple dummy response
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, "This is a dummy response for test mode. AI features are disabled."));
        return response;
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Return a simple dummy streaming response
        var update = new ChatResponseUpdate(ChatRole.Assistant, "This is a dummy streaming response for test mode. AI features are disabled.");
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

        var builder = new ConfigurationBuilder()
            .SetBasePath(workspaceRoot)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddJsonFile($"appsettings.{environment}.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables()
            .AddUserSecrets(typeof(DummyChatClient).Assembly, optional: true);

        return builder.Build();
    }

    private static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // Add configuration
        services.AddSingleton<IConfiguration>(configuration);

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
                Console.WriteLine("No real AI client available, using dummy client");
                return new DummyChatClient();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to create AI client: {ex.Message}, using dummy client");
            return new DummyChatClient();
        }
    }

    private static void RegisterAdapters(IServiceCollection services, IConfiguration configuration)
    {
        var useLiveAdapters = configuration.GetValue<bool>("UseLiveAdapters", false);

        if (useLiveAdapters)
        {
            // Register live adapters
            services.AddGitHubWorkItemAdapter();
            services.AddWorkItemAdapters(useReal: true);
            services.AddVcsGitAdapter(new VcsConfig(), isFake: false);
        }
        else
        {
            // Register mock adapters for testing (already registered by orchestrator)
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
}
