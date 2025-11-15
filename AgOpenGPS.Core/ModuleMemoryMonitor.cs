namespace AgOpenGPS.Core;

using Microsoft.Extensions.Logging;
using System.Diagnostics;

/// <summary>
/// Monitors memory usage per module and enforces memory limits
/// </summary>
public class ModuleMemoryMonitor : IDisposable
{
    private readonly ILogger<ModuleMemoryMonitor> _logger;
    private readonly long _maxMemoryPerModuleMB;
    private readonly long _globalMemoryWarningThresholdMB;
    private readonly Timer _monitorTimer;
    private readonly object _lock = new object();
    private readonly Dictionary<string, ModuleMemoryState> _moduleStates = new();
    private long _lastTotalMemoryMB;
    private bool _disposed;

    public event EventHandler<ModuleMemoryExceededEventArgs>? ModuleMemoryExceeded;

    public ModuleMemoryMonitor(
        ILogger<ModuleMemoryMonitor> logger,
        long maxMemoryPerModuleMB = 500,
        long globalMemoryWarningThresholdMB = 2048,
        TimeSpan? checkInterval = null)
    {
        _logger = logger;
        _maxMemoryPerModuleMB = maxMemoryPerModuleMB;
        _globalMemoryWarningThresholdMB = globalMemoryWarningThresholdMB;

        var interval = checkInterval ?? TimeSpan.FromSeconds(10);
        _monitorTimer = new Timer(CheckMemoryUsage, null, interval, interval);
    }

    public void RegisterModule(string moduleId)
    {
        lock (_lock)
        {
            if (!_moduleStates.ContainsKey(moduleId))
            {
                _moduleStates[moduleId] = new ModuleMemoryState
                {
                    ModuleId = moduleId,
                    BaselineMemoryMB = GetCurrentProcessMemoryMB(),
                    LastCheckTime = DateTimeOffset.UtcNow
                };

                _logger.LogDebug("Registered module {ModuleId} for memory monitoring", moduleId);
            }
        }
    }

    public void UnregisterModule(string moduleId)
    {
        lock (_lock)
        {
            if (_moduleStates.Remove(moduleId))
            {
                _logger.LogDebug("Unregistered module {ModuleId} from memory monitoring", moduleId);
            }
        }
    }

    public ModuleMemoryInfo GetModuleMemoryInfo(string moduleId)
    {
        lock (_lock)
        {
            if (_moduleStates.TryGetValue(moduleId, out var state))
            {
                var currentMemory = GetCurrentProcessMemoryMB();
                var estimatedUsage = Math.Max(0, currentMemory - state.BaselineMemoryMB);

                return new ModuleMemoryInfo
                {
                    ModuleId = moduleId,
                    EstimatedMemoryMB = estimatedUsage,
                    PeakMemoryMB = state.PeakMemoryMB,
                    LimitMemoryMB = _maxMemoryPerModuleMB,
                    WarningCount = state.WarningCount,
                    IsOverLimit = estimatedUsage > _maxMemoryPerModuleMB
                };
            }

            return new ModuleMemoryInfo
            {
                ModuleId = moduleId,
                EstimatedMemoryMB = 0,
                PeakMemoryMB = 0,
                LimitMemoryMB = _maxMemoryPerModuleMB,
                WarningCount = 0,
                IsOverLimit = false
            };
        }
    }

    public GlobalMemoryInfo GetGlobalMemoryInfo()
    {
        var currentMemory = GetCurrentProcessMemoryMB();
        var gcInfo = GC.GetGCMemoryInfo();

        return new GlobalMemoryInfo
        {
            ProcessMemoryMB = currentMemory,
            ManagedMemoryMB = gcInfo.HeapSizeBytes / (1024 * 1024),
            TotalModulesTracked = _moduleStates.Count,
            IsOverGlobalWarningThreshold = currentMemory > _globalMemoryWarningThresholdMB
        };
    }

