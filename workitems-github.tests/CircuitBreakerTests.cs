using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace JuniorDev.WorkItems.GitHub.Tests;

public class CircuitBreakerTests
{
    [Fact]
    public async Task ExecuteAsync_SuccessfulOperations_StaysClosed()
    {
        // Arrange
        var circuitBreaker = new CircuitBreaker(failureThreshold: 3, timeoutPeriod: TimeSpan.FromSeconds(1));

        // Act
        for (int i = 0; i < 5; i++)
        {
            await circuitBreaker.ExecuteAsync<string>(() => Task.FromResult("success"));
        }

        // Assert
        Assert.Equal(CircuitBreaker.CircuitState.Closed, circuitBreaker.State);
    }

    [Fact]
    public async Task ExecuteAsync_FailureThresholdReached_OpensCircuit()
    {
        // Arrange
        var circuitBreaker = new CircuitBreaker(failureThreshold: 2, timeoutPeriod: TimeSpan.FromSeconds(1));
        var callCount = 0;

        // Act
        for (int i = 0; i < 3; i++)
        {
            try
            {
                await circuitBreaker.ExecuteAsync<string>(() =>
                {
                    callCount++;
                    throw new Exception("Test failure");
                });
            }
            catch (Exception)
            {
                // Expected
            }
        }

        // Assert
        Assert.Equal(CircuitBreaker.CircuitState.Open, circuitBreaker.State);
        Assert.Equal(2, callCount); // Should have called twice before opening
    }

    [Fact]
    public async Task ExecuteAsync_OpenCircuit_ThrowsCircuitBreakerOpenException()
    {
        // Arrange
        var circuitBreaker = new CircuitBreaker(failureThreshold: 1, timeoutPeriod: TimeSpan.FromSeconds(10));
        var callCount = 0;

        // Open the circuit
        try
        {
            await circuitBreaker.ExecuteAsync<string>(() =>
            {
                callCount++;
                throw new Exception("Test failure");
            });
        }
        catch (Exception)
        {
            // Expected
        }

        // Act & Assert
        var exception = await Assert.ThrowsAsync<CircuitBreakerOpenException>(() =>
            circuitBreaker.ExecuteAsync<string>(() =>
            {
                callCount++;
                return Task.FromResult("should not execute");
            }));

        Assert.Equal("Circuit breaker is open", exception.Message);
        Assert.Equal(1, callCount); // Should not have executed again
    }

