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
    private readonly RateScheduler? _scheduler;
    private readonly CancellationTokenSource _shutdownCts = new();
    private volatile bool _disposed;
    private readonly bool _useScheduler;

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

        // Check if scheduler is enabled and create it first
        _useScheduler = _configuration.GetValue<bool>("Core:UseScheduler", true);

        if (_useScheduler)
        {
            var baseTickRateHz = _configuration.GetValue<double>("Core:SchedulerBaseRateHz", 100.0);
            _scheduler = new RateScheduler(
                baseTickRateHz,
                _timeProvider,
                _services.GetRequiredService<ILogger<RateScheduler>>());

            _logger.LogInformation("Rate scheduler enabled with base rate {BaseRate}Hz", baseTickRateHz);
        }
        else
        {
            _logger.LogInformation("Rate scheduler disabled - modules will use free-running execution");
        }

        // Create module manager with optional scheduler
        _moduleManager = new ModuleManager(
            _services,
            _configuration,
            _services.GetRequiredService<ILogger<ModuleManager>>(),
            _messageBus,
            _timeProvider,
            _scheduler
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

        // 4. Start scheduler if enabled
        if (_scheduler != null)
        {
            _scheduler.Start();
            _logger.LogInformation("Rate scheduler started");
        }

        // 5. Publish application started event
        _messageBus.Publish(new ApplicationStartedEvent
        {
            Timestamp = TimestampMetadata.Create(_timeProvider, 0, null)
        });

        var loadedCount = _moduleManager.GetLoadedModules().Count;
        if (_scheduler != null)
        {
            var stats = _scheduler.GetStatistics();
            _logger.LogInformation($"AgOpenGPS Core started successfully with {loadedCount} modules ({stats.ScheduledMethodCount} scheduled methods)");
        }
        else
        {
            _logger.LogInformation($"AgOpenGPS Core started successfully with {loadedCount} modules");
        }
    }

    public async Task StopAsync()
    {
        _logger.LogInformation("AgOpenGPS Core stopping...");

        // Stop scheduler first
        if (_scheduler != null)
        {
            _scheduler.Stop();
            _logger.LogInformation("Rate scheduler stopped");
        }

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

    /// <summary>
    /// Get scheduler statistics (if scheduler is enabled)
    /// </summary>
    public SchedulerStatistics? GetSchedulerStatistics()
    {
        return _scheduler?.GetStatistics();
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        _scheduler?.Dispose();
        _moduleManager.Dispose();
        _messageBus.Dispose();
        _shutdownCts.Dispose();
    }
}
