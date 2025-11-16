namespace AgOpenGPS.Core;

using System.Collections.Concurrent;
using AgOpenGPS.ModuleContracts;
using AgOpenGPS.ModuleContracts.Messages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

/// <summary>
/// Thread-safe module lifecycle manager with support for hot reload
/// </summary>
public class ModuleManager : IDisposable
{
    private readonly IServiceProvider _services;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ModuleManager> _logger;
    private readonly MessageBus _messageBus;
    private readonly ITimeProvider _timeProvider;
    private readonly IScheduler? _scheduler;
    private readonly ConcurrentDictionary<string, ModuleRegistration> _modules = new();
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly ModuleTaskScheduler _taskScheduler;
    private readonly ModuleWatchdog _watchdog;
    private readonly ModuleMemoryMonitor _memoryMonitor;
    private volatile bool _disposed;

    public ModuleManager(
        IServiceProvider services,
        IConfiguration configuration,
        ILogger<ModuleManager> logger,
        MessageBus messageBus,
        ITimeProvider timeProvider,
        IScheduler? scheduler = null)
    {
        _services = services;
        _configuration = configuration;
        _logger = logger;
        _messageBus = messageBus;
        _timeProvider = timeProvider;
        _scheduler = scheduler;
        _taskScheduler = new ModuleTaskScheduler(services.GetRequiredService<ILogger<ModuleTaskScheduler>>());
        _watchdog = new ModuleWatchdog(services.GetRequiredService<ILogger<ModuleWatchdog>>());
        _memoryMonitor = new ModuleMemoryMonitor(
            services.GetRequiredService<ILogger<ModuleMemoryMonitor>>(),
            maxMemoryPerModuleMB: 500,
            globalMemoryWarningThresholdMB: 2048);

        // Subscribe to monitoring events
        _watchdog.ModuleHangDetected += OnModuleHangDetected;
        _memoryMonitor.ModuleMemoryExceeded += OnModuleMemoryExceeded;
    }

    private void OnModuleHangDetected(object? sender, ModuleHangDetectedEventArgs e)
    {
        _logger.LogCritical(
            $"MODULE HANG DETECTED: {e.ModuleId} - Operation '{e.OperationName}' running for {e.Duration.TotalSeconds:F1}s on thread {e.ThreadId}");

        // Optionally auto-reload the hanging module
        // await ReloadModuleAsync(e.ModuleId);
    }

    private void OnModuleMemoryExceeded(object? sender, ModuleMemoryExceededEventArgs e)
    {
        _logger.LogWarning(
            "MODULE MEMORY LIMIT EXCEEDED: {ModuleId} - Estimated memory {EstimatedMB}MB exceeds limit {LimitMB}MB. " +
            "Warning count: {WarningCount}",
            e.ModuleId, e.EstimatedMemoryMB, e.LimitMB, e.WarningCount);

        // After 3 warnings, consider reloading the module
        if (e.WarningCount >= 3)
        {
            _logger.LogError(
                "Module {ModuleId} has exceeded memory limit {Count} times. Consider manual intervention.",
                e.ModuleId, e.WarningCount);
        }
    }

