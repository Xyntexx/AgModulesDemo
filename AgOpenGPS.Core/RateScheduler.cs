namespace AgOpenGPS.Core;

using AgOpenGPS.ModuleContracts;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

/// <summary>
/// Deterministic rate-based scheduler for modules.
/// Guarantees fixed-rate execution with drift compensation and deterministic ordering.
/// Provides the tick-based execution model required by SRS R-22-001.
/// Supports both ITickableModule and standalone scheduled methods.
/// </summary>
public class RateScheduler : IScheduler, IDisposable
{
    private readonly ITimeProvider _timeProvider;
    private readonly ILogger<RateScheduler> _logger;
    private readonly List<ScheduledModule> _scheduledModules = new();
    private readonly List<ScheduledMethodInfo> _scheduledMethods = new();
    private readonly Thread? _schedulerThread;
    private readonly CancellationTokenSource _cts = new();
    private long _globalTickNumber = 0;
    private readonly object _lock = new();
    private int _nextMethodId = 0;

    /// <summary>
    /// Scheduler configuration
    /// </summary>
    public double BaseTickRateHz { get; }
    public TimeSpan BaseTickInterval { get; }
    public bool IsRunning { get; private set; }

    /// <summary>
    /// Create a rate scheduler with a base tick rate
    /// </summary>
    /// <param name="baseTickRateHz">Base tick rate (modules will run at multiples/divisors of this)</param>
    /// <param name="timeProvider">Time provider for monotonic time</param>
    /// <param name="logger">Logger</param>
    public RateScheduler(double baseTickRateHz, ITimeProvider timeProvider, ILogger<RateScheduler> logger)
    {
        if (baseTickRateHz <= 0 || baseTickRateHz > 1000)
            throw new ArgumentException("Base tick rate must be between 0 and 1000 Hz", nameof(baseTickRateHz));

        BaseTickRateHz = baseTickRateHz;
        BaseTickInterval = TimeSpan.FromSeconds(1.0 / baseTickRateHz);
        _timeProvider = timeProvider;
        _logger = logger;

        _schedulerThread = new Thread(SchedulerLoop)
        {
            Name = "RateScheduler",
            IsBackground = false,
            Priority = ThreadPriority.AboveNormal
        };
    }

    /// <summary>
    /// Register a module for scheduled execution
    /// </summary>
    public void RegisterModule(ITickableModule module)
    {
        lock (_lock)
        {
            if (IsRunning)
                throw new InvalidOperationException("Cannot register modules while scheduler is running");

            // Calculate tick divisor (how often to skip ticks)
            var divisor = CalculateTickDivisor(module.TickRateHz);

            var scheduled = new ScheduledModule
            {
                Module = module,
                RequestedRateHz = module.TickRateHz,
                ActualRateHz = BaseTickRateHz / divisor,
                TickDivisor = divisor,
                Category = module.Category
            };

            _scheduledModules.Add(scheduled);

            _logger.LogInformation(
                "Registered {ModuleName} for scheduled execution: Requested={RequestedHz}Hz, Actual={ActualHz}Hz, Divisor={Divisor}",
                module.Name, scheduled.RequestedRateHz, scheduled.ActualRateHz, scheduled.TickDivisor);
        }
    }

    /// <summary>
    /// Schedule a standalone method for execution at a specific rate
    /// </summary>
    public IScheduledMethod Schedule(Action<long, long> method, double rateHz, string? name = null)
    {
        lock (_lock)
        {
            var divisor = CalculateTickDivisor(rateHz);
            var actualRateHz = BaseTickRateHz / divisor;

            var methodInfo = new ScheduledMethodInfo
            {
                Id = _nextMethodId++,
                Method = method,
                Name = name ?? method.Method.Name,
                RequestedRateHz = rateHz,
                ActualRateHz = actualRateHz,
                TickDivisor = divisor
            };

            _scheduledMethods.Add(methodInfo);

            _logger.LogInformation(
                "Scheduled method {Name}: Requested={RequestedHz}Hz, Actual={ActualHz}Hz, Divisor={Divisor}",
                methodInfo.Name, methodInfo.RequestedRateHz, methodInfo.ActualRateHz, methodInfo.TickDivisor);

            return new ScheduledMethodHandle(this, methodInfo);
        }
    }

