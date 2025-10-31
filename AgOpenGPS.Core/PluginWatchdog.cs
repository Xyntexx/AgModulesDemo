namespace AgOpenGPS.Core;

using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

/// <summary>
/// Monitors plugins for hanging/blocking behavior and can take corrective action
/// </summary>
public class PluginWatchdog : IDisposable
{
    private readonly ILogger<PluginWatchdog> _logger;
    private readonly ConcurrentDictionary<string, PluginMonitorState> _monitoredPlugins = new();
    private readonly Timer _checkTimer;
    private readonly TimeSpan _checkInterval;
    private readonly TimeSpan _hangThreshold;
    private volatile bool _disposed;

    public event EventHandler<PluginHangDetectedEventArgs>? PluginHangDetected;

    public PluginWatchdog(
        ILogger<PluginWatchdog> logger,
        TimeSpan? checkInterval = null,
        TimeSpan? hangThreshold = null)
    {
        _logger = logger;
        _checkInterval = checkInterval ?? TimeSpan.FromSeconds(5);
        _hangThreshold = hangThreshold ?? TimeSpan.FromSeconds(60);

        _checkTimer = new Timer(
            _ => CheckPlugins(),
            null,
            _checkInterval,
            _checkInterval);

        _logger.LogInformation($"Plugin watchdog started - check interval: {_checkInterval.TotalSeconds}s, hang threshold: {_hangThreshold.TotalSeconds}s");
    }

    /// <summary>
    /// Start monitoring a plugin operation
    /// </summary>
    public IDisposable MonitorOperation(string pluginId, string operationName)
    {
        if (_disposed) return new NullMonitor();

        var state = _monitoredPlugins.GetOrAdd(pluginId, _ => new PluginMonitorState(pluginId));

        var operation = new OperationMonitor
        {
            OperationName = operationName,
            StartTime = DateTime.UtcNow,
            ThreadId = Environment.CurrentManagedThreadId
        };

        state.StartOperation(operation);

        return new OperationCompletionToken(() => state.CompleteOperation(operation));
    }

    /// <summary>
    /// Record a heartbeat from a plugin
    /// </summary>
    public void Heartbeat(string pluginId)
    {
        if (_disposed) return;

        var state = _monitoredPlugins.GetOrAdd(pluginId, _ => new PluginMonitorState(pluginId));
        state.RecordHeartbeat();
    }

    /// <summary>
    /// Stop monitoring a plugin (called on unload)
    /// </summary>
    public void StopMonitoring(string pluginId)
    {
        _monitoredPlugins.TryRemove(pluginId, out _);
        _logger.LogDebug($"Stopped monitoring plugin {pluginId}");
    }

    /// <summary>
    /// Get monitoring statistics
    /// </summary>
    public List<PluginMonitorStats> GetStatistics()
    {
        return _monitoredPlugins.Values
            .Select(state => state.GetStats())
            .ToList();
    }

    private void CheckPlugins()
    {
        if (_disposed) return;

        var now = DateTime.UtcNow;

        foreach (var state in _monitoredPlugins.Values)
        {
            // Check for long-running operations
            var longRunningOps = state.GetLongRunningOperations(now, _hangThreshold);

            foreach (var op in longRunningOps)
            {
                var duration = now - op.StartTime;

                _logger.LogWarning(
                    $"Plugin {state.PluginId} operation '{op.OperationName}' has been running for {duration.TotalSeconds:F1}s (thread {op.ThreadId})");

                // Check if this is a new hang or ongoing
                if (!state.IsHangReported(op))
                {
                    state.MarkHangReported(op);

                    PluginHangDetected?.Invoke(this, new PluginHangDetectedEventArgs
                    {
                        PluginId = state.PluginId,
                        OperationName = op.OperationName,
                        Duration = duration,
                        ThreadId = op.ThreadId
                    });
                }
            }

            // Check for plugins with no heartbeat
            var timeSinceHeartbeat = now - state.LastHeartbeat;
            if (state.HeartbeatCount > 0 && timeSinceHeartbeat > _hangThreshold * 2)
            {
                _logger.LogWarning(
                    $"Plugin {state.PluginId} has not sent heartbeat for {timeSinceHeartbeat.TotalSeconds:F1}s");
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        _checkTimer?.Dispose();
        _monitoredPlugins.Clear();

        _logger.LogInformation("Plugin watchdog stopped");
    }

    private class NullMonitor : IDisposable
    {
        public void Dispose() { }
    }

    private class OperationCompletionToken : IDisposable
    {
        private readonly Action _onComplete;
        private int _disposed;

        public OperationCompletionToken(Action onComplete)
        {
            _onComplete = onComplete;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _onComplete();
            }
        }
    }
}

/// <summary>
/// Tracks monitoring state for a single plugin
/// </summary>
internal class PluginMonitorState
{
    public string PluginId { get; }
    public DateTime LastHeartbeat { get; private set; }
    public long HeartbeatCount { get; private set; }

    private readonly ConcurrentDictionary<OperationMonitor, bool> _activeOperations = new();
    private readonly ConcurrentDictionary<OperationMonitor, bool> _reportedHangs = new();
    private readonly object _lock = new();

    public PluginMonitorState(string pluginId)
    {
        PluginId = pluginId;
        LastHeartbeat = DateTime.UtcNow;
    }

    public void StartOperation(OperationMonitor operation)
    {
        _activeOperations[operation] = true;
    }

    public void CompleteOperation(OperationMonitor operation)
    {
        _activeOperations.TryRemove(operation, out _);
        _reportedHangs.TryRemove(operation, out _);
    }

    public void RecordHeartbeat()
    {
        LastHeartbeat = DateTime.UtcNow;
        var count = HeartbeatCount;
        Interlocked.Increment(ref count);
        HeartbeatCount = count;
    }

    public List<OperationMonitor> GetLongRunningOperations(DateTime now, TimeSpan threshold)
    {
        return _activeOperations.Keys
            .Where(op => (now - op.StartTime) > threshold)
            .ToList();
    }

    public bool IsHangReported(OperationMonitor operation)
    {
        return _reportedHangs.ContainsKey(operation);
    }

    public void MarkHangReported(OperationMonitor operation)
    {
        _reportedHangs[operation] = true;
    }

    public PluginMonitorStats GetStats()
    {
        var count = HeartbeatCount;
        return new PluginMonitorStats
        {
            PluginId = PluginId,
            ActiveOperations = _activeOperations.Count,
            HeartbeatCount = count,
            TimeSinceLastHeartbeat = DateTime.UtcNow - LastHeartbeat,
            LongestRunningOperation = _activeOperations.Keys
                .Select(op => DateTime.UtcNow - op.StartTime)
                .OrderByDescending(d => d)
                .FirstOrDefault()
        };
    }
}

/// <summary>
/// Information about a monitored operation
/// </summary>
internal class OperationMonitor
{
    public required string OperationName { get; set; }
    public DateTime StartTime { get; set; }
    public int ThreadId { get; set; }
}

/// <summary>
/// Statistics about plugin monitoring
/// </summary>
public class PluginMonitorStats
{
    public required string PluginId { get; set; }
    public int ActiveOperations { get; set; }
    public long HeartbeatCount { get; set; }
    public TimeSpan TimeSinceLastHeartbeat { get; set; }
    public TimeSpan LongestRunningOperation { get; set; }
}

/// <summary>
/// Event args for plugin hang detection
/// </summary>
public class PluginHangDetectedEventArgs : EventArgs
{
    public required string PluginId { get; set; }
    public required string OperationName { get; set; }
    public TimeSpan Duration { get; set; }
    public int ThreadId { get; set; }
}
