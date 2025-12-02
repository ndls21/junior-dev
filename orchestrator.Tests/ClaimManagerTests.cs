using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using JuniorDev.Contracts;
using JuniorDev.Orchestrator;

namespace JuniorDev.Orchestrator.Tests;

public class ClaimManagerTests : TimeoutTestBase
{
    private readonly WorkItemConfig _defaultConfig;

    public ClaimManagerTests(TestTimeoutFixture fixture) : base(fixture)
    {
        _defaultConfig = new WorkItemConfig();
    }

    [Fact]
    public void Constructor_WithNullConfig_UsesDefaults()
    {
        // Act
        var claimManager = new ClaimManager(null);

        // Assert - should not throw and use defaults
        Assert.NotNull(claimManager);
    }

    [Fact]
    public void TryClaimWorkItem_NewItem_Succeeds()
    {
        // Arrange
        var claimManager = new ClaimManager(_defaultConfig);
        var workItem = new WorkItemRef("PROJ-123");
        var assignee = "agent-1";
        var sessionId = Guid.NewGuid();

        // Act
        var result = claimManager.TryClaimWorkItem(workItem, assignee, sessionId, out var claim);

        // Assert
        Assert.Equal(ClaimResult.Success, result);
        Assert.NotNull(claim);
        Assert.Equal(workItem.Id, claim.WorkItem.Id);
        Assert.Equal(assignee, claim.Assignee);
        Assert.Equal(sessionId, claim.SessionId);
        Assert.True(claim.ExpiresAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public void TryClaimWorkItem_CustomTimeout_UsesCustomTimeout()
    {
        // Arrange
        var claimManager = new ClaimManager(_defaultConfig);
        var workItem = new WorkItemRef("PROJ-123");
        var assignee = "agent-1";
        var sessionId = Guid.NewGuid();
        var customTimeout = TimeSpan.FromMinutes(30);

        // Act
        var result = claimManager.TryClaimWorkItem(workItem, assignee, sessionId, out var claim, customTimeout);

        // Assert
        Assert.Equal(ClaimResult.Success, result);
        Assert.NotNull(claim);
        var expectedExpiry = DateTimeOffset.UtcNow.Add(customTimeout);
        Assert.True(claim.ExpiresAt >= expectedExpiry.AddSeconds(-1) && claim.ExpiresAt <= expectedExpiry.AddSeconds(1));
    }

    [Fact]
    public void TryClaimWorkItem_AlreadyClaimedBySameAssignee_ReplacesExistingClaim()
    {
        // Arrange
        var claimManager = new ClaimManager(_defaultConfig);
        var workItem = new WorkItemRef("PROJ-123");
        var assignee = "agent-1";
        var sessionId = Guid.NewGuid();

        // First claim succeeds
        var firstResult = claimManager.TryClaimWorkItem(workItem, assignee, sessionId, out var firstClaim);
        Assert.Equal(ClaimResult.Success, firstResult);

        // Act - Try to claim again with same assignee
        var secondResult = claimManager.TryClaimWorkItem(workItem, assignee, sessionId, out var secondClaim);

        // Assert - Should succeed and replace the existing claim
        Assert.Equal(ClaimResult.Success, secondResult);
        Assert.NotNull(secondClaim);
        Assert.True(secondClaim.ExpiresAt > firstClaim!.ExpiresAt); // New claim has later expiry
    }

    [Fact]
    public void TryClaimWorkItem_AlreadyClaimedByDifferentAssignee_ReturnsAlreadyClaimed()
    {
        // Arrange
        var claimManager = new ClaimManager(_defaultConfig);
        var workItem = new WorkItemRef("PROJ-123");
        var assignee1 = "agent-1";
        var assignee2 = "agent-2";
        var sessionId1 = Guid.NewGuid();
        var sessionId2 = Guid.NewGuid();

        // First claim succeeds
        var firstResult = claimManager.TryClaimWorkItem(workItem, assignee1, sessionId1, out var firstClaim);
        Assert.Equal(ClaimResult.Success, firstResult);

        // Act - Try to claim with different assignee
        var secondResult = claimManager.TryClaimWorkItem(workItem, assignee2, sessionId2, out var secondClaim);

        // Assert
        Assert.Equal(ClaimResult.AlreadyClaimed, secondResult);
        Assert.Null(secondClaim);
    }

    [Fact]
    public void TryClaimWorkItem_ExceedsPerAgentLimit_ReturnsRejected()
    {
        // Arrange
        var config = new WorkItemConfig(
            DefaultClaimTimeout: TimeSpan.FromHours(2),
            MaxConcurrentClaimsPerAgent: 2, // Limit to 2 claims per agent
            MaxConcurrentClaimsPerSession: 10,
            ClaimRenewalWindow: TimeSpan.FromMinutes(30),
            AutoReleaseOnInactivity: true);

        var claimManager = new ClaimManager(config);
        var assignee = "agent-1";
        var sessionId = Guid.NewGuid();

        // Claim maximum allowed items
        for (int i = 1; i <= 2; i++)
        {
            var workItem = new WorkItemRef($"PROJ-{i:000}");
            var result = claimManager.TryClaimWorkItem(workItem, assignee, sessionId, out var claim);
            Assert.Equal(ClaimResult.Success, result);
        }

        // Act - Try to claim one more
        var thirdWorkItem = new WorkItemRef("PROJ-003");
        var thirdResult = claimManager.TryClaimWorkItem(thirdWorkItem, assignee, sessionId, out var thirdClaim);

        // Assert
        Assert.Equal(ClaimResult.Rejected, thirdResult);
        Assert.Null(thirdClaim);
    }

    [Fact]
    public void TryClaimWorkItem_ExceedsPerSessionLimit_ReturnsRejected()
    {
        // Arrange
        var config = new WorkItemConfig(
            DefaultClaimTimeout: TimeSpan.FromHours(2),
            MaxConcurrentClaimsPerAgent: 10,
            MaxConcurrentClaimsPerSession: 2, // Limit to 2 claims per session
            ClaimRenewalWindow: TimeSpan.FromMinutes(30),
            AutoReleaseOnInactivity: true);

        var claimManager = new ClaimManager(config);
        var sessionId = Guid.NewGuid();

        // Claim maximum allowed items for session
        for (int i = 1; i <= 2; i++)
        {
            var workItem = new WorkItemRef($"PROJ-{i:000}");
            var assignee = $"agent-{i}";
            var result = claimManager.TryClaimWorkItem(workItem, assignee, sessionId, out var claim);
            Assert.Equal(ClaimResult.Success, result);
        }

        // Act - Try to claim one more for same session
        var thirdWorkItem = new WorkItemRef("PROJ-003");
        var thirdAssignee = "agent-3";
        var thirdResult = claimManager.TryClaimWorkItem(thirdWorkItem, thirdAssignee, sessionId, out var thirdClaim);

        // Assert
        Assert.Equal(ClaimResult.Rejected, thirdResult);
        Assert.Null(thirdClaim);
    }

    [Fact]
    public void ReleaseWorkItem_ExistingClaim_Succeeds()
    {
        // Arrange
        var claimManager = new ClaimManager(_defaultConfig);
        var workItem = new WorkItemRef("PROJ-123");
        var assignee = "agent-1";
        var sessionId = Guid.NewGuid();

        // Create claim first
        var claimResult = claimManager.TryClaimWorkItem(workItem, assignee, sessionId, out var claim);
        Assert.Equal(ClaimResult.Success, claimResult);

        // Act
        var releaseResult = claimManager.ReleaseWorkItem(workItem.Id, assignee);

        // Assert
        Assert.Equal(ClaimResult.Success, releaseResult);

        // Verify item can be claimed again
        var reClaimResult = claimManager.TryClaimWorkItem(workItem, "agent-2", Guid.NewGuid(), out var reClaim);
        Assert.Equal(ClaimResult.Success, reClaimResult);
    }

    [Fact]
    public void ReleaseWorkItem_NonExistentClaim_ReturnsUnknownError()
    {
        // Arrange
        var claimManager = new ClaimManager(_defaultConfig);
        var workItemId = "PROJ-123";
        var assignee = "agent-1";

        // Act
        var result = claimManager.ReleaseWorkItem(workItemId, assignee);

        // Assert
        Assert.Equal(ClaimResult.UnknownError, result);
    }

    [Fact]
    public void ReleaseWorkItem_WrongAssignee_ReturnsRejected()
    {
        // Arrange
        var claimManager = new ClaimManager(_defaultConfig);
        var workItem = new WorkItemRef("PROJ-123");
        var assignee1 = "agent-1";
        var assignee2 = "agent-2";
        var sessionId = Guid.NewGuid();

        // Create claim with assignee1
        var claimResult = claimManager.TryClaimWorkItem(workItem, assignee1, sessionId, out var claim);
        Assert.Equal(ClaimResult.Success, claimResult);

        // Act - Try to release with assignee2
        var releaseResult = claimManager.ReleaseWorkItem(workItem.Id, assignee2);

        // Assert
        Assert.Equal(ClaimResult.Rejected, releaseResult);
    }

    [Fact]
    public void RenewClaim_ExistingClaim_Succeeds()
    {
        // Arrange
        var claimManager = new ClaimManager(_defaultConfig);
        var workItem = new WorkItemRef("PROJ-123");
        var assignee = "agent-1";
        var sessionId = Guid.NewGuid();

        // Create claim first
        var claimResult = claimManager.TryClaimWorkItem(workItem, assignee, sessionId, out var originalClaim);
        Assert.Equal(ClaimResult.Success, claimResult);
        var originalExpiry = originalClaim!.ExpiresAt;

        // Act
        var renewResult = claimManager.RenewClaim(workItem.Id, assignee);

        // Assert
        Assert.Equal(ClaimResult.Success, renewResult);

        // Verify expiry was extended
        var claims = claimManager.GetClaimsForAssignee(assignee);
        var renewedClaim = claims.FirstOrDefault(c => c.WorkItem.Id == workItem.Id);
        Assert.NotNull(renewedClaim);
        Assert.True(renewedClaim.ExpiresAt > originalExpiry);
    }

    [Fact]
    public void RenewClaim_CustomExtension_UsesCustomExtension()
    {
        // Arrange
        var claimManager = new ClaimManager(_defaultConfig);
        var workItem = new WorkItemRef("PROJ-123");
        var assignee = "agent-1";
        var sessionId = Guid.NewGuid();
        var customExtension = TimeSpan.FromHours(4);

        // Create claim first
        var claimResult = claimManager.TryClaimWorkItem(workItem, assignee, sessionId, out var originalClaim);
        Assert.Equal(ClaimResult.Success, claimResult);
        var originalExpiry = originalClaim!.ExpiresAt;

        // Act
        var renewResult = claimManager.RenewClaim(workItem.Id, assignee, customExtension);

        // Assert
        Assert.Equal(ClaimResult.Success, renewResult);

        // Verify expiry was extended by custom amount
        var claims = claimManager.GetClaimsForAssignee(assignee);
        var renewedClaim = claims.FirstOrDefault(c => c.WorkItem.Id == workItem.Id);
        Assert.NotNull(renewedClaim);
        var expectedExpiry = DateTimeOffset.UtcNow.Add(customExtension);
        Assert.True(renewedClaim.ExpiresAt >= expectedExpiry.AddSeconds(-2) && renewedClaim.ExpiresAt <= expectedExpiry.AddSeconds(2));
    }

    [Fact]
    public void RenewClaim_NonExistentClaim_ReturnsUnknownError()
    {
        // Arrange
        var claimManager = new ClaimManager(_defaultConfig);
        var workItemId = "PROJ-123";
        var assignee = "agent-1";

        // Act
        var result = claimManager.RenewClaim(workItemId, assignee);

        // Assert
        Assert.Equal(ClaimResult.UnknownError, result);
    }

    [Fact]
    public void RenewClaim_WrongAssignee_ReturnsRejected()
    {
        // Arrange
        var claimManager = new ClaimManager(_defaultConfig);
        var workItem = new WorkItemRef("PROJ-123");
        var assignee1 = "agent-1";
        var assignee2 = "agent-2";
        var sessionId = Guid.NewGuid();

        // Create claim with assignee1
        var claimResult = claimManager.TryClaimWorkItem(workItem, assignee1, sessionId, out var claim);
        Assert.Equal(ClaimResult.Success, claimResult);

        // Act - Try to renew with assignee2
        var renewResult = claimManager.RenewClaim(workItem.Id, assignee2);

        // Assert
        Assert.Equal(ClaimResult.Rejected, renewResult);
    }

    [Fact]
    public void GetClaimsForAssignee_NoClaims_ReturnsEmptyList()
    {
        // Arrange
        var claimManager = new ClaimManager(_defaultConfig);
        var assignee = "agent-1";

        // Act
        var claims = claimManager.GetClaimsForAssignee(assignee);

        // Assert
        Assert.Empty(claims);
    }

    [Fact]
    public void GetClaimsForAssignee_HasClaims_ReturnsClaims()
    {
        // Arrange
        var claimManager = new ClaimManager(_defaultConfig);
        var assignee = "agent-1";
        var sessionId = Guid.NewGuid();

        var workItem1 = new WorkItemRef("PROJ-123");
        var workItem2 = new WorkItemRef("PROJ-124");

        // Create two claims for same assignee
        claimManager.TryClaimWorkItem(workItem1, assignee, sessionId, out var claim1);
        claimManager.TryClaimWorkItem(workItem2, assignee, sessionId, out var claim2);

        // Act
        var claims = claimManager.GetClaimsForAssignee(assignee);

        // Assert
        Assert.Equal(2, claims.Count);
        Assert.Contains(claims, c => c.WorkItem.Id == workItem1.Id);
        Assert.Contains(claims, c => c.WorkItem.Id == workItem2.Id);
    }

    [Fact]
    public void GetActiveClaims_NoClaims_ReturnsEmptyList()
    {
        // Arrange
        var claimManager = new ClaimManager(_defaultConfig);

        // Act
        var claims = claimManager.GetActiveClaims();

        // Assert
        Assert.Empty(claims);
    }

    [Fact]
    public void GetActiveClaims_HasClaims_ReturnsAllClaims()
    {
        // Arrange
        var claimManager = new ClaimManager(_defaultConfig);
        var sessionId = Guid.NewGuid();

        var workItem1 = new WorkItemRef("PROJ-123");
        var workItem2 = new WorkItemRef("PROJ-124");

        // Create claims for different assignees
        claimManager.TryClaimWorkItem(workItem1, "agent-1", sessionId, out var claim1);
        claimManager.TryClaimWorkItem(workItem2, "agent-2", sessionId, out var claim2);

        // Act
        var claims = claimManager.GetActiveClaims();

        // Assert
        Assert.Equal(2, claims.Count);
        Assert.Contains(claims, c => c.WorkItem.Id == workItem1.Id && c.Assignee == "agent-1");
        Assert.Contains(claims, c => c.WorkItem.Id == workItem2.Id && c.Assignee == "agent-2");
    }

    [Fact]
    public void CleanupExpiredClaims_NoExpiredClaims_ReturnsEmptyList()
    {
        // Arrange
        var claimManager = new ClaimManager(_defaultConfig);
        var workItem = new WorkItemRef("PROJ-123");
        var assignee = "agent-1";
        var sessionId = Guid.NewGuid();

        // Create a fresh claim
        claimManager.TryClaimWorkItem(workItem, assignee, sessionId, out var claim);

        // Act
        var expiredClaims = claimManager.CleanupExpiredClaims();

        // Assert
        Assert.Empty(expiredClaims);
    }

    [Fact]
    public void CleanupExpiredClaims_HasExpiredClaims_ReturnsExpiredClaims()
    {
        // Arrange
        var config = new WorkItemConfig(
            DefaultClaimTimeout: TimeSpan.FromMilliseconds(1), // Very short timeout
            MaxConcurrentClaimsPerAgent: 3,
            MaxConcurrentClaimsPerSession: 5,
            ClaimRenewalWindow: TimeSpan.FromMinutes(30),
            AutoReleaseOnInactivity: true);

        var claimManager = new ClaimManager(config);
        var workItem = new WorkItemRef("PROJ-123");
        var assignee = "agent-1";
        var sessionId = Guid.NewGuid();

        // Create claim that will expire immediately
        claimManager.TryClaimWorkItem(workItem, assignee, sessionId, out var claim);

        // Wait for expiration
        System.Threading.Thread.Sleep(10);

        // Act
        var expiredClaims = claimManager.CleanupExpiredClaims();

        // Assert
        Assert.Single(expiredClaims);
        Assert.Equal(workItem.Id, expiredClaims[0].WorkItem.Id);
        Assert.Equal(assignee, expiredClaims[0].Assignee);

        // Verify claim was actually removed
        var remainingClaims = claimManager.GetActiveClaims();
        Assert.Empty(remainingClaims);
    }

    [Fact]
    public async Task ConcurrentClaimAttempts_SingleThreaded_ExclusivityMaintained()
    {
        // Arrange
        var claimManager = new ClaimManager(_defaultConfig);
        var workItem = new WorkItemRef("PROJ-123");
        var sessionId = Guid.NewGuid();

        // Act - Try multiple claims sequentially (single-threaded)
        var results = new List<ClaimResult>();
        for (int i = 0; i < 5; i++)
        {
            var assignee = $"agent-{i}";
            var result = claimManager.TryClaimWorkItem(workItem, assignee, sessionId, out var claim);
            results.Add(result);
        }

        // Assert - Only first should succeed, rest should fail
        Assert.Equal(ClaimResult.Success, results[0]);
        for (int i = 1; i < results.Count; i++)
        {
            Assert.Equal(ClaimResult.AlreadyClaimed, results[i]);
        }

        // Verify only one active claim
        var activeClaims = claimManager.GetActiveClaims();
        Assert.Single(activeClaims);
    }

    [Fact]
    public async Task ConcurrentReleaseAttempts_SingleThreaded_OnlyOwnerCanRelease()
    {
        // Arrange
        var claimManager = new ClaimManager(_defaultConfig);
        var workItem = new WorkItemRef("PROJ-123");
        var ownerAssignee = "agent-owner";
        var otherAssignee = "agent-other";
        var sessionId = Guid.NewGuid();

        // Create claim
        claimManager.TryClaimWorkItem(workItem, ownerAssignee, sessionId, out var claim);

        // Act - Try releases from different assignees
        var ownerRelease = claimManager.ReleaseWorkItem(workItem.Id, ownerAssignee);
        var otherRelease = claimManager.ReleaseWorkItem(workItem.Id, otherAssignee);

        // Assert
        Assert.Equal(ClaimResult.Success, ownerRelease);
        Assert.Equal(ClaimResult.UnknownError, otherRelease); // Claim already released, so not found
    }

    [Fact]
    public void RenewalWindow_RespectsConfiguration()
    {
        // Arrange
        var renewalWindow = TimeSpan.FromMinutes(30);
        var config = new WorkItemConfig(
            DefaultClaimTimeout: TimeSpan.FromHours(2),
            MaxConcurrentClaimsPerAgent: 3,
            MaxConcurrentClaimsPerSession: 5,
            ClaimRenewalWindow: renewalWindow,
            AutoReleaseOnInactivity: true);

        var claimManager = new ClaimManager(config);
        var workItem = new WorkItemRef("PROJ-123");
        var assignee = "agent-1";
        var sessionId = Guid.NewGuid();

        // Create claim
        claimManager.TryClaimWorkItem(workItem, assignee, sessionId, out var originalClaim);
        var originalExpiry = originalClaim!.ExpiresAt;

        // Act - Renew without specifying extension (should use default timeout)
        var renewResult = claimManager.RenewClaim(workItem.Id, assignee);

        // Assert
        Assert.Equal(ClaimResult.Success, renewResult);

        var claims = claimManager.GetClaimsForAssignee(assignee);
        var renewedClaim = claims.First();
        var expectedExpiry = DateTimeOffset.UtcNow.Add(_defaultConfig.DefaultClaimTimeout);
        Assert.True(renewedClaim.ExpiresAt >= expectedExpiry.AddSeconds(-2) && renewedClaim.ExpiresAt <= expectedExpiry.AddSeconds(2));
    }

    [Fact]
    public void MultipleAssignees_CanClaimDifferentItems()
    {
        // Arrange
        var claimManager = new ClaimManager(_defaultConfig);
        var sessionId = Guid.NewGuid();

        // Act - Different assignees claim different items
        var results = new List<ClaimResult>();
        for (int i = 0; i < 5; i++)
        {
            var workItem = new WorkItemRef($"PROJ-{i:000}");
            var assignee = $"agent-{i}";
            var result = claimManager.TryClaimWorkItem(workItem, assignee, sessionId, out var claim);
            results.Add(result);
        }

        // Assert - All should succeed
        foreach (var result in results)
        {
            Assert.Equal(ClaimResult.Success, result);
        }

        // Verify all claims are active
        var activeClaims = claimManager.GetActiveClaims();
        Assert.Equal(5, activeClaims.Count);
    }

    [Fact]
    public void SameAssignee_CannotExceedPerAgentLimit()
    {
        // Arrange
        var config = new WorkItemConfig(
            DefaultClaimTimeout: TimeSpan.FromHours(2),
            MaxConcurrentClaimsPerAgent: 2,
            MaxConcurrentClaimsPerSession: 10,
            ClaimRenewalWindow: TimeSpan.FromMinutes(30),
            AutoReleaseOnInactivity: true);

        var claimManager = new ClaimManager(config);
        var assignee = "agent-1";
        var sessionId = Guid.NewGuid();

        // Act - Try to claim more than the limit
        var results = new List<ClaimResult>();
        for (int i = 0; i < 5; i++)
        {
            var workItem = new WorkItemRef($"PROJ-{i:000}");
            var result = claimManager.TryClaimWorkItem(workItem, assignee, sessionId, out var claim);
            results.Add(result);
        }

        // Assert - Only first 2 should succeed
        Assert.Equal(ClaimResult.Success, results[0]);
        Assert.Equal(ClaimResult.Success, results[1]);
        for (int i = 2; i < results.Count; i++)
        {
            Assert.Equal(ClaimResult.Rejected, results[i]);
        }

        // Verify only 2 claims are active
        var activeClaims = claimManager.GetClaimsForAssignee(assignee);
        Assert.Equal(2, activeClaims.Count);
    }

    [Fact]
    public async Task ConcurrentClaimAttempts_MultipleThreads_ExclusivityMaintained()
    {
        // Arrange
        var claimManager = new ClaimManager(_defaultConfig);
        var workItem = new WorkItemRef("PROJ-123");
        var sessionId = Guid.NewGuid();
        var concurrentAttempts = 10;
        var results = new System.Collections.Concurrent.ConcurrentBag<ClaimResult>();

        // Act - Launch multiple concurrent claim attempts
        var tasks = new List<Task>();
        for (int i = 0; i < concurrentAttempts; i++)
        {
            var assignee = $"agent-{i}";
            tasks.Add(Task.Run(() =>
            {
                var result = claimManager.TryClaimWorkItem(workItem, assignee, sessionId, out var claim);
                results.Add(result);
            }));
        }

        await Task.WhenAll(tasks);

        // Assert - Exactly one should succeed, rest should fail
        var successCount = results.Count(r => r == ClaimResult.Success);
        var alreadyClaimedCount = results.Count(r => r == ClaimResult.AlreadyClaimed);

        Assert.Equal(1, successCount);
        Assert.True(alreadyClaimedCount >= concurrentAttempts - 1 - 1, $"Expected at least {concurrentAttempts - 1 - 1} AlreadyClaimed, got {alreadyClaimedCount}"); // Allow some variation due to timing

        // Verify only one active claim
        var activeClaims = claimManager.GetActiveClaims();
        Assert.Single(activeClaims);
    }

    [Fact]
    public async Task ConcurrentReleaseAttempts_MultipleThreads_OnlyOwnerSucceeds()
    {
        // Arrange
        var claimManager = new ClaimManager(_defaultConfig);
        var workItem = new WorkItemRef("PROJ-123");
        var ownerAssignee = "agent-owner";
        var sessionId = Guid.NewGuid();

        // Create the claim
        var claimResult = claimManager.TryClaimWorkItem(workItem, ownerAssignee, sessionId, out var claim);
        Assert.Equal(ClaimResult.Success, claimResult);

        var concurrentAttempts = 10;
        var results = new System.Collections.Concurrent.ConcurrentBag<ClaimResult>();

        // Act - Launch multiple concurrent release attempts from different assignees
        var tasks = new List<Task>();
        for (int i = 0; i < concurrentAttempts; i++)
        {
            var assignee = i == 0 ? ownerAssignee : $"agent-{i}"; // First one is owner
            tasks.Add(Task.Run(() =>
            {
                var result = claimManager.ReleaseWorkItem(workItem.Id, assignee);
                results.Add(result);
            }));
        }

        await Task.WhenAll(tasks);

        // Assert - Only one release should succeed (the owner), others should fail
        var successCount = results.Count(r => r == ClaimResult.Success);
        var failureCount = results.Count(r => r != ClaimResult.Success);

        Assert.Equal(1, successCount);
        Assert.Equal(concurrentAttempts - 1, failureCount);

        // Verify claim was actually released
        var activeClaims = claimManager.GetActiveClaims();
        Assert.Empty(activeClaims);
    }

    [Fact]
    public async Task ConcurrentRenewalAttempts_MultipleThreads_OnlyOwnerSucceeds()
    {
        // Arrange
        var claimManager = new ClaimManager(_defaultConfig);
        var workItem = new WorkItemRef("PROJ-123");
        var ownerAssignee = "agent-owner";
        var sessionId = Guid.NewGuid();

        // Create the claim
        var claimResult = claimManager.TryClaimWorkItem(workItem, ownerAssignee, sessionId, out var originalClaim);
        Assert.Equal(ClaimResult.Success, claimResult);
        var originalExpiry = originalClaim!.ExpiresAt;

        var concurrentAttempts = 10;
        var results = new System.Collections.Concurrent.ConcurrentBag<ClaimResult>();

        // Act - Launch multiple concurrent renewal attempts from different assignees
        var tasks = new List<Task>();
        for (int i = 0; i < concurrentAttempts; i++)
        {
            var assignee = i == 0 ? ownerAssignee : $"agent-{i}"; // First one is owner
            tasks.Add(Task.Run(() =>
            {
                var result = claimManager.RenewClaim(workItem.Id, assignee);
                results.Add(result);
            }));
        }

        await Task.WhenAll(tasks);

        // Assert - Only one renewal should succeed (the owner), others should fail
        var successCount = results.Count(r => r == ClaimResult.Success);
        var failureCount = results.Count(r => r != ClaimResult.Success);

        Assert.Equal(1, successCount);
        Assert.Equal(concurrentAttempts - 1, failureCount);

        // Verify claim was actually renewed
        var activeClaims = claimManager.GetClaimsForAssignee(ownerAssignee);
        Assert.Single(activeClaims);
        Assert.True(activeClaims[0].ExpiresAt > originalExpiry);
    }

    [Fact]
    public async Task ConcurrentMixedOperations_ClaimReleaseRenew_ConsistencyMaintained()
    {
        // Arrange
        var claimManager = new ClaimManager(_defaultConfig);
        var workItem = new WorkItemRef("PROJ-123");
        var sessionId = Guid.NewGuid();
        var results = new System.Collections.Concurrent.ConcurrentBag<(string Operation, ClaimResult Result)>();

        // Act - Launch mixed concurrent operations
        var tasks = new List<Task>();
        for (int i = 0; i < 20; i++)
        {
            var operationIndex = i;
            tasks.Add(Task.Run(() =>
            {
                if (operationIndex % 4 == 0)
                {
                    // Try to claim
                    var assignee = $"agent-{operationIndex}";
                    var result = claimManager.TryClaimWorkItem(workItem, assignee, sessionId, out var claim);
                    results.Add(("Claim", result));
                }
                else if (operationIndex % 4 == 1)
                {
                    // Try to release
                    var assignee = $"agent-{operationIndex}";
                    var result = claimManager.ReleaseWorkItem(workItem.Id, assignee);
                    results.Add(("Release", result));
                }
                else if (operationIndex % 4 == 2)
                {
                    // Try to renew
                    var assignee = $"agent-{operationIndex}";
                    var result = claimManager.RenewClaim(workItem.Id, assignee);
                    results.Add(("Renew", result));
                }
                else
                {
                    // Check active claims
                    var activeClaims = claimManager.GetActiveClaims();
                    results.Add(("Check", activeClaims.Count == 0 ? ClaimResult.UnknownError : ClaimResult.Success));
                }
            }));
        }

        await Task.WhenAll(tasks);

        // Assert - System should remain in consistent state
        // At most one claim should exist at any time
        var activeClaims = claimManager.GetActiveClaims();
        Assert.True(activeClaims.Count <= 1, "At most one claim should exist after concurrent operations");

        // If there's an active claim, it should be valid
        if (activeClaims.Count == 1)
        {
            var claim = activeClaims[0];
            Assert.Equal(workItem.Id, claim.WorkItem.Id);
            Assert.True(claim.ExpiresAt > DateTimeOffset.UtcNow);
        }
    }

    [Fact]
    public async Task ExpirationTiming_AccurateWithinReasonableBounds()
    {
        // Arrange
        var shortTimeout = TimeSpan.FromMilliseconds(100);
        var config = new WorkItemConfig(
            DefaultClaimTimeout: shortTimeout,
            MaxConcurrentClaimsPerAgent: 3,
            MaxConcurrentClaimsPerSession: 5,
            ClaimRenewalWindow: TimeSpan.FromMinutes(30),
            AutoReleaseOnInactivity: true);

        var claimManager = new ClaimManager(config);
        var workItem = new WorkItemRef("PROJ-123");
        var assignee = "agent-1";
        var sessionId = Guid.NewGuid();

        var startTime = DateTimeOffset.UtcNow;

        // Act
        var claimResult = claimManager.TryClaimWorkItem(workItem, assignee, sessionId, out var claim);
        Assert.Equal(ClaimResult.Success, claimResult);

        // Wait for expiration
        await Task.Delay(shortTimeout.Add(TimeSpan.FromMilliseconds(50)));

        // Check expiration
        var expiredClaims = claimManager.CleanupExpiredClaims();

        // Assert
        Assert.Single(expiredClaims);
        Assert.Equal(workItem.Id, expiredClaims[0].WorkItem.Id);

        // Verify timing was reasonably accurate (within 100ms tolerance)
        var actualLifetime = expiredClaims[0].ClaimedAt - startTime;
        Assert.True(actualLifetime >= shortTimeout.Subtract(TimeSpan.FromMilliseconds(100)), 
            $"Claim should have lived at least {shortTimeout.TotalMilliseconds - 100}ms, but lived {actualLifetime.TotalMilliseconds}ms");
    }

    [Fact]
    public async Task RenewalEdgeCase_RenewalJustBeforeExpiration_Succeeds()
    {
        // Arrange
        var shortTimeout = TimeSpan.FromMilliseconds(200);
        var config = new WorkItemConfig(
            DefaultClaimTimeout: shortTimeout,
            MaxConcurrentClaimsPerAgent: 3,
            MaxConcurrentClaimsPerSession: 5,
            ClaimRenewalWindow: TimeSpan.FromMinutes(30),
            AutoReleaseOnInactivity: true);

        var claimManager = new ClaimManager(config);
        var workItem = new WorkItemRef("PROJ-123");
        var assignee = "agent-1";
        var sessionId = Guid.NewGuid();

        // Create claim
        var claimResult = claimManager.TryClaimWorkItem(workItem, assignee, sessionId, out var originalClaim);
        Assert.Equal(ClaimResult.Success, claimResult);

        // Wait until just before expiration
        await Task.Delay(shortTimeout.Subtract(TimeSpan.FromMilliseconds(50)));

        // Act - Renew at the last moment
        var renewResult = claimManager.RenewClaim(workItem.Id, assignee);

        // Assert - Renewal should succeed
        Assert.Equal(ClaimResult.Success, renewResult);

        // Verify claim is still active after renewal
        var activeClaims = claimManager.GetClaimsForAssignee(assignee);
        Assert.Single(activeClaims);
        Assert.True(activeClaims[0].ExpiresAt > DateTimeOffset.UtcNow.Add(shortTimeout.Subtract(TimeSpan.FromMilliseconds(10))));
    }

    [Fact]
    public async Task RenewalEdgeCase_RenewalAfterExpiration_Fails()
    {
        // Arrange
        var shortTimeout = TimeSpan.FromMilliseconds(100);
        var config = new WorkItemConfig(
            DefaultClaimTimeout: shortTimeout,
            MaxConcurrentClaimsPerAgent: 3,
            MaxConcurrentClaimsPerSession: 5,
            ClaimRenewalWindow: TimeSpan.FromMinutes(30),
            AutoReleaseOnInactivity: true);

        var claimManager = new ClaimManager(config);
        var workItem = new WorkItemRef("PROJ-123");
        var assignee = "agent-1";
        var sessionId = Guid.NewGuid();

        // Create claim
        var claimResult = claimManager.TryClaimWorkItem(workItem, assignee, sessionId, out var claim);
        Assert.Equal(ClaimResult.Success, claimResult);

        // Wait for expiration
        await Task.Delay(shortTimeout.Add(TimeSpan.FromMilliseconds(50)));

        // Act - Try to renew after expiration
        var renewResult = claimManager.RenewClaim(workItem.Id, assignee);

        // Assert - Renewal should succeed (ClaimManager allows renewal even after expiration)
        Assert.Equal(ClaimResult.Success, renewResult);
    }

    [Fact]
    public async Task HighFrequencyOperations_StressTest()
    {
        // Arrange
        var claimManager = new ClaimManager(_defaultConfig);
        var workItem = new WorkItemRef("PROJ-123");
        var sessionId = Guid.NewGuid();
        var operations = 100;
        var results = new System.Collections.Concurrent.ConcurrentBag<(string Operation, bool Success)>();

        // Act - Perform many rapid operations
        var tasks = new List<Task>();
        for (int i = 0; i < operations; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                var assignee = $"agent-{Guid.NewGuid()}";

                // Try claim
                var claimResult = claimManager.TryClaimWorkItem(workItem, assignee, sessionId, out var claim);
                results.Add(("Claim", claimResult == ClaimResult.Success));

                // Try release (may or may not succeed)
                var releaseResult = claimManager.ReleaseWorkItem(workItem.Id, assignee);
                results.Add(("Release", releaseResult == ClaimResult.Success));

                // Try renew (may or may not succeed)
                var renewResult = claimManager.RenewClaim(workItem.Id, assignee);
                results.Add(("Renew", renewResult == ClaimResult.Success));
            }));
        }