    /// <summary>
    /// Unschedule a method
    /// </summary>
    public void Unschedule(IScheduledMethod handle)
    {
        if (handle is ScheduledMethodHandle smh)
        {
            lock (_lock)
            {
                _scheduledMethods.RemoveAll(m => m.Id == smh.MethodInfo.Id);
                _logger.LogInformation("Unscheduled method {Name}", smh.Name);
            }
        }
    }

    /// <summary>
    /// Start the scheduler
    /// </summary>
    public void Start()
    {
        lock (_lock)
        {
            if (IsRunning)
                throw new InvalidOperationException("Scheduler is already running");

            // Sort modules by category and then by rate (higher rate first for better timing)
            _scheduledModules.Sort((a, b) =>
            {
                var catCompare = a.Category.CompareTo(b.Category);
                if (catCompare != 0) return catCompare;
                return b.ActualRateHz.CompareTo(a.ActualRateHz);
            });

            _logger.LogInformation("Starting rate scheduler at {BaseRate}Hz with {ModuleCount} modules",
                BaseTickRateHz, _scheduledModules.Count);

            foreach (var mod in _scheduledModules)
            {
                _logger.LogInformation("  [{Category}] {Name} @ {Rate}Hz",
                    mod.Category, mod.Module.Name, mod.ActualRateHz);
            }

            IsRunning = true;
            _schedulerThread?.Start();
        }
    }

    /// <summary>
    /// Stop the scheduler gracefully
    /// </summary>
    public void Stop()
    {
        lock (_lock)
        {
            if (!IsRunning) return;

            _logger.LogInformation("Stopping rate scheduler...");
            IsRunning = false;
            _cts.Cancel();
        }

        _schedulerThread?.Join(TimeSpan.FromSeconds(5));
        _logger.LogInformation("Rate scheduler stopped");
    }

    /// <summary>
    /// Get scheduler statistics
    /// </summary>
    public SchedulerStatistics GetStatistics()
    {
        lock (_lock)
        {
            return new SchedulerStatistics
            {
                GlobalTickNumber = _globalTickNumber,
                ModuleCount = _scheduledModules.Count,
                ScheduledMethodCount = _scheduledMethods.Count,
                ModuleStats = _scheduledModules.Select(m => new ModuleTickStats
                {
                    ModuleName = m.Module.Name,
                    RequestedRateHz = m.RequestedRateHz,
                    ActualRateHz = m.ActualRateHz,
                    TickCount = m.TickCount,
                    TotalExecutionMs = m.TotalExecutionMs,
                    AverageExecutionUs = m.TickCount > 0 ? (m.TotalExecutionMs * 1000.0 / m.TickCount) : 0,
                    MaxExecutionUs = m.MaxExecutionUs,
                    SkippedTicks = m.SkippedTicks
                }).ToList(),
                MethodStats = _scheduledMethods.Select(m => new ScheduledMethodStats
                {
                    Name = m.Name,
                    ModuleName = "Standalone",  // Methods don't belong to modules
                    RequestedRateHz = m.RequestedRateHz,
                    ActualRateHz = m.ActualRateHz,
                    CallCount = m.CallCount,
                    AverageExecutionUs = m.CallCount > 0 ? (m.TotalExecutionMs * 1000.0 / m.CallCount) : 0,
                    MaxExecutionUs = m.MaxExecutionUs,
                    IsPaused = m.IsPaused
                }).ToList()
            };
        }
    }

