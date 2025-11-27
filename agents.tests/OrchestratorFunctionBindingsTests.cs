using JuniorDev.Agents;
using JuniorDev.Contracts;
using JuniorDev.Orchestrator;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Moq;
using Xunit;

namespace JuniorDev.Agents.Tests;

public class OrchestratorFunctionBindingsTests
{
    private readonly Mock<ISessionManager> _sessionManagerMock;
    private readonly Mock<ILogger> _loggerMock;
    private readonly AgentSessionContext _context;
    private readonly OrchestratorFunctionBindings _bindings;

    public OrchestratorFunctionBindingsTests()
    {
        _sessionManagerMock = new Mock<ISessionManager>();
        _loggerMock = new Mock<ILogger>();

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
            _loggerMock.Object);

        _bindings = new OrchestratorFunctionBindings(_context);
    }

    [Fact]
    public async Task CreateBranchAsync_PublishesCommand()
    {
        // Arrange
        _sessionManagerMock.Setup(sm => sm.PublishCommand(It.IsAny<ICommand>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _bindings.CreateBranchAsync("test-repo", "feature/new-feature", "main");

        // Assert
        Assert.Contains("Created branch", result);
        _sessionManagerMock.Verify(sm => sm.PublishCommand(It.Is<CreateBranch>(cmd =>
            cmd.BranchName == "feature/new-feature" &&
            cmd.FromRef == "main")), Times.Once);
    }

    [Fact]
    public async Task ClaimItemAsync_SuccessfulClaim_ReturnsSuccessMessage()
    {
        // Arrange
        _sessionManagerMock.Setup(sm => sm.PublishCommand(It.IsAny<ICommand>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _bindings.ClaimItemAsync("TEST-123");

        // Assert
        Assert.Contains("Successfully claimed", result);
        _sessionManagerMock.Verify(sm => sm.PublishCommand(It.IsAny<SetAssignee>()), Times.Once);
        _sessionManagerMock.Verify(sm => sm.PublishCommand(It.IsAny<TransitionTicket>()), Times.Once);
        _sessionManagerMock.Verify(sm => sm.PublishCommand(It.IsAny<Comment>()), Times.Once);
    }

    [Fact]
    public async Task ListBacklogAsync_ReturnsNotImplementedMessage()
    {
        // Act
        var result = await _bindings.ListBacklogAsync();

        // Assert
        Assert.Contains("not yet implemented", result);
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task GetItemAsync_ReturnsNotImplementedMessage()
    {
        // Act
        var result = await _bindings.GetItemAsync("TEST-123");

        // Assert
        Assert.Contains("not yet implemented", result);
        Assert.Contains("TEST-123", result);
    }

    [Fact]
    public void RegisterFunctions_AddsPluginsToKernel()
    {
        // Arrange
        var kernel = new Kernel();

        // Act
        _bindings.RegisterFunctions(kernel);

        // Assert
        Assert.NotNull(kernel.Plugins["vcs"]);
        Assert.NotNull(kernel.Plugins["workitems"]);
        Assert.NotNull(kernel.Plugins["general"]);
    }

    [Fact]
    public async Task CreateBranchAsync_DryRun_ReturnsDryRunMessage()
    {
        // Arrange
        var agentConfig = AgentConfig.CreateDeterministic();
        agentConfig.DryRun = true;
        var context = new AgentSessionContext(
            Guid.NewGuid(),
            _context.Config,
            _sessionManagerMock.Object,
            agentConfig,
            _loggerMock.Object);
        var bindings = new OrchestratorFunctionBindings(context);

        // Act
        var result = await bindings.CreateBranchAsync("test-repo", "feature/new-feature", "main");

        // Assert
        Assert.Contains("[DRY RUN]", result);
        Assert.Contains("feature/new-feature", result);
        Assert.Contains("main", result);
        _sessionManagerMock.Verify(sm => sm.PublishCommand(It.IsAny<ICommand>()), Times.Never);
    }

    [Fact]
    public async Task CommitAsync_DryRun_ReturnsDryRunMessage()
    {
        // Arrange
        var agentConfig = AgentConfig.CreateDeterministic();
        agentConfig.DryRun = true;
        var context = new AgentSessionContext(
            Guid.NewGuid(),
            _context.Config,
            _sessionManagerMock.Object,
            agentConfig,
            _loggerMock.Object);
        var bindings = new OrchestratorFunctionBindings(context);

        // Act
        var result = await bindings.CommitAsync("test-repo", "Initial commit", false);

        // Assert
        Assert.Contains("[DRY RUN]", result);
        Assert.Contains("Initial commit", result);
        _sessionManagerMock.Verify(sm => sm.PublishCommand(It.IsAny<ICommand>()), Times.Never);
    }

    [Fact]
    public async Task CommentAsync_DryRun_ReturnsDryRunMessage()
    {
        // Arrange
        var agentConfig = AgentConfig.CreateDeterministic();
        agentConfig.DryRun = true;
        var context = new AgentSessionContext(
            Guid.NewGuid(),
            _context.Config,
            _sessionManagerMock.Object,
            agentConfig,
            _loggerMock.Object);
        var bindings = new OrchestratorFunctionBindings(context);

        // Act
        var result = await bindings.CommentAsync("TEST-123", "This is a test comment");

        // Assert
        Assert.Contains("[DRY RUN]", result);
        Assert.Contains("This is a test comment", result);
        Assert.Contains("TEST-123", result);
        _sessionManagerMock.Verify(sm => sm.PublishCommand(It.IsAny<ICommand>()), Times.Never);
    }
}