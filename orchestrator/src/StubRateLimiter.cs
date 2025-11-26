using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using JuniorDev.Contracts;

namespace JuniorDev.Orchestrator;

public class StubRateLimiter : IRateLimiter
{
    private readonly ConcurrentDictionary<(Guid SessionId, string Adapter), RateLimitState> _states = new();

    public Task<RateLimitResult> CheckRateLimit(ICommand command, SessionConfig sessionConfig)
    {
        var key = (sessionConfig.SessionId, GetAdapterName(command));
        var state = _states.GetOrAdd(key, _ => new RateLimitState());

        var limits = sessionConfig.Policy.Limits;
        if (limits?.CallsPerMinute.HasValue == true)
        {
            var now = DateTimeOffset.UtcNow;
            // Reset if minute passed
            if ((now - state.LastReset).TotalMinutes >= 1)
            {
                state.Count = 0;
                state.LastReset = now;
            }

            if (state.Count >= limits.CallsPerMinute.Value)
            {
                var retryAfter = state.LastReset.AddMinutes(1);
                return Task.FromResult(new RateLimitResult(false, retryAfter));
            }

            state.Count++;
        }

        return Task.FromResult(new RateLimitResult(true));
    }

    private string GetAdapterName(ICommand command)
    {
        // Simple mapping
        return command switch
        {
            CreateBranch or ApplyPatch or RunTests or Commit or Push or GetDiff => "Vcs",
            TransitionTicket or Comment or SetAssignee => "WorkItems",
            _ => "Unknown"
        };
    }

    private class RateLimitState
    {
        public int Count { get; set; }
        public DateTimeOffset LastReset { get; set; } = DateTimeOffset.UtcNow;
    }
}