# Scheduler Usage Guide

## Overview

The rate scheduler allows modules to schedule methods for deterministic execution at fixed rates using the `Schedule()` API. All scheduled methods maintain **deterministic execution** and **zero timing drift**.

## Option 1: Single Scheduled Method (Simple)

Best for modules with a single execution rate.

```csharp
public class SimpleModule : IAgModule
{
    private IScheduledMethod? _tickHandle;

    public Task InitializeAsync(IModuleContext context)
    {
        // Schedule single method at 10Hz
        _tickHandle = context.Scheduler?.Schedule(
            Tick,
            rateHz: 10.0,
            name: "MainTick");

        return Task.CompletedTask;
    }

    private void Tick(long tickNumber, long monotonicMs)
    {
        // All module logic here
        ReadSensors();
        ProcessData();
        PublishResults();
    }

    public Task ShutdownAsync()
    {
        _tickHandle?.Dispose();
        return Task.CompletedTask;
    }
}
```

**Pros:**
- Simple, straightforward
- Minimal boilerplate

**Cons:**
- Everything runs at same rate
- Can't easily separate fast/slow operations

## Option 2: Multiple Scheduled Methods (Flexible)

Best for modules with operations at different rates.

```csharp
public class MultiRateModule : IAgModule
{
    private ILogger _logger;
    private IScheduledMethod? _fastHandle;
    private IScheduledMethod? _slowHandle;

    public Task InitializeAsync(IModuleContext context)
    {
        _logger = context.Logger;

        if (context.Scheduler != null)
        {
            // Register multiple methods at different rates
            _fastHandle = context.Scheduler.Schedule(
                UpdateFastSensor,
                rateHz: 100.0,
                name: "FastSensor");

            _slowHandle = context.Scheduler.Schedule(
                UpdateSlowSensor,
                rateHz: 10.0,
                name: "SlowSensor");

            context.Scheduler.Schedule(
                RunDiagnostics,
                rateHz: 1.0,
                name: "Diagnostics");

            context.Scheduler.Schedule(
                SaveToDisk,
                rateHz: 0.1,
                name: "Persister");  // Every 10 seconds
        }

        return Task.CompletedTask;
    }

    private void UpdateFastSensor(long tickNumber, long monotonicMs)
    {
        // High-frequency work (IMU, gyro, etc.)
        _logger.LogDebug("Fast sensor update at {Tick}", tickNumber);
    }

    private void UpdateSlowSensor(long tickNumber, long monotonicMs)
    {
        // Medium-frequency work (GPS)
        _logger.LogInformation("Slow sensor update");
    }

    private void RunDiagnostics(long tickNumber, long monotonicMs)
    {
        // Low-frequency work (health checks)
        if (tickNumber % 10 == 0)  // Every 10 ticks at 1Hz = every 10 seconds
        {
            _logger.LogInformation("Running diagnostics...");
        }
    }

    private void SaveToDisk(long tickNumber, long monotonicMs)
    {
        // Very low frequency (periodic saves)
        _logger.LogInformation("Saving data to disk");
    }

    public Task StopAsync()
    {
        // Optional: Pause specific methods
        _fastHandle?.Pause();
        return Task.CompletedTask;
    }

    public Task ShutdownAsync()
    {
        // Cleanup (handles auto-dispose on module unload)
        _fastHandle?.Dispose();
        _slowHandle?.Dispose();
        return Task.CompletedTask;
    }
}
```

**Pros:**
- Fine-grained rate control
- Better CPU efficiency
- Clear separation of concerns
- Can pause/resume individual methods

**Cons:**
- More boilerplate
- Need to manage handles

## IScheduledMethod Handle

When you schedule a method, you get back an `IScheduledMethod` handle:

```csharp
var handle = context.Scheduler.Schedule(MyMethod, rateHz: 10.0);

// Properties
string name = handle.Name;                     // Method name
double requestedRate = handle.RequestedRateHz;  // What you asked for
double actualRate = handle.ActualRateHz;        // What scheduler provides
long callCount = handle.CallCount;              // How many times called
double avgUs = handle.AverageExecutionUs;       // Average execution time
long maxUs = handle.MaxExecutionUs;             // Peak execution time
bool paused = handle.IsPaused;                  // Is it paused?

// Control
handle.Pause();   // Stop calling this method (deterministic - still ticked)
handle.Resume();  // Resume calling
handle.Dispose(); // Unregister permanently
```

## Pause vs Dispose

### Pause (Temporary)

```csharp
// Scheduler still counts ticks but doesn't call method
handle.Pause();

// ... module is idle but deterministic timing maintained ...

handle.Resume();  // Resume from where it left off
```

