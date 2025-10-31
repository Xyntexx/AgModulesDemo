namespace AgOpenGPS.PluginContracts.Messages;

/// <summary>Guidance line for autosteer to follow</summary>
public struct GuidanceLineMessage
{
    public double StartLatitude;
    public double StartLongitude;
    public double HeadingDegrees;
    public double OffsetMeters;
}

/// <summary>Field boundary definition</summary>
public struct FieldBoundaryMessage
{
    public (double Lat, double Lon)[] BoundaryPoints;
}

/// <summary>Application lifecycle events</summary>
public struct ApplicationStartedEvent
{
    public long TimestampMs;
}

/// <summary>Application stopping event</summary>
public struct ApplicationStoppingEvent
{
    public long TimestampMs;
}

/// <summary>Settings changed event</summary>
public struct SettingsChangedMessage
{
    public string PluginName;
    public string SettingsJson;
    public long TimestampMs;
}
