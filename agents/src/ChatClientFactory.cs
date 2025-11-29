using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using JuniorDev.Contracts;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace JuniorDev.Agents;

/// <summary>
/// Factory for creating chat clients per agent with caching and fallback to defaults
/// </summary>
public class ChatClientFactory : IChatClientFactory
{
    private readonly AppConfig _appConfig;
    private readonly ILogger<ChatClientFactory> _logger;
    private readonly ConcurrentDictionary<string, Contracts.IChatClient> _clientCache = new();

    public ChatClientFactory(AppConfig appConfig, ILogger<ChatClientFactory> logger)
    {
        _appConfig = appConfig ?? throw new ArgumentNullException(nameof(appConfig));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Gets a chat client for the specified agent profile, with fallback to defaults
    /// </summary>
    public Contracts.IChatClient GetClientFor(string agentProfile)
    {
        if (string.IsNullOrWhiteSpace(agentProfile))
        {
            throw new ArgumentException("Agent profile cannot be null or empty", nameof(agentProfile));
        }

        return _clientCache.GetOrAdd(agentProfile, CreateClientForProfile);
    }

    /// <summary>
    /// Gets the underlying Microsoft.Extensions.AI.IChatClient for DevExpress integration
    /// </summary>
    public object GetUnderlyingClientFor(string agentProfile)
    {
        Console.WriteLine($"ChatClientFactory.GetUnderlyingClientFor called for agent '{agentProfile}'");
        var client = GetClientFor(agentProfile);
        if (client is ChatClientAdapter adapter)
        {
            Console.WriteLine($"Returning real client for agent '{agentProfile}'");
            return adapter.InnerClient;
        }
        // For dummy clients, return null and let caller handle fallback
        Console.WriteLine($"Returning null (dummy client) for agent '{agentProfile}'");
        return null;
    }

    private Contracts.IChatClient CreateClientForProfile(string agentProfile)
    {
        try
        {
            Console.WriteLine($"CreateClientForProfile: Creating client for agent '{agentProfile}'");

            // Get agent-specific config, fallback to defaults
            var agentConfig = GetAgentServiceProviderConfig(agentProfile);
            var provider = agentConfig.Provider ?? _appConfig.SemanticKernel?.DefaultProvider ?? "openai";
            var model = agentConfig.Model ?? _appConfig.SemanticKernel?.DefaultModel ?? "gpt-4";

            Console.WriteLine($"CreateClientForProfile: Using provider '{provider}' and model '{model}'");

            // Check if we have valid credentials for the provider
            if (!HasValidCredentials(provider))
            {
                Console.WriteLine($"CreateClientForProfile: No valid credentials for provider '{provider}' - using dummy client");
                _logger.LogWarning("No valid credentials for provider '{Provider}' (agent: {AgentProfile}). Using dummy client.", provider, agentProfile);
                return new DummyChatClientAdapter(provider, model);
            }

            Console.WriteLine($"CreateClientForProfile: Have valid credentials, creating real client");

            // Create the real client
            var client = CreateRealClient(provider, agentConfig);
            _logger.LogInformation("Created {Provider} client for agent '{AgentProfile}' using model '{Model}'", provider, agentProfile, model);
            return client;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"CreateClientForProfile: Exception creating client: {ex.Message}");
            _logger.LogError(ex, "Failed to create chat client for agent '{AgentProfile}'. Using dummy client.", agentProfile);
            return new DummyChatClientAdapter("unknown", "unknown");
        }
    }

    private AgentServiceProviderConfig GetAgentServiceProviderConfig(string agentProfile)
    {
        if (_appConfig.SemanticKernel?.AgentServiceProviders?.TryGetValue(agentProfile, out var config) == true)
        {
            return config;
        }

        // Return default config
        return new AgentServiceProviderConfig(
            Provider: _appConfig.SemanticKernel?.DefaultProvider ?? "openai",
            Model: _appConfig.SemanticKernel?.DefaultModel ?? "gpt-4",
            MaxTokens: _appConfig.SemanticKernel?.MaxTokens,
            Temperature: _appConfig.SemanticKernel?.Temperature
        );
    }

