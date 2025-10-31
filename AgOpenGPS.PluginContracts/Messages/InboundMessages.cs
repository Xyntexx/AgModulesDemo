namespace AgOpenGPS.ModuleContracts.Messages;

/// <summary>Raw data received from IO channels</summary>
public struct RawDataReceivedMessage
{
    public byte[] Data;
    public IOChannel Channel;
    public long TimestampMs;
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
    public long TimestampMs;
}

/// <summary>IMU orientation data</summary>
public struct IMUDataMessage
{
    public double Roll;
    public double Pitch;
    public double Yaw;
    public long TimestampMs;
}

/// <summary>Hardware status acknowledgment</summary>
public struct HardwareStatusMessage
{
    public bool AutosteerEngaged;
    public bool SectionsEnabled;
    public byte ErrorFlags;
    public long TimestampMs;
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