    private void CheckMemoryUsage(object? state)
    {
        if (_disposed) return;

        try
        {
            var currentTotalMemory = GetCurrentProcessMemoryMB();
            var memoryDelta = currentTotalMemory - _lastTotalMemoryMB;

            lock (_lock)
            {
                // Check global memory threshold
                if (currentTotalMemory > _globalMemoryWarningThresholdMB)
                {
                    _logger.LogWarning(
                        "Global memory usage {CurrentMB}MB exceeds warning threshold {ThresholdMB}MB. " +
                        "Modules tracked: {ModuleCount}",
                        currentTotalMemory, _globalMemoryWarningThresholdMB, _moduleStates.Count);

                    // Force garbage collection to reclaim memory
                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, true, true);
                    GC.WaitForPendingFinalizers();

                    var afterGC = GetCurrentProcessMemoryMB();
                    _logger.LogInformation("Memory after GC: {AfterGC}MB (reclaimed {Reclaimed}MB)",
                        afterGC, currentTotalMemory - afterGC);
                }

                // Check per-module memory estimates
                foreach (var kvp in _moduleStates)
                {
                    var moduleId = kvp.Key;
                    var moduleState = kvp.Value;

                    // Simple heuristic: divide memory growth among active modules
                    var estimatedModuleMemory = _moduleStates.Count > 0
                        ? currentTotalMemory / _moduleStates.Count
                        : 0;

                    moduleState.CurrentEstimatedMemoryMB = estimatedModuleMemory;
                    moduleState.PeakMemoryMB = Math.Max(moduleState.PeakMemoryMB, estimatedModuleMemory);
                    moduleState.LastCheckTime = DateTimeOffset.UtcNow;

                    // Check if module exceeds limit
                    if (estimatedModuleMemory > _maxMemoryPerModuleMB)
                    {
                        moduleState.WarningCount++;

                        _logger.LogWarning(
                            "Module {ModuleId} estimated memory {EstimatedMB}MB exceeds limit {LimitMB}MB. " +
                            "Warning count: {WarningCount}",
                            moduleId, estimatedModuleMemory, _maxMemoryPerModuleMB, moduleState.WarningCount);

                        // Raise event for external handling
                        ModuleMemoryExceeded?.Invoke(this, new ModuleMemoryExceededEventArgs
                        {
                            ModuleId = moduleId,
                            EstimatedMemoryMB = estimatedModuleMemory,
                            LimitMB = _maxMemoryPerModuleMB,
                            WarningCount = moduleState.WarningCount
                        });
                    }
                }
            }

            _lastTotalMemoryMB = currentTotalMemory;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during memory monitoring check");
        }
    }

    private static long GetCurrentProcessMemoryMB()
    {
        using var process = Process.GetCurrentProcess();
        return process.WorkingSet64 / (1024 * 1024);
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        _monitorTimer?.Dispose();

        _logger.LogInformation("Module memory monitor disposed");
    }

    private class ModuleMemoryState
    {
        public string ModuleId { get; set; } = null!;
        public long BaselineMemoryMB { get; set; }
        public long CurrentEstimatedMemoryMB { get; set; }
        public long PeakMemoryMB { get; set; }
        public int WarningCount { get; set; }
        public DateTimeOffset LastCheckTime { get; set; }
    }
}

public class ModuleMemoryInfo
{
    public string ModuleId { get; set; } = null!;
    public long EstimatedMemoryMB { get; set; }
    public long PeakMemoryMB { get; set; }
    public long LimitMemoryMB { get; set; }
    public int WarningCount { get; set; }
    public bool IsOverLimit { get; set; }
}

public class GlobalMemoryInfo
{
    public long ProcessMemoryMB { get; set; }
    public long ManagedMemoryMB { get; set; }
    public int TotalModulesTracked { get; set; }
    public bool IsOverGlobalWarningThreshold { get; set; }
}

public class ModuleMemoryExceededEventArgs : EventArgs
{
    public string ModuleId { get; set; } = null!;
    public long EstimatedMemoryMB { get; set; }
    public long LimitMB { get; set; }
    public int WarningCount { get; set; }
}
