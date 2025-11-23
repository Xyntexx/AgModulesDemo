namespace AgOpenGPS.Core;

using AgOpenGPS.ModuleContracts;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

/// <summary>
/// Unified event scheduler that coordinates both:
/// - Rate-based scheduled methods (ticks at fixed Hz)
/// - Time-based async delays (await timeProvider.Delay())
///
/// The scheduler determines the next event (whichever comes first)
/// and advances time appropriately, then executes all events at that time.
///
/// Works with both:
/// - SystemTimeProvider (real-time production use)
/// - SimulatedTimeProvider (testing/simulation with controlled time)
///
/// Implements IScheduler for compatibility with existing module system.
/// </summary>
public class EventScheduler : IScheduler, IDisposable
{
    private readonly ITimeProvider _timeProvider;
    private readonly ILogger<EventScheduler>? _logger;
    private readonly ConcurrentDictionary<Guid, ScheduledMethod> _scheduledMethods = new();
    private readonly object _lock = new();
    private bool _isRunning = false;
    private long _globalTickNumber = 0;
    private Thread? _backgroundThread;
    private readonly CancellationTokenSource _cts = new();
    private bool _disposed = false;

    public EventScheduler(ITimeProvider timeProvider, ILogger<EventScheduler>? logger = null)
    {
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Gets whether there are any events pending (scheduled methods or delays)
    /// </summary>
    public bool HasPendingEvents
    {
        get
        {
            if (_scheduledMethods.Count > 0)
                return true;

            // Check for pending delays if using SimulatedTimeProvider
            if (_timeProvider is SimulatedTimeProvider simTimeProvider)
                return simTimeProvider.HasPendingDelays;

            return false;
        }
    }

    /// <summary>
    /// IScheduler implementation: Schedule a method with tick parameters
    /// </summary>
    public IScheduledMethod Schedule(Action<long, long> method, double rateHz, string? name = null)
    {
        if (rateHz <= 0)
            throw new ArgumentException("Rate must be positive", nameof(rateHz));

        var intervalMs = 1000.0 / rateHz;
        var scheduledMethod = new ScheduledMethod
        {
            Id = Guid.NewGuid(),
            MethodWithTicks = method,
            IntervalMs = intervalMs,
            NextExecutionTime = _timeProvider.UtcNow.AddMilliseconds(intervalMs),
            Name = name ?? $"Method_{Guid.NewGuid():N}",
            IsPaused = false,
            RequestedRateHz = rateHz,
            ActualRateHz = rateHz
        };

        _scheduledMethods[scheduledMethod.Id] = scheduledMethod;
        return new ScheduledMethodHandle(scheduledMethod, this);
    }

    /// <summary>
    /// IScheduler implementation: Unschedule a method
    /// </summary>
    public void Unschedule(IScheduledMethod handle)
    {
        if (handle is ScheduledMethodHandle smh)
        {
            Unschedule(smh.MethodId);
        }
    }

    /// <summary>
    /// Schedules a method to run at a specific rate (Hz) - simple Action version
    /// </summary>
    /// <param name="method">The method to execute</param>
    /// <param name="rateHz">Execution rate in Hertz (calls per second)</param>
    /// <param name="name">Optional name for debugging</param>
    /// <returns>Handle to control the scheduled method</returns>
    public IScheduledMethodHandle Schedule(Action method, double rateHz, string? name = null)
    {
        if (rateHz <= 0)
            throw new ArgumentException("Rate must be positive", nameof(rateHz));

        var intervalMs = 1000.0 / rateHz;
        var scheduledMethod = new ScheduledMethod
        {
            Id = Guid.NewGuid(),
            Method = method,
            IntervalMs = intervalMs,
            NextExecutionTime = _timeProvider.UtcNow.AddMilliseconds(intervalMs),
            Name = name ?? $"Method_{Guid.NewGuid():N}",
            IsPaused = false,
            RequestedRateHz = rateHz,
            ActualRateHz = rateHz
        };

        _scheduledMethods[scheduledMethod.Id] = scheduledMethod;
        return new ScheduledMethodHandle(scheduledMethod, this);
    }

    /// <summary>
    /// Gets the time of the next event (either scheduled method or pending delay)
    /// </summary>
    public DateTimeOffset? GetNextEventTime()
    {
        DateTimeOffset? nextMethodTime = null;
        DateTimeOffset? nextDelayTime = null;

        // Get next delay time if using SimulatedTimeProvider
        if (_timeProvider is SimulatedTimeProvider simTimeProvider)
        {
            nextDelayTime = simTimeProvider.GetNextDelayTime();
        }

        // Find earliest scheduled method
        foreach (var method in _scheduledMethods.Values)
        {
            if (!method.IsPaused)
            {
                if (!nextMethodTime.HasValue || method.NextExecutionTime < nextMethodTime.Value)
                {
                    nextMethodTime = method.NextExecutionTime;
                }
            }
        }

        // Return whichever comes first
        if (nextMethodTime.HasValue && nextDelayTime.HasValue)
        {
            return nextMethodTime.Value < nextDelayTime.Value
                ? nextMethodTime.Value
                : nextDelayTime.Value;
        }
        else if (nextMethodTime.HasValue)
        {
            return nextMethodTime.Value;
        }
        else if (nextDelayTime.HasValue)
        {
            return nextDelayTime.Value;
        }

        return null;
    }

    /// <summary>
    /// Runs the event loop in simulation mode (unlimited speed).
    /// Only works with SimulatedTimeProvider - advances time to each event instantly.
    /// </summary>
    public async Task RunSimulationAsync(
        Task[] tasks,
        CancellationToken cancellationToken = default)
    {
        if (_timeProvider is not SimulatedTimeProvider simTimeProvider)
            throw new InvalidOperationException("RunSimulationAsync requires SimulatedTimeProvider");

        if (_isRunning)
            throw new InvalidOperationException("Scheduler is already running");

        _isRunning = true;
        try
        {
            var allTasks = Task.WhenAll(tasks);
            var stuckCount = 0;
            const int maxStuckIterations = 100;

            while (!allTasks.IsCompleted)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Yield to let tasks execute
                await Task.Yield();

                var nextEventTime = GetNextEventTime();

                if (nextEventTime.HasValue)
                {
                    // Advance time to next event (instant in simulation)
                    var timeToAdvance = nextEventTime.Value - _timeProvider.UtcNow;
                    if (timeToAdvance > TimeSpan.Zero)
                    {
                        simTimeProvider.Advance(timeToAdvance);
                    }

                    // Execute all scheduled methods at this time
                    ExecuteScheduledMethodsAt(_timeProvider.UtcNow);

                    stuckCount = 0;
                }
                else
                {
                    // No pending events - give tasks time to register
                    await Task.Delay(1, cancellationToken);
                    stuckCount++;

                    if (stuckCount > maxStuckIterations && !allTasks.IsCompleted)
                    {
                        throw new InvalidOperationException(
                            $"Simulation appears deadlocked: no pending events for {maxStuckIterations} iterations but tasks not complete");
                    }
                }
            }

            await allTasks;
        }
        finally
        {
            _isRunning = false;
        }
    }

