using System;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Ui.Shell;
using JuniorDev.Contracts;

namespace ui_shell.tests
{
    public class CredentialCheckTests
    {
        [Fact]
        public void AgentPanel_CheckValidAICredentials_NoServiceProvider_ReturnsFalse()
        {
            // Arrange
            var chatStream = new ChatStream(Guid.NewGuid(), "Test Agent");
            var agentPanel = new AgentPanel(chatStream, null, null);

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
            var agentPanel = new AgentPanel(chatStream, null, serviceProvider);

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
            var agentPanel = new AgentPanel(chatStream, null, serviceProvider);

            // Act
            var field = typeof(AgentPanel).GetField("_chatControl", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var chatControl = field?.GetValue(agentPanel);

            // Assert - Should be a placeholder control when factory returns null/dummy client
            Assert.IsType<Panel>(chatControl);
        }
    }

    // Mock factory that returns null (simulating no valid credentials)
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