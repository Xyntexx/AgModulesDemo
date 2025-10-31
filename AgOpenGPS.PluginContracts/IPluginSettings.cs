namespace AgOpenGPS.PluginContracts;

/// <summary>
/// Base interface for plugin settings
/// Plugins can implement this to expose configurable settings
/// </summary>
public interface IPluginSettings
{
    /// <summary>
    /// Unique identifier for the settings (typically matches plugin name)
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
/// Interface for plugins that support runtime configuration
/// </summary>
public interface IConfigurablePlugin : IAgPlugin
{
    /// <summary>
    /// Get the settings object for this plugin
    /// </summary>
    IPluginSettings GetSettings();

    /// <summary>
    /// Update settings from external source (e.g., UI)
    /// </summary>
    void UpdateSettings(IPluginSettings settings);
}