    /// <summary>
    /// Runs the event loop in real-time mode.
    /// For SimulatedTimeProvider: Waits in real-time (scaled by TimeScale) and advances simulated time.
    /// For SystemTimeProvider: Waits in real-time and executes methods at their scheduled times.
    /// </summary>
    public async Task RunRealTimeAsync(
        Task[] tasks,
        CancellationToken cancellationToken = default)
    {
        if (_isRunning)
            throw new InvalidOperationException("Scheduler is already running");

        _isRunning = true;
        try
        {
            var allTasks = Task.WhenAll(tasks);
            var stuckCount = 0;
            const int maxStuckIterations = 100;

            while (!allTasks.IsCompleted)
            {
                cancellationToken.ThrowIfCancellationRequested();

                await Task.Yield();

                var nextEventTime = GetNextEventTime();

                if (nextEventTime.HasValue)
                {
                    var timeToAdvance = nextEventTime.Value - _timeProvider.UtcNow;

                    if (timeToAdvance > TimeSpan.Zero)
                    {
                        // Calculate real-world delay
                        TimeSpan realDelay;

                        if (_timeProvider is SimulatedTimeProvider simTimeProvider)
                        {
                            // Scale the delay by TimeScale for simulated time
                            realDelay = TimeSpan.FromTicks(
                                (long)(timeToAdvance.Ticks / simTimeProvider.TimeScale));
                        }
                        else
                        {
                            // Use actual time delay for real-time provider
                            realDelay = timeToAdvance;
                        }

                        // Wait in real-time
                        await Task.Delay(realDelay, cancellationToken);

                        // Advance simulated time if applicable
                        if (_timeProvider is SimulatedTimeProvider simTime)
                        {
                            simTime.Advance(timeToAdvance);
                        }
                    }

                    // Execute all scheduled methods at this time
                    ExecuteScheduledMethodsAt(_timeProvider.UtcNow);

                    stuckCount = 0;
                }
                else
                {
                    await Task.Delay(1, cancellationToken);
                    stuckCount++;

                    if (stuckCount > maxStuckIterations && !allTasks.IsCompleted)
                    {
                        throw new InvalidOperationException(
                            "Scheduler appears deadlocked: no pending events");
                    }
                }
            }

            await allTasks;
        }
        finally
        {
            _isRunning = false;
        }
    }

