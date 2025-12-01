using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Sdk;

namespace JuniorDev.Orchestrator.Tests;

/// <summary>
/// Custom attribute that applies a timeout to test methods.
/// Usage: [Fact(Timeout = 5000)] or [TestTimeout] (uses default 30 seconds)
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class TestTimeoutAttribute : FactAttribute
{
    public TestTimeoutAttribute(int timeoutMilliseconds = 30000) // Default 30 seconds
    {
        Timeout = timeoutMilliseconds;
    }
}

/// <summary>
/// Collection fixture that provides timeout management for all tests in a collection.
/// </summary>
[CollectionDefinition("TimeoutCollection")]
public class TimeoutTestCollection : ICollectionFixture<TestTimeoutFixture>
{
}

/// <summary>
/// Collection fixture that provides timeout management for all tests in a collection.
/// </summary>
public class TestTimeoutFixture : IDisposable
{
    private readonly CancellationTokenSource _globalTimeoutCts = new();
    private readonly TimeSpan _defaultTimeout = TimeSpan.FromSeconds(30);

    public TestTimeoutFixture()
    {
        // Set up global timeout for the entire test collection
        _globalTimeoutCts.CancelAfter(_defaultTimeout);
    }

    public CancellationToken GlobalTimeoutToken => _globalTimeoutCts.Token;

    public void Dispose()
    {
        _globalTimeoutCts.Cancel();
        _globalTimeoutCts.Dispose();
    }
}

/// <summary>
/// Base class for tests that need timeout management.
/// </summary>
[Collection("TimeoutCollection")]
public abstract class TimeoutTestBase : IClassFixture<TestTimeoutFixture>
{
    protected readonly CancellationToken GlobalTimeoutToken;

    protected TimeoutTestBase(TestTimeoutFixture fixture)
    {
        GlobalTimeoutToken = fixture.GlobalTimeoutToken;
    }

    /// <summary>
    /// Helper method to run async operations with timeout.
    /// </summary>
    protected async Task<T> RunWithTimeout<T>(Task<T> task, TimeSpan? timeout = null)
    {
        var actualTimeout = timeout ?? TimeSpan.FromSeconds(10);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(GlobalTimeoutToken);
        cts.CancelAfter(actualTimeout);

        var completedTask = await Task.WhenAny(task, Task.Delay(actualTimeout, cts.Token));
        if (completedTask == task)
        {
            return await task;
        }
        else
        {
            throw new TestTimeoutException((int)actualTimeout.TotalMilliseconds);
        }
    }
}