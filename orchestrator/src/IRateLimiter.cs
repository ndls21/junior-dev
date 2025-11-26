using System;
using System.Threading.Tasks;
using JuniorDev.Contracts;

namespace JuniorDev.Orchestrator;

public interface IRateLimiter
{
    Task<RateLimitResult> CheckRateLimit(ICommand command, SessionConfig sessionConfig);
}

public record RateLimitResult(bool Allowed, DateTimeOffset? RetryAfter = null);