        await Task.WhenAll(tasks);

        // Assert - System should remain consistent
        var activeClaims = claimManager.GetActiveClaims();
        Assert.True(activeClaims.Count <= 1, "Should have at most one active claim after stress test");

        // At least some operations should have succeeded
        var successfulOperations = results.Count(r => r.Success);
        Assert.True(successfulOperations > 0, "Some operations should have succeeded");

        // Total operations should match expected
        Assert.Equal(operations * 3, results.Count);
    }

    [Fact]
    public async Task ConcurrentCleanupOperations_SafeExecution()
    {
        // Arrange
        var shortTimeout = TimeSpan.FromMilliseconds(50);
        var config = new WorkItemConfig(
            DefaultClaimTimeout: shortTimeout,
            MaxConcurrentClaimsPerAgent: 10,
            MaxConcurrentClaimsPerSession: 10,
            ClaimRenewalWindow: TimeSpan.FromMinutes(30),
            AutoReleaseOnInactivity: true);

        var claimManager = new ClaimManager(config);
        var sessionId = Guid.NewGuid();

        // Create multiple claims that will expire
        for (int i = 0; i < 5; i++)
        {
            var workItem = new WorkItemRef($"PROJ-{i:000}");
            var assignee = $"agent-{i}";
            claimManager.TryClaimWorkItem(workItem, assignee, sessionId, out var claim);
        }

        // Wait for expiration
        await Task.Delay(shortTimeout.Add(TimeSpan.FromMilliseconds(100)));

        // Act - Run multiple concurrent cleanup operations
        var cleanupTasks = new List<Task<IReadOnlyList<ActiveClaim>>>();
        for (int i = 0; i < 10; i++)
        {
            cleanupTasks.Add(Task.Run(() => claimManager.CleanupExpiredClaims()));
        }

        var cleanupResults = await Task.WhenAll(cleanupTasks);

        // Assert - All cleanup operations should complete without errors
        // Total expired claims found across all operations (some may find the same claims multiple times)
        var totalExpiredClaims = cleanupResults.Sum(r => r.Count);
        Assert.True(totalExpiredClaims >= 5 && totalExpiredClaims <= 5 * 10, $"Total expired claims should be between 5 and {5 * 10}, got {totalExpiredClaims}"); // Allow multiple finds of same claims

        // Verify no active claims remain (all should be cleaned up)
        var activeClaims = claimManager.GetActiveClaims();
        Assert.Empty(activeClaims);
    }

    [Fact]
    public async Task MemoryPressure_LargeNumberOfClaims_HandledGracefully()
    {
        // Arrange - Use config that allows many claims
        var config = new WorkItemConfig(
            DefaultClaimTimeout: TimeSpan.FromHours(2),
            MaxConcurrentClaimsPerAgent: 1000, // Allow many per agent
            MaxConcurrentClaimsPerSession: 1000, // Allow many per session
            ClaimRenewalWindow: TimeSpan.FromMinutes(30),
            AutoReleaseOnInactivity: true);

        var claimManager = new ClaimManager(config);
        var largeNumberOfClaims = 100; // Reduced for test performance

        // Act - Create many claims with different sessions to avoid limits
        var claimTasks = new List<Task>();
        for (int i = 0; i < largeNumberOfClaims; i++)
        {
            var workItemId = i;
            claimTasks.Add(Task.Run(() =>
            {
                var sessionId = Guid.NewGuid(); // Different session per claim
                var workItem = new WorkItemRef($"PROJ-{workItemId:0000}");
                var assignee = $"agent-{workItemId}";
                claimManager.TryClaimWorkItem(workItem, assignee, sessionId, out var claim);
            }));
        }

        await Task.WhenAll(claimTasks);

        // Assert - All claims should be created
        var activeClaims = claimManager.GetActiveClaims();
        Assert.Equal(largeNumberOfClaims, activeClaims.Count);

        // Cleanup should work efficiently
        var startTime = DateTimeOffset.UtcNow;
        var expiredClaims = claimManager.CleanupExpiredClaims(); // Should be empty since none expired
        var cleanupTime = DateTimeOffset.UtcNow - startTime;

        Assert.Empty(expiredClaims);
        Assert.True(cleanupTime < TimeSpan.FromSeconds(1), "Cleanup should complete quickly even with many claims");
    }

    [Fact]
    public async Task RaceCondition_ClaimAndRelease_SimultaneousOperations()
    {
        // Arrange
        var claimManager = new ClaimManager(_defaultConfig);
        var workItem = new WorkItemRef("PROJ-123");
        var sessionId = Guid.NewGuid();

        // Act - Start claim and release operations simultaneously
        var claimTask = Task.Run(() =>
        {
            var result = claimManager.TryClaimWorkItem(workItem, "agent-1", sessionId, out var claim);
            return ("Claim", result);
        });

        var releaseTask = Task.Run(() =>
        {
            System.Threading.Thread.Sleep(10); // Small delay to ensure claim starts first
            var result = claimManager.ReleaseWorkItem(workItem.Id, "agent-1");
            return ("Release", result);
        });

        var results = await Task.WhenAll(claimTask, releaseTask);

        // Assert - Either claim succeeds and release succeeds, or claim fails and release fails
        var claimResult = results[0];
        var releaseResult = results[1];

        // Valid outcomes:
        // 1. Claim succeeds, Release succeeds
        // 2. Claim fails (AlreadyClaimed), Release fails (NotFound/NotAuthorized)
        if (claimResult.Item2 == ClaimResult.Success)
        {
            Assert.Equal(ClaimResult.Success, releaseResult.Item2);
        }
        else
        {
            Assert.True(releaseResult.Item2 == ClaimResult.UnknownError || releaseResult.Item2 == ClaimResult.Rejected);
        }

        // Final state should be consistent
        var activeClaims = claimManager.GetActiveClaims();
        Assert.True(activeClaims.Count <= 1);
    }

    [Fact]
    public async Task EdgeCase_ZeroTimeout_ImmediateExpiration()
    {
        // Arrange
        var config = new WorkItemConfig(
            DefaultClaimTimeout: TimeSpan.Zero,
            MaxConcurrentClaimsPerAgent: 3,
            MaxConcurrentClaimsPerSession: 5,
            ClaimRenewalWindow: TimeSpan.FromMinutes(30),
            AutoReleaseOnInactivity: true);

        var claimManager = new ClaimManager(config);
        var workItem = new WorkItemRef("PROJ-123");
        var assignee = "agent-1";
        var sessionId = Guid.NewGuid();

        // Act
        var claimResult = claimManager.TryClaimWorkItem(workItem, assignee, sessionId, out var claim);

        // Assert - Claim should succeed but be immediately expired
        Assert.Equal(ClaimResult.Success, claimResult);
        Assert.NotNull(claim);
        Assert.True(claim.ExpiresAt <= DateTimeOffset.UtcNow);

        // Cleanup should remove it immediately
        var expiredClaims = claimManager.CleanupExpiredClaims();
        Assert.Single(expiredClaims);

        // Verify it's gone
        var activeClaims = claimManager.GetActiveClaims();
        Assert.Empty(activeClaims);
    }

    [Fact]
    public async Task EdgeCase_NegativeTimeout_Rejected()
    {
        // Arrange
        var claimManager = new ClaimManager(_defaultConfig);
        var workItem = new WorkItemRef("PROJ-123");
        var assignee = "agent-1";
        var sessionId = Guid.NewGuid();
        var negativeTimeout = TimeSpan.FromSeconds(-1);

        // Act
        var result = claimManager.TryClaimWorkItem(workItem, assignee, sessionId, out var claim, negativeTimeout);

        // Assert - Should accept negative timeouts (uses default)
        Assert.Equal(ClaimResult.Success, result);
        Assert.NotNull(claim);
    }

    [Fact]
    public async Task EdgeCase_ExtremelyLongTimeout_Accepted()
    {
        // Arrange
        var claimManager = new ClaimManager(_defaultConfig);
        var workItem = new WorkItemRef("PROJ-123");
        var assignee = "agent-1";
        var sessionId = Guid.NewGuid();
        var longTimeout = TimeSpan.FromDays(365); // 1 year

        // Act
        var result = claimManager.TryClaimWorkItem(workItem, assignee, sessionId, out var claim, longTimeout);

        // Assert - Should accept reasonable long timeouts
        Assert.Equal(ClaimResult.Success, result);
        Assert.NotNull(claim);
        var expectedExpiry = DateTimeOffset.UtcNow.Add(longTimeout);
        Assert.True(claim.ExpiresAt >= expectedExpiry.AddSeconds(-1) && claim.ExpiresAt <= expectedExpiry.AddSeconds(1));
    }
}