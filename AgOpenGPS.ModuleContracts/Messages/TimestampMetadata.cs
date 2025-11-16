namespace AgOpenGPS.ModuleContracts.Messages;

/// <summary>
/// Comprehensive timestamp metadata for deterministic replay and multi-system coordination.
/// Implements SRS requirements R-22-002 (SimClock Alignment) and 23-ADR-002 (Temporal Architecture).
/// </summary>
public readonly struct TimestampMetadata
{
    /// <summary>
    /// Deterministic simulation clock time in milliseconds since epoch.
    /// This is the authoritative timebase for replay and verification.
    /// All modules receive the same ITimeProvider, ensuring consistency.
    /// </summary>
    public long SimClockMs { get; init; }

    /// <summary>
    /// Wall clock UTC time in milliseconds since Unix epoch (1970-01-01).
    /// Used for cross-system correlation and human-readable logs.
    /// May differ from SimClockMs in simulation mode.
    /// </summary>
    public long UtcTimestampMs { get; init; }

    /// <summary>
    /// ISO 8601 formatted UTC timestamp (e.g., "2025-11-16T14:23:45.123Z").
    /// Human-readable format for logs, debugging, and compliance reporting.
    /// </summary>
    public string UtcIso8601 { get; init; }

    /// <summary>
    /// GPS week number (weeks since GPS epoch: 1980-01-06).
    /// Used for GPS time correlation and GNSS system synchronization.
    /// -1 if GPS time is unavailable.
    /// </summary>
    public int GpsWeek { get; init; }

    /// <summary>
    /// GPS seconds within the current week (0-604799).
    /// Provides sub-millisecond precision for GPS-based timing.
    /// -1.0 if GPS time is unavailable.
    /// </summary>
    public double GpsSecondsOfWeek { get; init; }

    /// <summary>
    /// Sequence number for this message within the current session.
    /// Enables detection of dropped messages and ordering verification.
    /// Increments monotonically per message type.
    /// </summary>
    public long SequenceNumber { get; init; }

    /// <summary>
    /// Creates timestamp metadata using the provided time provider.
    /// Uses current system time for UTC values and GPS time if available.
    /// </summary>
    /// <param name="timeProvider">Time provider (SystemTimeProvider or SimulatedTimeProvider)</param>
    /// <param name="sequenceNumber">Message sequence number</param>
    /// <param name="gpsTime">Optional GPS time (week, seconds). Use null if GPS unavailable.</param>
    public static TimestampMetadata Create(
        ITimeProvider timeProvider,
        long sequenceNumber,
        (int week, double seconds)? gpsTime = null)
    {
        var simClockMs = timeProvider.UnixTimeMilliseconds;
        var utcNow = DateTimeOffset.UtcNow;

        return new TimestampMetadata
        {
            SimClockMs = simClockMs,
            UtcTimestampMs = utcNow.ToUnixTimeMilliseconds(),
            UtcIso8601 = utcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
            GpsWeek = gpsTime?.week ?? -1,
            GpsSecondsOfWeek = gpsTime?.seconds ?? -1.0,
            SequenceNumber = sequenceNumber
        };
    }

    /// <summary>
    /// Creates timestamp metadata with only SimClock time (minimal overhead).
    /// Use this for high-frequency messages where GPS/UTC correlation is not needed.
    /// </summary>
    public static TimestampMetadata CreateSimClockOnly(ITimeProvider timeProvider, long sequenceNumber)
    {
        return new TimestampMetadata
        {
            SimClockMs = timeProvider.UnixTimeMilliseconds,
            UtcTimestampMs = 0,
            UtcIso8601 = string.Empty,
            GpsWeek = -1,
            GpsSecondsOfWeek = -1.0,
            SequenceNumber = sequenceNumber
        };
    }

    /// <summary>
    /// Creates timestamp metadata with explicit values (for testing/replay).
    /// </summary>
    public static TimestampMetadata CreateExplicit(
        long simClockMs,
        long utcTimestampMs,
        string utcIso8601,
        int gpsWeek,
        double gpsSecondsOfWeek,
        long sequenceNumber)
    {
        return new TimestampMetadata
        {
            SimClockMs = simClockMs,
            UtcTimestampMs = utcTimestampMs,
            UtcIso8601 = utcIso8601,
            GpsWeek = gpsWeek,
            GpsSecondsOfWeek = gpsSecondsOfWeek,
            SequenceNumber = sequenceNumber
        };
    }

    /// <summary>
    /// Checks if GPS time is available.
    /// </summary>
    public bool HasGpsTime => GpsWeek >= 0 && GpsSecondsOfWeek >= 0;

    /// <summary>
    /// Calculates time difference in milliseconds between this timestamp and another.
    /// Uses SimClockMs for deterministic comparison.
    /// </summary>
    public long DeltaMs(TimestampMetadata other) => SimClockMs - other.SimClockMs;

    public override string ToString()
    {
        if (!string.IsNullOrEmpty(UtcIso8601))
        {
            return $"[SimClock: {SimClockMs}ms, UTC: {UtcIso8601}, Seq: {SequenceNumber}]";
        }
        return $"[SimClock: {SimClockMs}ms, Seq: {SequenceNumber}]";
    }
}
