using System;
using System.IO;
using System.Windows.Forms;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.AI;
using JuniorDev.Contracts;
using JuniorDev.Orchestrator;

namespace Ui.Shell;

/// <summary>
/// Dummy chat client for test/offline mode that doesn't require external API keys
/// </summary>
public class DummyChatClient : IChatClient
{
    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        // Return a simple dummy response
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, "This is a dummy response for test mode. AI features are disabled."));
        return response;
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
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

        // Build configuration
        var solutionRoot = @"E:\workspace new\jDev";
        var configuration = ConfigBuilder.Build(environment: "Development", basePath: solutionRoot);

        // Setup DI container
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddOrchestrator();

        // Register dummy AI client for test mode
        services.AddSingleton<IChatClient, DummyChatClient>();

        // Add UI services
        services.AddTransient<MainForm>();

        var serviceProvider = services.BuildServiceProvider();

        // Run the application
        var mainForm = serviceProvider.GetRequiredService<MainForm>();
        Application.Run(mainForm);
    }
}
