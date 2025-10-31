namespace AgOpenGPS.PluginContracts.Messages;

/// <summary>
/// Event published when a plugin is loaded at runtime
/// </summary>
public struct PluginLoadedEvent
{
    public string PluginId { get; set; }
    public string PluginName { get; set; }
    public string Version { get; set; }
    public long TimestampMs { get; set; }
}

/// <summary>
/// Event published when a plugin is unloaded at runtime
/// </summary>
public struct PluginUnloadedEvent
{
    public string PluginId { get; set; }
    public string PluginName { get; set; }
    public long TimestampMs { get; set; }
}

/// <summary>
/// Event published when a plugin is reloaded at runtime
/// </summary>
public struct PluginReloadedEvent
{
    public string PluginId { get; set; }
    public string PluginName { get; set; }
    public long TimestampMs { get; set; }
}
