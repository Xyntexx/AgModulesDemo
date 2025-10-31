namespace AgOpenGPS.ModuleContracts.Messages;

/// <summary>
/// Event published when a module is loaded at runtime
/// </summary>
public struct ModuleLoadedEvent
{
    public string ModuleId { get; set; }
    public string ModuleName { get; set; }
    public string Version { get; set; }
    public long TimestampMs { get; set; }
}

/// <summary>
/// Event published when a module is unloaded at runtime
/// </summary>
public struct ModuleUnloadedEvent
{
    public string ModuleId { get; set; }
    public string ModuleName { get; set; }
    public long TimestampMs { get; set; }
}

/// <summary>
/// Event published when a module is reloaded at runtime
/// </summary>
public struct ModuleReloadedEvent
{
    public string ModuleId { get; set; }
    public string ModuleName { get; set; }
    public long TimestampMs { get; set; }
}
