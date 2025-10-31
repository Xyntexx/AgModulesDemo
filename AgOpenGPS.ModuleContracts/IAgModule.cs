namespace AgOpenGPS.ModuleContracts;

/// <summary>
/// Base interface for all AgOpenGPS modules
/// </summary>
public interface IAgModule
{
    /// <summary>Module name for display and logging</summary>
    string Name { get; }

    /// <summary>Module version</summary>
    Version Version { get; }

    /// <summary>Module category for load ordering</summary>
    ModuleCategory Category { get; }

    /// <summary>Names of other modules this depends on (loaded first)</summary>
    string[] Dependencies { get; }

    /// <summary>Initialize module with core services</summary>
    Task InitializeAsync(IModuleContext context);

    /// <summary>Start module processing (called after all modules initialized)</summary>
    Task StartAsync();

    /// <summary>Stop module processing gracefully</summary>
    Task StopAsync();

    /// <summary>Cleanup resources</summary>
    Task ShutdownAsync();

    /// <summary>Get current health status</summary>
    ModuleHealth GetHealth();
}

/// <summary>
/// Module categories for load ordering and organization
/// </summary>
public enum ModuleCategory
{
    IO = 0,              // Serial, UDP, CAN - load first
    DataProcessing = 10,  // PGN parser, Kalman filter
    Navigation = 20,      // Guidance algorithms
    Control = 30,         // Autosteer, section control
    Visualization = 40,   // Mapping, UI
    Logging = 50,         // Data logging - load last
    Integration = 60,     // External services
    Monitoring = 70       // Monitoring and diagnostics
}

/// <summary>
/// Module health status
/// </summary>
public enum ModuleHealth
{
    Healthy,
    Degraded,
    Unhealthy,
    Unknown
}
