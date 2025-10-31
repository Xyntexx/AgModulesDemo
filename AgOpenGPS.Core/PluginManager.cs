namespace AgOpenGPS.Core;

using System.Collections.Concurrent;
using AgOpenGPS.PluginContracts;
using AgOpenGPS.PluginContracts.Messages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

/// <summary>
/// Thread-safe plugin lifecycle manager with support for hot reload
/// </summary>
public class PluginManager : IDisposable
{
    private readonly IServiceProvider _services;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PluginManager> _logger;
    private readonly MessageBus _messageBus;
    private readonly ConcurrentDictionary<string, PluginRegistration> _plugins = new();
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly PluginTaskScheduler _taskScheduler;
    private readonly PluginWatchdog _watchdog;
    private volatile bool _disposed;

    public PluginManager(
        IServiceProvider services,
        IConfiguration configuration,
        ILogger<PluginManager> logger,
        MessageBus messageBus)
    {
        _services = services;
        _configuration = configuration;
        _logger = logger;
        _messageBus = messageBus;
        _taskScheduler = new PluginTaskScheduler(services.GetRequiredService<ILogger<PluginTaskScheduler>>());
        _watchdog = new PluginWatchdog(services.GetRequiredService<ILogger<PluginWatchdog>>());

        // Subscribe to hang detection events
        _watchdog.PluginHangDetected += OnPluginHangDetected;
    }

    private void OnPluginHangDetected(object? sender, PluginHangDetectedEventArgs e)
    {
        _logger.LogCritical(
            $"PLUGIN HANG DETECTED: {e.PluginId} - Operation '{e.OperationName}' running for {e.Duration.TotalSeconds:F1}s on thread {e.ThreadId}");

        // Optionally auto-reload the hanging plugin
        // await ReloadPluginAsync(e.PluginId);
    }

