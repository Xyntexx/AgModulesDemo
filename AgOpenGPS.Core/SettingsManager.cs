namespace AgOpenGPS.Core;

using System.Text.Json;
using AgOpenGPS.ModuleContracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

/// <summary>
/// Manages module settings, loading from configuration and coordinating updates
/// </summary>
public class SettingsManager
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<SettingsManager> _logger;
    private readonly Dictionary<string, IConfigurableModule> _configurablePlugins = new();

    public SettingsManager(IConfiguration configuration, ILogger<SettingsManager> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Register a module that supports configuration
    /// </summary>
    public void RegisterPlugin(IConfigurableModule plugin)
    {
        _configurablePlugins[plugin.Name] = plugin;
        _logger.LogDebug($"Registered configurable plugin: {plugin.Name}");

        // Try to load settings from configuration
        LoadSettingsFromConfiguration(plugin);
    }

    /// <summary>
    /// Load settings from appsettings.json for a plugin
    /// </summary>
    private void LoadSettingsFromConfiguration(IConfigurableModule plugin)
    {
        try
        {
            var settings = plugin.GetSettings();
            var settingsSection = _configuration.GetSection($"Plugins:{settings.SettingsId}");

            if (settingsSection.Exists())
            {
                // Deserialize settings from configuration
                var settingsType = settings.GetType();
                var loadedSettings = settingsSection.Get(settingsType) as IModuleSettings;

                if (loadedSettings != null)
                {
                    if (loadedSettings.Validate(out var errorMessage))
                    {
                        plugin.UpdateSettings(loadedSettings);
                        _logger.LogInformation($"Loaded settings for {plugin.Name} from configuration");
                    }
                    else
                    {
                        _logger.LogWarning($"Invalid settings in configuration for {plugin.Name}: {errorMessage}");
                    }
                }
                else
                {
                    _logger.LogWarning($"Failed to load settings for {plugin.Name} from configuration");
                }
            }
            else
            {
                _logger.LogDebug($"No configuration found for {plugin.Name}, using defaults");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error loading settings for {plugin.Name}");
        }
    }

    /// <summary>
    /// Get settings for a specific plugin
    /// </summary>
    public IModuleSettings? GetPluginSettings(string pluginName)
    {
        if (_configurablePlugins.TryGetValue(pluginName, out var plugin))
        {
            return plugin.GetSettings();
        }
        return null;
    }

    /// <summary>
    /// Update settings for a specific plugin
    /// </summary>
    public bool UpdatePluginSettings(string pluginName, IModuleSettings settings)
    {
        if (_configurablePlugins.TryGetValue(pluginName, out var plugin))
        {
            string? errorMessage;
            if (settings.Validate(out errorMessage))
            {
                plugin.UpdateSettings(settings);
                _logger.LogInformation($"Updated settings for {pluginName}");
                return true;
            }
            else
            {
                _logger.LogError($"Invalid settings for {pluginName}: {errorMessage}");
                return false;
            }
        }

        _logger.LogWarning($"Module {pluginName} not found or not configurable");
        return false;
    }

    /// <summary>
    /// Get all configurable plugins
    /// </summary>
    public IReadOnlyDictionary<string, IConfigurableModule> GetConfigurablePlugins()
    {
        return _configurablePlugins;
    }

    /// <summary>
    /// Export all current settings to JSON
    /// </summary>
    public string ExportAllSettings()
    {
        var allSettings = new Dictionary<string, object>();

        foreach (var kvp in _configurablePlugins)
        {
            allSettings[kvp.Key] = kvp.Value.GetSettings();
        }

        return JsonSerializer.Serialize(allSettings, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }
}
