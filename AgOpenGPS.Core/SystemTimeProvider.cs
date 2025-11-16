namespace AgOpenGPS.Core;

using AgOpenGPS.ModuleContracts;
using System.Diagnostics;

/// <summary>
/// Real-time implementation of ITimeProvider with monotonic clock.
///
/// Uses Stopwatch for monotonic time (never goes backward) and DateTimeOffset for wall clock.
/// This ensures message timestamps and timeouts are immune to system clock adjustments (NTP, DST, manual changes).
///
/// Design:
/// - MonotonicMilliseconds: Stopwatch-based, starts at system boot, never goes backward
/// - UnixTimeMilliseconds: Monotonic + offset calculated at initialization
/// - UtcNow: System wall clock, can jump due to NTP (use only for user display)
/// </summary>
public class SystemTimeProvider : ITimeProvider
{
    // Monotonic clock baseline (captured at initialization)
    private static readonly long _bootMonotonicMs;
    private static readonly long _bootWallClockMs;
    private static readonly long _unixEpochOffsetMs;

    static SystemTimeProvider()
    {
        // Capture both clocks at the same instant during initialization
        _bootWallClockMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _bootMonotonicMs = GetCurrentMonotonicMs();

        // Calculate offset: wall clock at boot - monotonic at boot
        // This allows us to convert monotonic time to Unix epoch time
        _unixEpochOffsetMs = _bootWallClockMs - _bootMonotonicMs;
    }

    /// <summary>
    /// Gets monotonic milliseconds from Stopwatch (high precision, never backward).
    /// </summary>
    private static long GetCurrentMonotonicMs()
    {
        return Stopwatch.GetTimestamp() * 1000 / Stopwatch.Frequency;
    }

    /// <summary>
    /// Monotonic clock: milliseconds since system start (never goes backward).
    /// Immune to NTP adjustments, DST changes, manual clock changes.
    /// </summary>
    public long MonotonicMilliseconds => GetCurrentMonotonicMs();

    /// <summary>
    /// Wall clock: current UTC time (can jump due to NTP/clock adjustments).
    /// Use ONLY for human-readable logging and user-facing displays.
    /// </summary>
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    /// <summary>
    /// Offset to convert monotonic time to Unix epoch time.
    /// Calculated at initialization: (wall clock at boot) - (monotonic at boot).
    /// </summary>
    public long UnixEpochOffsetMs => _unixEpochOffsetMs;

    /// <summary>
    /// Unix timestamp for message timestamping (monotonic-based, deterministic).
    /// Uses: MonotonicMilliseconds + UnixEpochOffsetMs
    ///
    /// This is the authoritative SimClock timebase (SRS R-22-002).
    /// Advantage: Immune to system clock adjustments while still correlating with real time.
    /// </summary>
    public long UnixTimeMilliseconds => MonotonicMilliseconds + UnixEpochOffsetMs;

    public Task Delay(TimeSpan duration, CancellationToken cancellationToken = default)
    {
        return Task.Delay(duration, cancellationToken);
    }
}
