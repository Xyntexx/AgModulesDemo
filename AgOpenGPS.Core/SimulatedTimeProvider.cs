namespace AgOpenGPS.Core;

using AgOpenGPS.ModuleContracts;
using System.Collections.Concurrent;

/// <summary>
/// Simulated time provider for testing and fast-forward scenarios.
/// Provides both monotonic time (for determinism) and wall clock time (for display).
/// Allows manual time control, time scaling, and instant delays.
/// Thread-safe for concurrent access.
/// </summary>
public class SimulatedTimeProvider : ITimeProvider
{
    private long _monotonicMs;  // Monotonic time in milliseconds
    private DateTimeOffset _wallClockTime;  // Wall clock time (for display)
    private readonly long _unixEpochOffsetMs;  // Offset to convert monotonic to Unix epoch
    private double _timeScale = 1.0;
    private readonly object _lock = new object();
    private readonly ConcurrentDictionary<Guid, DelayOperation> _pendingDelays = new();

    /// <summary>
    /// Creates a simulated time provider starting at the given time
    /// </summary>
    /// <param name="startTime">Initial wall clock time (defaults to current UTC time)</param>
    /// <param name="startMonotonicMs">Initial monotonic time in ms (defaults to 0)</param>
    public SimulatedTimeProvider(DateTimeOffset? startTime = null, long startMonotonicMs = 0)
    {
        _wallClockTime = startTime ?? DateTimeOffset.UtcNow;
        _monotonicMs = startMonotonicMs;

        // Calculate offset: wall clock at start - monotonic at start
        _unixEpochOffsetMs = _wallClockTime.ToUnixTimeMilliseconds() - _monotonicMs;
    }

    /// <summary>
    /// Monotonic clock: milliseconds since simulation start (never goes backward).
    /// Deterministic and controllable for testing.
    /// </summary>
    public long MonotonicMilliseconds
    {
        get
        {
            lock (_lock)
            {
                return _monotonicMs;
            }
        }
    }

    /// <summary>
    /// Wall clock: current simulated UTC time (for display).
    /// </summary>
    public DateTimeOffset UtcNow
    {
        get
        {
            lock (_lock)
            {
                return _wallClockTime;
            }
        }
    }

    /// <summary>
    /// Offset to convert monotonic time to Unix epoch time.
    /// Fixed at initialization for consistency.
    /// </summary>
    public long UnixEpochOffsetMs => _unixEpochOffsetMs;

    /// <summary>
    /// Unix timestamp for message timestamping (monotonic-based).
    /// Uses: MonotonicMilliseconds + UnixEpochOffsetMs
    /// </summary>
    public long UnixTimeMilliseconds => MonotonicMilliseconds + UnixEpochOffsetMs;

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
    /// Gets the number of pending delays waiting to complete
    /// </summary>
    public int PendingDelayCount => _pendingDelays.Count;

    /// <summary>
    /// Gets whether there are any pending delays
    /// </summary>
    public bool HasPendingDelays => _pendingDelays.Count > 0;

    /// <summary>
    /// Gets the time of the next pending delay, or null if no delays pending
    /// </summary>
    public DateTimeOffset? GetNextDelayTime()
    {
        var delays = _pendingDelays.Values.OrderBy(d => d.Deadline).ToList();
        return delays.Count > 0 ? delays[0].Deadline : null;
    }

    /// <summary>
    /// Manually advances time by the specified duration.
    /// Advances both monotonic and wall clock time.
    /// Completes any delays that expire during this advance.
    /// </summary>
    public void Advance(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
            return;

        DateTimeOffset newWallTime;
        lock (_lock)
        {
            _monotonicMs += (long)duration.TotalMilliseconds;
            _wallClockTime = _wallClockTime.Add(duration);
            newWallTime = _wallClockTime;
        }

        // Complete any delays that have expired
        CompleteExpiredDelays(newWallTime);
    }

    /// <summary>
    /// Sets the wall clock time to a specific value.
    /// Only allows moving forward in time.
    /// Also advances monotonic time by the same amount.
    /// </summary>
    public void SetTime(DateTimeOffset time)
    {
        DateTimeOffset newWallTime;
        lock (_lock)
        {
            if (time < _wallClockTime)
                throw new ArgumentException("Cannot move time backwards", nameof(time));

            var delta = time - _wallClockTime;
            _monotonicMs += (long)delta.TotalMilliseconds;
            _wallClockTime = time;
            newWallTime = _wallClockTime;
        }

        CompleteExpiredDelays(newWallTime);
    }

    /// <summary>
    /// Manually advances monotonic time by the specified milliseconds.
    /// Does NOT advance wall clock (useful for testing clock skew scenarios).
    /// </summary>
    public void AdvanceMonotonic(long milliseconds)
    {
        if (milliseconds <= 0)
            return;

        DateTimeOffset wallTime;
        lock (_lock)
        {
            _monotonicMs += milliseconds;
            wallTime = _wallClockTime;
        }

        // Note: We use wall clock for delay deadlines, so this won't complete delays
        // This is intentional - monotonic time is for message timestamps, not delays
    }

    /// <summary>
    /// Asynchronously waits for a specified time span.
    /// ALWAYS creates a pending delay and waits for external time advancement.
    /// This ensures clean separation: delays register intent, runners advance time.
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
            // Wait for a runner to advance time and complete this delay
            await operation.CompletionSource.Task;
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
