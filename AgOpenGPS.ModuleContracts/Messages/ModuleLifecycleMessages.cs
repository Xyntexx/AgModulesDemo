namespace AgOpenGPS.ModuleContracts.Messages;

/// <summary>
/// Event published when a module is loaded at runtime
/// </summary>
public struct ModuleLoadedEvent
{
    public string ModuleId { get; set; }
    public string ModuleName { get; set; }
    public string Version { get; set; }

    /// <summary>Comprehensive timestamp with SimClock, UTC, and GPS time</summary>
    public TimestampMetadata Timestamp { get; set; }

    /// <summary>Legacy timestamp field for backward compatibility (deprecated - use Timestamp.SimClockMs)</summary>
    [Obsolete("Use Timestamp.SimClockMs instead")]
    public long TimestampMs
    {
        get => Timestamp.SimClockMs;
        set => Timestamp = TimestampMetadata.CreateExplicit(value, value, string.Empty, -1, -1.0, 0);
    }
}

/// <summary>
/// Event published when a module is unloaded at runtime
/// </summary>
public struct ModuleUnloadedEvent
{
    public string ModuleId { get; set; }
    public string ModuleName { get; set; }

    /// <summary>Comprehensive timestamp with SimClock, UTC, and GPS time</summary>
    public TimestampMetadata Timestamp { get; set; }

    /// <summary>Legacy timestamp field for backward compatibility (deprecated - use Timestamp.SimClockMs)</summary>
    [Obsolete("Use Timestamp.SimClockMs instead")]
    public long TimestampMs
    {
        get => Timestamp.SimClockMs;
        set => Timestamp = TimestampMetadata.CreateExplicit(value, value, string.Empty, -1, -1.0, 0);
    }
}

/// <summary>
/// Event published when a module is reloaded at runtime
/// </summary>
public struct ModuleReloadedEvent
{
    public string ModuleId { get; set; }
    public string ModuleName { get; set; }

    /// <summary>Comprehensive timestamp with SimClock, UTC, and GPS time</summary>
    public TimestampMetadata Timestamp { get; set; }

    /// <summary>Legacy timestamp field for backward compatibility (deprecated - use Timestamp.SimClockMs instead")]
    [Obsolete("Use Timestamp.SimClockMs instead")]
    public long TimestampMs
    {
        get => Timestamp.SimClockMs;
        set => Timestamp = TimestampMetadata.CreateExplicit(value, value, string.Empty, -1, -1.0, 0);
    }
}
