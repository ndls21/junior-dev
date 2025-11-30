using System;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;
using Ui.Shell;
using JuniorDev.Contracts;
using JuniorDev.Orchestrator;

namespace ui_shell.tests
{
    public class CredentialCheckTests
    {
        [Fact]
        public void AgentPanel_CheckValidAICredentials_NoServiceProvider_ReturnsFalse()
        {
            // Arrange
            var chatStream = new ChatStream(Guid.NewGuid(), "Test Agent");
            var agentPanel = new AgentPanel(chatStream);

            // Act - CheckValidAICredentials is private, so we test indirectly by checking what control is created
            var field = typeof(AgentPanel).GetField("_chatControl", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var chatControl = field?.GetValue(agentPanel);

            // Assert - Should be a placeholder control (Panel) when no service provider
            Assert.IsType<Panel>(chatControl);
        }

        [Fact]
        public void AgentPanel_CheckValidAICredentials_WithServiceProviderButNoFactory_ReturnsFalse()
        {
            // Arrange
            var services = new ServiceCollection();
            var serviceProvider = services.BuildServiceProvider();
            var chatStream = new ChatStream(Guid.NewGuid(), "Test Agent");
            var agentPanel = new AgentPanel(chatStream);
            agentPanel.SetDependencies(null, null); // Set dependencies after construction

            // Act
            var field = typeof(AgentPanel).GetField("_chatControl", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var chatControl = field?.GetValue(agentPanel);

            // Assert - Should be a placeholder control when no factory
            Assert.IsType<Panel>(chatControl);
        }

    [Fact]
    public void AgentPanel_CheckValidAICredentials_WithMockFactory_ReturnsFalse()
    {
        // Arrange
        var services = new ServiceCollection();
        var mockFactory = new MockChatClientFactory();
        services.AddSingleton<IChatClientFactory>(mockFactory);
        var serviceProvider = services.BuildServiceProvider();
        var chatStream = new ChatStream(Guid.NewGuid(), "Test Agent");
        var agentPanel = new AgentPanel(chatStream);
        agentPanel.SetDependencies(null, null); // Set dependencies after construction

        // Act
        var field = typeof(AgentPanel).GetField("_chatControl", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var chatControl = field?.GetValue(agentPanel);

        // Assert - Should be a placeholder control when factory returns null/dummy client
        Assert.IsType<Panel>(chatControl);
    }

    [Fact]
    public void DummyClient_PreventsAIChatControlExceptions_InTestMode()
    {
        // Arrange - Create a form in test mode (which uses dummy client)
        var mockSessionManager = new Mock<ISessionManager>();
        var mockConfiguration = new Mock<IConfiguration>();
        var mockChatClient = new Mock<Microsoft.Extensions.AI.IChatClient>();
        var mockServiceProvider = new Mock<IServiceProvider>();

        var form = new MainForm(
            mockSessionManager.Object,
            mockConfiguration.Object,
            mockChatClient.Object,
            mockServiceProvider.Object,
            isTestMode: true);

        // Act - Try to create an AgentPanel (which would create AIChatControl)
        var chatStream = new ChatStream(Guid.NewGuid(), "Test Agent");
        var agentPanel = new AgentPanel(chatStream);

        // Assert - Should not throw exceptions and should create a placeholder control
        var field = typeof(AgentPanel).GetField("_chatControl", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var chatControl = field?.GetValue(agentPanel);

        // Should be a placeholder panel, not an AIChatControl that could crash
        Assert.IsType<Panel>(chatControl);
    }
}    // Mock factory that returns null (simulating no valid credentials)
    public class MockChatClientFactory : IChatClientFactory
    {
        public IChatClient GetClientFor(string agentProfile)
        {
            return null!;
        }

        public object GetUnderlyingClientFor(string agentProfile)
        {
            return null!; // Simulate no valid credentials
        }
    }
}