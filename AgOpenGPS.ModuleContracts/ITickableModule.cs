namespace AgOpenGPS.ModuleContracts;

/// <summary>
/// Interface for modules that want scheduled tick-based execution.
/// Provides deterministic, rate-controlled execution instead of free-running loops.
/// </summary>
public interface ITickableModule : IAgModule
{
    /// <summary>
    /// Desired tick rate in Hz (e.g., 10.0 for 10Hz, 50.0 for 50Hz).
    /// The scheduler will call Tick() at approximately this rate.
    /// Common rates: 10Hz (GPS), 20Hz (IMU), 50Hz (control loops), 100Hz (fast sensors)
    /// </summary>
    double TickRateHz { get; }

    /// <summary>
    /// Called by scheduler at TickRateHz.
    /// This should be fast and non-blocking (typically &lt;1ms).
    /// Use for: updating state, publishing messages, reading sensors.
    /// Avoid: blocking I/O, long computations, Thread.Sleep.
    /// </summary>
    /// <param name="tickNumber">Monotonically increasing tick count (starts at 0)</param>
    /// <param name="monotonicMs">Current monotonic time in milliseconds</param>
    void Tick(long tickNumber, long monotonicMs);
}
