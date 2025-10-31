namespace AgOpenGPS.Core;

using AgOpenGPS.PluginContracts;
using AgOpenGPS.PluginContracts.Messages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

/// <summary>
/// Main application kernel - manages plugin lifecycle with hot reload support
/// </summary>
public class ApplicationCore : IDisposable
{
    private readonly IServiceProvider _services;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ApplicationCore> _logger;
    private readonly MessageBus _messageBus;
    private readonly PluginManager _pluginManager;
    private readonly CancellationTokenSource _shutdownCts = new();
    private volatile bool _disposed;

    public ApplicationCore(
        IServiceProvider services,
        IConfiguration configuration,
        ILogger<ApplicationCore> logger,
        MessageBus messageBus)
    {
        _services = services;
        _configuration = configuration;
        _logger = logger;
        _messageBus = messageBus;
        _pluginManager = new PluginManager(
            _services,
            _configuration,
            _services.GetRequiredService<ILogger<PluginManager>>(),
            _messageBus
        );
    }

    /// <summary>
    /// Get access to plugin manager for runtime operations
    /// </summary>
    public PluginManager PluginManager => _pluginManager;

    public async Task StartAsync()
    {
        _logger.LogInformation("AgOpenGPS Core starting...");

        // 1. Discover plugins
        var pluginDir = _configuration.GetValue<string>("Core:PluginDirectory") ?? "./plugins";
        var loader = new PluginLoader(pluginDir, _services.GetRequiredService<ILogger<PluginLoader>>());
        var plugins = loader.DiscoverPlugins();

        // 2. Resolve load order
        var orderedPlugins = loader.ResolveLoadOrder(plugins);

        // 3. Load all plugins using PluginManager
        foreach (var plugin in orderedPlugins)
        {
            var result = await _pluginManager.LoadPluginAsync(plugin);
            if (!result.Success)
            {
                _logger.LogError($"Failed to load plugin {plugin.Name}: {result.ErrorMessage}");
                if (result.MissingDependencies != null)
                {
                    _logger.LogError($"Missing dependencies: {string.Join(", ", result.MissingDependencies)}");
                }
            }
        }

        // 4. Publish application started event
        _messageBus.Publish(new ApplicationStartedEvent
        {
            TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        });

        var loadedCount = _pluginManager.GetLoadedPlugins().Count;
        _logger.LogInformation($"AgOpenGPS Core started successfully with {loadedCount} plugins");
    }

    public async Task StopAsync()
    {
        _logger.LogInformation("AgOpenGPS Core stopping...");

        // Signal shutdown
        _shutdownCts.Cancel();

        _messageBus.Publish(new ApplicationStoppingEvent
        {
            TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        });

        // Shutdown all plugins via PluginManager
        await _pluginManager.ShutdownAllAsync();

        _logger.LogInformation("AgOpenGPS Core stopped");
    }

    /// <summary>
    /// Runtime plugin management - load a new plugin
    /// </summary>
    public async Task<PluginLoadResult> LoadPluginAsync(IAgPlugin plugin)
    {
        return await _pluginManager.LoadPluginAsync(plugin);
    }

    /// <summary>
    /// Runtime plugin management - unload a plugin
    /// </summary>
    public async Task<PluginUnloadResult> UnloadPluginAsync(string pluginId)
    {
        return await _pluginManager.UnloadPluginAsync(pluginId);
    }

    /// <summary>
    /// Runtime plugin management - reload a plugin
    /// </summary>
    public async Task<PluginReloadResult> ReloadPluginAsync(string pluginId)
    {
        return await _pluginManager.ReloadPluginAsync(pluginId);
    }

    /// <summary>
    /// Get information about all loaded plugins
    /// </summary>
    public IReadOnlyList<PluginInfo> GetLoadedPlugins()
    {
        return _pluginManager.GetLoadedPlugins();
    }

    /// <summary>
    /// Perform health check on all plugins
    /// </summary>
    public async Task<HealthCheckResult> PerformHealthCheckAsync()
    {
        return await _pluginManager.PerformHealthCheckAsync();
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        _pluginManager.Dispose();
        _messageBus.Dispose();
        _shutdownCts.Dispose();
    }
}
