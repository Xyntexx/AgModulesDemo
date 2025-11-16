namespace AgOpenGPS.ModuleContracts.Messages;

using System.Collections.Concurrent;

/// <summary>
/// Thread-safe timestamp generator with automatic sequence numbering per message type.
/// Implements SRS requirements for deterministic replay and cross-system correlation.
/// </summary>
public class TimestampGenerator
{
    private readonly ITimeProvider _timeProvider;
    private readonly ConcurrentDictionary<Type, long> _sequenceNumbers = new();
    private long _globalSequence = 0;

    public TimestampGenerator(ITimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Creates a full timestamp with GPS time correlation.
    /// Use this for GPS-derived messages where GPS time is available.
    /// </summary>
    /// <param name="messageType">Type of message (for sequence numbering)</param>
    /// <param name="gpsWeek">GPS week number</param>
    /// <param name="gpsSeconds">GPS seconds of week</param>
    public TimestampMetadata CreateWithGps<T>(int gpsWeek, double gpsSeconds)
    {
        var sequence = GetNextSequence<T>();
        return TimestampMetadata.Create(_timeProvider, sequence, (gpsWeek, gpsSeconds));
    }

    /// <summary>
    /// Creates a timestamp with UTC correlation but no GPS time.
    /// Use this for most messages where GPS time is not available.
    /// </summary>
    public TimestampMetadata Create<T>()
    {
        var sequence = GetNextSequence<T>();
        return TimestampMetadata.Create(_timeProvider, sequence, null);
    }

    /// <summary>
    /// Creates a minimal timestamp with only SimClock time (fastest).
    /// Use this for high-frequency messages (>100Hz) where UTC/GPS overhead is too high.
    /// </summary>
    public TimestampMetadata CreateSimClockOnly<T>()
    {
        var sequence = GetNextSequence<T>();
        return TimestampMetadata.CreateSimClockOnly(_timeProvider, sequence);
    }

    /// <summary>
    /// Gets the next sequence number for a specific message type.
    /// Thread-safe and monotonically increasing per type.
    /// </summary>
    private long GetNextSequence<T>()
    {
        var type = typeof(T);
        return _sequenceNumbers.AddOrUpdate(type, 1, (_, current) => current + 1);
    }

    /// <summary>
    /// Gets the next global sequence number (across all message types).
    /// Useful for total message ordering across the entire system.
    /// </summary>
    public long GetNextGlobalSequence()
    {
        return Interlocked.Increment(ref _globalSequence);
    }

    /// <summary>
    /// Resets all sequence counters (for testing or session restart).
    /// </summary>
    public void ResetSequences()
    {
        _sequenceNumbers.Clear();
        _globalSequence = 0;
    }

    /// <summary>
    /// Gets the current sequence number for a message type (without incrementing).
    /// </summary>
    public long GetCurrentSequence<T>()
    {
        var type = typeof(T);
        return _sequenceNumbers.TryGetValue(type, out var seq) ? seq : 0;
    }
}
