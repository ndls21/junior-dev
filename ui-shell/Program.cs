using System;
using System.IO;
using System.Windows.Forms;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using JuniorDev.Contracts;
using JuniorDev.Orchestrator;
using JuniorDev.Agents;
using JuniorDev.Agents.Sk;
using JuniorDev.WorkItems.Jira;
using JuniorDev.VcsGit;
using JuniorDev.WorkItems.GitHub;
using JuniorDev.Build.Dotnet;
using Microsoft.SemanticKernel;
using Microsoft.Extensions.AI;

namespace Ui.Shell;

static class Program
{
    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // Setup global exception handling to prevent JIT dialogs during testing
        Application.ThreadException += (sender, e) =>
        {
            Console.WriteLine($"UI Thread Exception: {e.Exception.Message}");
            Console.WriteLine($"Stack Trace: {e.Exception.StackTrace}");
            // Don't show dialog - just log and continue
        };

        AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
        {
            var exception = e.ExceptionObject as Exception;
            Console.WriteLine($"Unhandled Exception: {exception?.Message ?? e.ExceptionObject?.ToString()}");
            Console.WriteLine($"Stack Trace: {exception?.StackTrace ?? "No stack trace available"}");
            // Don't show dialog - just log
        };

        // Setup DI container with orchestrator services
        var services = ConfigureServices();
        var serviceProvider = services.BuildServiceProvider();

        // Configure DevExpress AI to use our service provider BEFORE creating the main form
        try
        {
            Console.WriteLine("Attempting to configure DevExpress AI service provider...");

            // Try multiple approaches to configure DevExpress AI integration
            ConfigureDevExpressAI(serviceProvider);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to configure DevExpress AI service provider: {ex.Message}");
        }

        // Create and run the main form with DI services
        var mainForm = serviceProvider.GetRequiredService<MainForm>();
        
