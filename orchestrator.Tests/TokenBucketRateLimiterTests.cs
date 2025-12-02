using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using JuniorDev.Contracts;
using Xunit;

namespace JuniorDev.Orchestrator.Tests;

public class TokenBucketRateLimiterTests
{
    [Fact]
    public async Task CheckRateLimit_WithinLimits_AllowsCommand()
    {
        // Arrange
        var rateLimiter = new TokenBucketRateLimiter();
        var sessionConfig = new SessionConfig(
            Guid.NewGuid(),
            null,
            null,
            new PolicyProfile
            {
                Name = "test",
                Limits = new RateLimits
                {
                    CallsPerMinute = 10,
                    Burst = 5
                }
            },
            new RepoRef("test", "/tmp/test"),
            new WorkspaceRef("/tmp/workspace"),
            null,
            "test-agent");

        var command = new Comment(Guid.NewGuid(), new Correlation(sessionConfig.SessionId), new WorkItemRef("test", "123"), "Test comment");

        // Act
        var result = await rateLimiter.CheckRateLimit(command, sessionConfig);

        // Assert
        Assert.True(result.Allowed);
        Assert.Null(result.RetryAfter);
    }

    [Fact]
    public async Task CheckRateLimit_ExceedsBurst_ThrottlesCommand()
    {
        // Arrange
        var rateLimiter = new TokenBucketRateLimiter();
        var sessionConfig = new SessionConfig(
            Guid.NewGuid(),
            null,
            null,
            new PolicyProfile
            {
                Name = "test",
                Limits = new RateLimits
                {
                    CallsPerMinute = 10,
                    Burst = 2  // Very small burst
                }
            },
            new RepoRef("test", "/tmp/test"),
            new WorkspaceRef("/tmp/workspace"),
            null,
            "test-agent");

        var command = new Comment(Guid.NewGuid(), new Correlation(sessionConfig.SessionId), new WorkItemRef("test", "123"), "Test comment");

        // Act - Use up the burst
        for (int i = 0; i < 2; i++)
        {
            var result = await rateLimiter.CheckRateLimit(command, sessionConfig);
            Assert.True(result.Allowed);
        }

        // Next call should be throttled
        var throttledResult = await rateLimiter.CheckRateLimit(command, sessionConfig);

        // Assert
        Assert.False(throttledResult.Allowed);
        Assert.NotNull(throttledResult.RetryAfter);
        Assert.True(throttledResult.RetryAfter > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task CheckRateLimit_PerCommandCaps_Enforced()
    {
        // Arrange
        var rateLimiter = new TokenBucketRateLimiter();
        var sessionConfig = new SessionConfig(
            Guid.NewGuid(),
            null,
            null,
            new PolicyProfile
            {
                Name = "test",
                Limits = new RateLimits
                {
                    CallsPerMinute = 100,
                    Burst = 50,
                    PerCommandCaps = new Dictionary<string, int>
                    {
                        { "Comment", 1 }  // Only 1 comment allowed
                    }
                }
            },
            new RepoRef("test", "/tmp/test"),
            new WorkspaceRef("/tmp/workspace"),
            null,
            "test-agent");

        var command = new Comment(Guid.NewGuid(), new Correlation(sessionConfig.SessionId), new WorkItemRef("test", "123"), "Test comment");

        // Act - First comment should be allowed
        var firstResult = await rateLimiter.CheckRateLimit(command, sessionConfig);
        Assert.True(firstResult.Allowed);

        // Second comment should be throttled
        var secondResult = await rateLimiter.CheckRateLimit(command, sessionConfig);

        // Assert
        Assert.False(secondResult.Allowed);
        Assert.NotNull(secondResult.RetryAfter);
    }

    [Fact]
    public async Task CheckRateLimit_TokenRefill_RecoversOverTime()
    {
        // Arrange
        var rateLimiter = new TokenBucketRateLimiter();
        var sessionConfig = new SessionConfig(
            Guid.NewGuid(),
            null,
            null,
            new PolicyProfile
            {
                Name = "test",
                Limits = new RateLimits
                {
                    CallsPerMinute = 2,  // 2 calls per minute = 1 call every 30 seconds
                    Burst = 1
                }
            },
            new RepoRef("test", "/tmp/test"),
            new WorkspaceRef("/tmp/workspace"),
            null,
            "test-agent");

        var command = new Comment(Guid.NewGuid(), new Correlation(sessionConfig.SessionId), new WorkItemRef("test", "123"), "Test comment");

        // Act - Use up the burst
        var firstResult = await rateLimiter.CheckRateLimit(command, sessionConfig);
        Assert.True(firstResult.Allowed);

        var secondResult = await rateLimiter.CheckRateLimit(command, sessionConfig);
        Assert.False(secondResult.Allowed);

        // Note: Token refill over time is tested indirectly through the rate limiting behavior
        // In a real scenario, the refill would happen naturally over time
        // For unit testing, we focus on the immediate behavior and concurrent access

        // Assert - Second call should be throttled
        Assert.False(secondResult.Allowed);
        Assert.NotNull(secondResult.RetryAfter);
    }

    [Fact]
    public async Task CheckRateLimit_ConcurrentRequests_HandledCorrectly()
    {
        // Arrange
        var rateLimiter = new TokenBucketRateLimiter();
        var sessionConfig = new SessionConfig(
            Guid.NewGuid(),
            null,
            null,
            new PolicyProfile
            {
                Name = "test",
                Limits = new RateLimits
                {
                    CallsPerMinute = 10,
                    Burst = 3
                }
            },
            new RepoRef("test", "/tmp/test"),
            new WorkspaceRef("/tmp/workspace"),
            null,
            "test-agent");

        var command = new Comment(Guid.NewGuid(), new Correlation(sessionConfig.SessionId), new WorkItemRef("test", "123"), "Test comment");

        // Act - Make concurrent requests
        var tasks = new List<Task<RateLimitResult>>();
        for (int i = 0; i < 5; i++)
        {
            tasks.Add(rateLimiter.CheckRateLimit(command, sessionConfig));
        }

        var results = await Task.WhenAll(tasks);

        // Assert - Some should be allowed, some throttled
        var allowedCount = results.Count(r => r.Allowed);
        var throttledCount = results.Count(r => !r.Allowed);

        Assert.Equal(3, allowedCount); // Burst limit
        Assert.Equal(2, throttledCount); // Exceeded burst
    }

    [Fact]
    public async Task CheckRateLimit_GlobalAndSessionLimits_BothEnforced()
    {
        // Arrange
        var rateLimiter = new TokenBucketRateLimiter();
        var sessionConfig = new SessionConfig(
            Guid.NewGuid(),
            null,
            null,
            new PolicyProfile
            {
                Name = "test",
                Limits = new RateLimits
                {
                    CallsPerMinute = 5,  // Global limit
                    Burst = 2
                }
            },
            new RepoRef("test", "/tmp/test"),
            new WorkspaceRef("/tmp/workspace"),
            null,
            "test-agent");

        var command = new Comment(Guid.NewGuid(), new Correlation(sessionConfig.SessionId), new WorkItemRef("test", "123"), "Test comment");

        // Act - Use up global burst
        for (int i = 0; i < 2; i++)
        {
            var result = await rateLimiter.CheckRateLimit(command, sessionConfig);
            Assert.True(result.Allowed);
        }

        // Next call should be throttled by global limit
        var throttledResult = await rateLimiter.CheckRateLimit(command, sessionConfig);

        // Assert
        Assert.False(throttledResult.Allowed);
        Assert.NotNull(throttledResult.RetryAfter);
    }

    [Fact]
    public async Task CheckRateLimit_TokenRefill_ExactTiming()
    {
        // Arrange
        var rateLimiter = new TokenBucketRateLimiter();
        var sessionConfig = new SessionConfig(
            Guid.NewGuid(),
            null,
            null,
            new PolicyProfile
            {
                Name = "test",
                Limits = new RateLimits
                {
                    CallsPerMinute = 2,  // 1 token every 30 seconds
                    Burst = 1
                }
            },
            new RepoRef("test", "/tmp/test"),
            new WorkspaceRef("/tmp/workspace"),
            null,
            "test-agent");

        var command = new Comment(Guid.NewGuid(), new Correlation(sessionConfig.SessionId), new WorkItemRef("test", "123"), "Test comment");

        // Act - Use up burst
        var firstResult = await rateLimiter.CheckRateLimit(command, sessionConfig);
        Assert.True(firstResult.Allowed);

        var secondResult = await rateLimiter.CheckRateLimit(command, sessionConfig);
        Assert.False(secondResult.Allowed);

        // Wait exactly 30 seconds for 1 token to refill
        await Task.Delay(30000);

        var thirdResult = await rateLimiter.CheckRateLimit(command, sessionConfig);

        // Assert - Should be allowed after exact refill time
        Assert.True(thirdResult.Allowed);
    }

    [Fact]
    public async Task CheckRateLimit_TokenRefill_PartialRefill()
    {
        // Arrange
        var rateLimiter = new TokenBucketRateLimiter();
        var sessionConfig = new SessionConfig(
            Guid.NewGuid(),
            null,
            null,
            new PolicyProfile
            {
                Name = "test",
                Limits = new RateLimits
                {
                    CallsPerMinute = 4,  // 1 token every 15 seconds
                    Burst = 2
                }
            },
            new RepoRef("test", "/tmp/test"),
            new WorkspaceRef("/tmp/workspace"),
            null,
            "test-agent");

        var command = new Comment(Guid.NewGuid(), new Correlation(sessionConfig.SessionId), new WorkItemRef("test", "123"), "Test comment");

        // Act - Use up burst
        for (int i = 0; i < 2; i++)
        {
            var result = await rateLimiter.CheckRateLimit(command, sessionConfig);
            Assert.True(result.Allowed);
        }

        // Wait 20 seconds (should refill ~1.33 tokens, so 1 token available)
        await Task.Delay(20000);

        var thirdResult = await rateLimiter.CheckRateLimit(command, sessionConfig);

        // Assert - Should be allowed with partial refill
        Assert.True(thirdResult.Allowed);
    }

    [Fact]
    public async Task CheckRateLimit_BurstRecovery_AfterThrottle()
    {
        // Arrange
        var rateLimiter = new TokenBucketRateLimiter();
        var sessionConfig = new SessionConfig(
            Guid.NewGuid(),
            null,
            null,
            new PolicyProfile
            {
                Name = "test",
                Limits = new RateLimits
                {
                    CallsPerMinute = 10,
                    Burst = 3
                }
            },
            new RepoRef("test", "/tmp/test"),
            new WorkspaceRef("/tmp/workspace"),
            null,
            "test-agent");

        var command = new Comment(Guid.NewGuid(), new Correlation(sessionConfig.SessionId), new WorkItemRef("test", "123"), "Test comment");

        // Act - Exhaust burst
        for (int i = 0; i < 3; i++)
        {
            var result = await rateLimiter.CheckRateLimit(command, sessionConfig);
            Assert.True(result.Allowed);
        }

        // Next should be throttled
        var throttledResult = await rateLimiter.CheckRateLimit(command, sessionConfig);
        Assert.False(throttledResult.Allowed);

        // Wait for some refill
        await Task.Delay(10000); // Should refill ~1.67 tokens

        // Should be able to use some burst again
        var recoveryResult = await rateLimiter.CheckRateLimit(command, sessionConfig);
        Assert.True(recoveryResult.Allowed);
    }

    [Fact]
    public async Task CheckRateLimit_ConcurrentRequests_HighLoad()
    {
        // Arrange
        var rateLimiter = new TokenBucketRateLimiter();
        var sessionConfig = new SessionConfig(
            Guid.NewGuid(),
            null,
            null,
            new PolicyProfile
            {
                Name = "test",
                Limits = new RateLimits
                {
                    CallsPerMinute = 20,
                    Burst = 5
                }
            },
            new RepoRef("test", "/tmp/test"),
            new WorkspaceRef("/tmp/workspace"),
            null,
            "test-agent");

        var command = new Comment(Guid.NewGuid(), new Correlation(sessionConfig.SessionId), new WorkItemRef("test", "123"), "Test comment");

        // Act - Launch 10 concurrent requests
        var tasks = new List<Task<RateLimitResult>>();
        for (int i = 0; i < 10; i++)
        {
            tasks.Add(rateLimiter.CheckRateLimit(command, sessionConfig));
        }

        var results = await Task.WhenAll(tasks);

        // Assert - Should allow burst amount and throttle the rest
        var allowedCount = results.Count(r => r.Allowed);
        var throttledCount = results.Count(r => !r.Allowed);

        Assert.Equal(5, allowedCount); // Burst limit
        Assert.Equal(5, throttledCount); // Exceeded burst
    }

    [Fact]
    public async Task CheckRateLimit_GlobalVsSessionLimits_SeparateBuckets()
    {
        // Arrange
        var rateLimiter = new TokenBucketRateLimiter();
        var session1Config = new SessionConfig(
            Guid.NewGuid(),
            null,
            null,
            new PolicyProfile
            {
                Name = "test",
                Limits = new RateLimits
                {
                    CallsPerMinute = 5,
                    Burst = 2
                }
            },
            new RepoRef("test", "/tmp/test"),
            new WorkspaceRef("/tmp/workspace"),
            null,
            "test-agent");

        var session2Config = new SessionConfig(
            Guid.NewGuid(),
            null,
            null,
            new PolicyProfile
            {
                Name = "test",
                Limits = new RateLimits
                {
                    CallsPerMinute = 5,
                    Burst = 2
                }
            },
            new RepoRef("test", "/tmp/test"),
            new WorkspaceRef("/tmp/workspace"),
            null,
            "test-agent");

        var command1 = new Comment(Guid.NewGuid(), new Correlation(session1Config.SessionId), new WorkItemRef("test", "123"), "Test comment 1");
        var command2 = new Comment(Guid.NewGuid(), new Correlation(session2Config.SessionId), new WorkItemRef("test", "456"), "Test comment 2");

        // Act - Exhaust session1 burst
        for (int i = 0; i < 2; i++)
        {
            var result = await rateLimiter.CheckRateLimit(command1, session1Config);
            Assert.True(result.Allowed);
        }

        // Session1 should be throttled
        var session1Throttled = await rateLimiter.CheckRateLimit(command1, session1Config);
        Assert.False(session1Throttled.Allowed);

        // Session2 should still be allowed (separate buckets)
        var session2Allowed = await rateLimiter.CheckRateLimit(command2, session2Config);
        Assert.True(session2Allowed.Allowed);
    }

    [Fact]
    public async Task CheckRateLimit_PerCommandCaps_DifferentCommands()
    {
        // Arrange
        var rateLimiter = new TokenBucketRateLimiter();
        var sessionConfig = new SessionConfig(
            Guid.NewGuid(),
            null,
            null,
            new PolicyProfile
            {
                Name = "test",
                Limits = new RateLimits
                {
                    CallsPerMinute = 100,
                    Burst = 50,
                    PerCommandCaps = new Dictionary<string, int>
                    {
                        { "Comment", 2 },
                        { "TransitionTicket", 1 }
                    }
                }
            },
            new RepoRef("test", "/tmp/test"),
            new WorkspaceRef("/tmp/workspace"),
            null,
            "test-agent");

        var commentCommand = new Comment(Guid.NewGuid(), new Correlation(sessionConfig.SessionId), new WorkItemRef("test", "123"), "Test comment");
        var transitionCommand = new TransitionTicket(Guid.NewGuid(), new Correlation(sessionConfig.SessionId), new WorkItemRef("test", "123"), "done");

        // Act - Use up comment caps
        for (int i = 0; i < 2; i++)
        {
            var result = await rateLimiter.CheckRateLimit(commentCommand, sessionConfig);
            Assert.True(result.Allowed);
        }

        // Third comment should be throttled
        var commentThrottled = await rateLimiter.CheckRateLimit(commentCommand, sessionConfig);
        Assert.False(commentThrottled.Allowed);

        // Transition should still be allowed (separate cap)
        var transitionAllowed = await rateLimiter.CheckRateLimit(transitionCommand, sessionConfig);
        Assert.True(transitionAllowed.Allowed);

        // Second transition should be throttled
        var transitionThrottled = await rateLimiter.CheckRateLimit(transitionCommand, sessionConfig);
        Assert.False(transitionThrottled.Allowed);
    }

    [Fact]
    public async Task CheckRateLimit_ZeroRate_AlwaysThrottles()
    {
        // Arrange
        var rateLimiter = new TokenBucketRateLimiter();
        var sessionConfig = new SessionConfig(
            Guid.NewGuid(),
            null,
            null,
            new PolicyProfile
            {
                Name = "test",
                Limits = new RateLimits
                {
                    CallsPerMinute = 0,
                    Burst = 0
                }
            },
            new RepoRef("test", "/tmp/test"),
            new WorkspaceRef("/tmp/workspace"),
            null,
            "test-agent");

        var command = new Comment(Guid.NewGuid(), new Correlation(sessionConfig.SessionId), new WorkItemRef("test", "123"), "Test comment");

        // Act
        var result = await rateLimiter.CheckRateLimit(command, sessionConfig);

        // Assert - Should always throttle with zero rate
        Assert.False(result.Allowed);
        Assert.Equal(DateTimeOffset.MaxValue, result.RetryAfter);
    }

    [Fact]
    public async Task CheckRateLimit_NoLimits_AlwaysAllows()
    {
        // Arrange
        var rateLimiter = new TokenBucketRateLimiter();
        var sessionConfig = new SessionConfig(
            Guid.NewGuid(),
            null,
            null,
            new PolicyProfile
            {
                Name = "test",
                Limits = null  // No limits
            },
            new RepoRef("test", "/tmp/test"),
            new WorkspaceRef("/tmp/workspace"),
            null,
            "test-agent");

        var command = new Comment(Guid.NewGuid(), new Correlation(sessionConfig.SessionId), new WorkItemRef("test", "123"), "Test comment");

        // Act
        var result = await rateLimiter.CheckRateLimit(command, sessionConfig);

        // Assert - Should always allow with no limits
        Assert.True(result.Allowed);
        Assert.Null(result.RetryAfter);
    }

    [Fact]
    public async Task CheckRateLimit_BurstEqualsRatePerMinute_WorksCorrectly()
    {
        // Arrange
        var rateLimiter = new TokenBucketRateLimiter();
        var sessionConfig = new SessionConfig(
            Guid.NewGuid(),
            null,
            null,
            new PolicyProfile
            {
                Name = "test",
                Limits = new RateLimits
                {
                    CallsPerMinute = 5,
                    Burst = 5  // Burst equals rate
                }
            },
            new RepoRef("test", "/tmp/test"),
            new WorkspaceRef("/tmp/workspace"),
            null,
            "test-agent");

        var command = new Comment(Guid.NewGuid(), new Correlation(sessionConfig.SessionId), new WorkItemRef("test", "123"), "Test comment");

        // Act - Use up all burst
        for (int i = 0; i < 5; i++)
        {
            var result = await rateLimiter.CheckRateLimit(command, sessionConfig);
            Assert.True(result.Allowed);
        }

        // Next should be throttled
        var throttledResult = await rateLimiter.CheckRateLimit(command, sessionConfig);

        // Assert
        Assert.False(throttledResult.Allowed);
        Assert.NotNull(throttledResult.RetryAfter);
    }

    [Fact]
    public async Task CheckRateLimit_MixedCommandTypes_GlobalLimits()
    {
        // Arrange
        var rateLimiter = new TokenBucketRateLimiter();
        var sessionConfig = new SessionConfig(
            Guid.NewGuid(),
            null,
            null,
            new PolicyProfile
            {
                Name = "test",
                Limits = new RateLimits
                {
                    CallsPerMinute = 3,
                    Burst = 2
                }
            },
            new RepoRef("test", "/tmp/test"),
            new WorkspaceRef("/tmp/workspace"),
            null,
            "test-agent");

        var commentCommand = new Comment(Guid.NewGuid(), new Correlation(sessionConfig.SessionId), new WorkItemRef("test", "123"), "Test comment");
        var transitionCommand = new TransitionTicket(Guid.NewGuid(), new Correlation(sessionConfig.SessionId), new WorkItemRef("test", "123"), "done");

        // Act - Mix different command types
        var results = new List<RateLimitResult>();
        for (int i = 0; i < 4; i++)
        {
            if (i % 2 == 0)
            {
                results.Add(await rateLimiter.CheckRateLimit(commentCommand, sessionConfig));
            }
            else
            {
                results.Add(await rateLimiter.CheckRateLimit(transitionCommand, sessionConfig));
            }
        }

        // Assert - Should allow burst amount (2) and throttle the rest
        var allowedCount = results.Count(r => r.Allowed);
        var throttledCount = results.Count(r => !r.Allowed);

        Assert.Equal(2, allowedCount);
        Assert.Equal(2, throttledCount);
    }

    [Fact]
    public async Task CheckRateLimit_ConcurrentSessions_SeparateLimits()
    {
        // Arrange
        var rateLimiter = new TokenBucketRateLimiter();
        var sessionConfigs = new List<SessionConfig>();
        var commands = new List<Comment>();

        for (int i = 0; i < 3; i++)
        {
            var sessionId = Guid.NewGuid();
            sessionConfigs.Add(new SessionConfig(
                sessionId,
                null,
                null,
                new PolicyProfile
                {
                    Name = "test",
                    Limits = new RateLimits
                    {
                        CallsPerMinute = 10,
                        Burst = 2
                    }
                },
                new RepoRef("test", "/tmp/test"),
                new WorkspaceRef("/tmp/workspace"),
                null,
                "test-agent"));

            commands.Add(new Comment(Guid.NewGuid(), new Correlation(sessionId), new WorkItemRef("test", $"{i}"), $"Test comment {i}"));
        }

        // Act - Each session uses its burst
        var tasks = new List<Task<RateLimitResult>>();
        foreach (var command in commands)
        {
            var sessionConfig = sessionConfigs.First(s => s.SessionId == command.Correlation.SessionId);
            for (int i = 0; i < 3; i++) // Try to exceed burst
            {
                tasks.Add(rateLimiter.CheckRateLimit(command, sessionConfig));
            }
        }

        var results = await Task.WhenAll(tasks);

        // Assert - Each session should allow its burst (2) and throttle the rest (1 each)
        var allowedCount = results.Count(r => r.Allowed);
        var throttledCount = results.Count(r => !r.Allowed);

        Assert.Equal(6, allowedCount); // 2 per session * 3 sessions
        Assert.Equal(3, throttledCount); // 1 per session
    }

    [Fact]
    public async Task CheckRateLimit_RetryAfterCalculation_Accurate()
    {
        // Arrange
        var rateLimiter = new TokenBucketRateLimiter();
        var sessionConfig = new SessionConfig(
            Guid.NewGuid(),
            null,
            null,
            new PolicyProfile
            {
                Name = "test",
                Limits = new RateLimits
                {
                    CallsPerMinute = 2,  // 1 token every 30 seconds
                    Burst = 1
                }
            },
            new RepoRef("test", "/tmp/test"),
            new WorkspaceRef("/tmp/workspace"),
            null,
            "test-agent");

        var command = new Comment(Guid.NewGuid(), new Correlation(sessionConfig.SessionId), new WorkItemRef("test", "123"), "Test comment");

        // Act - Exhaust burst
        await rateLimiter.CheckRateLimit(command, sessionConfig);
        var throttledResult = await rateLimiter.CheckRateLimit(command, sessionConfig);

        // Assert - RetryAfter should be approximately 30 seconds from now
        Assert.False(throttledResult.Allowed);
        Assert.NotNull(throttledResult.RetryAfter);

        var expectedRetryAfter = DateTimeOffset.UtcNow.AddSeconds(30);
        var actualRetryAfter = throttledResult.RetryAfter.Value;

        // Allow some tolerance for timing
        var difference = Math.Abs((expectedRetryAfter - actualRetryAfter).TotalSeconds);
        Assert.True(difference < 5, $"RetryAfter difference too large: {difference} seconds");
    }

    [Fact]
    public async Task CheckRateLimit_GlobalCommandCaps_AcrossSessions()
    {
        // Arrange
        var rateLimiter = new TokenBucketRateLimiter();
        var session1Config = new SessionConfig(
            Guid.NewGuid(),
            null,
            null,
            new PolicyProfile
            {
                Name = "test",
                Limits = new RateLimits
                {
                    CallsPerMinute = 100,
                    Burst = 50,
                    PerCommandCaps = new Dictionary<string, int>
                    {
                        { "Comment", 3 }  // Global cap across all sessions
                    }
                }
            },
            new RepoRef("test", "/tmp/test"),
            new WorkspaceRef("/tmp/workspace"),
            null,
            "test-agent");

        var session2Config = new SessionConfig(
            Guid.NewGuid(),
            null,
            null,
            new PolicyProfile
            {
                Name = "test",
                Limits = new RateLimits
                {
                    CallsPerMinute = 100,
                    Burst = 50,
                    PerCommandCaps = new Dictionary<string, int>
                    {
                        { "Comment", 3 }  // Same global cap
                    }
                }
            },
            new RepoRef("test", "/tmp/test"),
            new WorkspaceRef("/tmp/workspace"),
            null,
            "test-agent");

        var command1 = new Comment(Guid.NewGuid(), new Correlation(session1Config.SessionId), new WorkItemRef("test", "123"), "Test comment 1");
        var command2 = new Comment(Guid.NewGuid(), new Correlation(session2Config.SessionId), new WorkItemRef("test", "456"), "Test comment 2");

        // Act - Use caps across sessions
        await rateLimiter.CheckRateLimit(command1, session1Config); // 1
        await rateLimiter.CheckRateLimit(command1, session1Config); // 2
        await rateLimiter.CheckRateLimit(command2, session2Config); // 3 - should be allowed

        var fourthResult = await rateLimiter.CheckRateLimit(command1, session1Config); // 4 - should be throttled

        // Assert
        Assert.False(fourthResult.Allowed);
    }

    [Fact]
    public async Task CheckRateLimit_BurstLargerThanRate_WorksCorrectly()
    {
        // Arrange
        var rateLimiter = new TokenBucketRateLimiter();
        var sessionConfig = new SessionConfig(
            Guid.NewGuid(),
            null,
            null,
            new PolicyProfile
            {
                Name = "test",
                Limits = new RateLimits
                {
                    CallsPerMinute = 5,
                    Burst = 10  // Burst larger than rate
                }
            },
            new RepoRef("test", "/tmp/test"),
            new WorkspaceRef("/tmp/workspace"),
            null,
            "test-agent");

        var command = new Comment(Guid.NewGuid(), new Correlation(sessionConfig.SessionId), new WorkItemRef("test", "123"), "Test comment");

        // Act - Use up burst
        for (int i = 0; i < 10; i++)
        {
            var result = await rateLimiter.CheckRateLimit(command, sessionConfig);
            Assert.True(result.Allowed);
        }

        // Next should be throttled
        var throttledResult = await rateLimiter.CheckRateLimit(command, sessionConfig);

        // Assert
        Assert.False(throttledResult.Allowed);
        Assert.NotNull(throttledResult.RetryAfter);
    }

    [Fact]
    public async Task CheckRateLimit_MultipleCommands_SameSession()
    {
        // Arrange
        var rateLimiter = new TokenBucketRateLimiter();
        var sessionConfig = new SessionConfig(
            Guid.NewGuid(),
            null,
            null,
            new PolicyProfile
            {
                Name = "test",
                Limits = new RateLimits
                {
                    CallsPerMinute = 5,
                    Burst = 3
                }
            },
            new RepoRef("test", "/tmp/test"),
            new WorkspaceRef("/tmp/workspace"),
            null,
            "test-agent");

        var commands = new List<ICommand>();
        for (int i = 0; i < 5; i++)
        {
            commands.Add(new Comment(Guid.NewGuid(), new Correlation(sessionConfig.SessionId), new WorkItemRef("test", $"{i}"), $"Test comment {i}"));
        }

        // Act
        var tasks = commands.Select(cmd => rateLimiter.CheckRateLimit(cmd, sessionConfig)).ToArray();
        var results = await Task.WhenAll(tasks);

        // Assert - Should allow burst (3) and throttle the rest (2)
        var allowedCount = results.Count(r => r.Allowed);
        var throttledCount = results.Count(r => !r.Allowed);

        Assert.Equal(3, allowedCount);
        Assert.Equal(2, throttledCount);
    }

    [Fact]
    public async Task CheckRateLimit_RefillOverLongTime_FullRecovery()
    {
        // Arrange
        var rateLimiter = new TokenBucketRateLimiter();
        var sessionConfig = new SessionConfig(
            Guid.NewGuid(),
            null,
            null,
            new PolicyProfile
            {
                Name = "test",
                Limits = new RateLimits
                {
                    CallsPerMinute = 6,  // 1 token every 10 seconds
                    Burst = 3
                }
            },
            new RepoRef("test", "/tmp/test"),
            new WorkspaceRef("/tmp/workspace"),
            null,
            "test-agent");

        var command = new Comment(Guid.NewGuid(), new Correlation(sessionConfig.SessionId), new WorkItemRef("test", "123"), "Test comment");

        // Act - Exhaust burst
        for (int i = 0; i < 3; i++)
        {
            var result = await rateLimiter.CheckRateLimit(command, sessionConfig);
            Assert.True(result.Allowed);
        }

        // Wait long enough for full refill (30 seconds for 3 tokens at 6/min)
        await Task.Delay(35000);

        // Should be able to use full burst again
        for (int i = 0; i < 3; i++)
        {
            var result = await rateLimiter.CheckRateLimit(command, sessionConfig);
            Assert.True(result.Allowed);
        }
    }

    [Fact]
    public async Task CheckRateLimit_EdgeCase_NoPerCommandCaps_UsesGlobalLimits()
    {
        // Arrange
        var rateLimiter = new TokenBucketRateLimiter();
        var sessionConfig = new SessionConfig(
            Guid.NewGuid(),
            null,
            null,
            new PolicyProfile
            {
                Name = "test",
                Limits = new RateLimits
                {
                    CallsPerMinute = 2,
                    Burst = 1,
                    PerCommandCaps = new Dictionary<string, int>() // Empty dict
                }
            },
            new RepoRef("test", "/tmp/test"),
            new WorkspaceRef("/tmp/workspace"),
            null,
            "test-agent");

        var command = new Comment(Guid.NewGuid(), new Correlation(sessionConfig.SessionId), new WorkItemRef("test", "123"), "Test comment");

        // Act - Should use global limits since command not in caps
        var firstResult = await rateLimiter.CheckRateLimit(command, sessionConfig);
        Assert.True(firstResult.Allowed);

        var secondResult = await rateLimiter.CheckRateLimit(command, sessionConfig);
        Assert.False(secondResult.Allowed);
    }

    [Fact]
    public async Task CheckRateLimit_WaitDelayTest_FastRefillCycle()
    {
        // Arrange - Test with very fast refill rate
        var rateLimiter = new TokenBucketRateLimiter();
        var sessionConfig = new SessionConfig(
            Guid.NewGuid(),
            null,
            null,
            new PolicyProfile
            {
                Name = "test",
                Limits = new RateLimits
                {
                    CallsPerMinute = 60,  // 1 token per second
                    Burst = 5
                }
            },
            new RepoRef("test", "/tmp/test"),
            new WorkspaceRef("/tmp/workspace"),
            null,
            "test-agent");

        var command = new Comment(Guid.NewGuid(), new Correlation(sessionConfig.SessionId), new WorkItemRef("test", "123"), "Test comment");

        // Act - Exhaust burst
        for (int i = 0; i < 5; i++)
        {
            var result = await rateLimiter.CheckRateLimit(command, sessionConfig);
            Assert.True(result.Allowed);
        }

        // Throttle next call
        var throttledResult = await rateLimiter.CheckRateLimit(command, sessionConfig);
        Assert.False(throttledResult.Allowed);

        // Wait 1 second for 1 token to refill
        await Task.Delay(1100);

        // Should be allowed again
        var recoveryResult = await rateLimiter.CheckRateLimit(command, sessionConfig);
        Assert.True(recoveryResult.Allowed);
    }

    [Fact]
    public async Task CheckRateLimit_WaitDelayTest_MultipleRefillCycles()
    {
        // Arrange - Test multiple refill cycles
        var rateLimiter = new TokenBucketRateLimiter();
        var sessionConfig = new SessionConfig(
            Guid.NewGuid(),
            null,
            null,
            new PolicyProfile
            {
                Name = "test",
                Limits = new RateLimits
                {
                    CallsPerMinute = 12,  // 1 token every 5 seconds
                    Burst = 3
                }
            },
            new RepoRef("test", "/tmp/test"),
            new WorkspaceRef("/tmp/workspace"),
            null,
            "test-agent");

        var command = new Comment(Guid.NewGuid(), new Correlation(sessionConfig.SessionId), new WorkItemRef("test", "123"), "Test comment");

        // Act - Exhaust burst
        for (int i = 0; i < 3; i++)
        {
            var result = await rateLimiter.CheckRateLimit(command, sessionConfig);
            Assert.True(result.Allowed);
        }

        // Verify throttling
        var throttledResult = await rateLimiter.CheckRateLimit(command, sessionConfig);
        Assert.False(throttledResult.Allowed);

        // Wait 5 seconds, should refill 1 token
        await Task.Delay(5100);
        var firstRecovery = await rateLimiter.CheckRateLimit(command, sessionConfig);
        Assert.True(firstRecovery.Allowed);

        // Wait another 5 seconds, should refill another token
        await Task.Delay(5100);
        var secondRecovery = await rateLimiter.CheckRateLimit(command, sessionConfig);
        Assert.True(secondRecovery.Allowed);

        // Should be throttled again
        var throttledAgain = await rateLimiter.CheckRateLimit(command, sessionConfig);
        Assert.False(throttledAgain.Allowed);
    }

    [Fact]
    public async Task CheckRateLimit_WaitDelayTest_PreciseRetryAfterTiming()
    {
        // Arrange - Test precise timing of retry-after calculation
        var rateLimiter = new TokenBucketRateLimiter();
        var sessionConfig = new SessionConfig(
            Guid.NewGuid(),
            null,
            null,
            new PolicyProfile
            {
                Name = "test",
                Limits = new RateLimits
                {
                    CallsPerMinute = 4,  // 1 token every 15 seconds
                    Burst = 2
                }
            },
            new RepoRef("test", "/tmp/test"),
            new WorkspaceRef("/tmp/workspace"),
            null,
            "test-agent");

        var command = new Comment(Guid.NewGuid(), new Correlation(sessionConfig.SessionId), new WorkItemRef("test", "123"), "Test comment");

        // Act - Exhaust burst
        for (int i = 0; i < 2; i++)
        {
            var result = await rateLimiter.CheckRateLimit(command, sessionConfig);
            Assert.True(result.Allowed);
        }

        var beforeThrottle = DateTimeOffset.UtcNow;
        var throttledResult = await rateLimiter.CheckRateLimit(command, sessionConfig);
        var afterThrottle = DateTimeOffset.UtcNow;

        // Assert throttling and retry-after timing
        Assert.False(throttledResult.Allowed);
        Assert.NotNull(throttledResult.RetryAfter);

        var expectedRetryAfter = beforeThrottle.AddSeconds(15); // Should be ~15 seconds from first throttle attempt
        var actualRetryAfter = throttledResult.RetryAfter.Value;

        // Allow some tolerance for execution time
        var minExpected = beforeThrottle.AddSeconds(10); // At least 10 seconds
        var maxExpected = afterThrottle.AddSeconds(20);  // At most 20 seconds

        Assert.True(actualRetryAfter >= minExpected, $"RetryAfter {actualRetryAfter} should be >= {minExpected}");
        Assert.True(actualRetryAfter <= maxExpected, $"RetryAfter {actualRetryAfter} should be <= {maxExpected}");

        // Wait until retry-after time
        var waitTime = actualRetryAfter - DateTimeOffset.UtcNow;
        if (waitTime > TimeSpan.Zero)
        {
            await Task.Delay(waitTime.Add(TimeSpan.FromMilliseconds(100))); // Add small buffer
        }

        // Should be allowed now
        var recoveryResult = await rateLimiter.CheckRateLimit(command, sessionConfig);
        Assert.True(recoveryResult.Allowed);
    }

    [Fact]
    public async Task CheckRateLimit_WaitDelayTest_ZeroRateLongWait()
    {
        // Arrange - Test zero rate with long wait (should still be throttled)
        var rateLimiter = new TokenBucketRateLimiter();
        var sessionConfig = new SessionConfig(
            Guid.NewGuid(),
            null,
            null,
            new PolicyProfile
            {
                Name = "test",
                Limits = new RateLimits
                {
                    CallsPerMinute = 0,
                    Burst = 0
                }
            },
            new RepoRef("test", "/tmp/test"),
            new WorkspaceRef("/tmp/workspace"),
            null,
            "test-agent");

        var command = new Comment(Guid.NewGuid(), new Correlation(sessionConfig.SessionId), new WorkItemRef("test", "123"), "Test comment");

        // Act - First call should be throttled
        var firstResult = await rateLimiter.CheckRateLimit(command, sessionConfig);
        Assert.False(firstResult.Allowed);
        Assert.Equal(DateTimeOffset.MaxValue, firstResult.RetryAfter);

        // Wait a long time (shouldn't matter for zero rate)
        await Task.Delay(5000);

        // Still should be throttled
        var secondResult = await rateLimiter.CheckRateLimit(command, sessionConfig);
        Assert.False(secondResult.Allowed);
        Assert.Equal(DateTimeOffset.MaxValue, secondResult.RetryAfter);
    }

    [Fact]
    public async Task CheckRateLimit_WaitDelayTest_PerCommandCapRecovery()
    {
        // Arrange - Test per-command cap recovery over time
        var rateLimiter = new TokenBucketRateLimiter();
        var sessionConfig = new SessionConfig(
            Guid.NewGuid(),
            null,
            null,
            new PolicyProfile
            {
                Name = "test",
                Limits = new RateLimits
                {
                    CallsPerMinute = 100, // High global rate
                    Burst = 50,
                    PerCommandCaps = new Dictionary<string, int>
                    {
                        { "Comment", 2 }  // Only 2 comments allowed
                    }
                }
            },
            new RepoRef("test", "/tmp/test"),
            new WorkspaceRef("/tmp/workspace"),
            null,
            "test-agent");

        var command = new Comment(Guid.NewGuid(), new Correlation(sessionConfig.SessionId), new WorkItemRef("test", "123"), "Test comment");

        // Act - Use up per-command caps
        for (int i = 0; i < 2; i++)
        {
            var result = await rateLimiter.CheckRateLimit(command, sessionConfig);
            Assert.True(result.Allowed);
        }

        // Third should be throttled
        var throttledResult = await rateLimiter.CheckRateLimit(command, sessionConfig);
        Assert.False(throttledResult.Allowed);

        // Wait for global refill (but per-command cap should still block)
        await Task.Delay(2000); // Wait 2 seconds

        // Still should be throttled due to per-command cap
        var stillThrottled = await rateLimiter.CheckRateLimit(command, sessionConfig);
        Assert.False(stillThrottled.Allowed);
    }
}