    /// <summary>
    /// Load a module and initialize it
    /// </summary>
    public async Task<ModuleLoadResult> LoadModuleAsync(IAgModule module, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        await _lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            var moduleId = GetModuleId(module);

            // Check if already loaded
            if (_modules.ContainsKey(moduleId))
            {
                _logger.LogWarning($"Module {module.Name} is already loaded");
                return ModuleLoadResult.AlreadyLoaded(moduleId);
            }

            // Check dependencies
            var missingDeps = CheckDependencies(module);
            if (missingDeps.Any())
            {
                _logger.LogError($"Module {module.Name} has missing dependencies: {string.Join(", ", missingDeps)}");
                return ModuleLoadResult.CreateMissingDependencies(moduleId, missingDeps);
            }

            var registration = new ModuleRegistration
            {
                Module = module,
                ModuleId = moduleId,
                State = ModuleState.Loading,
                LoadedAt = DateTimeOffset.UtcNow
            };

            try
            {
                // Create module context with scoped message bus
                var context = new ScopedModuleContext(
                    _messageBus,
                    moduleId,
                    _services,
                    _configuration,
                    _logger,
                    _timeProvider,
                    _shutdownCts.Token,
                    _scheduler);

                registration.Context = context;

                _logger.LogInformation($"Initializing module: {module.Name} v{module.Version}");

                // Initialize with comprehensive exception handling on dedicated thread
                registration.State = ModuleState.Initializing;
                var initResult = await _taskScheduler.ExecuteOnModuleThreadAsync(
                    moduleId,
                    async () =>
                    {
                        using var monitor = _watchdog.MonitorOperation(moduleId, "InitializeAsync");
                        return await SafeModuleExecutor.ExecuteWithTimeoutAsync(
                            () => module.InitializeAsync(context),
                            TimeSpan.FromSeconds(30),
                            "InitializeAsync",
                            module.Name,
                            _logger);
                    });

                if (!initResult.IsSuccess)
                {
                    throw new InvalidOperationException($"Module initialization failed: {initResult.ErrorMessage}");
                }

                // Register with memory monitor before starting
                _memoryMonitor.RegisterModule(moduleId);

                // Start with comprehensive exception handling on dedicated thread
                registration.State = ModuleState.Starting;
                var startResult = await _taskScheduler.ExecuteOnModuleThreadAsync(
                    moduleId,
                    async () =>
                    {
                        using var monitor = _watchdog.MonitorOperation(moduleId, "StartAsync");
                        return await SafeModuleExecutor.ExecuteWithTimeoutAsync(
                            () => module.StartAsync(),
                            TimeSpan.FromSeconds(30),
                            "StartAsync",
                            module.Name,
                            _logger);
                    });

                if (!startResult.IsSuccess)
                {
                    throw new InvalidOperationException($"Module start failed: {startResult.ErrorMessage}");
                }

                registration.State = ModuleState.Running;
                registration.LastHealthCheck = DateTimeOffset.UtcNow;

                _modules[moduleId] = registration;

                _logger.LogInformation($"Module {module.Name} loaded successfully");

                // Publish module loaded event
                _messageBus.Publish(new ModuleLoadedEvent
                {
                    ModuleId = moduleId,
                    ModuleName = module.Name,
                    Version = module.Version.ToString(),
                    Timestamp = TimestampMetadata.Create(_timeProvider, 0, null)
                });

                return ModuleLoadResult.CreateSuccess(moduleId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to load module {module.Name}");
                registration.State = ModuleState.Failed;
                registration.LastError = ex.Message;

                // Cleanup on failure
                if (registration.Context != null)
                {
                    _messageBus.UnsubscribeScope(moduleId);
                }

                return ModuleLoadResult.Failed(moduleId, ex.Message);
            }
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    /// <summary>
    /// Unload a module by ID
    /// </summary>
    public async Task<ModuleUnloadResult> UnloadModuleAsync(string moduleId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        await _lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            if (!_modules.TryGetValue(moduleId, out var registration))
            {
                _logger.LogWarning($"Module {moduleId} not found");
                return ModuleUnloadResult.NotFound(moduleId);
            }

            // Check if other modules depend on this one
            var dependents = GetDependentModules(moduleId);
            if (dependents.Any())
            {
                _logger.LogError($"Cannot unload {moduleId}, other modules depend on it: {string.Join(", ", dependents)}");
                return ModuleUnloadResult.HasDependents(moduleId, dependents);
            }

            try
            {
                _logger.LogInformation($"Unloading module: {registration.Module.Name}");

                // Stop with comprehensive exception handling and timeout
                registration.State = ModuleState.Stopping;
                var stopResult = await SafeModuleExecutor.ExecuteWithTimeoutAsync(
                    () => registration.Module.StopAsync(),
                    TimeSpan.FromSeconds(10),
                    "StopAsync",
                    registration.Module.Name,
                    _logger);

                if (!stopResult.IsSuccess && !stopResult.IsCancelled)
                {
                    _logger.LogWarning($"Module {registration.Module.Name} stop had issues: {stopResult.ErrorMessage}");
                }

                // Shutdown with comprehensive exception handling and timeout
                registration.State = ModuleState.ShuttingDown;
                var shutdownResult = await SafeModuleExecutor.ExecuteWithTimeoutAsync(
                    () => registration.Module.ShutdownAsync(),
                    TimeSpan.FromSeconds(10),
                    "ShutdownAsync",
                    registration.Module.Name,
                    _logger);

                if (!shutdownResult.IsSuccess && !shutdownResult.IsCancelled)
                {
                    _logger.LogWarning($"Module {registration.Module.Name} shutdown had issues: {shutdownResult.ErrorMessage}");
                }

                // Cleanup message bus subscriptions
                _messageBus.UnsubscribeScope(moduleId);

                // Cleanup monitoring and task scheduler
                _watchdog.StopMonitoring(moduleId);
                _taskScheduler.CleanupModule(moduleId);
                _memoryMonitor.UnregisterModule(moduleId);

                registration.State = ModuleState.Unloaded;

                _modules.TryRemove(moduleId, out _);

                _logger.LogInformation($"Module {registration.Module.Name} unloaded successfully");

                // Publish module unloaded event
                _messageBus.Publish(new ModuleUnloadedEvent
                {
                    ModuleId = moduleId,
                    ModuleName = registration.Module.Name,
                    Timestamp = TimestampMetadata.Create(_timeProvider, 0, null)
                });

                return ModuleUnloadResult.CreateSuccess(moduleId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error unloading module {moduleId}");
                registration.State = ModuleState.Failed;
                registration.LastError = ex.Message;
                return ModuleUnloadResult.Failed(moduleId, ex.Message);
            }
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    /// <summary>
    /// Reload a module (unload then load)
    /// </summary>
    public async Task<ModuleReloadResult> ReloadModuleAsync(string moduleId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (!_modules.TryGetValue(moduleId, out var registration))
        {
            return ModuleReloadResult.NotFound(moduleId);
        }

        var module = registration.Module;

        var unloadResult = await UnloadModuleAsync(moduleId, cancellationToken);
        if (!unloadResult.Success)
        {
            return ModuleReloadResult.UnloadFailed(moduleId, unloadResult.ErrorMessage);
        }

        var loadResult = await LoadModuleAsync(module, cancellationToken);
        if (!loadResult.Success)
        {
            return ModuleReloadResult.LoadFailed(moduleId, loadResult.ErrorMessage);
        }

        return ModuleReloadResult.CreateSuccess(moduleId);
    }

    /// <summary>
    /// Get module state
    /// </summary>
    public ModuleState? GetModuleState(string moduleId)
    {
        return _modules.TryGetValue(moduleId, out var reg) ? reg.State : null;
    }

    /// <summary>
    /// Get all loaded modules
    /// </summary>
    public IReadOnlyList<ModuleInfo> GetLoadedModules()
    {
        return _modules.Values.Select(r => new ModuleInfo
        {
            ModuleId = r.ModuleId,
            Name = r.Module.Name,
            Version = r.Module.Version.ToString(),
            Category = r.Module.Category,
            State = r.State,
            Health = r.Module.GetHealth(),
            LoadedAt = r.LoadedAt,
            LastHealthCheck = r.LastHealthCheck,
            LastError = r.LastError
        }).ToList();
    }

    /// <summary>
    /// Perform health check on all modules
    /// </summary>
    public async Task<HealthCheckResult> PerformHealthCheckAsync()
    {
        var results = new List<ModuleHealthInfo>();

        foreach (var reg in _modules.Values)
        {
            // Use safe executor with timeout for health checks
            var healthResult = await SafeModuleExecutor.ExecuteWithTimeoutAsync(
                async () =>
                {
                    var health = await Task.Run(() => reg.Module.GetHealth());
                    reg.LastHealthCheck = DateTimeOffset.UtcNow;

                    results.Add(new ModuleHealthInfo
                    {
                        ModuleId = reg.ModuleId,
                        ModuleName = reg.Module.Name,
                        Health = health,
                        State = reg.State
                    });

                    if (health == ModuleHealth.Unhealthy)
                    {
                        _logger.LogWarning($"Module {reg.Module.Name} reported unhealthy status");
                    }
                },
                TimeSpan.FromSeconds(5),
                "GetHealth",
                reg.Module.Name,
                _logger);

            if (!healthResult.IsSuccess)
            {
                _logger.LogError($"Error checking health of module {reg.Module.Name}: {healthResult.ErrorMessage}");
                results.Add(new ModuleHealthInfo
                {
                    ModuleId = reg.ModuleId,
                    ModuleName = reg.Module.Name,
                    Health = ModuleHealth.Unknown,
                    State = reg.State,
                    Error = healthResult.ErrorMessage
                });
            }
        }

        return new HealthCheckResult
        {
            CheckedAt = DateTimeOffset.UtcNow,
            ModuleHealths = results
        };
    }

    /// <summary>
    /// Shutdown all modules
    /// </summary>
    public async Task ShutdownAllAsync()
    {
        _shutdownCts.Cancel();

        var modulesToShutdown = _modules.Values
            .OrderByDescending(r => (int)r.Module.Category) // Reverse category order
            .ToList();

        foreach (var reg in modulesToShutdown)
        {
            try
            {
                await UnloadModuleAsync(reg.ModuleId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error shutting down module {reg.Module.Name}");
            }
        }
    }

    private string GetModuleId(IAgModule module)
    {
        return $"{module.Name}:{module.Version}".Replace(" ", "_");
    }

    private List<string> CheckDependencies(IAgModule module)
    {
        var missing = new List<string>();

        foreach (var dep in module.Dependencies)
        {
            var found = _modules.Values.Any(r =>
                r.Module.Name.Equals(dep, StringComparison.OrdinalIgnoreCase) &&
                r.State == ModuleState.Running);

            if (!found)
            {
                missing.Add(dep);
            }
        }

        return missing;
    }

    private List<string> GetDependentModules(string moduleId)
    {
        if (!_modules.TryGetValue(moduleId, out var targetReg))
        {
            return new List<string>();
        }

        return _modules.Values
            .Where(r => r.Module.Dependencies.Any(d =>
                d.Equals(targetReg.Module.Name, StringComparison.OrdinalIgnoreCase)))
            .Select(r => r.ModuleId)
            .ToList();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ModuleManager));
        }
    }

    /// <summary>
    /// Get task scheduler statistics
    /// </summary>
    public Dictionary<string, ModuleThreadPoolStats> GetTaskSchedulerStats()
    {
        return _taskScheduler.GetStatistics();
    }

    /// <summary>
    /// Get watchdog monitoring statistics
    /// </summary>
    public List<ModuleMonitorStats> GetWatchdogStats()
    {
        return _watchdog.GetStatistics();
    }

    /// <summary>
    /// Get memory usage information for a specific module
    /// </summary>
    public ModuleMemoryInfo GetModuleMemoryInfo(string moduleId)
    {
        return _memoryMonitor.GetModuleMemoryInfo(moduleId);
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        _shutdownCts.Cancel();
        _watchdog.Dispose();
        _memoryMonitor.Dispose();
        _lifecycleLock.Dispose();
        _shutdownCts.Dispose();
    }
}

/// <summary>
/// Scoped module context that tracks subscriptions
/// </summary>
internal class ScopedModuleContext : IModuleContext
{
    private readonly MessageBus _messageBus;
    private readonly string _scope;

    public ScopedModuleContext(
        MessageBus messageBus,
        string scope,
        IServiceProvider services,
        IConfiguration configuration,
        ILogger logger,
        ITimeProvider timeProvider,
        CancellationToken appShutdownToken,
        IScheduler? scheduler = null)
    {
        _messageBus = messageBus;
        _scope = scope;
        Services = services;
        Configuration = configuration;
        Logger = logger;
        TimeProvider = timeProvider;
        AppShutdownToken = appShutdownToken;
        Scheduler = scheduler;
    }

    public IMessageBus MessageBus => new ScopedMessageBus(_messageBus, _scope);
    public IServiceProvider Services { get; }
    public IConfiguration Configuration { get; }
    public ILogger Logger { get; }
    public ITimeProvider TimeProvider { get; }
    public CancellationToken AppShutdownToken { get; }
    public IScheduler? Scheduler { get; }

    public IMessageQueue CreateMessageQueue()
    {
        return new MessageQueue();
    }
}

/// <summary>
/// Message bus wrapper that automatically scopes subscriptions
/// </summary>
internal class ScopedMessageBus : IMessageBus
{
    private readonly MessageBus _innerBus;
    private readonly string _scope;

    public ScopedMessageBus(MessageBus innerBus, string scope)
    {
        _innerBus = innerBus;
        _scope = scope;
    }

    public IDisposable Subscribe<T>(Action<T> handler) where T : struct
    {
        return _innerBus.Subscribe(handler, _scope, priority: 0);
    }

    public IDisposable Subscribe<T>(Action<T> handler, int priority) where T : struct
    {
        return _innerBus.Subscribe(handler, _scope, priority);
    }

    public IDisposable SubscribeQueued<T>(Action<T> handler, IMessageQueue queue) where T : struct
    {
        return _innerBus.SubscribeQueued(handler, queue);
    }

    public void Publish<T>(in T message) where T : struct
    {
        _innerBus.Publish(in message);
    }

    public Task PublishAsync<T>(T message) where T : struct
    {
        return _innerBus.PublishAsync(message);
    }

    public bool TryGetLastMessage<T>(out T message, out DateTimeOffset timestamp) where T : struct
    {
        return _innerBus.TryGetLastMessage(out message, out timestamp);
    }
}

// Module lifecycle state
public enum ModuleState
{
    Loading,
    Initializing,
    Starting,
    Running,
    Stopping,
    ShuttingDown,
    Unloaded,
    Failed
}

// Module registration info
internal class ModuleRegistration
{
    public required IAgModule Module { get; set; }
    public required string ModuleId { get; set; }
    public ModuleState State { get; set; }
    public ScopedModuleContext? Context { get; set; }
    public DateTimeOffset LoadedAt { get; set; }
    public DateTimeOffset LastHealthCheck { get; set; }
    public string? LastError { get; set; }
}

// Public module info
public class ModuleInfo
{
    public required string ModuleId { get; set; }
    public required string Name { get; set; }
    public required string Version { get; set; }
    public ModuleCategory Category { get; set; }
    public ModuleState State { get; set; }
    public ModuleHealth Health { get; set; }
    public DateTimeOffset LoadedAt { get; set; }
    public DateTimeOffset LastHealthCheck { get; set; }
    public string? LastError { get; set; }
}

// Result types
public class ModuleLoadResult
{
    public bool Success { get; set; }
    public required string ModuleId { get; set; }
    public string? ErrorMessage { get; set; }
    public List<string>? MissingDependencies { get; set; }

    public static ModuleLoadResult CreateSuccess(string moduleId) =>
        new() { Success = true, ModuleId = moduleId };

    public static ModuleLoadResult AlreadyLoaded(string moduleId) =>
        new() { Success = false, ModuleId = moduleId, ErrorMessage = "Already loaded" };

    public static ModuleLoadResult Failed(string moduleId, string error) =>
        new() { Success = false, ModuleId = moduleId, ErrorMessage = error };

    public static ModuleLoadResult CreateMissingDependencies(string moduleId, List<string> deps) =>
        new() { Success = false, ModuleId = moduleId, MissingDependencies = deps, ErrorMessage = "Missing dependencies" };
}

public class ModuleUnloadResult
{
    public bool Success { get; set; }
    public required string ModuleId { get; set; }
    public string? ErrorMessage { get; set; }
    public List<string>? DependentModules { get; set; }

    public static ModuleUnloadResult CreateSuccess(string moduleId) =>
        new() { Success = true, ModuleId = moduleId };

    public static ModuleUnloadResult NotFound(string moduleId) =>
        new() { Success = false, ModuleId = moduleId, ErrorMessage = "Module not found" };

    public static ModuleUnloadResult Failed(string moduleId, string error) =>
        new() { Success = false, ModuleId = moduleId, ErrorMessage = error };

    public static ModuleUnloadResult HasDependents(string moduleId, List<string> dependents) =>
        new() { Success = false, ModuleId = moduleId, DependentModules = dependents, ErrorMessage = "Has dependent modules" };
}

public class ModuleReloadResult
{
    public bool Success { get; set; }
    public required string ModuleId { get; set; }
    public string? ErrorMessage { get; set; }

    public static ModuleReloadResult CreateSuccess(string moduleId) =>
        new() { Success = true, ModuleId = moduleId };

    public static ModuleReloadResult NotFound(string moduleId) =>
        new() { Success = false, ModuleId = moduleId, ErrorMessage = "Module not found" };

    public static ModuleReloadResult UnloadFailed(string moduleId, string? error) =>
        new() { Success = false, ModuleId = moduleId, ErrorMessage = $"Unload failed: {error}" };

    public static ModuleReloadResult LoadFailed(string moduleId, string? error) =>
        new() { Success = false, ModuleId = moduleId, ErrorMessage = $"Load failed: {error}" };
}

public class HealthCheckResult
{
    public DateTimeOffset CheckedAt { get; set; }
    public required List<ModuleHealthInfo> ModuleHealths { get; set; }
}

public class ModuleHealthInfo
{
    public required string ModuleId { get; set; }
    public required string ModuleName { get; set; }
    public ModuleHealth Health { get; set; }
    public ModuleState State { get; set; }
    public string? Error { get; set; }
}
