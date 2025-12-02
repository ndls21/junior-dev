using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace JuniorDev.WorkItems.GitHub.Tests;

public class RetryLogicTests
{
    [Fact]
    public void CalculateDelayWithJitter_ExponentialBackoffWithJitter()
    {
        // Arrange
        var random = new Random(42); // Fixed seed for deterministic test
        var baseDelay = 1000;
        var maxDelay = 30000;

        // Act & Assert
        for (int attempt = 0; attempt < 5; attempt++)
        {
            var delay = CalculateDelayWithJitter(attempt, baseDelay, maxDelay, random);

            // Should be at least baseDelay * 2^attempt
            var minExpected = baseDelay * Math.Pow(2, attempt);
            // Should be less than minExpected + minExpected/2 (jitter)
            var maxExpected = minExpected + minExpected / 2;
            // Should be capped at maxDelay
            var expectedMax = Math.Min(maxExpected, maxDelay);

            Assert.True(delay >= minExpected, $"Delay {delay} should be >= {minExpected} for attempt {attempt}");
            Assert.True(delay <= expectedMax, $"Delay {delay} should be <= {expectedMax} for attempt {attempt}");
        }
    }

    [Fact]
    public void CalculateDelayWithJitter_MaxDelayCap_Enforced()
    {
        // Arrange
        var random = new Random(42);
        var baseDelay = 1000;
        var maxDelay = 5000;

        // Act - High attempt number that would normally exceed maxDelay
        var delay = CalculateDelayWithJitter(10, baseDelay, maxDelay, random);

        // Assert
        Assert.Equal(maxDelay, delay);
    }

    [Theory]
    [InlineData(0, 1000)] // First retry: 1000ms base
    [InlineData(1, 2000)] // Second retry: 2000ms base
    [InlineData(2, 4000)] // Third retry: 4000ms base
    public void CalculateDelayWithJitter_BaseDelayCalculation_Correct(int attempt, int expectedBase)
    {
        // Arrange
        var random = new Random(42);
        var baseDelay = 1000;
        var maxDelay = 30000;

        // Act
        var delay = CalculateDelayWithJitter(attempt, baseDelay, maxDelay, random);

        // Assert - Should be at least the base delay for this attempt
        Assert.True(delay >= expectedBase, $"Delay {delay} should be >= {expectedBase} for attempt {attempt}");
    }

    [Fact]
    public void IsTransientError_CorrectlyIdentifiesTransientErrors()
    {
        // Arrange & Act & Assert
        Assert.True(IsTransientError(HttpStatusCode.RequestTimeout));
        Assert.True(IsTransientError(HttpStatusCode.InternalServerError));
        Assert.True(IsTransientError(HttpStatusCode.BadGateway));
        Assert.True(IsTransientError(HttpStatusCode.ServiceUnavailable));
        Assert.True(IsTransientError(HttpStatusCode.GatewayTimeout));

        // Non-transient errors
        Assert.False(IsTransientError(HttpStatusCode.Unauthorized));
        Assert.False(IsTransientError(HttpStatusCode.Forbidden));
        Assert.False(IsTransientError(HttpStatusCode.NotFound));
        Assert.False(IsTransientError(HttpStatusCode.TooManyRequests));
        Assert.False(IsTransientError(HttpStatusCode.OK));
    }

    [Fact]
    public void IsRetryableError_CorrectlyIdentifiesRetryableErrors()
    {
        // Arrange & Act & Assert
        Assert.True(IsRetryableError(HttpStatusCode.TooManyRequests));
        Assert.True(IsRetryableError(HttpStatusCode.RequestTimeout));
        Assert.True(IsRetryableError(HttpStatusCode.InternalServerError));
        Assert.True(IsRetryableError(HttpStatusCode.BadGateway));
        Assert.True(IsRetryableError(HttpStatusCode.ServiceUnavailable));
        Assert.True(IsRetryableError(HttpStatusCode.GatewayTimeout));

        // Non-retryable errors
        Assert.False(IsRetryableError(HttpStatusCode.Unauthorized));
        Assert.False(IsRetryableError(HttpStatusCode.Forbidden));
        Assert.False(IsRetryableError(HttpStatusCode.NotFound));
        Assert.False(IsRetryableError(HttpStatusCode.OK));
    }

    [Fact]
    public async Task ExecuteWithRetry_SuccessfulOperation_NoRetries()
    {
        // Arrange
        var callCount = 0;
        var circuitBreaker = new CircuitBreaker();

        // Act
        var result = await ExecuteWithRetry(
            () =>
            {
                callCount++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            },
            circuitBreaker);

        // Assert
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task ExecuteWithRetry_TransientError_RetriesAndSucceeds()
    {
        // Arrange
        var callCount = 0;
        var circuitBreaker = new CircuitBreaker();

        // Act
        var result = await ExecuteWithRetry(
            () =>
            {
                callCount++;
                if (callCount == 1)
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
                }
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            },
            circuitBreaker);

        // Assert
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal(2, callCount); // One failure, one success
    }

