namespace AgOpenGPS.Core;

using AgOpenGPS.ModuleContracts;
using System.Collections.Concurrent;

/// <summary>
/// Simulated time provider for testing and fast-forward scenarios
/// Allows manual time control, time scaling, and instant delays
/// Thread-safe for concurrent access
/// </summary>
public class SimulatedTimeProvider : ITimeProvider
{
    private DateTimeOffset _currentTime;
    private double _timeScale = 1.0;
    private readonly object _lock = new object();
    private readonly ConcurrentDictionary<Guid, DelayOperation> _pendingDelays = new();

    /// <summary>
    /// Creates a simulated time provider starting at the given time
    /// </summary>
    /// <param name="startTime">Initial time (defaults to current UTC time)</param>
    public SimulatedTimeProvider(DateTimeOffset? startTime = null)
    {
        _currentTime = startTime ?? DateTimeOffset.UtcNow;
    }

    public DateTimeOffset UtcNow
    {
        get
        {
            lock (_lock)
            {
                return _currentTime;
            }
        }
    }

    /// <summary>
    /// Gets or sets the time scale multiplier
    /// 1.0 = real-time, 10.0 = 10x speed, 0.0 = frozen time
    /// </summary>
    public double TimeScale
    {
        get
        {
            lock (_lock)
            {
                return _timeScale;
            }
        }
        set
        {
            lock (_lock)
            {
                _timeScale = Math.Max(0.0, value);
            }
        }
    }

    /// <summary>
    /// Manually advances time by the specified duration
    /// Completes any delays that expire during this advance
    /// </summary>
    public void Advance(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
            return;

        DateTimeOffset newTime;
        lock (_lock)
        {
            _currentTime = _currentTime.Add(duration);
            newTime = _currentTime;
        }

        // Complete any delays that have expired
        CompleteExpiredDelays(newTime);
    }

    /// <summary>
    /// Sets the current time to a specific value
    /// Only allows moving forward in time
    /// </summary>
    public void SetTime(DateTimeOffset time)
    {
        DateTimeOffset newTime;
        lock (_lock)
        {
            if (time < _currentTime)
                throw new ArgumentException("Cannot move time backwards", nameof(time));

            _currentTime = time;
            newTime = _currentTime;
        }

        CompleteExpiredDelays(newTime);
    }

    /// <summary>
    /// Asynchronously waits for a specified time span
    /// Completes instantly if time is manually advanced past the deadline
    /// Respects time scaling for automatic advancement
    /// </summary>
    public async Task Delay(TimeSpan duration, CancellationToken cancellationToken = default)
    {
        if (duration <= TimeSpan.Zero)
            return;

        var operation = new DelayOperation
        {
            Id = Guid.NewGuid(),
            Deadline = UtcNow.Add(duration),
            CompletionSource = new TaskCompletionSource<bool>()
        };

        _pendingDelays[operation.Id] = operation;

        // Register cancellation
        using var registration = cancellationToken.Register(() =>
        {
            if (_pendingDelays.TryRemove(operation.Id, out var op))
            {
                op.CompletionSource.TrySetCanceled(cancellationToken);
            }
        });

        try
        {
            // If time scale > 0, simulate real-time delay with scaling
            if (TimeScale > 0)
            {
                var scaledDuration = TimeSpan.FromTicks((long)(duration.Ticks / TimeScale));
                var delayTask = Task.Delay(scaledDuration, cancellationToken);
                var completionTask = operation.CompletionSource.Task;

                var completedTask = await Task.WhenAny(delayTask, completionTask);

                // If delay task completed naturally, advance time
                if (completedTask == delayTask)
                {
                    Advance(duration);
                    _pendingDelays.TryRemove(operation.Id, out _);
                }
            }
            else
            {
                // Time is frozen, just wait for manual advancement
                await operation.CompletionSource.Task;
            }
        }
        catch (OperationCanceledException)
        {
            _pendingDelays.TryRemove(operation.Id, out _);
            throw;
        }
    }

    /// <summary>
    /// Advances time until all pending delays complete
    /// Useful for fast-forwarding through idle periods in tests
    /// </summary>
    public void AdvanceToNextDelay()
    {
        var delays = _pendingDelays.Values.OrderBy(d => d.Deadline).ToList();
        if (delays.Count > 0)
        {
            var nextDeadline = delays[0].Deadline;
            var advanceAmount = nextDeadline - UtcNow;
            if (advanceAmount > TimeSpan.Zero)
            {
                Advance(advanceAmount);
            }
        }
    }

    /// <summary>
    /// Completes all pending delays by advancing to the latest deadline
    /// </summary>
    public void CompleteAllDelays()
    {
        while (_pendingDelays.Count > 0)
        {
            AdvanceToNextDelay();
        }
    }

    private void CompleteExpiredDelays(DateTimeOffset currentTime)
    {
        foreach (var kvp in _pendingDelays)
        {
            if (currentTime >= kvp.Value.Deadline)
            {
                if (_pendingDelays.TryRemove(kvp.Key, out var operation))
                {
                    operation.CompletionSource.TrySetResult(true);
                }
            }
        }
    }

    private class DelayOperation
    {
        public Guid Id { get; set; }
        public DateTimeOffset Deadline { get; set; }
        public TaskCompletionSource<bool> CompletionSource { get; set; } = null!;
    }
}
