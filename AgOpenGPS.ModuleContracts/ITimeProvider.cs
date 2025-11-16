namespace AgOpenGPS.ModuleContracts;

/// <summary>
/// Time abstraction for testable and controllable time in modules.
/// Provides both monotonic time (for durations/timeouts) and wall clock time (for user display).
///
/// IMPORTANT: Use the correct clock for your use case:
/// - MonotonicMilliseconds: Durations, timeouts, message timestamps (never goes backward)
/// - UtcNow: Human-readable logging, user display (can jump due to NTP/clock adjustments)
/// </summary>
public interface ITimeProvider
{
    /// <summary>
    /// Monotonic clock in milliseconds since system start.
    /// NEVER moves backward, immune to system clock adjustments.
    /// Use for: message timestamps, timeouts, durations, rate limiting.
    /// </summary>
    long MonotonicMilliseconds { get; }

    /// <summary>
    /// Wall clock UTC time (may jump backward due to NTP/clock adjustments).
    /// Use ONLY for: human-readable logging, user-facing displays.
    /// DO NOT use for: timeouts, durations, message ordering.
    /// </summary>
    DateTimeOffset UtcNow { get; }

    /// <summary>
    /// Gets Unix timestamp in milliseconds for message timestamping.
    /// Uses monotonic base + offset for consistency.
    /// This is the authoritative timebase for SimClock (SRS R-22-002).
    /// </summary>
    long UnixTimeMilliseconds => MonotonicMilliseconds + UnixEpochOffsetMs;

    /// <summary>
    /// Offset to add to MonotonicMilliseconds to get Unix epoch time.
    /// Calculated at provider initialization: (wall clock at boot) - (monotonic at boot).
    /// </summary>
    long UnixEpochOffsetMs { get; }

    /// <summary>
    /// Asynchronously waits for a specified time span.
    /// Respects time scaling in simulated time providers.
    /// Uses monotonic time internally for accuracy.
    /// </summary>
    Task Delay(TimeSpan duration, CancellationToken cancellationToken = default);
}
