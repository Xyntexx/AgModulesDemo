namespace AgOpenGPS.ModuleContracts.Messages;

/// <summary>Raw data received from IO channels</summary>
public struct RawDataReceivedMessage
{
    public byte[] Data;
    public IOChannel Channel;

    /// <summary>Comprehensive timestamp with SimClock, UTC, and GPS time</summary>
    public TimestampMetadata Timestamp;
}

/// <summary>GPS position data (parsed from PGN)</summary>
public struct GpsPositionMessage
{
    public double Latitude;
    public double Longitude;
    public double Altitude;
    public double Heading;
    public double Speed;
    public GpsFixQuality FixQuality;
    public int SatelliteCount;

    /// <summary>Comprehensive timestamp with SimClock, UTC, and GPS time</summary>
    public TimestampMetadata Timestamp;
}

/// <summary>IMU orientation data</summary>
public struct IMUDataMessage
{
    public double Roll;
    public double Pitch;
    public double Yaw;

    /// <summary>Comprehensive timestamp with SimClock, UTC, and GPS time</summary>
    public TimestampMetadata Timestamp;
}

/// <summary>Hardware status acknowledgment</summary>
public struct HardwareStatusMessage
{
    public bool AutosteerEngaged;
    public bool SectionsEnabled;
    public byte ErrorFlags;

    /// <summary>Comprehensive timestamp with SimClock, UTC, and GPS time</summary>
    public TimestampMetadata Timestamp;
}

/// <summary>IO channel types</summary>
public enum IOChannel
{
    Serial,
    UDP,
    CAN
}

/// <summary>GPS fix quality levels</summary>
public enum GpsFixQuality
{
    NoFix = 0,
    GPS = 1,
    DGPS = 2,
    RTK_Fixed = 4,
    RTK_Float = 5
}
