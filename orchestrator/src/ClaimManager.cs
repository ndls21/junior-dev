using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JuniorDev.Contracts;

namespace JuniorDev.Orchestrator;

/// <summary>
/// Represents an active work item claim
/// </summary>
public class ActiveClaim
{
    public WorkItemRef WorkItem { get; }
    public string Assignee { get; }
    public Guid SessionId { get; }
    public DateTimeOffset ClaimedAt { get; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset LastActivity { get; set; }

    public ActiveClaim(WorkItemRef workItem, string assignee, Guid sessionId, DateTimeOffset expiresAt)
    {
        WorkItem = workItem;
        Assignee = assignee;
        SessionId = sessionId;
        ClaimedAt = DateTimeOffset.UtcNow;
        ExpiresAt = expiresAt;
        LastActivity = ClaimedAt;
    }

    public bool IsExpired => DateTimeOffset.UtcNow > ExpiresAt;
    public bool IsExpiringSoon(TimeSpan warningWindow) => DateTimeOffset.UtcNow > ExpiresAt.Subtract(warningWindow);
}

/// <summary>
/// Manages work item claims with exclusivity, timeouts, and concurrency limits
/// </summary>
public class ClaimManager
{
    private readonly ConcurrentDictionary<string, ActiveClaim> _activeClaims = new();
    private readonly WorkItemConfig _config;
    private readonly ConcurrentDictionary<string, int> _agentClaimCounts = new();
    private readonly ConcurrentDictionary<Guid, int> _sessionClaimCounts = new();

    public ClaimManager(WorkItemConfig config)
    {
        _config = config ?? new WorkItemConfig();
    }

    /// <summary>
    /// Attempts to claim a work item with exclusivity checking
    /// </summary>
    public ClaimResult TryClaimWorkItem(WorkItemRef workItem, string assignee, Guid sessionId, out ActiveClaim? claim, TimeSpan? timeout = null)
    {
        claim = null;
        var claimTimeout = timeout ?? _config.DefaultClaimTimeout;

        // Check if item is already claimed
        if (_activeClaims.TryGetValue(workItem.Id, out var existingClaim))
        {
            if (!existingClaim.IsExpired && existingClaim.Assignee != assignee)
            {
                return ClaimResult.AlreadyClaimed;
            }

            // If expired or same assignee, remove the old claim
            _activeClaims.TryRemove(workItem.Id, out _);
            _agentClaimCounts.AddOrUpdate(existingClaim.Assignee, -1, (k, v) => Math.Max(0, v - 1));
            _sessionClaimCounts.AddOrUpdate(existingClaim.SessionId, -1, (k, v) => Math.Max(0, v - 1));
        }

        // Check concurrency limits
        var agentCount = _agentClaimCounts.GetOrAdd(assignee, 0);
        if (agentCount >= _config.MaxConcurrentClaimsPerAgent)
        {
            return ClaimResult.Rejected;
        }

        var sessionCount = _sessionClaimCounts.GetOrAdd(sessionId, 0);
        if (sessionCount >= _config.MaxConcurrentClaimsPerSession)
        {
            return ClaimResult.Rejected;
        }

        // Create new claim
        var expiresAt = DateTimeOffset.UtcNow.Add(claimTimeout);
        claim = new ActiveClaim(workItem, assignee, sessionId, expiresAt);

        if (_activeClaims.TryAdd(workItem.Id, claim))
        {
            _agentClaimCounts[assignee] = agentCount + 1;
            _sessionClaimCounts[sessionId] = sessionCount + 1;
            return ClaimResult.Success;
        }

        return ClaimResult.UnknownError;
    }

    /// <summary>
    /// Releases a work item claim
    /// </summary>
    public ClaimResult ReleaseWorkItem(string workItemId, string assignee)
    {
        if (!_activeClaims.TryGetValue(workItemId, out var claim))
        {
            return ClaimResult.UnknownError;
        }

        if (claim.Assignee != assignee)
        {
            return ClaimResult.Rejected;
        }

        if (_activeClaims.TryRemove(workItemId, out _))
        {
            _agentClaimCounts.AddOrUpdate(assignee, -1, (k, v) => Math.Max(0, v - 1));
            _sessionClaimCounts.AddOrUpdate(claim.SessionId, -1, (k, v) => Math.Max(0, v - 1));
            return ClaimResult.Success;
        }

        return ClaimResult.UnknownError;
    }

    /// <summary>
    /// Renews a claim's expiration time
    /// </summary>
    public ClaimResult RenewClaim(string workItemId, string assignee, TimeSpan? extension = null)
    {
        if (!_activeClaims.TryGetValue(workItemId, out var claim))
        {
            return ClaimResult.UnknownError;
        }

        if (claim.Assignee != assignee)
        {
            return ClaimResult.Rejected;
        }

        var extensionTime = extension ?? _config.DefaultClaimTimeout;
        claim.ExpiresAt = DateTimeOffset.UtcNow.Add(extensionTime);
        claim.LastActivity = DateTimeOffset.UtcNow;

        return ClaimResult.Success;
    }

    /// <summary>
    /// Updates last activity time for a claim
    /// </summary>
    public void UpdateActivity(string workItemId, string assignee)
    {
        if (_activeClaims.TryGetValue(workItemId, out var claim) && claim.Assignee == assignee)
        {
            claim.LastActivity = DateTimeOffset.UtcNow;
        }
    }

    /// <summary>
    /// Gets all active claims
    /// </summary>
    public IReadOnlyList<ActiveClaim> GetActiveClaims()
    {
        return _activeClaims.Values.ToList();
    }

    /// <summary>
    /// Gets claims for a specific assignee
    /// </summary>
    public IReadOnlyList<ActiveClaim> GetClaimsForAssignee(string assignee)
    {
        return _activeClaims.Values.Where(c => c.Assignee == assignee).ToList();
    }

    /// <summary>
    /// Gets claims for a specific session
    /// </summary>
    public IReadOnlyList<ActiveClaim> GetClaimsForSession(Guid sessionId)
    {
        return _activeClaims.Values.Where(c => c.SessionId == sessionId).ToList();
    }

    /// <summary>
    /// Cleans up expired claims
    /// </summary>
    public IReadOnlyList<ActiveClaim> CleanupExpiredClaims()
    {
        var expiredClaims = new List<ActiveClaim>();

        foreach (var kvp in _activeClaims)
        {
            if (kvp.Value.IsExpired)
            {
                expiredClaims.Add(kvp.Value);
                _activeClaims.TryRemove(kvp.Key, out _);
                _agentClaimCounts.AddOrUpdate(kvp.Value.Assignee, -1, (k, v) => Math.Max(0, v - 1));
                _sessionClaimCounts.AddOrUpdate(kvp.Value.SessionId, -1, (k, v) => Math.Max(0, v - 1));
            }
        }

        return expiredClaims;
    }

    /// <summary>
    /// Gets claims that are expiring soon
    /// </summary>
    public IReadOnlyList<ActiveClaim> GetExpiringClaims(TimeSpan warningWindow)
    {
        return _activeClaims.Values.Where(c => c.IsExpiringSoon(warningWindow)).ToList();
    }
}