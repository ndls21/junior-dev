using System;
using System.Threading;
using System.Threading.Tasks;

namespace JuniorDev.WorkItems.Jira;

public class CircuitBreaker
{
    private readonly int _failureThreshold;
    private readonly TimeSpan _timeoutPeriod;
    private readonly TimeSpan _monitoringPeriod;
    private int _failureCount;
    private DateTime _lastFailureTime;
    private CircuitState _state;

    public CircuitBreaker(int failureThreshold = 5, TimeSpan? timeoutPeriod = null, TimeSpan? monitoringPeriod = null)
    {
        _failureThreshold = failureThreshold;
        _timeoutPeriod = timeoutPeriod ?? TimeSpan.FromMinutes(1);
        _monitoringPeriod = monitoringPeriod ?? TimeSpan.FromMinutes(5);
        _state = CircuitState.Closed;
        _failureCount = 0;
        _lastFailureTime = DateTime.MinValue;
    }

    public async Task<T> ExecuteAsync<T>(Func<Task<T>> operation)
    {
        if (_state == CircuitState.Open)
        {
            if (DateTime.UtcNow - _lastFailureTime > _timeoutPeriod)
            {
                // Try half-open state
                _state = CircuitState.HalfOpen;
            }
            else
            {
                throw new CircuitBreakerOpenException("Circuit breaker is open");
            }
        }

        try
        {
            var result = await operation();
            OnSuccess();
            return result;
        }
        catch (Exception)
        {
            OnFailure();
            throw;
        }
    }

    private void OnSuccess()
    {
        _failureCount = 0;
        _state = CircuitState.Closed;
    }

    private void OnFailure()
    {
        _failureCount++;
        _lastFailureTime = DateTime.UtcNow;

        if (_failureCount >= _failureThreshold)
        {
            _state = CircuitState.Open;
        }
    }

    public void Reset()
    {
        _failureCount = 0;
        _state = CircuitState.Closed;
        _lastFailureTime = DateTime.MinValue;
    }

    public CircuitState State => _state;

    public enum CircuitState
    {
        Closed,
        Open,
        HalfOpen
    }
}

public class CircuitBreakerOpenException : Exception
{
    public CircuitBreakerOpenException(string message) : base(message) { }
}