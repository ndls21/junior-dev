using System;
using System.Diagnostics.Metrics;
using System.Threading;
using System.Threading.Tasks;

namespace JuniorDev.WorkItems.GitHub;

public class CircuitBreaker
{
    private readonly int _failureThreshold;
    private readonly TimeSpan _timeoutPeriod;
    private readonly TimeSpan _monitoringPeriod;
    private int _failureCount;
    private DateTime _lastFailureTime;
    private CircuitState _state;
    private readonly object _lock = new object();
    private readonly Meter _meter;
    private readonly Counter<long> _circuitBreakerTrips;

    public CircuitBreaker(int failureThreshold = 5, TimeSpan? timeoutPeriod = null, TimeSpan? monitoringPeriod = null)
    {
        _failureThreshold = failureThreshold;
        _timeoutPeriod = timeoutPeriod ?? TimeSpan.FromMinutes(1);
        _monitoringPeriod = monitoringPeriod ?? TimeSpan.FromMinutes(5);
        _state = CircuitState.Closed;
        _failureCount = 0;
        _lastFailureTime = DateTime.MinValue;
        _meter = new Meter("JuniorDev.WorkItems.GitHub.CircuitBreaker", "1.0.0");
        _circuitBreakerTrips = _meter.CreateCounter<long>("circuit_breaker_trips", "trips", "Number of times circuit breaker opened");
    }

    public async Task<T> ExecuteAsync<T>(Func<Task<T>> operation)
    {
        CircuitState currentState;
        lock (_lock)
        {
            currentState = _state;
            if (currentState == CircuitState.Open)
            {
                if (DateTime.UtcNow - _lastFailureTime > _timeoutPeriod)
                {
                    // Try half-open state
                    _state = CircuitState.HalfOpen;
                    currentState = CircuitState.HalfOpen;
                }
                else
                {
                    throw new CircuitBreakerOpenException("Circuit breaker is open");
                }
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
        lock (_lock)
        {
            _failureCount = 0;
            _state = CircuitState.Closed;
        }
    }

    private void OnFailure()
    {
        lock (_lock)
        {
            _failureCount++;
            _lastFailureTime = DateTime.UtcNow;

            if (_failureCount >= _failureThreshold)
            {
                _state = CircuitState.Open;
                _circuitBreakerTrips.Add(1);
            }
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            _failureCount = 0;
            _state = CircuitState.Closed;
            _lastFailureTime = DateTime.MinValue;
        }
    }

    public CircuitState State
    {
        get
        {
            lock (_lock)
            {
                return _state;
            }
        }
    }

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