using System;
using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Threading.Tasks;
using JuniorDev.Contracts;

namespace JuniorDev.Orchestrator;

/// <summary>
/// Token bucket rate limiter implementation supporting per-session, per-command, and global limits.
/// </summary>
public class TokenBucketRateLimiter : IRateLimiter
{
    private readonly ConcurrentDictionary<string, TokenBucket> _buckets = new();
    private readonly Meter _meter;
    private readonly Counter<long> _throttleCount;

    public TokenBucketRateLimiter()
    {
        _meter = new Meter("JuniorDev.Orchestrator.TokenBucketRateLimiter", "1.0.0");
        _throttleCount = _meter.CreateCounter<long>("rate_limit_throttles", "throttles", "Number of requests throttled by rate limiter");
    }

    public Task<RateLimitResult> CheckRateLimit(ICommand command, SessionConfig sessionConfig)
    {
        var now = DateTimeOffset.UtcNow;

        // Check global per-command caps first
        if (sessionConfig.Policy.Limits?.PerCommandCaps != null && sessionConfig.Policy.Limits.PerCommandCaps.TryGetValue(command.Kind, out var globalCommandCap))
        {
            var globalCommandKey = $"global:command:{command.Kind}";
            if (!CheckTokenBucket(globalCommandKey, globalCommandCap, globalCommandCap, now, out var globalCommandRetryAfter))
            {
                _throttleCount.Add(1);
                return Task.FromResult(new RateLimitResult(false, globalCommandRetryAfter));
            }
        }
        else
        {
            // Check global limits only if no per-command caps for this command
            var globalLimits = sessionConfig.Policy.Limits;
            if (globalLimits != null)
            {
                var globalKey = $"global:{sessionConfig.SessionId}";
                if (!CheckLimits(globalKey, globalLimits, command, now, out var globalRetryAfter))
                {
                    _throttleCount.Add(1);
                    return Task.FromResult(new RateLimitResult(false, globalRetryAfter));
                }
            }

            // Check per-session limits only if no per-command caps for this command
            var sessionLimits = sessionConfig.Policy.Limits;
            if (sessionLimits != null)
            {
                var sessionKey = $"session:{sessionConfig.SessionId}";
                if (!CheckLimits(sessionKey, sessionLimits, command, now, out var sessionRetryAfter))
                {
                    _throttleCount.Add(1);
                    return Task.FromResult(new RateLimitResult(false, sessionRetryAfter));
                }
            }
        }

        return Task.FromResult(new RateLimitResult(true));
    }

    private bool CheckLimits(string key, RateLimits limits, ICommand command, DateTimeOffset now, out DateTimeOffset? retryAfter)
    {
        retryAfter = null;

        // Check calls per minute with burst
        if (limits.CallsPerMinute.HasValue)
        {
            var burst = limits.Burst ?? limits.CallsPerMinute.Value; // Default burst to CallsPerMinute
            if (!CheckTokenBucket(key, limits.CallsPerMinute.Value, burst, now, out retryAfter))
            {
                return false;
            }
        }

        return true;
    }

    private bool CheckTokenBucket(string key, int ratePerMinute, int burst, DateTimeOffset now, out DateTimeOffset? retryAfter)
    {
        retryAfter = null;

        var bucket = _buckets.GetOrAdd(key, _ => new TokenBucket(ratePerMinute, burst, now));

        // Refill tokens based on time passed
        bucket.Refill(now);

        if (bucket.Tokens >= 1)
        {
            bucket.Tokens -= 1;
            return true;
        }

        // Calculate when next token will be available
        if (ratePerMinute == 0)
        {
            // Never allow when rate is 0
            retryAfter = DateTimeOffset.MaxValue;
            return false;
        }

        var tokensNeeded = 1 - bucket.Tokens;
        var refillTime = TimeSpan.FromMinutes(tokensNeeded / (double)ratePerMinute);
        retryAfter = now + refillTime;
        return false;
    }

    private class TokenBucket
    {
        public double Tokens { get; set; }
        public DateTimeOffset LastRefill { get; set; }
        private readonly int _ratePerMinute;
        private readonly int _capacity;

        public TokenBucket(int ratePerMinute, int capacity, DateTimeOffset now)
        {
            _ratePerMinute = ratePerMinute;
            _capacity = capacity;
            Tokens = capacity; // Start full
            LastRefill = now;
        }

        public void Refill(DateTimeOffset now)
        {
            var timePassed = now - LastRefill;
            var tokensToAdd = timePassed.TotalMinutes * _ratePerMinute;

            Tokens = Math.Min(_capacity, Tokens + tokensToAdd);
            LastRefill = now;
        }
    }
}
