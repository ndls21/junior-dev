using System;
using System.IO;
using System.Windows.Forms;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using JuniorDev.Contracts;
using JuniorDev.Orchestrator;

namespace Ui.Shell;

static class Program
{
    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // Build configuration
        var configuration = ConfigBuilder.Build(environment: "Development", basePath: Directory.GetCurrentDirectory());

        // Setup DI container
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddOrchestrator();

        // Add UI services
        services.AddTransient<MainForm>();

        var serviceProvider = services.BuildServiceProvider();

        // Run the application
        var mainForm = serviceProvider.GetRequiredService<MainForm>();
        Application.Run(mainForm);
    }
}
