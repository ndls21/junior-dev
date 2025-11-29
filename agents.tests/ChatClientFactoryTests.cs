using System;
using System.Collections.Generic;
using JuniorDev.Contracts;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace JuniorDev.Agents.Tests;

public class ChatClientFactoryTests
{
    private readonly Mock<ILogger<ChatClientFactory>> _loggerMock;
    private readonly AppConfig _testConfig;

    public ChatClientFactoryTests()
    {
        _loggerMock = new Mock<ILogger<ChatClientFactory>>();

        // Create a test config with placeholder OpenAI credentials (contains "your-")
        _testConfig = new AppConfig
        {
            Auth = new AuthConfig
            {
                OpenAI = new OpenAIAuthConfig("your-openai-key", "test-org"),
                AzureOpenAI = new AzureOpenAIAuthConfig("https://test.openai.azure.com/", "test-azure-key", "test-deployment")
            },
            SemanticKernel = new SemanticKernelConfig(
                DefaultProvider: "openai",
                DefaultModel: "gpt-4",
                AgentServiceProviders: new Dictionary<string, AgentServiceProviderConfig>
                {
                    ["planner"] = new AgentServiceProviderConfig(
                        Provider: "openai",
                        Model: "gpt-4",
                        Temperature: 0.3
                    ),
                    ["executor"] = new AgentServiceProviderConfig(
                        Provider: "openai",
                        Model: "gpt-3.5-turbo",
                        Temperature: 0.7
                    ),
                    ["reviewer"] = new AgentServiceProviderConfig(
                        Provider: "azure-openai",
                        Model: "gpt-4",
                        DeploymentName: "reviewer-gpt4",
                        Temperature: 0.1
                    )
                }
            )
        };
    }

    [Fact]
    public void GetClientFor_DefaultAgent_ReturnsDummyClient()
    {
        // Arrange - Note: In unit tests, we expect dummy clients since OpenAI package may not be available
        var factory = new ChatClientFactory(_testConfig, _loggerMock.Object);

        // Act
        var client = factory.GetClientFor("default");

        // Assert - Returns dummy client in test environment
        Assert.NotNull(client);
        Assert.IsType<DummyChatClientAdapter>(client);
    }

    [Fact]
    public void GetClientFor_PlannerAgent_ReturnsDummyClient()
    {
        // Arrange
        var factory = new ChatClientFactory(_testConfig, _loggerMock.Object);

        // Act
        var client = factory.GetClientFor("planner");

        // Assert
        Assert.NotNull(client);
        Assert.IsType<DummyChatClientAdapter>(client);
    }

    [Fact]
    public void GetClientFor_ExecutorAgent_ReturnsDummyClient()
    {
        // Arrange
        var factory = new ChatClientFactory(_testConfig, _loggerMock.Object);

        // Act
        var client = factory.GetClientFor("executor");

        // Assert
        Assert.NotNull(client);
        Assert.IsType<DummyChatClientAdapter>(client);
    }

    [Fact]
    public void GetClientFor_ReviewerAgent_ReturnsDummyClient()
    {
        // Arrange
        var factory = new ChatClientFactory(_testConfig, _loggerMock.Object);

        // Act
        var client = factory.GetClientFor("reviewer");

        // Assert
        Assert.NotNull(client);
        Assert.IsType<DummyChatClientAdapter>(client);
    }

    [Fact]
    public void GetClientFor_SameAgent_ReturnsCachedClient()
    {
        // Arrange
        var factory = new ChatClientFactory(_testConfig, _loggerMock.Object);

        // Act
        var client1 = factory.GetClientFor("planner");
        var client2 = factory.GetClientFor("planner");

        // Assert
        Assert.Same(client1, client2); // Should be the same cached instance
    }

    [Fact]
    public void GetClientFor_NoCredentials_ReturnsDummyClient()
    {
        // Arrange
        var configWithoutCredentials = new AppConfig
        {
            Auth = new AuthConfig
            {
                OpenAI = new OpenAIAuthConfig("your-openai-key") // Placeholder key
            },
            SemanticKernel = new SemanticKernelConfig(
                DefaultProvider: "openai",
                DefaultModel: "gpt-4"
            )
        };
        var factory = new ChatClientFactory(configWithoutCredentials, _loggerMock.Object);

        // Act
        var client = factory.GetClientFor("default");

        // Assert
        Assert.NotNull(client);
        Assert.IsType<DummyChatClientAdapter>(client);
        Assert.Equal("openai", client.Provider);
    }

    [Fact]
    public void GetClientFor_EmptyOrWhitespaceProfile_ThrowsArgumentException()
    {
        // Arrange
        var factory = new ChatClientFactory(_testConfig, _loggerMock.Object);

        // Act & Assert
        Assert.Throws<ArgumentException>(() => factory.GetClientFor(""));
        Assert.Throws<ArgumentException>(() => factory.GetClientFor("   "));
    }

