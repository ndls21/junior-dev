using JuniorDev.Agents;
using JuniorDev.Contracts;
using JuniorDev.Orchestrator;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace JuniorDev.Agents.Tests;

public class ClaimUtilitiesTests
{
    private readonly Mock<ISessionManager> _sessionManagerMock;
    private readonly Mock<ILogger> _loggerMock;
    private readonly AgentSessionContext _context;
    private readonly ClaimUtilities _claimUtil;

    public ClaimUtilitiesTests()
    {
        _sessionManagerMock = new Mock<ISessionManager>();
        _loggerMock = new Mock<ILogger>();

        var sessionConfig = new SessionConfig(
            Guid.NewGuid(),
            null,
            null,
            new PolicyProfile { Name = "test", ProtectedBranches = new HashSet<string>() },
            new RepoRef("test", "/repos/test"),
            new WorkspaceRef("/workspaces/test"),
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

        _claimUtil = new ClaimUtilities(_context);
    }

    [Fact]
    public async Task TryClaimWorkItemAsync_Success_ReturnsSuccess()
    {
        // Arrange
        _sessionManagerMock.Setup(sm => sm.PublishCommand(It.IsAny<ICommand>()))
            .Returns(Task.CompletedTask);

        var workItem = new WorkItemRef("TEST-123");

        // Act
        var result = await _claimUtil.TryClaimWorkItemAsync(workItem, "test-user");

        // Assert
        Assert.Equal(ClaimResult.Success, result);
        _sessionManagerMock.Verify(sm => sm.PublishCommand(It.IsAny<ClaimWorkItem>()), Times.Once);
    }

    [Fact]
    public async Task TryClaimWorkItemAsync_AlreadyClaimed_ReturnsAlreadyClaimed()
    {
        // Arrange
        _sessionManagerMock.Setup(sm => sm.PublishCommand(It.IsAny<ClaimWorkItem>()))
            .ThrowsAsync(new Exception("Work item already assigned"));

        var workItem = new WorkItemRef("TEST-123");

        // Act
        var result = await _claimUtil.TryClaimWorkItemAsync(workItem, "test-user");

        // Assert
        Assert.Equal(ClaimResult.AlreadyClaimed, result);
    }

    [Fact]
    public async Task TryClaimWorkItemAsync_Rejected_ReturnsRejected()
    {
        // Arrange
        _sessionManagerMock.Setup(sm => sm.PublishCommand(It.IsAny<ClaimWorkItem>()))
            .ThrowsAsync(new Exception("Claim rejected by policy"));

        var workItem = new WorkItemRef("TEST-123");

        // Act
        var result = await _claimUtil.TryClaimWorkItemAsync(workItem, "test-user");

        // Assert
        Assert.Equal(ClaimResult.Rejected, result);
    }

    [Fact]
    public async Task TryClaimWorkItemAsync_NetworkError_ReturnsNetworkError()
    {
        // Arrange
        _sessionManagerMock.Setup(sm => sm.PublishCommand(It.IsAny<ClaimWorkItem>()))
            .ThrowsAsync(new Exception("Connection timeout occurred"));

        var workItem = new WorkItemRef("TEST-123");

        // Act
        var result = await _claimUtil.TryClaimWorkItemAsync(workItem, "test-user");

        // Assert
        Assert.Equal(ClaimResult.UnknownError, result);
    }

    [Fact]
    public async Task TryClaimWorkItemAsync_UnknownError_ReturnsUnknownError()
    {
        // Arrange
        _sessionManagerMock.Setup(sm => sm.PublishCommand(It.IsAny<ClaimWorkItem>()))
            .ThrowsAsync(new Exception("Unexpected error"));

        var workItem = new WorkItemRef("TEST-123");

        // Act
        var result = await _claimUtil.TryClaimWorkItemAsync(workItem, "test-user");

        // Assert
        Assert.Equal(ClaimResult.UnknownError, result);
    }

    [Fact]
    public void GenerateBranchName_WithTitle_CreatesDescriptiveName()
    {
        // Arrange
        var workItem = new WorkItemRef("TEST-123");
        var protectedBranches = new[] { "main", "develop" };

        // Act
        var branchName = _claimUtil.GenerateBranchName(workItem, protectedBranches, "Fix login bug");

        // Assert
        Assert.Equal("feature/TEST-123-fix-login-bug", branchName);
    }

    [Fact]
    public void GenerateBranchName_WithoutTitle_UsesIdOnly()
    {
        // Arrange
        var workItem = new WorkItemRef("TEST-123");
        var protectedBranches = new[] { "main", "develop" };

        // Act
        var branchName = _claimUtil.GenerateBranchName(workItem, protectedBranches);

        // Assert
        Assert.Equal("feature/TEST-123", branchName);
    }

    [Fact]
    public void GenerateBranchName_AvoidsProtectedBranches()
    {
        // Arrange
        var workItem = new WorkItemRef("TEST-123");
        var protectedBranches = new[] { "main", "develop", "feature/TEST-123-fix-bug" };

        // Act
        var branchName = _claimUtil.GenerateBranchName(workItem, protectedBranches, "Fix bug");

        // Assert
        Assert.Equal("feature/TEST-123-fix-bug-1", branchName);
    }

    [Fact]
    public void GenerateBranchName_TruncatesLongTitles()
    {
        // Arrange
        var workItem = new WorkItemRef("TEST-123");
        var protectedBranches = Array.Empty<string>();
        var longTitle = new string('a', 200);

        // Act
        var branchName = _claimUtil.GenerateBranchName(workItem, protectedBranches, longTitle);

        // Assert
        Assert.True(branchName.Length <= 100);
        Assert.StartsWith("feature/TEST-123-", branchName);
    }
}