    /// <summary>
    /// Main scheduler loop - runs on dedicated thread
    /// </summary>
    private void SchedulerLoop()
    {
        _logger.LogInformation("Scheduler thread started");

        var startMonotonicMs = _timeProvider.MonotonicMilliseconds;
        var nextTickMonotonicMs = startMonotonicMs + (long)BaseTickInterval.TotalMilliseconds;
        var sw = Stopwatch.StartNew();

        try
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                var tickStartMonotonicMs = _timeProvider.MonotonicMilliseconds;

                // Execute tick for all modules that should run this tick
                ExecuteTick(_globalTickNumber, tickStartMonotonicMs);

                _globalTickNumber++;

                // Calculate sleep time with drift compensation
                var executionTimeMs = _timeProvider.MonotonicMilliseconds - tickStartMonotonicMs;
                var sleepTimeMs = nextTickMonotonicMs - _timeProvider.MonotonicMilliseconds;

                if (sleepTimeMs > 0)
                {
                    // Sleep until next tick (using SpinWait for last 1ms for precision)
                    if (sleepTimeMs > 2)
                    {
                        Thread.Sleep((int)sleepTimeMs - 1);
                    }

                    // Spin-wait for final precision
                    var spinDeadline = nextTickMonotonicMs;
                    while (_timeProvider.MonotonicMilliseconds < spinDeadline && !_cts.Token.IsCancellationRequested)
                    {
                        Thread.SpinWait(100);
                    }
                }
                else
                {
                    // Tick overran - log warning
                    _logger.LogWarning(
                        "Scheduler overrun: Tick {TickNumber} took {ExecutionMs}ms (budget: {BudgetMs}ms, overrun: {OverrunMs}ms)",
                        _globalTickNumber, executionTimeMs, BaseTickInterval.TotalMilliseconds, -sleepTimeMs);
                }

                // Calculate next tick time (drift compensation)
                nextTickMonotonicMs += (long)BaseTickInterval.TotalMilliseconds;

                // Log statistics every 10 seconds
                if (_globalTickNumber % (int)(BaseTickRateHz * 10) == 0)
                {
                    LogStatistics();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Scheduler thread crashed!");
        }
        finally
        {
            _logger.LogInformation("Scheduler thread exiting");
        }
    }

    /// <summary>
    /// Execute one tick - call all modules and methods that should run this tick
    /// </summary>
    private void ExecuteTick(long globalTick, long monotonicMs)
    {
        // 1. Execute scheduled modules
        foreach (var module in _scheduledModules)
        {
            // Check if this module should run this tick
            if (globalTick % module.TickDivisor == 0)
            {
                var startMonotonicMs = _timeProvider.MonotonicMilliseconds;

                try
                {
                    module.Module.Tick(globalTick, monotonicMs);

                    // Track statistics
                    var executionUs = (_timeProvider.MonotonicMilliseconds - startMonotonicMs) * 1000;
                    module.TickCount++;
                    module.TotalExecutionMs += executionUs / 1000.0;
                    if (executionUs > module.MaxExecutionUs)
                    {
                        module.MaxExecutionUs = executionUs;
                    }

                    // Warn if module took too long
                    if (executionUs > 1000) // > 1ms
                    {
                        _logger.LogWarning(
                            "{ModuleName}.Tick() took {ExecutionUs}μs (>1ms) - consider optimizing",
                            module.Module.Name, executionUs);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "{ModuleName}.Tick() failed at tick {TickNumber}",
                        module.Module.Name, globalTick);
                    module.SkippedTicks++;
                }
            }
        }

        // 2. Execute scheduled methods
        foreach (var method in _scheduledMethods)
        {
            // Skip if paused
            if (method.IsPaused)
                continue;

            // Check if this method should run this tick
            if (globalTick % method.TickDivisor == 0)
            {
                var startMonotonicMs = _timeProvider.MonotonicMilliseconds;

                try
                {
                    method.Method(globalTick, monotonicMs);

                    // Track statistics
                    var executionUs = (_timeProvider.MonotonicMilliseconds - startMonotonicMs) * 1000;
                    method.CallCount++;
                    method.TotalExecutionMs += executionUs / 1000.0;
                    if (executionUs > method.MaxExecutionUs)
                    {
                        method.MaxExecutionUs = executionUs;
                    }

                    // Warn if method took too long
                    if (executionUs > 1000) // > 1ms
                    {
                        _logger.LogWarning(
                            "Scheduled method {MethodName} took {ExecutionUs}μs (>1ms) - consider optimizing",
                            method.Name, executionUs);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Scheduled method {MethodName} failed at tick {TickNumber}",
                        method.Name, globalTick);
                }
            }
        }
    }