    /// <summary>
    /// Load a plugin and initialize it
    /// </summary>
    public async Task<PluginLoadResult> LoadPluginAsync(IAgPlugin plugin, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        await _lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            var pluginId = GetPluginId(plugin);

            // Check if already loaded
            if (_plugins.ContainsKey(pluginId))
            {
                _logger.LogWarning($"Plugin {plugin.Name} is already loaded");
                return PluginLoadResult.AlreadyLoaded(pluginId);
            }

            // Check dependencies
            var missingDeps = CheckDependencies(plugin);
            if (missingDeps.Any())
            {
                _logger.LogError($"Plugin {plugin.Name} has missing dependencies: {string.Join(", ", missingDeps)}");
                return PluginLoadResult.CreateMissingDependencies(pluginId, missingDeps);
            }

            var registration = new PluginRegistration
            {
                Plugin = plugin,
                PluginId = pluginId,
                State = PluginState.Loading,
                LoadedAt = DateTimeOffset.UtcNow
            };

            try
            {
                // Create plugin context with scoped message bus
                var context = new ScopedPluginContext(
                    _messageBus,
                    pluginId,
                    _services,
                    _configuration,
                    _logger,
                    _shutdownCts.Token
                );

                registration.Context = context;

                _logger.LogInformation($"Initializing plugin: {plugin.Name} v{plugin.Version}");

                // Initialize with comprehensive exception handling on dedicated thread
                registration.State = PluginState.Initializing;
                var initResult = await _taskScheduler.ExecuteOnPluginThreadAsync(
                    pluginId,
                    async () =>
                    {
                        using var monitor = _watchdog.MonitorOperation(pluginId, "InitializeAsync");
                        return await SafePluginExecutor.ExecuteWithTimeoutAsync(
                            () => plugin.InitializeAsync(context),
                            TimeSpan.FromSeconds(30),
                            "InitializeAsync",
                            plugin.Name,
                            _logger);
                    });

                if (!initResult.IsSuccess)
                {
                    throw new InvalidOperationException($"Plugin initialization failed: {initResult.ErrorMessage}");
                }

                // Start with comprehensive exception handling on dedicated thread
                registration.State = PluginState.Starting;
                var startResult = await _taskScheduler.ExecuteOnPluginThreadAsync(
                    pluginId,
                    async () =>
                    {
                        using var monitor = _watchdog.MonitorOperation(pluginId, "StartAsync");
                        return await SafePluginExecutor.ExecuteWithTimeoutAsync(
                            () => plugin.StartAsync(),
                            TimeSpan.FromSeconds(30),
                            "StartAsync",
                            plugin.Name,
                            _logger);
                    });

                if (!startResult.IsSuccess)
                {
                    throw new InvalidOperationException($"Plugin start failed: {startResult.ErrorMessage}");
                }

                registration.State = PluginState.Running;
                registration.LastHealthCheck = DateTimeOffset.UtcNow;

                _plugins[pluginId] = registration;

                _logger.LogInformation($"Plugin {plugin.Name} loaded successfully");

                // Publish plugin loaded event
                _messageBus.Publish(new PluginLoadedEvent
                {
                    PluginId = pluginId,
                    PluginName = plugin.Name,
                    Version = plugin.Version.ToString(),
                    TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                });

                return PluginLoadResult.CreateSuccess(pluginId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to load plugin {plugin.Name}");
                registration.State = PluginState.Failed;
                registration.LastError = ex.Message;

                // Cleanup on failure
                if (registration.Context != null)
                {
                    _messageBus.UnsubscribeScope(pluginId);
                }

                return PluginLoadResult.Failed(pluginId, ex.Message);
            }
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    /// <summary>
    /// Unload a plugin by ID
    /// </summary>
    public async Task<PluginUnloadResult> UnloadPluginAsync(string pluginId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        await _lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            if (!_plugins.TryGetValue(pluginId, out var registration))
            {
                _logger.LogWarning($"Plugin {pluginId} not found");
                return PluginUnloadResult.NotFound(pluginId);
            }

            // Check if other plugins depend on this one
            var dependents = GetDependentPlugins(pluginId);
            if (dependents.Any())
            {
                _logger.LogError($"Cannot unload {pluginId}, other plugins depend on it: {string.Join(", ", dependents)}");
                return PluginUnloadResult.HasDependents(pluginId, dependents);
            }

            try
            {
                _logger.LogInformation($"Unloading plugin: {registration.Plugin.Name}");

                // Stop with comprehensive exception handling and timeout
                registration.State = PluginState.Stopping;
                var stopResult = await SafePluginExecutor.ExecuteWithTimeoutAsync(
                    () => registration.Plugin.StopAsync(),
                    TimeSpan.FromSeconds(10),
                    "StopAsync",
                    registration.Plugin.Name,
                    _logger);

                if (!stopResult.IsSuccess && !stopResult.IsCancelled)
                {
                    _logger.LogWarning($"Plugin {registration.Plugin.Name} stop had issues: {stopResult.ErrorMessage}");
                }

                // Shutdown with comprehensive exception handling and timeout
                registration.State = PluginState.ShuttingDown;
                var shutdownResult = await SafePluginExecutor.ExecuteWithTimeoutAsync(
                    () => registration.Plugin.ShutdownAsync(),
                    TimeSpan.FromSeconds(10),
                    "ShutdownAsync",
                    registration.Plugin.Name,
                    _logger);

                if (!shutdownResult.IsSuccess && !shutdownResult.IsCancelled)
                {
                    _logger.LogWarning($"Plugin {registration.Plugin.Name} shutdown had issues: {shutdownResult.ErrorMessage}");
                }

                // Cleanup message bus subscriptions
                _messageBus.UnsubscribeScope(pluginId);

                // Cleanup monitoring and task scheduler
                _watchdog.StopMonitoring(pluginId);
                _taskScheduler.CleanupPlugin(pluginId);

                registration.State = PluginState.Unloaded;

                _plugins.TryRemove(pluginId, out _);

                _logger.LogInformation($"Plugin {registration.Plugin.Name} unloaded successfully");

                // Publish plugin unloaded event
                _messageBus.Publish(new PluginUnloadedEvent
                {
                    PluginId = pluginId,
                    PluginName = registration.Plugin.Name,
                    TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                });

                return PluginUnloadResult.CreateSuccess(pluginId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error unloading plugin {pluginId}");
                registration.State = PluginState.Failed;
                registration.LastError = ex.Message;
                return PluginUnloadResult.Failed(pluginId, ex.Message);
            }
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    /// <summary>
    /// Reload a plugin (unload then load)
    /// </summary>
    public async Task<PluginReloadResult> ReloadPluginAsync(string pluginId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (!_plugins.TryGetValue(pluginId, out var registration))
        {
            return PluginReloadResult.NotFound(pluginId);
        }

        var plugin = registration.Plugin;

        var unloadResult = await UnloadPluginAsync(pluginId, cancellationToken);
        if (!unloadResult.Success)
        {
            return PluginReloadResult.UnloadFailed(pluginId, unloadResult.ErrorMessage);
        }

        var loadResult = await LoadPluginAsync(plugin, cancellationToken);
        if (!loadResult.Success)
        {
            return PluginReloadResult.LoadFailed(pluginId, loadResult.ErrorMessage);
        }

        return PluginReloadResult.CreateSuccess(pluginId);
    }

    /// <summary>
    /// Get plugin state
    /// </summary>
    public PluginState? GetPluginState(string pluginId)
    {
        return _plugins.TryGetValue(pluginId, out var reg) ? reg.State : null;
    }

    /// <summary>
    /// Get all loaded plugins
    /// </summary>
    public IReadOnlyList<PluginInfo> GetLoadedPlugins()
    {
        return _plugins.Values.Select(r => new PluginInfo
        {
            PluginId = r.PluginId,
            Name = r.Plugin.Name,
            Version = r.Plugin.Version.ToString(),
            Category = r.Plugin.Category,
            State = r.State,
            Health = r.Plugin.GetHealth(),
            LoadedAt = r.LoadedAt,
            LastHealthCheck = r.LastHealthCheck,
            LastError = r.LastError
        }).ToList();
    }

    /// <summary>
    /// Perform health check on all plugins
    /// </summary>
    public async Task<HealthCheckResult> PerformHealthCheckAsync()
    {
        var results = new List<PluginHealthInfo>();

        foreach (var reg in _plugins.Values)
        {
            // Use safe executor with timeout for health checks
            var healthResult = await SafePluginExecutor.ExecuteWithTimeoutAsync(
                async () =>
                {
                    var health = await Task.Run(() => reg.Plugin.GetHealth());
                    reg.LastHealthCheck = DateTimeOffset.UtcNow;

                    results.Add(new PluginHealthInfo
                    {
                        PluginId = reg.PluginId,
                        PluginName = reg.Plugin.Name,
                        Health = health,
                        State = reg.State
                    });

                    if (health == PluginHealth.Unhealthy)
                    {
                        _logger.LogWarning($"Plugin {reg.Plugin.Name} reported unhealthy status");
                    }
                },
                TimeSpan.FromSeconds(5),
                "GetHealth",
                reg.Plugin.Name,
                _logger);

            if (!healthResult.IsSuccess)
            {
                _logger.LogError($"Error checking health of plugin {reg.Plugin.Name}: {healthResult.ErrorMessage}");
                results.Add(new PluginHealthInfo
                {
                    PluginId = reg.PluginId,
                    PluginName = reg.Plugin.Name,
                    Health = PluginHealth.Unknown,
                    State = reg.State,
                    Error = healthResult.ErrorMessage
                });
            }
        }

        return new HealthCheckResult
        {
            CheckedAt = DateTimeOffset.UtcNow,
            PluginHealths = results
        };
    }

    /// <summary>
    /// Shutdown all plugins
    /// </summary>
    public async Task ShutdownAllAsync()
    {
        _shutdownCts.Cancel();

        var pluginsToShutdown = _plugins.Values
            .OrderByDescending(r => (int)r.Plugin.Category) // Reverse category order
            .ToList();

        foreach (var reg in pluginsToShutdown)
        {
            try
            {
                await UnloadPluginAsync(reg.PluginId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error shutting down plugin {reg.Plugin.Name}");
            }
        }
    }

    private string GetPluginId(IAgPlugin plugin)
    {
        return $"{plugin.Name}:{plugin.Version}".Replace(" ", "_");
    }

    private List<string> CheckDependencies(IAgPlugin plugin)
    {
        var missing = new List<string>();

        foreach (var dep in plugin.Dependencies)
        {
            var found = _plugins.Values.Any(r =>
                r.Plugin.Name.Equals(dep, StringComparison.OrdinalIgnoreCase) &&
                r.State == PluginState.Running);

            if (!found)
            {
                missing.Add(dep);
            }
        }

        return missing;
    }

    private List<string> GetDependentPlugins(string pluginId)
    {
        if (!_plugins.TryGetValue(pluginId, out var targetReg))
        {
            return new List<string>();
        }

        return _plugins.Values
            .Where(r => r.Plugin.Dependencies.Any(d =>
                d.Equals(targetReg.Plugin.Name, StringComparison.OrdinalIgnoreCase)))
            .Select(r => r.PluginId)
            .ToList();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(PluginManager));
        }
    }

    /// <summary>
    /// Get task scheduler statistics
    /// </summary>
    public Dictionary<string, PluginThreadPoolStats> GetTaskSchedulerStats()
    {
        return _taskScheduler.GetStatistics();
    }

    /// <summary>
    /// Get watchdog monitoring statistics
    /// </summary>
    public List<PluginMonitorStats> GetWatchdogStats()
    {
        return _watchdog.GetStatistics();
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        _shutdownCts.Cancel();
        _watchdog.Dispose();
        _lifecycleLock.Dispose();
        _shutdownCts.Dispose();
    }
}

/// <summary>
/// Scoped plugin context that tracks subscriptions
/// </summary>
internal class ScopedPluginContext : IPluginContext
{
    private readonly MessageBus _messageBus;
    private readonly string _scope;

    public ScopedPluginContext(
        MessageBus messageBus,
        string scope,
        IServiceProvider services,
        IConfiguration configuration,
        ILogger logger,
        CancellationToken appShutdownToken)
    {
        _messageBus = messageBus;
        _scope = scope;
        Services = services;
        Configuration = configuration;
        Logger = logger;
        AppShutdownToken = appShutdownToken;
    }

    public IMessageBus MessageBus => new ScopedMessageBus(_messageBus, _scope);
    public IServiceProvider Services { get; }
    public IConfiguration Configuration { get; }
    public ILogger Logger { get; }
    public CancellationToken AppShutdownToken { get; }
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

    public void Publish<T>(in T message) where T : struct
    {
        _innerBus.Publish(in message);
    }

    public Task PublishAsync<T>(T message) where T : struct
    {
        return _innerBus.PublishAsync(message);
    }
}

// Plugin lifecycle state
public enum PluginState
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

// Plugin registration info
internal class PluginRegistration
{
    public required IAgPlugin Plugin { get; set; }
    public required string PluginId { get; set; }
    public PluginState State { get; set; }
    public ScopedPluginContext? Context { get; set; }
    public DateTimeOffset LoadedAt { get; set; }
    public DateTimeOffset LastHealthCheck { get; set; }
    public string? LastError { get; set; }
}

// Public plugin info
public class PluginInfo
{
    public required string PluginId { get; set; }
    public required string Name { get; set; }
    public required string Version { get; set; }
    public PluginCategory Category { get; set; }
    public PluginState State { get; set; }
    public PluginHealth Health { get; set; }
    public DateTimeOffset LoadedAt { get; set; }
    public DateTimeOffset LastHealthCheck { get; set; }
    public string? LastError { get; set; }
}

// Result types
public class PluginLoadResult
{
    public bool Success { get; set; }
    public required string PluginId { get; set; }
    public string? ErrorMessage { get; set; }
    public List<string>? MissingDependencies { get; set; }

    public static PluginLoadResult CreateSuccess(string pluginId) =>
        new() { Success = true, PluginId = pluginId };

    public static PluginLoadResult AlreadyLoaded(string pluginId) =>
        new() { Success = false, PluginId = pluginId, ErrorMessage = "Already loaded" };

    public static PluginLoadResult Failed(string pluginId, string error) =>
        new() { Success = false, PluginId = pluginId, ErrorMessage = error };

    public static PluginLoadResult CreateMissingDependencies(string pluginId, List<string> deps) =>
        new() { Success = false, PluginId = pluginId, MissingDependencies = deps, ErrorMessage = "Missing dependencies" };
}

public class PluginUnloadResult
{
    public bool Success { get; set; }
    public required string PluginId { get; set; }
    public string? ErrorMessage { get; set; }
    public List<string>? DependentPlugins { get; set; }

    public static PluginUnloadResult CreateSuccess(string pluginId) =>
        new() { Success = true, PluginId = pluginId };

    public static PluginUnloadResult NotFound(string pluginId) =>
        new() { Success = false, PluginId = pluginId, ErrorMessage = "Plugin not found" };

    public static PluginUnloadResult Failed(string pluginId, string error) =>
        new() { Success = false, PluginId = pluginId, ErrorMessage = error };

    public static PluginUnloadResult HasDependents(string pluginId, List<string> dependents) =>
        new() { Success = false, PluginId = pluginId, DependentPlugins = dependents, ErrorMessage = "Has dependent plugins" };
}

public class PluginReloadResult
{
    public bool Success { get; set; }
    public required string PluginId { get; set; }
    public string? ErrorMessage { get; set; }

    public static PluginReloadResult CreateSuccess(string pluginId) =>
        new() { Success = true, PluginId = pluginId };

    public static PluginReloadResult NotFound(string pluginId) =>
        new() { Success = false, PluginId = pluginId, ErrorMessage = "Plugin not found" };

    public static PluginReloadResult UnloadFailed(string pluginId, string? error) =>
        new() { Success = false, PluginId = pluginId, ErrorMessage = $"Unload failed: {error}" };

    public static PluginReloadResult LoadFailed(string pluginId, string? error) =>
        new() { Success = false, PluginId = pluginId, ErrorMessage = $"Load failed: {error}" };
}

public class HealthCheckResult
{
    public DateTimeOffset CheckedAt { get; set; }
    public required List<PluginHealthInfo> PluginHealths { get; set; }
}

public class PluginHealthInfo
{
    public required string PluginId { get; set; }
    public required string PluginName { get; set; }
    public PluginHealth Health { get; set; }
    public PluginState State { get; set; }
    public string? Error { get; set; }
}