**Use when:**
- Module is temporarily idle
- Want to maintain tick alignment
- May resume later

### Dispose (Permanent)

```csharp
// Method is unregistered completely
handle.Dispose();

// ... method will never be called again ...
```

**Use when:**
- Module shutting down
- Feature disabled permanently
- Freeing resources

## Statistics and Monitoring

Get comprehensive statistics:

```csharp
var stats = context.Scheduler.GetStatistics();

Console.WriteLine($"Global Tick: {stats.GlobalTickNumber}");
Console.WriteLine($"Modules: {stats.ModuleCount}");
Console.WriteLine($"Scheduled Methods: {stats.ScheduledMethodCount}");

// Per-module stats
foreach (var mod in stats.ModuleStats)
{
    Console.WriteLine($"{mod.ModuleName}: {mod.TickCount} ticks, " +
                     $"avg {mod.AverageExecutionUs:F1}μs, " +
                     $"max {mod.MaxExecutionUs}μs");
}

// Per-method stats
foreach (var method in stats.MethodStats)
{
    Console.WriteLine($"{method.Name}: {method.CallCount} calls, " +
                     $"avg {method.AverageExecutionUs:F1}μs, " +
                     $"paused: {method.IsPaused}");
}
```

## Real-World Example: Sensor Fusion Module

```csharp
public class SensorFusionModule : IAgModule
{
    private ILogger _logger;
    private IScheduledMethod _imuHandle;
    private IScheduledMethod _gpsHandle;

    // Sensor data
    private Vector3 _imuAccel;
    private Vector3 _imuGyro;
    private GpsData _gpsData;

    public Task InitializeAsync(IModuleContext context)
    {
        _logger = context.Logger;

        if (context.Scheduler == null)
        {
            _logger.LogWarning("Scheduler not available - module will not function");
            return Task.CompletedTask;
        }

        // IMU at 100Hz (high-frequency)
        _imuHandle = context.Scheduler.Schedule(
            ReadIMU,
            rateHz: 100.0,
            name: "IMU_Reader");

        // GPS at 10Hz (medium-frequency)
        _gpsHandle = context.Scheduler.Schedule(
            ReadGPS,
            rateHz: 10.0,
            name: "GPS_Reader");

        // Fusion at 50Hz (between IMU and GPS)
        context.Scheduler.Schedule(
            FuseSensorData,
            rateHz: 50.0,
            name: "Sensor_Fusion");

        // Publish results at 20Hz (don't need full 50Hz)
        context.Scheduler.Schedule(
            PublishResults,
            rateHz: 20.0,
            name: "Result_Publisher");

        // Health check at 1Hz (low-frequency)
        context.Scheduler.Schedule(
            HealthCheck,
            rateHz: 1.0,
            name: "Health_Check");

        _logger.LogInformation("Sensor fusion module initialized with multi-rate scheduling");
        return Task.CompletedTask;
    }

    private void ReadIMU(long tickNumber, long monotonicMs)
    {
        // Read IMU at 100Hz (every 10ms)
        _imuAccel = ReadAccelerometer();
        _imuGyro = ReadGyroscope();
    }

    private void ReadGPS(long tickNumber, long monotonicMs)
    {
        // Read GPS at 10Hz (every 100ms)
        _gpsData = ReadGPSReceiver();
    }

    private void FuseSensorData(long tickNumber, long monotonicMs)
    {
        // Kalman filter at 50Hz
        // Uses latest IMU (updated at 100Hz) and GPS (updated at 10Hz)
        RunKalmanFilter(_imuAccel, _imuGyro, _gpsData);
    }

    private void PublishResults(long tickNumber, long monotonicMs)
    {
        // Publish fused position/attitude at 20Hz
        var fused = GetFusedState();
        PublishToMessageBus(fused);
    }

    private void HealthCheck(long tickNumber, long monotonicMs)
    {
        // Check if sensors are responding
        if (_imuHandle.AverageExecutionUs > 5000)  // >5ms
        {
            _logger.LogWarning("IMU reading taking too long: {AvgUs}μs",
                _imuHandle.AverageExecutionUs);
        }

        if (_gpsHandle.CallCount == 0 && tickNumber > 100)
        {
            _logger.LogError("GPS not responding after {Ticks} ticks", tickNumber);
        }
    }

    // ... sensor reading implementations ...
}
```

## Performance Considerations

### Execution Time Budget

Each method should complete in **<1ms** to avoid scheduler overrun:

```csharp
private void FastOperation(long tickNumber, long monotonicMs)
{
    // GOOD: Fast operation (~100μs)
    var data = ReadSensor();
    ProcessQuickly(data);
}

private void SlowOperation(long tickNumber, long monotonicMs)
{
    // BAD: Slow operation (>1ms)
    var data = ReadSensor();
    for (int i = 0; i < 1000000; i++)
    {
        HeavyComputation();  // Will cause scheduler overrun!
    }
}
```