    /// <summary>
    /// Start the scheduler in background thread mode (for production use with ApplicationCore).
    /// Automatically runs in real-time mode.
    /// </summary>
    public void Start()
    {
        if (_isRunning)
            return;

        _isRunning = true;

        _backgroundThread = new Thread(async () => await BackgroundLoop())
        {
            Name = "EventScheduler",
            IsBackground = false,
            Priority = ThreadPriority.AboveNormal
        };

        _backgroundThread.Start();
    }

    /// <summary>
    /// Stop the background scheduler thread
    /// </summary>
    public void Stop()
    {
        if (!_isRunning)
            return;

        _cts.Cancel();

        // Wait for thread to exit
        if (_backgroundThread != null && _backgroundThread.IsAlive)
        {
            _backgroundThread.Join(TimeSpan.FromSeconds(5));
        }

        _isRunning = false;
    }

    /// <summary>
    /// Background loop for thread-based execution
    /// </summary>
    private async Task BackgroundLoop()
    {
        try
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                var nextEventTime = GetNextEventTime();

                if (nextEventTime.HasValue)
                {
                    var timeToWait = nextEventTime.Value - _timeProvider.UtcNow;

                    if (timeToWait > TimeSpan.Zero)
                    {
                        // Wait until next event
                        await Task.Delay(timeToWait, _cts.Token);
                    }

                    // Execute all methods at this time
                    ExecuteScheduledMethodsAt(_timeProvider.UtcNow);
                }
                else
                {
                    // No events scheduled, wait a bit
                    await Task.Delay(10, _cts.Token);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "EventScheduler background loop error");
        }
    }

    /// <summary>
    /// Executes all scheduled methods that should run at or before the specified time
    /// </summary>
    private void ExecuteScheduledMethodsAt(DateTimeOffset currentTime)
    {
        foreach (var method in _scheduledMethods.Values)
        {
            if (!method.IsPaused && method.NextExecutionTime <= currentTime)
            {
                var startTime = System.Diagnostics.Stopwatch.GetTimestamp();

                try
                {
                    // Increment global and local tick counters
                    _globalTickNumber++;
                    method.CallCount++;

                    // Call appropriate method type
                    if (method.MethodWithTicks != null)
                    {
                        method.MethodWithTicks(_globalTickNumber, method.CallCount);
                    }
                    else if (method.Method != null)
                    {
                        method.Method();
                    }
                }
                catch (Exception ex)
                {
                    // Log error but continue with other methods
                    _logger?.LogError(ex, "Error executing scheduled method {MethodName}", method.Name);
                }
                finally
                {
                    // Track execution time
                    var elapsed = System.Diagnostics.Stopwatch.GetTimestamp() - startTime;
                    var elapsedUs = (long)((elapsed * 1_000_000.0) / System.Diagnostics.Stopwatch.Frequency);

                    method.TotalExecutionUs += elapsedUs;
                    if (elapsedUs > method.MaxExecutionUs)
                        method.MaxExecutionUs = elapsedUs;
                }

                // Schedule next execution
                method.NextExecutionTime = currentTime.AddMilliseconds(method.IntervalMs);
            }
        }
    }

    /// <summary>
    /// Removes a scheduled method
    /// </summary>
    internal void Unschedule(Guid methodId)
    {
        _scheduledMethods.TryRemove(methodId, out _);
    }

    /// <summary>
    /// Pauses a scheduled method
    /// </summary>
    internal void Pause(Guid methodId)
    {
        if (_scheduledMethods.TryGetValue(methodId, out var method))
        {
            method.IsPaused = true;
        }
    }

    /// <summary>
    /// Resumes a scheduled method
    /// </summary>
    internal void Resume(Guid methodId)
    {
        if (_scheduledMethods.TryGetValue(methodId, out var method))
        {
            method.IsPaused = false;
            // Reset next execution time to current time + interval
            method.NextExecutionTime = _timeProvider.UtcNow.AddMilliseconds(method.IntervalMs);
        }
    }

    /// <summary>
    /// IScheduler implementation: Get scheduler statistics
    /// </summary>
    public SchedulerStatistics GetStatistics()
    {
        var stats = new SchedulerStatistics
        {
            GlobalTickNumber = _globalTickNumber,
            ScheduledMethodCount = _scheduledMethods.Count,
            MethodStats = new List<ScheduledMethodStats>()
        };

        foreach (var method in _scheduledMethods.Values)
        {
            stats.MethodStats.Add(new ScheduledMethodStats
            {
                Name = method.Name,
                ModuleName = string.Empty, // Not tracking module names in EventScheduler
                RequestedRateHz = method.RequestedRateHz,
                ActualRateHz = method.ActualRateHz,
                CallCount = method.CallCount,
                AverageExecutionUs = method.CallCount > 0
                    ? (double)method.TotalExecutionUs / method.CallCount
                    : 0,
                MaxExecutionUs = method.MaxExecutionUs,
                IsPaused = method.IsPaused
            });
        }

        return stats;
    }

    private class ScheduledMethod
    {
        public Guid Id { get; set; }
        public Action? Method { get; set; }
        public Action<long, long>? MethodWithTicks { get; set; }
        public double IntervalMs { get; set; }
        public DateTimeOffset NextExecutionTime { get; set; }
        public string Name { get; set; } = null!;
        public bool IsPaused { get; set; }
        public double RequestedRateHz { get; set; }
        public double ActualRateHz { get; set; }
        public long CallCount { get; set; }
        public long TotalExecutionUs { get; set; }
        public long MaxExecutionUs { get; set; }
    }

    private class ScheduledMethodHandle : IScheduledMethod, IScheduledMethodHandle
    {
        private readonly ScheduledMethod _method;
        private readonly EventScheduler _scheduler;
        private bool _disposed = false;

        public ScheduledMethodHandle(ScheduledMethod method, EventScheduler scheduler)
        {
            _method = method;
            _scheduler = scheduler;
        }

        // IScheduledMethod properties
        public string Name => _method.Name;
        public double RequestedRateHz => _method.RequestedRateHz;
        public double ActualRateHz => _method.ActualRateHz;
        public long CallCount => _method.CallCount;
        public double AverageExecutionUs =>
            _method.CallCount > 0 ? (double)_method.TotalExecutionUs / _method.CallCount : 0;
        public long MaxExecutionUs => _method.MaxExecutionUs;
        public bool IsPaused => _method.IsPaused;

        // Internal property for Unschedule
        internal Guid MethodId => _method.Id;

        public void Pause()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ScheduledMethodHandle));
            _scheduler.Pause(_method.Id);
        }

        public void Resume()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ScheduledMethodHandle));
            _scheduler.Resume(_method.Id);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _scheduler.Unschedule(_method.Id);
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// IDisposable implementation
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Stop();
        _cts.Dispose();
    }
}

/// <summary>
/// Handle for controlling a scheduled method (simplified interface)
/// </summary>
public interface IScheduledMethodHandle : IDisposable
{
    void Pause();
    void Resume();
    bool IsPaused { get; }
}
