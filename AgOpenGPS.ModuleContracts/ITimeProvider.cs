namespace AgOpenGPS.ModuleContracts;

/// <summary>
/// Time abstraction for testable and controllable time in modules
/// Allows real-time, simulated time, and fast-forward scenarios
/// </summary>
public interface ITimeProvider
{
    /// <summary>
    /// Gets the current UTC time
    /// </summary>
    DateTimeOffset UtcNow { get; }

    /// <summary>
    /// Asynchronously waits for a specified time span
    /// Respects time scaling in simulated time providers
    /// </summary>
    Task Delay(TimeSpan duration, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current Unix timestamp in milliseconds
    /// Convenience method equivalent to UtcNow.ToUnixTimeMilliseconds()
    /// </summary>
    long UnixTimeMilliseconds => UtcNow.ToUnixTimeMilliseconds();
}
