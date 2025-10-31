namespace AgOpenGPS.Core;

using System.Text.Json;
using AgOpenGPS.PluginContracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

/// <summary>
/// Manages plugin settings, loading from configuration and coordinating updates
/// </summary>
public class SettingsManager
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<SettingsManager> _logger;
    private readonly Dictionary<string, IConfigurablePlugin> _configurablePlugins = new();

    public SettingsManager(IConfiguration configuration, ILogger<SettingsManager> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Register a plugin that supports configuration
    /// </summary>
    public void RegisterPlugin(IConfigurablePlugin plugin)
    {
        _configurablePlugins[plugin.Name] = plugin;
        _logger.LogDebug($"Registered configurable plugin: {plugin.Name}");

        // Try to load settings from configuration
        LoadSettingsFromConfiguration(plugin);
    }

    /// <summary>
    /// Load settings from appsettings.json for a plugin
    /// </summary>
    private void LoadSettingsFromConfiguration(IConfigurablePlugin plugin)
    {
        try
        {
            var settings = plugin.GetSettings();
            var settingsSection = _configuration.GetSection($"Plugins:{settings.SettingsId}");

            if (settingsSection.Exists())
            {
                // Deserialize settings from configuration
                var settingsType = settings.GetType();
                var loadedSettings = settingsSection.Get(settingsType) as IPluginSettings;

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
    public IPluginSettings? GetPluginSettings(string pluginName)
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
    public bool UpdatePluginSettings(string pluginName, IPluginSettings settings)
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

        _logger.LogWarning($"Plugin {pluginName} not found or not configurable");
        return false;
    }

    /// <summary>
    /// Get all configurable plugins
    /// </summary>
    public IReadOnlyDictionary<string, IConfigurablePlugin> GetConfigurablePlugins()
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
