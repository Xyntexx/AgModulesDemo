namespace AgOpenGPS.ModuleContracts.Messages;

/// <summary>Guidance line for autosteer to follow</summary>
public struct GuidanceLineMessage
{
    public double StartLatitude;
    public double StartLongitude;
    public double HeadingDegrees;
    public double OffsetMeters;

    /// <summary>Comprehensive timestamp with SimClock, UTC, and GPS time</summary>
    public TimestampMetadata Timestamp;

    /// <summary>Legacy timestamp field for backward compatibility (deprecated - use Timestamp.SimClockMs)</summary>
    [Obsolete("Use Timestamp.SimClockMs instead")]
    public long TimestampMs => Timestamp.SimClockMs;
}

/// <summary>Field boundary definition</summary>
public struct FieldBoundaryMessage
{
    public (double Lat, double Lon)[] BoundaryPoints;

    /// <summary>Comprehensive timestamp with SimClock, UTC, and GPS time</summary>
    public TimestampMetadata Timestamp;

    /// <summary>Legacy timestamp field for backward compatibility (deprecated - use Timestamp.SimClockMs)</summary>
    [Obsolete("Use Timestamp.SimClockMs instead")]
    public long TimestampMs => Timestamp.SimClockMs;
}

/// <summary>Application lifecycle events</summary>
public struct ApplicationStartedEvent
{
    /// <summary>Comprehensive timestamp with SimClock, UTC, and GPS time</summary>
    public TimestampMetadata Timestamp;

    /// <summary>Legacy timestamp field for backward compatibility (deprecated - use Timestamp.SimClockMs)</summary>
    [Obsolete("Use Timestamp.SimClockMs instead")]
    public long TimestampMs => Timestamp.SimClockMs;
}

/// <summary>Application stopping event</summary>
public struct ApplicationStoppingEvent
{
    /// <summary>Comprehensive timestamp with SimClock, UTC, and GPS time</summary>
    public TimestampMetadata Timestamp;

    /// <summary>Legacy timestamp field for backward compatibility (deprecated - use Timestamp.SimClockMs)</summary>
    [Obsolete("Use Timestamp.SimClockMs instead")]
    public long TimestampMs => Timestamp.SimClockMs;
}

/// <summary>Settings changed event</summary>
public struct SettingsChangedMessage
{
    public string ModuleName;
    public string SettingsJson;

    /// <summary>Comprehensive timestamp with SimClock, UTC, and GPS time</summary>
    public TimestampMetadata Timestamp;

    /// <summary>Legacy timestamp field for backward compatibility (deprecated - use Timestamp.SimClockMs)</summary>
    [Obsolete("Use Timestamp.SimClockMs instead")]
    public long TimestampMs => Timestamp.SimClockMs;
}
