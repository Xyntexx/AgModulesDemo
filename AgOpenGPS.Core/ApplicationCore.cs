namespace AgOpenGPS.Core;

using AgOpenGPS.ModuleContracts;
using AgOpenGPS.ModuleContracts.Messages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

/// <summary>
/// Main application kernel - manages module lifecycle with hot reload support
/// </summary>
public class ApplicationCore : IDisposable
{
    private readonly IServiceProvider _services;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ApplicationCore> _logger;
    private readonly MessageBus _messageBus;
    private readonly ITimeProvider _timeProvider;
    private readonly ModuleManager _moduleManager;
    private readonly CancellationTokenSource _shutdownCts = new();
    private volatile bool _disposed;

    public ApplicationCore(
        IServiceProvider services,
        IConfiguration configuration,
        ILogger<ApplicationCore> logger,
        MessageBus messageBus,
        ITimeProvider timeProvider)
    {
        _services = services;
        _configuration = configuration;
        _logger = logger;
        _messageBus = messageBus;
        _timeProvider = timeProvider;
        _moduleManager = new ModuleManager(
            _services,
            _configuration,
            _services.GetRequiredService<ILogger<ModuleManager>>(),
            _messageBus,
            _timeProvider
        );
    }

    /// <summary>
    /// Get access to module manager for runtime operations
    /// </summary>
    public ModuleManager ModuleManager => _moduleManager;

    public async Task StartAsync()
    {
        _logger.LogInformation("AgOpenGPS Core starting...");

        // 1. Discover modules
        var moduleDir = _configuration.GetValue<string>("Core:ModuleDirectory") ?? "./modules";
        var loader = new ModuleLoader(moduleDir, _services.GetRequiredService<ILogger<ModuleLoader>>());
        var modules = loader.DiscoverModules();

        // 2. Resolve load order
        var orderedModules = loader.ResolveLoadOrder(modules);

        // 3. Load all modules using ModuleManager
        foreach (var module in orderedModules)
        {
            var result = await _moduleManager.LoadModuleAsync(module);
            if (!result.Success)
            {
                _logger.LogError($"Failed to load module {module.Name}: {result.ErrorMessage}");
                if (result.MissingDependencies != null)
                {
                    _logger.LogError($"Missing dependencies: {string.Join(", ", result.MissingDependencies)}");
                }
            }
        }

        // 4. Publish application started event
        _messageBus.Publish(new ApplicationStartedEvent
        {
            Timestamp = TimestampMetadata.Create(_timeProvider, 0, null)
        });

        var loadedCount = _moduleManager.GetLoadedModules().Count;
        _logger.LogInformation($"AgOpenGPS Core started successfully with {loadedCount} modules");
    }

    public async Task StopAsync()
    {
        _logger.LogInformation("AgOpenGPS Core stopping...");

        // Signal shutdown
        _shutdownCts.Cancel();

        _messageBus.Publish(new ApplicationStoppingEvent
        {
            Timestamp = TimestampMetadata.Create(_timeProvider, 0, null)
        });

        // Shutdown all modules via ModuleManager
        await _moduleManager.ShutdownAllAsync();

        _logger.LogInformation("AgOpenGPS Core stopped");
    }

    /// <summary>
    /// Runtime module management - load a new module
    /// </summary>
    public async Task<ModuleLoadResult> LoadModuleAsync(IAgModule module)
    {
        return await _moduleManager.LoadModuleAsync(module);
    }

    /// <summary>
    /// Runtime module management - unload a module
    /// </summary>
    public async Task<ModuleUnloadResult> UnloadModuleAsync(string moduleId)
    {
        return await _moduleManager.UnloadModuleAsync(moduleId);
    }

    /// <summary>
    /// Runtime module management - reload a module
    /// </summary>
    public async Task<ModuleReloadResult> ReloadModuleAsync(string moduleId)
    {
        return await _moduleManager.ReloadModuleAsync(moduleId);
    }

    /// <summary>
    /// Get information about all loaded modules
    /// </summary>
    public IReadOnlyList<ModuleInfo> GetLoadedModules()
    {
        return _moduleManager.GetLoadedModules();
    }

    /// <summary>
    /// Perform health check on all modules
    /// </summary>
    public async Task<HealthCheckResult> PerformHealthCheckAsync()
    {
        return await _moduleManager.PerformHealthCheckAsync();
    }

    /// <summary>
    /// Get memory usage information for a specific module
    /// </summary>
    public ModuleMemoryInfo GetModuleMemoryInfo(string moduleId)
    {
        return _moduleManager.GetModuleMemoryInfo(moduleId);
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        _moduleManager.Dispose();
        _messageBus.Dispose();
        _shutdownCts.Dispose();
    }
}
