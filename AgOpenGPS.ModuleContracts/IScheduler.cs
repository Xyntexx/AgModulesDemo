namespace AgOpenGPS.ModuleContracts;

/// <summary>
/// Scheduler interface for modules to register scheduled methods.
/// Allows multiple methods at different rates for fine-grained control.
/// </summary>
public interface IScheduler
{
    /// <summary>
    /// Schedule a method to be called at a specific rate.
    /// Returns a handle that can be used to unregister the method.
    /// </summary>
    /// <param name="method">Method to call (must be fast, non-blocking)</param>
    /// <param name="rateHz">Rate in Hz (e.g., 10.0 for 10Hz, 0.1 for once per 10 seconds)</param>
    /// <param name="name">Optional name for diagnostics (defaults to method name)</param>
    IScheduledMethod Schedule(Action<long, long> method, double rateHz, string? name = null);

    /// <summary>
    /// Unregister a scheduled method.
    /// </summary>
    void Unschedule(IScheduledMethod handle);

    /// <summary>
    /// Get statistics for all scheduled methods.
    /// </summary>
    SchedulerStatistics GetStatistics();
}

/// <summary>
/// Handle to a scheduled method.
/// Dispose to unregister the method.
/// </summary>
public interface IScheduledMethod : IDisposable
{
    /// <summary>Method name for diagnostics</summary>
    string Name { get; }

    /// <summary>Requested rate in Hz</summary>
    double RequestedRateHz { get; }

    /// <summary>Actual rate in Hz (may differ due to base rate divisor)</summary>
    double ActualRateHz { get; }

    /// <summary>Number of times this method has been called</summary>
    long CallCount { get; }

    /// <summary>Average execution time in microseconds</summary>
    double AverageExecutionUs { get; }

    /// <summary>Maximum execution time in microseconds</summary>
    long MaxExecutionUs { get; }

    /// <summary>
    /// Pause execution of this method.
    /// Tick counter continues but method is not called.
    /// </summary>
    void Pause();

    /// <summary>
    /// Resume execution of this method.
    /// </summary>
    void Resume();

    /// <summary>
    /// Check if method is currently paused.
    /// </summary>
    bool IsPaused { get; }
}

/// <summary>
/// Statistics for scheduler performance monitoring.
/// </summary>
public class SchedulerStatistics
{
    public long GlobalTickNumber { get; set; }
    public int ModuleCount { get; set; }
    public int ScheduledMethodCount { get; set; }
    public List<ModuleTickStats> ModuleStats { get; set; } = new();
    public List<ScheduledMethodStats> MethodStats { get; set; } = new();
}

public class ScheduledMethodStats
{
    public string Name { get; set; } = string.Empty;
    public string ModuleName { get; set; } = string.Empty;
    public double RequestedRateHz { get; set; }
    public double ActualRateHz { get; set; }
    public long CallCount { get; set; }
    public double AverageExecutionUs { get; set; }
    public long MaxExecutionUs { get; set; }
    public bool IsPaused { get; set; }
}

public class ModuleTickStats
{
    public string ModuleName { get; set; } = string.Empty;
    public double RequestedRateHz { get; set; }
    public double ActualRateHz { get; set; }
    public long TickCount { get; set; }
    public double TotalExecutionMs { get; set; }
    public double AverageExecutionUs { get; set; }
    public long MaxExecutionUs { get; set; }
    public long SkippedTicks { get; set; }
}
