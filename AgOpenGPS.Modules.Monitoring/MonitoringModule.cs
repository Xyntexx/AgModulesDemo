namespace AgOpenGPS.Modules.Monitoring;

using AgOpenGPS.ModuleContracts;
using AgOpenGPS.ModuleContracts.Messages;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;

/// <summary>
/// Monitoring module that collects metrics for timing, load, and crash tests
/// Tracks message bus throughput, module performance, and system health
/// </summary>
public class MonitoringModule : IAgModule
{
    public string Name => "System Monitor";
    public Version Version => new Version(1, 0, 0);
    public ModuleCategory Category => ModuleCategory.Monitoring;
    public string[] Dependencies => Array.Empty<string>();

    private IModuleContext? _context;
    private ILogger? _logger;
    private readonly ConcurrentDictionary<string, ModuleMetrics> _moduleMetrics = new();
    private readonly ConcurrentDictionary<Type, MessageTypeMetrics> _messageMetrics = new();
    private readonly Stopwatch _uptime = new();
    private long _totalMessagesProcessed;

    public Task InitializeAsync(IModuleContext context)
    {
        _context = context;
        _logger = context.Logger;

        // Subscribe to all key message types to track throughput
        SubscribeToMessages();

        // Subscribe to module lifecycle events
        context.MessageBus.Subscribe<ModuleLoadedEvent>(OnModuleLoaded);
        context.MessageBus.Subscribe<ModuleUnloadedEvent>(OnModuleUnloaded);

        _logger.LogInformation("Monitoring Module initialized");
        return Task.CompletedTask;
    }

    public Task StartAsync()
    {
        _uptime.Start();
        _logger?.LogInformation("Monitoring Module started - collecting metrics");
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        _uptime.Stop();
        _logger?.LogInformation("Monitoring Module stopped");
        return Task.CompletedTask;
    }

    public Task ShutdownAsync()
    {
        PrintFinalStatistics();
        return Task.CompletedTask;
    }

    public ModuleHealth GetHealth()
    {
        // Monitor is always healthy
        return ModuleHealth.Healthy;
    }

    private void SubscribeToMessages()
    {
        // Subscribe to GPS messages
        _context?.MessageBus.Subscribe<GpsPositionMessage>(msg => TrackMessage(typeof(GpsPositionMessage)));

        // Subscribe to steer commands
        _context?.MessageBus.Subscribe<SteerCommandMessage>(msg => TrackMessage(typeof(SteerCommandMessage)));

        // Subscribe to raw data
        _context?.MessageBus.Subscribe<RawDataReceivedMessage>(msg => TrackMessage(typeof(RawDataReceivedMessage)));
        _context?.MessageBus.Subscribe<RawDataToSendMessage>(msg => TrackMessage(typeof(RawDataToSendMessage)));
    }

    private void TrackMessage(Type messageType)
    {
        Interlocked.Increment(ref _totalMessagesProcessed);

        var metrics = _messageMetrics.GetOrAdd(messageType, _ => new MessageTypeMetrics
        {
            MessageTypeName = messageType.Name,
            FirstSeen = DateTime.UtcNow
        });

        metrics.IncrementCount();
    }

    private void OnModuleLoaded(ModuleLoadedEvent evt)
    {
        var metrics = new ModuleMetrics
        {
            ModuleName = evt.ModuleName,
            LoadedAt = DateTimeOffset.FromUnixTimeMilliseconds(evt.TimestampMs).DateTime
        };

        _moduleMetrics[evt.ModuleId] = metrics;
        _logger?.LogInformation($"Tracking module: {evt.ModuleName}");
    }

    private void OnModuleUnloaded(ModuleUnloadedEvent evt)
    {
        if (_moduleMetrics.TryGetValue(evt.ModuleId, out var metrics))
        {
            metrics.UnloadedAt = DateTimeOffset.FromUnixTimeMilliseconds(evt.TimestampMs).DateTime;
            _logger?.LogInformation($"Module unloaded: {evt.ModuleName}, uptime: {metrics.Uptime}");
        }
    }

    /// <summary>
    /// Get current system metrics - useful for tests
    /// </summary>
    public SystemMetrics GetSystemMetrics()
    {
        return new SystemMetrics
        {
            UptimeSeconds = _uptime.Elapsed.TotalSeconds,
            TotalMessagesProcessed = Interlocked.Read(ref _totalMessagesProcessed),
            MessagesPerSecond = Interlocked.Read(ref _totalMessagesProcessed) / Math.Max(1, _uptime.Elapsed.TotalSeconds),
            ModuleCount = _moduleMetrics.Count,
            MessageTypes = _messageMetrics.Values.ToList(),
            Modules = _moduleMetrics.Values.ToList()
        };
    }

    /// <summary>
    /// Get metrics for a specific module
    /// </summary>
    public ModuleMetrics? GetModuleMetrics(string moduleId)
    {
        return _moduleMetrics.TryGetValue(moduleId, out var metrics) ? metrics : null;
    }

    private void PrintFinalStatistics()
    {
        _logger?.LogInformation("=== Final Monitoring Statistics ===");
        _logger?.LogInformation($"Uptime: {_uptime.Elapsed}");
        _logger?.LogInformation($"Total Messages: {_totalMessagesProcessed:N0}");
        _logger?.LogInformation($"Avg Throughput: {_totalMessagesProcessed / Math.Max(1, _uptime.Elapsed.TotalSeconds):F2} msg/sec");
        _logger?.LogInformation($"Tracked Modules: {_moduleMetrics.Count}");

        _logger?.LogInformation("\nMessage Type Breakdown:");
        foreach (var metric in _messageMetrics.Values.OrderByDescending(m => m.Count))
        {
            _logger?.LogInformation($"  {metric.MessageTypeName}: {metric.Count:N0} ({metric.MessagesPerSecond:F2}/sec)");
        }
    }
}

/// <summary>
/// Overall system metrics
/// </summary>
public class SystemMetrics
{
    public double UptimeSeconds { get; set; }
    public long TotalMessagesProcessed { get; set; }
    public double MessagesPerSecond { get; set; }
    public int ModuleCount { get; set; }
    public List<MessageTypeMetrics> MessageTypes { get; set; } = new();
    public List<ModuleMetrics> Modules { get; set; } = new();
}

/// <summary>
/// Metrics for a specific module
/// </summary>
public class ModuleMetrics
{
    public required string ModuleName { get; set; }
    public DateTime LoadedAt { get; set; }
    public DateTime? UnloadedAt { get; set; }

    public TimeSpan Uptime => (UnloadedAt ?? DateTime.UtcNow) - LoadedAt;
}

/// <summary>
/// Metrics for a specific message type
/// </summary>
public class MessageTypeMetrics
{
    public required string MessageTypeName { get; set; }
    public DateTime FirstSeen { get; set; }
    private long _count;

    public long Count => Interlocked.Read(ref _count);

    public double MessagesPerSecond => Count / Math.Max(1, (DateTime.UtcNow - FirstSeen).TotalSeconds);

    public void IncrementCount()
    {
        Interlocked.Increment(ref _count);
    }
}