If you get warnings like:
```
Scheduled method SlowOperation took 5000μs (>1ms) - consider optimizing
```

**Solutions:**
1. Optimize the method
2. Lower the rate (if 10Hz → 1Hz)
3. Split work across multiple ticks
4. Move to background thread (lose determinism)

### Rate Limitations

Modules can only run at **divisors of the base rate**:

| Base Rate | Module Rates Available |
|-----------|------------------------|
| 100Hz     | 100Hz, 50Hz, 33Hz, 25Hz, 20Hz, 10Hz, 5Hz, 4Hz, 3Hz, 2Hz, 1Hz, 0.5Hz, 0.1Hz... |

Request 15Hz → Get 16.7Hz (100/6)
Request 7Hz → Get 7.1Hz (100/14)

### Memory Usage

Each scheduled method adds ~200 bytes overhead. For 100 methods: ~20KB (negligible).

## Configuration

Enable/disable scheduler and set base rate in `appsettings.json`:

```json
{
  "Core": {
    "UseScheduler": true,
    "SchedulerBaseRateHz": 100.0
  }
}
```

**Base rate selection:**
- **100Hz** (default): Good balance, supports common rates (50Hz, 20Hz, 10Hz)
- **1000Hz**: High precision, fast control loops, higher CPU usage
- **10Hz**: Low CPU, only for slow systems

## Migration from While Loops

### Before (Non-Deterministic)

```csharp
public class OldModule : IAgModule
{
    private CancellationToken _cancelled;

    public async Task StartAsync()
    {
        _ = Task.Run(FastLoop);
        _ = Task.Run(SlowLoop);
    }

    private async Task FastLoop()
    {
        while (!_cancelled.IsCancellationRequested)
        {
            DoFastWork();
            await Task.Delay(10);  // ~100Hz, but drift!
        }
    }

    private async Task SlowLoop()
    {
        while (!_cancelled.IsCancellationRequested)
        {
            DoSlowWork();
            await Task.Delay(1000);  // ~1Hz, but drift!
        }
    }
}
```

### After (Deterministic)

```csharp
public class NewModule : IAgModule
{
    private IScheduledMethod? _fastHandle;
    private IScheduledMethod? _slowHandle;

    public Task InitializeAsync(IModuleContext context)
    {
        _fastHandle = context.Scheduler?.Schedule(DoFastWork, 100.0, "FastWork");  // Exactly 100Hz, zero drift
        _slowHandle = context.Scheduler?.Schedule(DoSlowWork, 1.0, "SlowWork");    // Exactly 1Hz, zero drift
        return Task.CompletedTask;
    }

    private void DoFastWork(long tick, long ms) { /* ... */ }
    private void DoSlowWork(long tick, long ms) { /* ... */ }

    public Task ShutdownAsync()
    {
        _fastHandle?.Dispose();
        _slowHandle?.Dispose();
        return Task.CompletedTask;
    }
}
```

## Best Practices

1. **Keep methods fast** (<1ms target, <100μs ideal)
2. **Use appropriate rates** (don't run slow ops at high rate)
3. **Name your methods** (helps debugging)
4. **Monitor statistics** (check AverageExecutionUs)
5. **Handle null scheduler** (graceful degradation)
6. **Dispose handles** (in ShutdownAsync)
7. **Pause, don't busy-wait** (use Pause() when idle)

## Troubleshooting

### "Scheduler overrun" Warning

```
Scheduler overrun: Tick 1000 took 15ms (budget: 10ms, overrun: 5ms)
```

**Cause:** Methods took too long, missed deadline
**Fix:** Optimize slow methods or lower their rates

### Method Not Being Called

```csharp
// Check if scheduler is available
if (context.Scheduler == null)
{
    _logger.LogWarning("Scheduler not available!");
}

// Check if method is paused
if (handle.IsPaused)
{
    _logger.LogInformation("Method is paused");
}

// Check statistics
_logger.LogInformation("Method called {Count} times", handle.CallCount);
```

### Unexpected Rate

```csharp
// Requested 15Hz, got different rate
var handle = context.Scheduler.Schedule(MyMethod, 15.0);
_logger.LogInformation("Requested: 15Hz, Actual: {Actual}Hz", handle.ActualRateHz);
// Output: "Requested: 15Hz, Actual: 16.67Hz" (100/6 = 16.67)
```

**Cause:** Rate is rounded to nearest divisor of base rate
**Fix:** Request a divisor of base rate (for 100Hz base: 50, 33, 25, 20, 10, 5, 2, 1...)