    [Fact]
    public async Task ExecuteWithRetry_RateLimit_RetriesWithBackoff()
    {
        // Arrange
        var callCount = 0;
        var circuitBreaker = new CircuitBreaker();
        var startTime = DateTime.UtcNow;

        // Act
        var result = await ExecuteWithRetry(
            () =>
            {
                callCount++;
                if (callCount <= 2)
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests));
                }
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            },
            circuitBreaker);

        var duration = DateTime.UtcNow - startTime;

        // Assert
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal(3, callCount); // Two failures, one success
        Assert.True(duration.TotalMilliseconds >= 1000, "Should have waited at least 1 second for backoff");
    }

    [Fact]
    public async Task ExecuteWithRetry_MaxRetriesExceeded_ThrowsException()
    {
        // Arrange
        var callCount = 0;
        var circuitBreaker = new CircuitBreaker();

        // Act & Assert
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            ExecuteWithRetry(
                () =>
                {
                    callCount++;
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
                },
                circuitBreaker));

        Assert.Equal(3, callCount); // MaxRetries = 3
    }

    [Fact]
    public async Task ExecuteWithRetry_NonRetryableError_NoRetries()
    {
        // Arrange
        var callCount = 0;
        var circuitBreaker = new CircuitBreaker();

        // Act & Assert
        await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await ExecuteWithRetry(
                () =>
                {
                    callCount++;
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
                },
                circuitBreaker);
        });

        Assert.Equal(1, callCount); // Should not retry non-retryable errors
    }

    // Helper methods (copied from GitHubAdapter for testing)
    private static int CalculateDelayWithJitter(int attempt, int baseDelay, int maxDelay, Random random)
    {
        var exponentialDelay = baseDelay * Math.Pow(2, attempt);
        var jitter = random.Next(0, (int)(exponentialDelay / 2));
        var totalDelay = (int)exponentialDelay + jitter;
        return Math.Min(totalDelay, maxDelay);
    }

    private static bool IsTransientError(HttpStatusCode? statusCode)
    {
        return statusCode == HttpStatusCode.RequestTimeout ||
               statusCode == HttpStatusCode.InternalServerError ||
               statusCode == HttpStatusCode.BadGateway ||
               statusCode == HttpStatusCode.ServiceUnavailable ||
               statusCode == HttpStatusCode.GatewayTimeout;
    }

    private static bool IsRetryableError(HttpStatusCode? statusCode)
    {
        return statusCode == HttpStatusCode.TooManyRequests ||
               statusCode == HttpStatusCode.RequestTimeout ||
               statusCode == HttpStatusCode.InternalServerError ||
               statusCode == HttpStatusCode.BadGateway ||
               statusCode == HttpStatusCode.ServiceUnavailable ||
               statusCode == HttpStatusCode.GatewayTimeout;
    }

    private static async Task<HttpResponseMessage> ExecuteWithRetry(Func<Task<HttpResponseMessage>> operation, CircuitBreaker circuitBreaker)
    {
        const int MaxRetries = 3;
        const int BaseDelayMs = 1000;
        const int MaxDelayMs = 30000;
        var random = new Random(42);

        return await circuitBreaker.ExecuteAsync(async () =>
        {
            for (int attempt = 0; attempt < MaxRetries; attempt++)
            {
                try
                {
                    var response = await operation();
                    response.EnsureSuccessStatusCode();
                    return response;
                }
                catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    if (attempt < MaxRetries - 1)
                    {
                        var delay = CalculateDelayWithJitter(attempt, BaseDelayMs, MaxDelayMs, random);
                        await Task.Delay(delay);
                        continue;
                    }
                    throw new InvalidOperationException($"Rate limit exceeded after {MaxRetries} attempts");
                }
                catch (HttpRequestException ex) when (IsTransientError(ex.StatusCode))
                {
                    if (attempt < MaxRetries - 1)
                    {
                        var delay = CalculateDelayWithJitter(attempt, BaseDelayMs, MaxDelayMs, random);
                        await Task.Delay(delay);
                        continue;
                    }
                    throw;
                }
                catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized ||
                                                      ex.StatusCode == HttpStatusCode.Forbidden ||
                                                      ex.StatusCode == HttpStatusCode.NotFound)
                {
                    throw;
                }
                catch (HttpRequestException)
                {
                    if (attempt < MaxRetries - 1)
                    {
                        var delay = CalculateDelayWithJitter(attempt, BaseDelayMs, MaxDelayMs, random);
                        await Task.Delay(delay);
                        continue;
                    }
                    throw;
                }
            }
            throw new InvalidOperationException("Max retries exceeded");
        });
    }
}