    [Fact]
    public async Task ExecuteAsync_TimeoutExpired_TransitionsToHalfOpen()
    {
        // Arrange
        var circuitBreaker = new CircuitBreaker(failureThreshold: 1, timeoutPeriod: TimeSpan.FromMilliseconds(100));
        var callCount = 0;

        // Open the circuit
        try
        {
            await circuitBreaker.ExecuteAsync<string>(() =>
            {
                callCount++;
                throw new Exception("Test failure");
            });
        }
        catch (Exception)
        {
            // Expected
        }

        Assert.Equal(CircuitBreaker.CircuitState.Open, circuitBreaker.State);

        // Wait for timeout
        await Task.Delay(150);

        // Act - Next call should transition to half-open
        try
        {
            await circuitBreaker.ExecuteAsync<string>(() =>
            {
                callCount++;
                throw new Exception("Still failing");
            });
        }
        catch (Exception)
        {
            // Expected
        }

        // Assert - Should transition to half-open and execute, but failure should return to open
        Assert.Equal(CircuitBreaker.CircuitState.Open, circuitBreaker.State);
        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task ExecuteAsync_HalfOpenSuccess_TransitionsToClosed()
    {
        // Arrange
        var circuitBreaker = new CircuitBreaker(failureThreshold: 1, timeoutPeriod: TimeSpan.FromMilliseconds(100));
        var callCount = 0;

        // Open the circuit
        try
        {
            await circuitBreaker.ExecuteAsync<string>(() =>
            {
                callCount++;
                throw new Exception("Test failure");
            });
        }
        catch (Exception)
        {
            // Expected
        }

        // Wait for timeout to transition to half-open
        await Task.Delay(150);

        // Act - Successful call in half-open state
        var result = await circuitBreaker.ExecuteAsync<string>(() =>
        {
            callCount++;
            return Task.FromResult("success");
        });

        // Assert
        Assert.Equal("success", result);
        Assert.Equal(CircuitBreaker.CircuitState.Closed, circuitBreaker.State);
        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task ExecuteAsync_HalfOpenFailure_StaysOpen()
    {
        // Arrange
        var circuitBreaker = new CircuitBreaker(failureThreshold: 1, timeoutPeriod: TimeSpan.FromMilliseconds(100));
        var callCount = 0;

        // Open the circuit
        try
        {
            await circuitBreaker.ExecuteAsync<string>(() =>
            {
                callCount++;
                throw new Exception("Test failure");
            });
        }
        catch (Exception)
        {
            // Expected
        }

        // Wait for timeout to transition to half-open
        await Task.Delay(150);

        // Act - Failed call in half-open state
        try
        {
            await circuitBreaker.ExecuteAsync<string>(() =>
            {
                callCount++;
                throw new Exception("Still failing");
            });
        }
        catch (Exception)
        {
            // Expected
        }

        // Assert - Should stay open after half-open failure
        Assert.Equal(CircuitBreaker.CircuitState.Open, circuitBreaker.State);
        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task Reset_ResetsCircuitToClosed()
    {
        // Arrange
        var circuitBreaker = new CircuitBreaker(failureThreshold: 1, timeoutPeriod: TimeSpan.FromSeconds(10));

        // Open the circuit
        try
        {
            await circuitBreaker.ExecuteAsync<string>(() => throw new Exception("Test failure"));
        }
        catch (Exception)
        {
            // Expected
        }

        Assert.Equal(CircuitBreaker.CircuitState.Open, circuitBreaker.State);

        // Act
        circuitBreaker.Reset();

        // Assert
        Assert.Equal(CircuitBreaker.CircuitState.Closed, circuitBreaker.State);
    }

    [Fact]
    public async Task ExecuteAsync_ConcurrentCallsDuringStateChange_HandledCorrectly()
    {
        // Arrange
        var circuitBreaker = new CircuitBreaker(failureThreshold: 1, timeoutPeriod: TimeSpan.FromMilliseconds(100));
        var callCount = 0;

        // Open the circuit
        try
        {
            await circuitBreaker.ExecuteAsync<string>(() =>
            {
                callCount++;
                throw new Exception("Test failure");
            });
        }
        catch (Exception)
        {
            // Expected
        }

        // Wait for timeout
        await Task.Delay(150);

        // Act - Multiple concurrent calls when transitioning to half-open
        var tasks = new Task<string>[3];
        for (int i = 0; i < 3; i++)
        {
            tasks[i] = circuitBreaker.ExecuteAsync<string>(() =>
            {
                Interlocked.Increment(ref callCount);
                return Task.FromResult("success");
            });
        }

        // Wait for all tasks
        await Task.WhenAll(tasks);

        // Assert - Circuit should be closed after successful calls
        Assert.Equal(CircuitBreaker.CircuitState.Closed, circuitBreaker.State);
        Assert.Equal(4, callCount); // 1 initial failure + 3 successful calls
    }

    [Fact]
    public async Task ExecuteAsync_MultipleFailures_InHalfOpen()
    {
        // Arrange
        var circuitBreaker = new CircuitBreaker(failureThreshold: 1, timeoutPeriod: TimeSpan.FromMilliseconds(100));
        var callCount = 0;

        // Open the circuit
        try
        {
            await circuitBreaker.ExecuteAsync<string>(() =>
            {
                callCount++;
                throw new Exception("Test failure");
            });
        }
        catch (Exception)
        {
            // Expected
        }

        // Wait for timeout to transition to half-open
        await Task.Delay(150);

        // Act - Multiple calls in half-open state, some fail
        var tasks = new Task[3];
        for (int i = 0; i < 3; i++)
        {
            tasks[i] = circuitBreaker.ExecuteAsync<string>(() =>
            {
                Interlocked.Increment(ref callCount);
                if (callCount <= 3) // First 2 calls in half-open fail
                    throw new Exception("Still failing");
                return Task.FromResult("success");
            });
        }

        // Wait for all tasks (some will throw)
        try
        {
            await Task.WhenAll(tasks);
        }
        catch (Exception)
        {
            // Expected - some tasks fail
        }

        // Assert - Should be back to open after failure in half-open
        Assert.Equal(CircuitBreaker.CircuitState.Open, circuitBreaker.State);
        Assert.Equal(2, callCount); // 1 initial + 1 half-open call (fails and opens again)
    }

    [Fact]
    public async Task ExecuteAsync_TimeoutRecovery_CustomTimeout()
    {
        // Arrange
        var circuitBreaker = new CircuitBreaker(failureThreshold: 1, timeoutPeriod: TimeSpan.FromSeconds(2));
        var callCount = 0;

        // Open the circuit
        try
        {
            await circuitBreaker.ExecuteAsync<string>(() =>
            {
                callCount++;
                throw new Exception("Test failure");
            });
        }
        catch (Exception)
        {
            // Expected
        }

        Assert.Equal(CircuitBreaker.CircuitState.Open, circuitBreaker.State);

        // Wait for custom timeout
        await Task.Delay(2100);

        // Act - Should transition to half-open
        var result = await circuitBreaker.ExecuteAsync<string>(() =>
        {
            callCount++;
            return Task.FromResult("success");
        });

        // Assert
        Assert.Equal("success", result);
        Assert.Equal(CircuitBreaker.CircuitState.Closed, circuitBreaker.State);
        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task ExecuteAsync_MonitoringPeriodBehavior()
    {
        // Arrange
        var circuitBreaker = new CircuitBreaker(failureThreshold: 2, timeoutPeriod: TimeSpan.FromSeconds(1), monitoringPeriod: TimeSpan.FromSeconds(5));
        var callCount = 0;

        // Act - Cause failures over time
        for (int i = 0; i < 3; i++)
        {
            try
            {
                await circuitBreaker.ExecuteAsync<string>(() =>
                {
                    callCount++;
                    throw new Exception("Test failure");
                });
            }
            catch (Exception)
            {
                // Expected
            }
            await Task.Delay(100); // Small delay between failures
        }

        // Assert - Should be open after 2 failures
        Assert.Equal(CircuitBreaker.CircuitState.Open, circuitBreaker.State);
        Assert.Equal(2, callCount); // Should have stopped calling after 2 failures
    }

    [Fact]
    public async Task ExecuteAsync_SuccessAfterMultipleFailures_ResetsCount()
    {
        // Arrange
        var circuitBreaker = new CircuitBreaker(failureThreshold: 3, timeoutPeriod: TimeSpan.FromSeconds(1));
        var callCount = 0;

        // Act - Cause some failures
        for (int i = 0; i < 2; i++)
        {
            try
            {
                await circuitBreaker.ExecuteAsync<string>(() =>
                {
                    callCount++;
                    throw new Exception("Test failure");
                });
            }
            catch (Exception)
            {
                // Expected
            }
        }

        // Successful call should reset failure count
        await circuitBreaker.ExecuteAsync<string>(() =>
        {
            callCount++;
            return Task.FromResult("success");
        });

        // More failures should start count again
        try
        {
            await circuitBreaker.ExecuteAsync<string>(() =>
            {
                callCount++;
                throw new Exception("Test failure again");
            });
        }
        catch (Exception)
        {
            // Expected
        }

        // Assert - Should still be closed (failure count reset by success)
        Assert.Equal(CircuitBreaker.CircuitState.Closed, circuitBreaker.State);
        Assert.Equal(4, callCount); // 2 failures + 1 success + 1 more failure
    }

    [Fact]
    public async Task ExecuteAsync_ConcurrentFailures_ThreadSafe()
    {
        // Arrange
        var circuitBreaker = new CircuitBreaker(failureThreshold: 5, timeoutPeriod: TimeSpan.FromSeconds(1));
        var callCount = 0;

        // Act - Launch concurrent failing operations
        var tasks = new Task[10];
        for (int i = 0; i < 10; i++)
        {
            tasks[i] = Task.Run(async () =>
            {
                try
                {
                    await circuitBreaker.ExecuteAsync<string>(() =>
                    {
                        Interlocked.Increment(ref callCount);
                        throw new Exception("Concurrent failure");
                    });
                }
                catch (Exception)
                {
                    // Expected
                }
            });
        }

        await Task.WhenAll(tasks);

        // Assert - Should be open and have called at least failureThreshold times (may be more due to concurrency)
        Assert.Equal(CircuitBreaker.CircuitState.Open, circuitBreaker.State);
        Assert.True(callCount >= 5, $"Expected at least 5 calls, but got {callCount}"); // Should have called at least threshold times
    }

    [Fact]
    public async Task ExecuteAsync_HalfOpenConcurrent_SuccessCloses()
    {
        // Arrange
        var circuitBreaker = new CircuitBreaker(failureThreshold: 1, timeoutPeriod: TimeSpan.FromMilliseconds(100));
        var callCount = 0;

        // Open the circuit
        try
        {
            await circuitBreaker.ExecuteAsync<string>(() =>
            {
                callCount++;
                throw new Exception("Test failure");
            });
        }
        catch (Exception)
        {
            // Expected
        }

        // Wait for timeout
        await Task.Delay(150);

        // Act - Multiple concurrent calls in half-open
        var tasks = new Task<string>[5];
        for (int i = 0; i < 5; i++)
        {
            tasks[i] = circuitBreaker.ExecuteAsync<string>(() =>
            {
                Interlocked.Increment(ref callCount);
                return Task.FromResult($"success{i}");
            });
        }

        var results = await Task.WhenAll(tasks);

        // Assert - All should succeed and circuit should close
        Assert.All(results, r => Assert.StartsWith("success", r));
        Assert.Equal(CircuitBreaker.CircuitState.Closed, circuitBreaker.State);
        Assert.Equal(6, callCount); // 1 initial failure + 5 successes
    }

    [Fact]
    public async Task ExecuteAsync_ResetDuringOpen_AllowsImmediateCalls()
    {
        // Arrange
        var circuitBreaker = new CircuitBreaker(failureThreshold: 1, timeoutPeriod: TimeSpan.FromMinutes(5)); // Long timeout
        var callCount = 0;

        // Open the circuit
        try
        {
            await circuitBreaker.ExecuteAsync<string>(() =>
            {
                callCount++;
                throw new Exception("Test failure");
            });
        }
        catch (Exception)
        {
            // Expected
        }

        Assert.Equal(CircuitBreaker.CircuitState.Open, circuitBreaker.State);

        // Act - Reset while open
        circuitBreaker.Reset();

        // Should allow calls immediately
        var result = await circuitBreaker.ExecuteAsync<string>(() =>
        {
            callCount++;
            return Task.FromResult("success");
        });

        // Assert
        Assert.Equal("success", result);
        Assert.Equal(CircuitBreaker.CircuitState.Closed, circuitBreaker.State);
        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task ExecuteAsync_ExceptionTypes_HandledCorrectly()
    {
        // Arrange
        var circuitBreaker = new CircuitBreaker(failureThreshold: 2, timeoutPeriod: TimeSpan.FromSeconds(1));
        var callCount = 0;

        // Act - Different exception types should all count as failures
        var exceptionTypes = new[] { typeof(InvalidOperationException), typeof(ArgumentException), typeof(Exception) };

        foreach (var exceptionType in exceptionTypes)
        {
            try
            {
                await circuitBreaker.ExecuteAsync<string>(() =>
                {
                    callCount++;
                    throw (Exception)Activator.CreateInstance(exceptionType, "Test exception")!;
                });
            }
            catch (Exception)
            {
                // Expected
            }
        }

        // Assert - Should be open after 2 failures
        Assert.Equal(CircuitBreaker.CircuitState.Open, circuitBreaker.State);
        Assert.Equal(2, callCount); // Should have stopped after threshold
    }
}