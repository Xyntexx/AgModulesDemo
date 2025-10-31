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
    private readonly Dictionary<string, IConfigurableModule> _configurableModules = new();

    public SettingsManager(IConfiguration configuration, ILogger<SettingsManager> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Register a module that supports configuration
    /// </summary>
    public void RegisterModule(IConfigurableModule module)
    {
        _configurableModules[module.Name] = module;
        _logger.LogDebug($"Registered configurable module: {module.Name}");

        // Try to load settings from configuration
        LoadSettingsFromConfiguration(module);
    }

    /// <summary>
    /// Load settings from appsettings.json for a module
    /// </summary>
    private void LoadSettingsFromConfiguration(IConfigurableModule module)
    {
        try
        {
            var settings = module.GetSettings();
            var settingsSection = _configuration.GetSection($"Modules:{settings.SettingsId}");

            if (settingsSection.Exists())
            {
                // Deserialize settings from configuration
                var settingsType = settings.GetType();
                var loadedSettings = settingsSection.Get(settingsType) as IModuleSettings;

                if (loadedSettings != null)
                {
                    if (loadedSettings.Validate(out var errorMessage))
                    {
                        module.UpdateSettings(loadedSettings);
                        _logger.LogInformation($"Loaded settings for {module.Name} from configuration");
                    }
                    else
                    {
                        _logger.LogWarning($"Invalid settings in configuration for {module.Name}: {errorMessage}");
                    }
                }
                else
                {
                    _logger.LogWarning($"Failed to load settings for {module.Name} from configuration");
                }
            }
            else
            {
                _logger.LogDebug($"No configuration found for {module.Name}, using defaults");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error loading settings for {module.Name}");
        }
    }

    /// <summary>
    /// Get settings for a specific module
    /// </summary>
    public IModuleSettings? GetModuleSettings(string moduleName)
    {
        if (_configurableModules.TryGetValue(moduleName, out var module))
        {
            return module.GetSettings();
        }
        return null;
    }

    /// <summary>
    /// Update settings for a specific module
    /// </summary>
    public bool UpdateModuleSettings(string moduleName, IModuleSettings settings)
    {
        if (_configurableModules.TryGetValue(moduleName, out var module))
        {
            string? errorMessage;
            if (settings.Validate(out errorMessage))
            {
                module.UpdateSettings(settings);
                _logger.LogInformation($"Updated settings for {moduleName}");
                return true;
            }
            else
            {
                _logger.LogError($"Invalid settings for {moduleName}: {errorMessage}");
                return false;
            }
        }

        _logger.LogWarning($"Module {moduleName} not found or not configurable");
        return false;
    }

    /// <summary>
    /// Get all configurable modules
    /// </summary>
    public IReadOnlyDictionary<string, IConfigurableModule> GetConfigurableModules()
    {
        return _configurableModules;
    }

    /// <summary>
    /// Export all current settings to JSON
    /// </summary>
    public string ExportAllSettings()
    {
        var allSettings = new Dictionary<string, object>();

        foreach (var kvp in _configurableModules)
        {
            allSettings[kvp.Key] = kvp.Value.GetSettings();
        }

        return JsonSerializer.Serialize(allSettings, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }
}
