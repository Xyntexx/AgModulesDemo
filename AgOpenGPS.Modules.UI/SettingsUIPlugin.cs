namespace AgOpenGPS.Modules.UI;

using System.Text.Json;
using AgOpenGPS.ModuleContracts;
using AgOpenGPS.ModuleContracts.Messages;
using Microsoft.Extensions.Logging;

/// <summary>
/// UI Plugin for managing settings across all configurable plugins
/// Provides a simple console interface for viewing and modifying settings
/// </summary>
public class SettingsUIPlugin : IAgModule
{
    public string Name => "Settings UI";
    public Version Version => new Version(1, 0, 0);
    public ModuleCategory Category => ModuleCategory.Visualization;
    public string[] Dependencies => Array.Empty<string>();

    private IMessageBus? _messageBus;
    private ILogger? _logger;
    private IModuleContext? _context;
    private readonly Dictionary<string, IConfigurableModule> _configurablePlugins = new();

    public Task InitializeAsync(IModuleContext context)
    {
        _messageBus = context.MessageBus;
        _logger = context.Logger;
        _context = context;

        _logger.LogInformation("Settings UI plugin initialized");
        return Task.CompletedTask;
    }

    public Task StartAsync()
    {
        _logger?.LogInformation("Settings UI started");
        _logger?.LogInformation("=====================================");
        _logger?.LogInformation("Settings UI Commands:");
        _logger?.LogInformation("  - Use SettingsManager API to get/set plugin settings");
        _logger?.LogInformation("=====================================");
        return Task.CompletedTask;
    }

    public Task StopAsync() => Task.CompletedTask;
    public Task ShutdownAsync() => Task.CompletedTask;
    public ModuleHealth GetHealth() => ModuleHealth.Healthy;

    /// <summary>
    /// Register a configurable plugin with the UI
    /// </summary>
    public void RegisterPlugin(IConfigurableModule plugin)
    {
        _configurablePlugins[plugin.Name] = plugin;
        _logger?.LogDebug($"Registered configurable plugin: {plugin.Name}");
    }

    /// <summary>
    /// Get all registered plugins with their settings
    /// </summary>
    public Dictionary<string, IModuleSettings> GetAllSettings()
    {
        var settings = new Dictionary<string, IModuleSettings>();
        foreach (var kvp in _configurablePlugins)
        {
            settings[kvp.Key] = kvp.Value.GetSettings();
        }
        return settings;
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
            if (settings.Validate(out var errorMessage))
            {
                plugin.UpdateSettings(settings);

                // Publish settings changed message
                if (_messageBus != null)
                {
                    var msg = new SettingsChangedMessage
                    {
                        ModuleName = pluginName,
                        SettingsJson = JsonSerializer.Serialize(settings),
                        TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    };
                    _messageBus.Publish(in msg);
                }

                _logger?.LogInformation($"Updated settings for {pluginName}");
                return true;
            }
            else
            {
                _logger?.LogError($"Invalid settings for {pluginName}: {errorMessage}");
                return false;
            }
        }

        _logger?.LogWarning($"Plugin {pluginName} not found or not configurable");
        return false;
    }

    /// <summary>
    /// Export all settings to JSON
    /// </summary>
    public string ExportSettingsToJson()
    {
        var allSettings = GetAllSettings();
        var export = new Dictionary<string, object>();

        foreach (var kvp in allSettings)
        {
            export[kvp.Key] = kvp.Value;
        }

        return JsonSerializer.Serialize(export, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }

    /// <summary>
    /// List all configurable plugins
    /// </summary>
    public List<string> ListConfigurablePlugins()
    {
        return _configurablePlugins.Keys.ToList();
    }
}
