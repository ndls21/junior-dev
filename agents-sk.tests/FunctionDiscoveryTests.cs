using JuniorDev.Agents;
using JuniorDev.Agents.Sk;
using JuniorDev.Contracts;
using JuniorDev.Orchestrator;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Moq;
using Xunit;

namespace JuniorDev.Agents.Sk.Tests;

/// <summary>
/// Tests to verify that agent functions are properly discoverable via Semantic Kernel.
/// </summary>
public class FunctionDiscoveryTests
{
    private readonly Mock<ISessionManager> _sessionManagerMock;
    private readonly Mock<ILogger<ExecutorAgent>> _loggerMock;
    private readonly Kernel _kernel;
    private readonly AgentSessionContext _context;

    public FunctionDiscoveryTests()
    {
        _sessionManagerMock = new Mock<ISessionManager>();
        _loggerMock = new Mock<ILogger<ExecutorAgent>>();
        _kernel = new Kernel();

        var sessionConfig = new SessionConfig(
            Guid.NewGuid(),
            null,
            null,
            new PolicyProfile("test", null, null, Array.Empty<string>(), null, false, false, null, null),
            new RepoRef("test-repo", "/repos/test-repo"),
            new WorkspaceRef("/workspaces/test-ws"),
            null,
            "test-agent");

        var agentConfig = AgentConfig.CreateDeterministic();

        _context = new AgentSessionContext(
            Guid.NewGuid(),
            sessionConfig,
            _sessionManagerMock.Object,
            agentConfig,
            _loggerMock.Object,
            "test-agent");
    }

    [Fact]
    public async Task AgentFunctions_AreDiscoverableViaKernel()
    {
        // Arrange
        var agent = new ExecutorAgent(_kernel);
        await agent.StartAsync(_context);

        // Act - Get all plugins from the kernel
        var plugins = _kernel.Plugins.ToList();

        // Assert - Verify expected plugins exist
        Assert.Contains(plugins, p => p.Name == "vcs");
        Assert.Contains(plugins, p => p.Name == "workitems");
        Assert.Contains(plugins, p => p.Name == "general");
        Assert.Contains(plugins, p => p.Name == "executor_agent");
    }

    [Fact]
    public async Task ExecutorAgentPlugin_ContainsExpectedFunctions()
    {
        // Arrange
        var agent = new ExecutorAgent(_kernel);
        await agent.StartAsync(_context);

        // Act
        var executorPlugin = _kernel.Plugins["executor_agent"];
        var functions = executorPlugin.ToList();

        // Assert
        Assert.NotEmpty(functions);
        Assert.Contains(functions, f => f.Name == "execute_work_item");
    }

    [Fact]
    public async Task ExecuteWorkItemFunction_HasProperMetadata()
    {
        // Arrange
        var agent = new ExecutorAgent(_kernel);
        await agent.StartAsync(_context);

        // Act
        var executorPlugin = _kernel.Plugins["executor_agent"];
        var executeFunction = executorPlugin["execute_work_item"];

        // Assert
        Assert.NotNull(executeFunction);
        Assert.NotNull(executeFunction.Description);
        Assert.Contains("work item", executeFunction.Description, StringComparison.OrdinalIgnoreCase);
        
        // Verify it has the expected parameter
        Assert.NotEmpty(executeFunction.Metadata.Parameters);
        Assert.Contains(executeFunction.Metadata.Parameters, p => p.Name == "workItemId");
    }

    [Fact]
    public async Task VcsPlugin_ContainsExpectedFunctions()
    {
        // Arrange
        var agent = new ExecutorAgent(_kernel);
        await agent.StartAsync(_context);

        // Act
        var vcsPlugin = _kernel.Plugins["vcs"];
        var functionNames = vcsPlugin.Select(f => f.Name).ToList();

        // Assert
        Assert.Contains("create_branch", functionNames);
        Assert.Contains("apply_patch", functionNames);
        Assert.Contains("run_tests", functionNames);
        Assert.Contains("commit", functionNames);
        Assert.Contains("push", functionNames);
        Assert.Contains("get_diff", functionNames);
    }

    [Fact]
    public async Task WorkItemsPlugin_ContainsExpectedFunctions()
    {
        // Arrange
        var agent = new ExecutorAgent(_kernel);
        await agent.StartAsync(_context);

        // Act
        var workitemsPlugin = _kernel.Plugins["workitems"];
        var functionNames = workitemsPlugin.Select(f => f.Name).ToList();

        // Assert
        Assert.Contains("list_backlog", functionNames);
        Assert.Contains("get_item", functionNames);
        Assert.Contains("claim_item", functionNames);
        Assert.Contains("comment", functionNames);
        Assert.Contains("transition", functionNames);
    }

    [Fact]
    public async Task GeneralPlugin_ContainsExpectedFunctions()
    {
        // Arrange
        var agent = new ExecutorAgent(_kernel);
        await agent.StartAsync(_context);

        // Act
        var generalPlugin = _kernel.Plugins["general"];
        var functionNames = generalPlugin.Select(f => f.Name).ToList();

        // Assert
        Assert.Contains("upload_artifact", functionNames);
        Assert.Contains("request_approval", functionNames);
    }

    [Fact]
    public async Task AllPluginFunctions_HaveDescriptions()
    {
        // Arrange
        var agent = new ExecutorAgent(_kernel);
        await agent.StartAsync(_context);

        // Act & Assert - Verify all functions have descriptions for LLM discovery
        foreach (var plugin in _kernel.Plugins)
        {
            foreach (var function in plugin)
            {
                Assert.False(string.IsNullOrWhiteSpace(function.Description),
                    $"Function {plugin.Name}.{function.Name} is missing a description");
            }
        }
    }

    [Fact]
    public async Task AllPluginFunctions_HaveParameterDescriptions()
    {
        // Arrange
        var agent = new ExecutorAgent(_kernel);
        await agent.StartAsync(_context);

        // Act & Assert - Verify all function parameters have descriptions
        foreach (var plugin in _kernel.Plugins)
        {
            foreach (var function in plugin)
            {
                foreach (var parameter in function.Metadata.Parameters)
                {
                    Assert.False(string.IsNullOrWhiteSpace(parameter.Description),
                        $"Parameter {parameter.Name} in function {plugin.Name}.{function.Name} is missing a description");
                }
            }
        }
    }

    [Fact]
    public async Task ExecuteWorkItem_CanBeInvokedViaKernel()
    {
        // Arrange
        var agent = new ExecutorAgent(_kernel);
        _sessionManagerMock.Setup(sm => sm.PublishCommand(It.IsAny<ICommand>()))
            .Returns(Task.CompletedTask);

        var contextWithWorkItem = new AgentSessionContext(
            _context.SessionId,
            _context.Config with { WorkItem = new WorkItemRef("TEST-123") },
            _sessionManagerMock.Object,
            _context.AgentConfig,
            _context.Logger,
            "test-agent");

        await agent.StartAsync(contextWithWorkItem);

        // Act - Invoke the function via kernel (as an LLM would)
        var result = await _kernel.InvokeAsync("executor_agent", "execute_work_item", new KernelArguments
        {
            ["workItemId"] = "TEST-456"
        });

        // Assert
        Assert.NotNull(result);
        var resultString = result.ToString();
        Assert.Contains("TEST-456", resultString);
    }
}