    private bool HasValidCredentials(string provider)
    {
        var hasCredentials = provider switch
        {
            "openai" => _appConfig.Auth?.OpenAI?.ApiKey != null &&
                       !_appConfig.Auth.OpenAI.ApiKey.Contains("your-"),
            "azure-openai" => false, // TODO: Add Azure support when package is available
            _ => false
        };

        Console.WriteLine($"HasValidCredentials: Provider '{provider}', API key present: {_appConfig.Auth?.OpenAI?.ApiKey != null}, Contains 'your-': {_appConfig.Auth?.OpenAI?.ApiKey?.Contains("your-")}, Result: {hasCredentials}");

        _logger.LogInformation("HasValidCredentials for provider '{Provider}': {HasCredentials}. API key present: {ApiKeyPresent}, Contains 'your-': {ContainsPlaceholder}",
            provider, hasCredentials,
            _appConfig.Auth?.OpenAI?.ApiKey != null,
            _appConfig.Auth?.OpenAI?.ApiKey?.Contains("your-") ?? false);

        return hasCredentials;
    }

    private Contracts.IChatClient CreateRealClient(string provider, AgentServiceProviderConfig config)
    {
        return provider switch
        {
            "openai" => CreateOpenAIClient(config),
            "azure-openai" => CreateAzureOpenAIClient(config),
            _ => throw new NotSupportedException($"Provider '{provider}' is not supported")
        };
    }