    /// <summary>
    /// Calculate tick divisor to achieve desired rate from base rate
    /// </summary>
    private int CalculateTickDivisor(double desiredRateHz)
    {
        // Find divisor that gets closest to desired rate
        // BaseRate / divisor ≈ desiredRate
        // divisor ≈ BaseRate / desiredRate

        var idealDivisor = BaseTickRateHz / desiredRateHz;
        var divisor = (int)Math.Round(idealDivisor);

        if (divisor < 1) divisor = 1;

        return divisor;
    }

    /// <summary>
    /// Log scheduler statistics
    /// </summary>
    private void LogStatistics()
    {
        var stats = GetStatistics();
        _logger.LogInformation("Scheduler Stats: Tick={Tick}, Modules={Count}",
            stats.GlobalTickNumber, stats.ModuleCount);

        foreach (var mod in stats.ModuleStats)
        {
            _logger.LogDebug("  {Name}: Ticks={Count}, Avg={AvgUs}μs, Max={MaxUs}μs, Skipped={Skipped}",
                mod.ModuleName, mod.TickCount, mod.AverageExecutionUs.ToString("F1"),
                mod.MaxExecutionUs, mod.SkippedTicks);
        }
    }

    public void Dispose()
    {
        Stop();
        _cts.Dispose();
    }

    private class ScheduledModule
    {
        public ITickableModule Module { get; set; } = null!;
        public double RequestedRateHz { get; set; }
        public double ActualRateHz { get; set; }
        public int TickDivisor { get; set; }
        public ModuleCategory Category { get; set; }

        // Statistics
        public long TickCount { get; set; }
        public double TotalExecutionMs { get; set; }
        public long MaxExecutionUs { get; set; }
        public long SkippedTicks { get; set; }
    }
}

/// <summary>
/// Internal class for tracking scheduled methods
/// </summary>
internal class ScheduledMethodInfo
{
    public int Id { get; set; }
    public Action<long, long> Method { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public double RequestedRateHz { get; set; }
    public double ActualRateHz { get; set; }
    public int TickDivisor { get; set; }
    public bool IsPaused { get; set; }

    // Statistics
    public long CallCount { get; set; }
    public double TotalExecutionMs { get; set; }
    public long MaxExecutionUs { get; set; }
}

/// <summary>
/// Handle to a scheduled method
/// </summary>
internal class ScheduledMethodHandle : IScheduledMethod
{
    private readonly RateScheduler _scheduler;
    internal readonly ScheduledMethodInfo MethodInfo;

    public ScheduledMethodHandle(RateScheduler scheduler, ScheduledMethodInfo methodInfo)
    {
        _scheduler = scheduler;
        MethodInfo = methodInfo;
    }

    public string Name => MethodInfo.Name;
    public double RequestedRateHz => MethodInfo.RequestedRateHz;
    public double ActualRateHz => MethodInfo.ActualRateHz;
    public long CallCount => MethodInfo.CallCount;
    public double AverageExecutionUs => MethodInfo.CallCount > 0 ? (MethodInfo.TotalExecutionMs * 1000.0 / MethodInfo.CallCount) : 0;
    public long MaxExecutionUs => MethodInfo.MaxExecutionUs;
    public bool IsPaused => MethodInfo.IsPaused;

    public void Pause() => MethodInfo.IsPaused = true;
    public void Resume() => MethodInfo.IsPaused = false;

    public void Dispose()
    {
        _scheduler.Unschedule(this);
    }
}