        Application.Run(mainForm);
    }

    private static IServiceCollection ConfigureServices()
    {
        var services = new ServiceCollection();

        // Load configuration
        var configuration = LoadConfiguration();
        services.AddSingleton<IConfiguration>(configuration);

        // Add logging
        services.AddLogging();

        // Get app config
        var appConfig = ConfigBuilder.GetAppConfig(configuration);
        services.AddSingleton(appConfig);

        // Debug: Check if OpenAI key is loaded
        Console.WriteLine($"OpenAI API Key configured: {!string.IsNullOrEmpty(appConfig.Auth?.OpenAI?.ApiKey) && !appConfig.Auth.OpenAI.ApiKey.Contains("your-")}");
        if (appConfig.Auth?.OpenAI?.ApiKey != null)
        {
            Console.WriteLine($"OpenAI API Key length: {appConfig.Auth.OpenAI.ApiKey.Length}");
            Console.WriteLine($"OpenAI API Key starts with: {appConfig.Auth.OpenAI.ApiKey.Substring(0, Math.Min(10, appConfig.Auth.OpenAI.ApiKey.Length))}");
        }

        // Register orchestrator services
        services.AddOrchestrator();
        services.AddAgentSdk();
        
        // Register build adapter
        services.AddDotnetBuildAdapter();
        
        // Register AI services - factory handles per-agent configuration
        services.AddSingleton<Microsoft.Extensions.AI.IChatClient>(sp =>
        {
            Console.WriteLine("Creating DevExpress AI client...");
            var factory = sp.GetRequiredService<IChatClientFactory>();
            Console.WriteLine("Got factory, getting underlying client for 'default'...");
            // Get a default client for DevExpress (uses first available agent profile or defaults)
            var underlyingClient = factory.GetUnderlyingClientFor("default");
            if (underlyingClient is Microsoft.Extensions.AI.IChatClient chatClient)
            {
                Console.WriteLine("Got real chat client from factory");
                return chatClient;
            }
            // Fallback to dummy if factory returns null (for dummy clients)
            Console.WriteLine("Factory returned null, using dummy client");
            return new DummyChatClient();
        });
        
        // Register Semantic Kernel for agents (per-agent kernels will be created by agents themselves)
        services.AddSingleton<Kernel>(sp => new Kernel());
        
        services.AddAgent<PlannerAgent>();
        services.AddAgent<ExecutorAgent>();
        services.AddAgent<ReviewerAgent>();

        // Check if we should use live adapters (validate that config has real values, not placeholders)
        var hasJiraConfig = appConfig.Auth?.Jira != null &&
                           !string.IsNullOrEmpty(appConfig.Auth.Jira.BaseUrl) &&
                           !string.IsNullOrEmpty(appConfig.Auth.Jira.Username) &&
                           !string.IsNullOrEmpty(appConfig.Auth.Jira.ApiToken) &&
                           !appConfig.Auth.Jira.BaseUrl.Contains("your-") &&
                           !appConfig.Auth.Jira.Username.Contains("your-") &&
                           !appConfig.Auth.Jira.ApiToken.Contains("your-");

        var hasGitConfig = appConfig.Auth?.Git != null &&
                          (!string.IsNullOrEmpty(appConfig.Auth.Git.PersonalAccessToken) ||
                           !string.IsNullOrEmpty(appConfig.Auth.Git.SshKeyPath)) &&
                          !appConfig.Auth.Git.PersonalAccessToken?.Contains("your-") == true;

        var hasGitHubConfig = appConfig.Auth?.GitHub != null &&
                             !string.IsNullOrEmpty(appConfig.Auth.GitHub.Token) &&
                             (!string.IsNullOrEmpty(appConfig.Auth.GitHub.DefaultOrg) ||
                              !string.IsNullOrEmpty(appConfig.Auth.GitHub.DefaultRepo)) &&
                             !appConfig.Auth.GitHub.Token.Contains("your-") &&
                             !appConfig.Auth.GitHub.DefaultOrg?.Contains("your-") == true &&
                             !appConfig.Auth.GitHub.DefaultRepo?.Contains("your-") == true;

        var useLiveJira = hasJiraConfig || Environment.GetEnvironmentVariable("JIRA_URL") != null;
        var useLiveGit = hasGitConfig || Environment.GetEnvironmentVariable("GIT_CONFIGURED") == "1";
        var useLiveGitHub = hasGitHubConfig || Environment.GetEnvironmentVariable("GITHUB_TOKEN") != null;

        if (useLiveJira)
        {
            // Set environment variables for live Jira adapter
            if (appConfig.Auth?.Jira != null)
            {
                Environment.SetEnvironmentVariable("JIRA_URL", appConfig.Auth.Jira.BaseUrl);
                Environment.SetEnvironmentVariable("JIRA_USER", appConfig.Auth.Jira.Username);
                Environment.SetEnvironmentVariable("JIRA_TOKEN", appConfig.Auth.Jira.ApiToken);
                Environment.SetEnvironmentVariable("JIRA_PROJECT", "TEST"); // Default for testing
            }

            // Override with real Jira adapter
            services.AddSingleton<IAdapter>(new JiraAdapter());
            Console.WriteLine("Registered real Jira adapter");
        }

        if (useLiveGit)
        {
            // Override with real Git adapter
            services.AddSingleton<IAdapter>(new VcsGitAdapter(new VcsConfig
            {
                RepoPath = "/tmp/ui-git-repo",
                AllowPush = Environment.GetEnvironmentVariable("UI_ALLOW_PUSH") == "1",
                IsIntegrationTest = true
            }, isFake: false));
            Console.WriteLine("Registered real Git adapter");
        }

        if (useLiveGitHub)
        {
            // Set environment variables for live GitHub adapter
            if (appConfig.Auth?.GitHub != null)
            {
                Environment.SetEnvironmentVariable("GITHUB_TOKEN", appConfig.Auth.GitHub.Token);
                // Set repo from config or use environment variable
                var repo = !string.IsNullOrEmpty(appConfig.Auth.GitHub.DefaultRepo) 
                    ? $"{appConfig.Auth.GitHub.DefaultOrg ?? "owner"}/{appConfig.Auth.GitHub.DefaultRepo}"
                    : Environment.GetEnvironmentVariable("GITHUB_REPO") ?? "owner/repo";
                Environment.SetEnvironmentVariable("GITHUB_REPO", repo);
            }

            // Override with real GitHub adapter
            services.AddSingleton<IAdapter>(new GitHubAdapter());
            Console.WriteLine("Registered real GitHub adapter");
        }

        if (useLiveJira || useLiveGit || useLiveGitHub)
        {
            Console.WriteLine("UI configured for LIVE mode with real adapters");
        }
        else
        {
            Console.WriteLine("UI configured for MOCK mode with fake adapters");
        }

        // Register the main form with service provider injection
        services.AddTransient<MainForm>(sp => new MainForm(
            sp.GetService<ISessionManager>(),
            sp.GetService<AppConfig>(),
            false, // isTestMode
            sp // serviceProvider
        ));

        return services;
    }

    private static IConfiguration LoadConfiguration()
    {
        // Find the workspace root by looking for appsettings.json
        var currentDir = Directory.GetCurrentDirectory();
        var workspaceRoot = FindWorkspaceRoot(currentDir);

        var builder = new ConfigurationBuilder()
            .SetBasePath(workspaceRoot)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables("JUNIORDEV__")
            .AddUserSecrets(typeof(Program).Assembly, optional: true);

        return builder.Build();
    }

    private static void ConfigureDevExpressAI(IServiceProvider serviceProvider)
    {
        // Get the chat client (real or dummy) that should be registered
        var chatClient = serviceProvider.GetService(typeof(Microsoft.Extensions.AI.IChatClient)) as Microsoft.Extensions.AI.IChatClient;
        if (chatClient == null)
        {
            chatClient = new DummyChatClient();
            Console.WriteLine("No chat client available, using dummy client");
        }

        // Try the supported API first: AIExtensionsContainerDesktop.Default.RegisterChatClient
        try
        {
            var aiExtensionsType = Type.GetType("DevExpress.AIIntegration.AIExtensionsContainerDesktop, DevExpress.AIIntegration");
            if (aiExtensionsType != null)
            {
                var defaultProperty = aiExtensionsType.GetProperty("Default");
                if (defaultProperty != null)
                {
                    var defaultInstance = defaultProperty.GetValue(null);
                    if (defaultInstance != null)
                    {
                        var registerMethod = aiExtensionsType.GetMethod("RegisterChatClient", new[] { typeof(Microsoft.Extensions.AI.IChatClient) });
                        if (registerMethod != null)
                        {
                            registerMethod.Invoke(defaultInstance, new object[] { chatClient });
                            Console.WriteLine($"Successfully registered {chatClient.GetType().Name} with AIExtensionsContainerDesktop");
                            return;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to register with AIExtensionsContainerDesktop: {ex.Message}");
        }

        // Fallback: Try other approaches if the primary API fails
        Console.WriteLine("Warning: Could not configure DevExpress AI integration via primary API - trying fallbacks");

        // Try to set ServiceProvider on DevExpress.AIIntegration.AIIntegration
        var aiIntegrationType = Type.GetType("DevExpress.AIIntegration.AIIntegration, DevExpress.AIIntegration");
        if (aiIntegrationType != null)
        {
            var serviceProviderProperty = aiIntegrationType.GetProperty("ServiceProvider");
            if (serviceProviderProperty != null && serviceProviderProperty.CanWrite)
            {
                serviceProviderProperty.SetValue(null, serviceProvider);
                Console.WriteLine("Set DevExpress AI service provider successfully");
                return;
            }
        }

        // Try to register the client via alternative methods
        RegisterClientViaFallbackMethods(chatClient);
    }

    private static void RegisterClientViaFallbackMethods(Microsoft.Extensions.AI.IChatClient chatClient)
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        var devExpressAssemblies = assemblies.Where(a => a.FullName?.Contains("DevExpress") == true &&
                                                        a.FullName?.Contains("AI") == true).ToList();

        foreach (var asm in devExpressAssemblies)
        {
            try
            {
                var types = asm.GetTypes();
                foreach (var type in types)
                {
                    // Look for static methods that accept IChatClient
                    var clientMethods = type.GetMethods(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public)
                        .Where(m => m.GetParameters().Length == 1 &&
                                   m.GetParameters()[0].ParameterType == typeof(Microsoft.Extensions.AI.IChatClient));

                    foreach (var method in clientMethods)
                    {
                        try
                        {
                            method.Invoke(null, new object[] { chatClient });
                            Console.WriteLine($"Registered {chatClient.GetType().Name} via {type.FullName}.{method.Name}");
                            return;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Failed to invoke {type.FullName}.{method.Name}: {ex.Message}");
                        }
                    }

                    // Also try properties that accept IChatClient
                    var clientProperties = type.GetProperties(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public)
                        .Where(p => p.PropertyType == typeof(Microsoft.Extensions.AI.IChatClient) && p.CanWrite);

                    foreach (var prop in clientProperties)
                    {
                        try
                        {
                            prop.SetValue(null, chatClient);
                            Console.WriteLine($"Set {chatClient.GetType().Name} via {type.FullName}.{prop.Name}");
                            return;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Failed to set {type.FullName}.{prop.Name}: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error searching assembly {asm.FullName}: {ex.Message}");
            }
        }

        // Last resort: Try to create a minimal service provider with just the client
        try
        {
            var dummyServices = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
            dummyServices.AddSingleton(typeof(Microsoft.Extensions.AI.IChatClient), chatClient);
            var dummyServiceProvider = dummyServices.BuildServiceProvider();

            var aiIntegrationType = Type.GetType("DevExpress.AIIntegration.AIIntegration, DevExpress.AIIntegration");
            if (aiIntegrationType != null)
            {
                var spProperty = aiIntegrationType.GetProperty("ServiceProvider");
                if (spProperty != null && spProperty.CanWrite)
                {
                    spProperty.SetValue(null, dummyServiceProvider);
                    Console.WriteLine("Set dummy service provider on DevExpress.AIIntegration.AIIntegration");
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to create dummy service provider: {ex.Message}");
        }

        Console.WriteLine($"Warning: Could not register {chatClient.GetType().Name} with DevExpress - AIChatControl may still fail to initialize");
    }

    private static string FindWorkspaceRoot(string startDir)
    {
        var dir = new DirectoryInfo(startDir);

        // Walk up directories until we find appsettings.json
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "appsettings.json")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }

        // Fallback to current directory if not found
        return startDir;
    }
}

/// <summary>
/// Dummy chat client to prevent DevExpress AI Chat control errors when OpenAI is not configured.
/// </summary>
public class DummyChatClient : Microsoft.Extensions.AI.IChatClient
{
    public ChatClientMetadata Metadata => new ChatClientMetadata("dummy", new Uri("http://dummy"), "Dummy Chat Client");

    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> chatMessages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        await Task.Delay(100, cancellationToken); // Simulate some processing
        return new ChatResponse(new[]
        {
            new ChatMessage(ChatRole.Assistant, "AI chat is not configured. Please set up OpenAI API key in appsettings.json.")
        });
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> chatMessages, ChatOptions? options = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Delay(100, cancellationToken);
        yield return new ChatResponseUpdate
        {
            Contents = new[] { new Microsoft.Extensions.AI.TextContent("AI chat is not configured. Please set up OpenAI API key in appsettings.json.") },
            Role = ChatRole.Assistant
        };
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
        // Nothing to dispose
    }
}
