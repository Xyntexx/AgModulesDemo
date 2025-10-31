namespace AgOpenGPS.PluginContracts;

/// <summary>
/// Base interface for all AgOpenGPS plugins
/// </summary>
public interface IAgPlugin
{
    /// <summary>Plugin name for display and logging</summary>
    string Name { get; }

    /// <summary>Plugin version</summary>
    Version Version { get; }

    /// <summary>Plugin category for load ordering</summary>
    PluginCategory Category { get; }

    /// <summary>Names of other plugins this depends on (loaded first)</summary>
    string[] Dependencies { get; }

    /// <summary>Initialize plugin with core services</summary>
    Task InitializeAsync(IPluginContext context);

    /// <summary>Start plugin processing (called after all plugins initialized)</summary>
    Task StartAsync();

    /// <summary>Stop plugin processing gracefully</summary>
    Task StopAsync();

    /// <summary>Cleanup resources</summary>
    Task ShutdownAsync();

    /// <summary>Get current health status</summary>
    PluginHealth GetHealth();
}

/// <summary>
/// Plugin categories for load ordering and organization
/// </summary>
public enum PluginCategory
{
    IO = 0,              // Serial, UDP, CAN - load first
    DataProcessing = 10,  // PGN parser, Kalman filter
    Navigation = 20,      // Guidance algorithms
    Control = 30,         // Autosteer, section control
    Visualization = 40,   // Mapping, UI
    Logging = 50,         // Data logging - load last
    Integration = 60      // External services
}

/// <summary>
/// Plugin health status
/// </summary>
public enum PluginHealth
{
    Healthy,
    Degraded,
    Unhealthy,
    Unknown
}