    private Contracts.IChatClient CreateOpenAIClient(AgentServiceProviderConfig config)
    {
        var apiKey = config.ApiKey ?? _appConfig.Auth?.OpenAI?.ApiKey;
        if (string.IsNullOrEmpty(apiKey))
        {
            throw new InvalidOperationException("OpenAI API key is required but not configured");
        }

        // Ensure the Microsoft.Extensions.AI.OpenAI assembly is loaded
        try
        {
            Console.WriteLine("Attempting to load Microsoft.Extensions.AI.OpenAI assembly...");
            var assembly = System.Reflection.Assembly.Load("Microsoft.Extensions.AI.OpenAI");
            Console.WriteLine($"Successfully loaded assembly: {assembly.FullName}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load Microsoft.Extensions.AI.OpenAI assembly: {ex.Message}");
            throw new InvalidOperationException($"Failed to load Microsoft.Extensions.AI.OpenAI assembly: {ex.Message}");
        }

        // Use reflection to create OpenAI client since we don't want to add the package to contracts
        Console.WriteLine("Looking for OpenAIChatClient type...");
        var openAIType = Type.GetType("Microsoft.Extensions.AI.OpenAIChatClient, Microsoft.Extensions.AI.OpenAI");
        Console.WriteLine($"OpenAIChatClient type found: {openAIType != null}");
        if (openAIType == null)
        {
            // Try the correct namespace from the assembly listing
            openAIType = Type.GetType("Microsoft.Extensions.AI.OpenAIChatClient");
            Console.WriteLine($"OpenAIChatClient type found (no assembly): {openAIType != null}");
            
            if (openAIType == null)
            {
                // List all types in the loaded assembly
                var loadedAssembly = System.Reflection.Assembly.Load("Microsoft.Extensions.AI.OpenAI");
                var types = loadedAssembly.GetTypes();
                Console.WriteLine($"Types in Microsoft.Extensions.AI.OpenAI assembly:");
                foreach (var type in types.Where(t => t.Name.Contains("Chat")))
                {
                    Console.WriteLine($"  - {type.FullName}");
                }
                
                throw new InvalidOperationException("Microsoft.Extensions.AI.OpenAI package is not available");
            }
        }

        // Need to create the underlying OpenAI ChatClient first
        Console.WriteLine("Looking for OpenAI ChatClient type...");
        
        // Search through all loaded assemblies for ChatClient types
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        Type? chatClientType = null;
        
        foreach (var asm in assemblies)
        {
            try
            {
                var types = asm.GetTypes().Where(t => t.Name.Contains("ChatClient") && !t.Name.Contains("OpenAIChatClient")).ToList();
                if (types.Any())
                {
                    Console.WriteLine($"Found ChatClient types in {asm.FullName}:");
                    foreach (var type in types)
                    {
                        Console.WriteLine($"  - {type.FullName}");
                        if (type.FullName == "OpenAI.Chat.ChatClient")
                        {
                            chatClientType = type;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Some assemblies might not be accessible
                Console.WriteLine($"Could not search assembly {asm.FullName}: {ex.Message}");
            }
        }
        
        if (chatClientType == null)
        {
            throw new InvalidOperationException("Cannot find OpenAI.ChatClient type");
        }
        
        // Create ChatClient instance - likely needs apiKey and model
        var chatClientConstructor = chatClientType.GetConstructor(new[] { typeof(string), typeof(string) });
        if (chatClientConstructor == null)
        {
            Console.WriteLine("ChatClient constructor with (string, string) not found. Available constructors:");
            var chatClientConstructors = chatClientType.GetConstructors();
            foreach (var ctor in chatClientConstructors)
            {
                var parameters = ctor.GetParameters();
                var paramTypes = string.Join(", ", parameters.Select(p => p.ParameterType.Name));
                Console.WriteLine($"  - ChatClient.{ctor.Name}({paramTypes})");
            }
            throw new InvalidOperationException("Cannot find ChatClient constructor");
        }
        
        var chatClientInstance = chatClientConstructor.Invoke(new object[] { apiKey, config.Model });
        
        // Now create the OpenAIChatClient with the ChatClient
        var constructor = openAIType.GetConstructor(new[] { chatClientType });
        if (constructor == null)
        {
            Console.WriteLine("OpenAIChatClient constructor with ChatClient not found. Available constructors:");
            var constructors = openAIType.GetConstructors();
            foreach (var ctor in constructors)
            {
                var parameters = ctor.GetParameters();
                var paramTypes = string.Join(", ", parameters.Select(p => p.ParameterType.Name));
                Console.WriteLine($"  - OpenAIChatClient.{ctor.Name}({paramTypes})");
            }
            
            throw new InvalidOperationException("Cannot find OpenAI client constructor");
        }

        var client = (Microsoft.Extensions.AI.IChatClient)constructor.Invoke(new object[] { chatClientInstance });
        return new ChatClientAdapter(client, config.Provider, config.Model);
    }

    private Contracts.IChatClient CreateAzureOpenAIClient(AgentServiceProviderConfig config)
    {
        // TODO: Implement Azure OpenAI support when package is available
        throw new NotSupportedException("Azure OpenAI is not yet supported");
    }
}

/// <summary>
/// Adapter to bridge Microsoft.Extensions.AI.IChatClient to our contracts IChatClient
/// </summary>
internal class ChatClientAdapter : Contracts.IChatClient
{
    private readonly Microsoft.Extensions.AI.IChatClient _innerClient;

    public string Provider { get; }
    public string Model { get; }

    public ChatClientAdapter(Microsoft.Extensions.AI.IChatClient innerClient, string provider, string model)
    {
        _innerClient = innerClient ?? throw new ArgumentNullException(nameof(innerClient));
        Provider = provider ?? throw new ArgumentNullException(nameof(provider));
        Model = model ?? throw new ArgumentNullException(nameof(model));
    }

    public Microsoft.Extensions.AI.IChatClient InnerClient => _innerClient;
}

/// <summary>
/// Dummy chat client for when no real credentials are available
/// </summary>
public class DummyChatClientAdapter : Contracts.IChatClient
{
    public string Provider { get; }
    public string Model { get; }

    public DummyChatClientAdapter(string provider, string model)
    {
        Provider = provider;
        Model = model;
    }
}