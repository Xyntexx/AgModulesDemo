namespace AgOpenGPS.ModuleContracts;

/// <summary>
/// Base interface for module settings
/// Modules can implement this to expose configurable settings
/// </summary>
public interface IModuleSettings
{
    /// <summary>
    /// Unique identifier for the settings (typically matches module name)
    /// </summary>
    string SettingsId { get; }

    /// <summary>
    /// Validate the current settings
    /// </summary>
    /// <returns>True if settings are valid, false otherwise</returns>
    bool Validate(out string? errorMessage);

    /// <summary>
    /// Apply settings changes (called when settings are updated from UI)
    /// </summary>
    void Apply();
}

/// <summary>
/// Interface for modules that support runtime configuration
/// </summary>
public interface IConfigurableModule : IAgModule
{
    /// <summary>
    /// Get the settings object for this module
    /// </summary>
    IModuleSettings GetSettings();

    /// <summary>
    /// Update settings from external source (e.g., UI)
    /// </summary>
    void UpdateSettings(IModuleSettings settings);
}