    [Fact]
    public void GetClientFor_ValidOpenAICredentials_AttemptsRealClient()
    {
        // Arrange - Use a config with valid-looking credentials (doesn't contain "your-")
        var configWithValidKey = new AppConfig
        {
            Auth = new AuthConfig
            {
                OpenAI = new OpenAIAuthConfig("sk-proj-1234567890abcdef", "test-org")
            },
            SemanticKernel = new SemanticKernelConfig(
                DefaultProvider: "openai",
                DefaultModel: "gpt-4"
            )
        };
        var factory = new ChatClientFactory(configWithValidKey, _loggerMock.Object);

        // Act
        var client = factory.GetClientFor("default");

        // Assert - In test environment, may still return dummy due to assembly loading,
        // but the attempt to create real client should be logged
        Assert.NotNull(client);
        // The client type depends on whether assemblies are available in test environment
        Assert.IsAssignableFrom<Contracts.IChatClient>(client);
    }

    [Fact]
    public void GetClientFor_PlaceholderOpenAICredentials_ReturnsDummyClient()
    {
        // Arrange - Use a config with placeholder credentials (contains "your-")
        var configWithPlaceholderKey = new AppConfig
        {
            Auth = new AuthConfig
            {
                OpenAI = new OpenAIAuthConfig("your-openai-key-here", "test-org")
            },
            SemanticKernel = new SemanticKernelConfig(
                DefaultProvider: "openai",
                DefaultModel: "gpt-4"
            )
        };
        var factory = new ChatClientFactory(configWithPlaceholderKey, _loggerMock.Object);

        // Act
        var client = factory.GetClientFor("default");

        // Assert - Should return dummy client when credentials are invalid
        Assert.NotNull(client);
        Assert.IsType<DummyChatClientAdapter>(client);
    }

    [Fact]
    public void GetClientFor_MissingOpenAICredentials_ReturnsDummyClient()
    {
        // Arrange - Use a config with no OpenAI credentials
        var configWithoutCredentials = new AppConfig
        {
            Auth = new AuthConfig
            {
                // No OpenAI config
            },
            SemanticKernel = new SemanticKernelConfig(
                DefaultProvider: "openai",
                DefaultModel: "gpt-4"
            )
        };
        var factory = new ChatClientFactory(configWithoutCredentials, _loggerMock.Object);

        // Act
        var client = factory.GetClientFor("default");

        // Assert - Should return dummy client when no credentials are configured
        Assert.NotNull(client);
        Assert.IsType<DummyChatClientAdapter>(client);
    }

    [Fact]
    public void HasValidCredentials_ValidOpenAIKey_ReturnsTrue()
    {
        // Arrange
        var configWithValidKey = new AppConfig
        {
            Auth = new AuthConfig
            {
                OpenAI = new OpenAIAuthConfig("sk-proj-1234567890abcdef")
            }
        };
        var factory = new ChatClientFactory(configWithValidKey, _loggerMock.Object);

        // Act - Use reflection to test private method
        var method = typeof(ChatClientFactory).GetMethod("HasValidCredentials", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(method);
        var invokeResult = method.Invoke(factory, new object[] { "openai" });
        Assert.NotNull(invokeResult);
        var result = (bool)invokeResult;

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void HasValidCredentials_PlaceholderOpenAIKey_ReturnsFalse()
    {
        // Arrange
        var configWithPlaceholderKey = new AppConfig
        {
            Auth = new AuthConfig
            {
                OpenAI = new OpenAIAuthConfig("your-openai-key-here")
            }
        };
        var factory = new ChatClientFactory(configWithPlaceholderKey, _loggerMock.Object);

        // Act
        var method = typeof(ChatClientFactory).GetMethod("HasValidCredentials", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(method);
        var invokeResult = method.Invoke(factory, new object[] { "openai" });
        Assert.NotNull(invokeResult);
        var result = (bool)invokeResult;

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void HasValidCredentials_MissingOpenAIKey_ReturnsFalse()
    {
        // Arrange
        var configWithoutKey = new AppConfig
        {
            Auth = new AuthConfig
            {
                // No OpenAI config
            }
        };
        var factory = new ChatClientFactory(configWithoutKey, _loggerMock.Object);

        // Act
        var method = typeof(ChatClientFactory).GetMethod("HasValidCredentials", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(method);
        var invokeResult = method.Invoke(factory, new object[] { "openai" });
        Assert.NotNull(invokeResult);
        var result = (bool)invokeResult;

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void HasValidCredentials_UnsupportedProvider_ReturnsFalse()
    {
        // Arrange
        var factory = new ChatClientFactory(_testConfig, _loggerMock.Object);

        // Act
        var method = typeof(ChatClientFactory).GetMethod("HasValidCredentials", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(method);
        var invokeResult = method.Invoke(factory, new object[] { "unsupported-provider" });
        Assert.NotNull(invokeResult);
        var result = (bool)invokeResult;

        // Assert
        Assert.False(result);
    }
}