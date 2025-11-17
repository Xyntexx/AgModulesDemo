namespace AgOpenGPS.ModuleContracts.Messages;

/// <summary>Autosteer engage/disengage command from UI</summary>
public readonly struct AutosteerEngageMessage
{
    public bool IsEngaged { get; init; }

    /// <summary>Comprehensive timestamp with SimClock, UTC, and GPS time</summary>
    public TimestampMetadata Timestamp { get; init; }
}

/// <summary>Steer command to autosteer controller</summary>
public struct SteerCommandMessage
{
    public double SteerAngleDegrees;  // -45 to +45
    public byte SpeedPWM;              // 0-255
    public bool IsEngaged;             // Autosteer active

    /// <summary>Comprehensive timestamp with SimClock, UTC, and GPS time</summary>
    public TimestampMetadata Timestamp;
}

/// <summary>Section control (on/off for each spray section)</summary>
public struct SectionControlMessage
{
    public ushort SectionBitmap;  // Bit flags for up to 16 sections

    /// <summary>Comprehensive timestamp with SimClock, UTC, and GPS time</summary>
    public TimestampMetadata Timestamp;
}

/// <summary>Relay control command</summary>
public struct RelayCommandMessage
{
    public byte RelayNumber;
    public bool State;

    /// <summary>Comprehensive timestamp with SimClock, UTC, and GPS time</summary>
    public TimestampMetadata Timestamp;
}

/// <summary>Raw data to send to hardware (from PGN encoder)</summary>
public struct RawDataToSendMessage
{
    public byte[] Data;
    public IOChannel TargetChannel;
    public bool RequireAcknowledgment;
}
