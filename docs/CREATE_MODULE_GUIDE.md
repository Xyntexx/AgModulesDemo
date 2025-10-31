# AgOpenGPS Module Creation Guide

This guide shows you how to create modules for the AgOpenGPS microkernel architecture. Modules are self-contained components that communicate via a high-performance message bus.

## Table of Contents

1. [Module Basics](#module-basics)
2. [Creating Your First Module](#creating-your-first-module)
3. [Module Lifecycle](#module-lifecycle)
4. [Message Bus Communication](#message-bus-communication)
5. [Module Categories](#module-categories)
6. [Dependencies](#dependencies)
7. [Best Practices](#best-practices)
8. [Examples](#examples)

---

## Module Basics

### What is a Module?

A module is an independent component that:
- Implements the `IAgModule` interface
- Has a specific purpose (IO, data processing, control, etc.)
- Communicates with other modules via messages
- Can be loaded, unloaded, and reloaded at runtime
- Runs isolated from other modules (failures don't cascade)

### Module Interface

```csharp
public interface IAgModule
{
    string Name { get; }                    // Display name
    Version Version { get; }                 // Semantic version
    ModuleCategory Category { get; }         // Load order category
    string[] Dependencies { get; }           // Required modules

    Task InitializeAsync(IModuleContext context);  // Setup
    Task StartAsync();                       // Begin operation
    Task StopAsync();                        // Stop gracefully
    Task ShutdownAsync();                    // Cleanup resources
    ModuleHealth GetHealth();                // Health status
}
```

---

## Creating Your First Module

### Step 1: Create Project

```bash
dotnet new classlib -n AgOpenGPS.Modules.MyModule
cd AgOpenGPS.Modules.MyModule
dotnet add reference ../AgOpenGPS.PluginContracts/AgOpenGPS.PluginContracts.csproj
```

### Step 2: Implement IAgModule

```csharp
using AgOpenGPS.ModuleContracts;
using AgOpenGPS.ModuleContracts.Messages;
using Microsoft.Extensions.Logging;

namespace AgOpenGPS.Modules.MyModule;

public class MyModule : IAgModule
{
    // Module identity
    public string Name => "My Custom Module";
    public Version Version => new Version(1, 0, 0);
    public ModuleCategory Category => ModuleCategory.DataProcessing;
    public string[] Dependencies => Array.Empty<string>();

    // Module state
    private IModuleContext? _context;
    private IMessageBus? _messageBus;
    private ILogger? _logger;

    public Task InitializeAsync(IModuleContext context)
    {
        _context = context;
        _messageBus = context.MessageBus;
        _logger = context.Logger;

        // Subscribe to messages
        _messageBus.Subscribe<GpsPositionMessage>(OnGpsPosition);

        _logger.LogInformation($"{Name} initialized");
        return Task.CompletedTask;
    }

    public Task StartAsync()
    {
        _logger?.LogInformation($"{Name} started");
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        _logger?.LogInformation($"{Name} stopped");
        return Task.CompletedTask;
    }

    public Task ShutdownAsync()
    {
        _logger?.LogInformation($"{Name} shutdown");
        return Task.CompletedTask;
    }

    public ModuleHealth GetHealth()
    {
        return ModuleHealth.Healthy;
    }

    private void OnGpsPosition(GpsPositionMessage msg)
    {
        _logger?.LogDebug($"GPS: {msg.Latitude:F6}, {msg.Longitude:F6}");
        // Process GPS data here
    }
}
```

### Step 3: Build and Test

```bash
dotnet build
dotnet test
```

---

## Module Lifecycle

Modules go through a well-defined lifecycle:

```
┌─────────────────────────────────────────────────────────┐
│                    Module Lifecycle                     │
└─────────────────────────────────────────────────────────┘

    Load Request
         │
         ▼
    ┌─────────────┐
    │  Loading    │  - Check dependencies
    └──────┬──────┘  - Validate module
           │
           ▼
    ┌─────────────┐
    │Initializing │  - InitializeAsync() called
    └──────┬──────┘  - Subscribe to messages
           │         - Setup resources
           ▼
    ┌─────────────┐
    │  Starting   │  - StartAsync() called
    └──────┬──────┘  - Begin processing
           │
           ▼
    ┌─────────────┐
    │   Running   │  - Normal operation
    └──────┬──────┘  - Process messages
           │         - Perform work
           │
    ┌──────┴──────┐
    │ Hot Reload  │  (Optional)
    │   Request   │
    └──────┬──────┘
           │
           ▼
    ┌─────────────┐
    │  Stopping   │  - StopAsync() called
    └──────┬──────┘  - Stop processing
           │
           ▼
    ┌─────────────┐
    │Shutting Down│  - ShutdownAsync() called
    └──────┬──────┘  - Cleanup resources
           │         - Auto-unsubscribe from bus
           ▼
    ┌─────────────┐
    │  Unloaded   │
    └─────────────┘
```

### Lifecycle Methods

#### InitializeAsync(IModuleContext context)

Called once when module is loaded. Use this to:
- Store the context for later use
- Subscribe to messages
- Load configuration
- Initialize data structures

**Do NOT:** Start background tasks, open hardware connections, or begin processing data.

```csharp
public Task InitializeAsync(IModuleContext context)
{
    _context = context;
    _messageBus = context.MessageBus;
    _logger = context.Logger;
    _config = context.Configuration;

    // Subscribe to messages
    _messageBus.Subscribe<GpsPositionMessage>(OnGpsPosition);
    _messageBus.Subscribe<IMUDataMessage>(OnIMUData);

    // Load settings
    _settings = _config.GetSection("MyModule").Get<MyModuleSettings>();

    _logger.LogInformation($"{Name} initialized successfully");
    return Task.CompletedTask;
}
```

#### StartAsync()

Called after all modules are initialized. Use this to:
- Start background tasks
- Open hardware connections (serial, UDP, CAN)
- Begin data acquisition or processing

```csharp
private CancellationTokenSource? _cts;
private Task? _processingTask;

public Task StartAsync()
{
    _cts = new CancellationTokenSource();

    // Start background processing
    _processingTask = Task.Run(async () =>
    {
        while (!_cts.Token.IsCancellationRequested)
        {
            await ProcessDataAsync();
            await Task.Delay(100, _cts.Token);
        }
    }, _cts.Token);

    _logger?.LogInformation($"{Name} started");
    return Task.CompletedTask;
}
```

#### StopAsync()

Called when module needs to stop (before unload or reload). Use this to:
- Stop background tasks
- Close hardware connections
- Flush buffers

```csharp
public async Task StopAsync()
{
    _logger?.LogInformation($"{Name} stopping...");

    // Signal background tasks to stop
    _cts?.Cancel();

    // Wait for tasks to complete (with timeout)
    if (_processingTask != null)
    {
        await Task.WhenAny(_processingTask, Task.Delay(5000));
    }

    _logger?.LogInformation($"{Name} stopped");
}
```

#### ShutdownAsync()

Called last for final cleanup. Use this to:
- Release unmanaged resources
- Save state if needed
- Final logging

Note: Message bus subscriptions are automatically cleaned up - you don't need to unsubscribe.

```csharp
public Task ShutdownAsync()
{
    // Dispose resources
    _cts?.Dispose();

    // Save state if needed
    SaveStateToFile();

    _logger?.LogInformation($"{Name} shutdown complete");
    return Task.CompletedTask;
}
```

#### GetHealth()

Called periodically to check module health. Return:
- `ModuleHealth.Healthy` - Operating normally
- `ModuleHealth.Degraded` - Working but with issues
- `ModuleHealth.Unhealthy` - Critical problems
- `ModuleHealth.Unknown` - Unable to determine

```csharp
public ModuleHealth GetHealth()
{
    if (_serialPort?.IsOpen != true)
        return ModuleHealth.Unhealthy;

    if (_messagesPerSecond < 5)
        return ModuleHealth.Degraded;

    return ModuleHealth.Healthy;
}
```

---

## Message Bus Communication

The message bus is the heart of the microkernel. Modules communicate by publishing and subscribing to messages.

### Message Types

Messages are **structs** for zero-allocation performance:

```csharp
public struct GpsPositionMessage
{
    public double Latitude;
    public double Longitude;
    public double Altitude;
    public double Heading;
    public double Speed;
    public GpsFixQuality FixQuality;
    public int SatelliteCount;
    public long TimestampMs;
}
```

### Subscribing to Messages

Subscribe in `InitializeAsync()`:

```csharp
public Task InitializeAsync(IModuleContext context)
{
    _messageBus = context.MessageBus;

    // Basic subscription
    _messageBus.Subscribe<GpsPositionMessage>(OnGpsPosition);

    // Priority subscription (higher = called first)
    _messageBus.Subscribe<SteerCommandMessage>(OnSteerCommand, priority: 10);

    return Task.CompletedTask;
}

private void OnGpsPosition(GpsPositionMessage msg)
{
    _logger?.LogDebug($"GPS: Lat={msg.Latitude:F6}, Lon={msg.Longitude:F6}, Speed={msg.Speed:F2} m/s");

    // Process GPS data
    UpdateVehiclePosition(msg);
}
```

### Publishing Messages

Publish messages when you have data to share:

```csharp
// Publish GPS position (from IO module)
_messageBus.Publish(new GpsPositionMessage
{
    Latitude = latitude,
    Longitude = longitude,
    Altitude = altitude,
    Heading = heading,
    Speed = speed,
    FixQuality = GpsFixQuality.RTK_Fixed,
    SatelliteCount = 12,
    TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
});

// Publish steer command (from autosteer module)
_messageBus.Publish(new SteerCommandMessage
{
    SteerAngleDegrees = steerAngle,
    SpeedPWM = speedPWM,
    TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
});
```

### Common Message Types

**Input Messages** (from hardware):
- `RawDataReceivedMessage` - Raw bytes from serial/UDP/CAN
- `GpsPositionMessage` - Parsed GPS position
- `IMUDataMessage` - Roll, pitch, yaw from IMU
- `HardwareStatusMessage` - Status from Arduino/Teensy

**Internal Messages**:
- `GuidanceLineMessage` - AB line or curve for autosteer
- `FieldBoundaryMessage` - Field boundary points

**Output Messages** (to hardware):
- `SteerCommandMessage` - Steer angle and speed
- `SectionControlMessage` - Section on/off states
- `RelayCommandMessage` - Relay control
- `RawDataToSendMessage` - Raw bytes to send

---

## Module Categories

Modules are loaded in category order to ensure dependencies are satisfied:

```csharp
public enum ModuleCategory
{
    IO = 0,              // Load first: Serial, UDP, CAN drivers
    DataProcessing = 10,  // PGN parser, Kalman filter
    Navigation = 20,      // Guidance algorithms
    Control = 30,         // Autosteer, section control
    Visualization = 40,   // Mapping, UI
    Logging = 50,         // Data logging
    Integration = 60,     // External services
    Monitoring = 70       // System monitoring - load last
}
```

Choose the appropriate category for your module:

| Category | Purpose | Examples |
|----------|---------|----------|
| **IO** | Hardware communication | Serial driver, UDP receiver, CAN bus |
| **DataProcessing** | Parse and filter data | PGN parser, NMEA parser, Kalman filter |
| **Navigation** | Calculate guidance | AB line, curves, boundary following |
| **Control** | Actuate hardware | Autosteer PID, section control |
| **Visualization** | Display data | Field map, status display |
| **Logging** | Record data | CSV logger, database writer |
| **Integration** | External systems | Cloud sync, telemetry |
| **Monitoring** | System health | Performance monitor, diagnostics |

---

## Dependencies

If your module requires another module, specify it in `Dependencies`:

```csharp
public class AutosteerModule : IAgModule
{
    public string Name => "Autosteer Controller";
    public ModuleCategory Category => ModuleCategory.Control;

    // This module requires GPS and PGN Parser
    public string[] Dependencies => new[] { "GPS IO", "PGN Parser" };

    // ...
}
```

The kernel will:
1. Load dependency modules first
2. Refuse to load your module if dependencies are missing
3. Prevent unloading dependency modules while your module is loaded

---

## Best Practices

### 1. Keep InitializeAsync() Fast

Initialization should be quick (< 100ms). Don't do heavy work here.

**Good:**
```csharp
public Task InitializeAsync(IModuleContext context)
{
    _messageBus = context.MessageBus;
    _messageBus.Subscribe<GpsPositionMessage>(OnGpsPosition);
    return Task.CompletedTask;
}
```

**Bad:**
```csharp
public Task InitializeAsync(IModuleContext context)
{
    _messageBus = context.MessageBus;

    // DON'T do heavy I/O in init!
    var largeFile = File.ReadAllText("10GB_file.txt");
    ProcessData(largeFile);

    return Task.CompletedTask;
}
```

### 2. Handle Message Processing Errors

Your message handlers can be called from any thread. Handle errors gracefully:

```csharp
private void OnGpsPosition(GpsPositionMessage msg)
{
    try
    {
        // Process message
        UpdatePosition(msg);
    }
    catch (Exception ex)
    {
        _logger?.LogError(ex, "Error processing GPS position");
        // Don't rethrow - would crash other subscribers
    }
}
```

### 3. Use Cancellation Tokens

Respect the shutdown token from context:

```csharp
public Task StartAsync()
{
    _processingTask = Task.Run(async () =>
    {
        while (!_context.AppShutdownToken.IsCancellationRequested)
        {
            await ProcessDataAsync();
            await Task.Delay(100, _context.AppShutdownToken);
        }
    }, _context.AppShutdownToken);

    return Task.CompletedTask;
}
```

### 4. Log Appropriately

Use log levels correctly:

```csharp
_logger.LogTrace("GPS message received");           // Very detailed
_logger.LogDebug($"Position: {lat}, {lon}");        // Debugging
_logger.LogInformation("GPS fix acquired");          // Important events
_logger.LogWarning("GPS signal weak");               // Potential issues
_logger.LogError(ex, "Failed to parse GPS data");    // Errors
_logger.LogCritical("Hardware failure detected");    // Critical problems
```

### 5. Make Modules Testable

Structure modules for easy testing:

```csharp
public class GpsParserModule : IAgModule
{
    private readonly IGpsParser _parser;

    // Constructor injection for testing
    public GpsParserModule(IGpsParser? parser = null)
    {
        _parser = parser ?? new NmeaParser();
    }

    // Testable parsing logic
    internal GpsPositionMessage ParseNmea(string nmea)
    {
        return _parser.Parse(nmea);
    }
}
```

### 6. Implement Health Checks

Provide meaningful health status:

```csharp
public ModuleHealth GetHealth()
{
    var timeSinceLastMessage = DateTime.UtcNow - _lastMessageTime;

    if (timeSinceLastMessage > TimeSpan.FromSeconds(10))
        return ModuleHealth.Unhealthy;  // No data for 10s

    if (_errorCount > 10)
        return ModuleHealth.Degraded;   // Many errors

    if (_messagesPerSecond < 5)
        return ModuleHealth.Degraded;   // Low throughput

    return ModuleHealth.Healthy;
}
```

---

## Examples

### Example 1: GPS Simulator Module

A complete example that simulates GPS data:

```csharp
using AgOpenGPS.ModuleContracts;
using AgOpenGPS.ModuleContracts.Messages;
using Microsoft.Extensions.Logging;

namespace AgOpenGPS.Modules.GpsSimulator;

public class GpsSimulatorModule : IAgModule
{
    public string Name => "GPS Simulator";
    public Version Version => new Version(1, 0, 0);
    public ModuleCategory Category => ModuleCategory.IO;
    public string[] Dependencies => Array.Empty<string>();

    private IModuleContext? _context;
    private IMessageBus? _messageBus;
    private ILogger? _logger;
    private CancellationTokenSource? _cts;
    private Task? _simulationTask;

    // Simulation state
    private double _latitude = 45.5;
    private double _longitude = -122.6;
    private double _heading = 45.0;
    private const double Speed = 2.0; // m/s

    public Task InitializeAsync(IModuleContext context)
    {
        _context = context;
        _messageBus = context.MessageBus;
        _logger = context.Logger;

        _logger.LogInformation($"{Name} initialized at position {_latitude}, {_longitude}");
        return Task.CompletedTask;
    }

    public Task StartAsync()
    {
        _cts = new CancellationTokenSource();
        _simulationTask = Task.Run(SimulationLoop, _cts.Token);

        _logger?.LogInformation($"{Name} started - generating GPS at 10Hz");
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        _cts?.Cancel();
        _logger?.LogInformation($"{Name} stopped");
        return Task.CompletedTask;
    }

    public Task ShutdownAsync()
    {
        _cts?.Dispose();
        return Task.CompletedTask;
    }

    public ModuleHealth GetHealth() => ModuleHealth.Healthy;

    private async Task SimulationLoop()
    {
        while (!_cts!.Token.IsCancellationRequested)
        {
            // Update simulated position
            UpdatePosition();

            // Publish GPS message
            _messageBus!.Publish(new GpsPositionMessage
            {
                Latitude = _latitude,
                Longitude = _longitude,
                Altitude = 100.0,
                Heading = _heading,
                Speed = Speed,
                FixQuality = GpsFixQuality.RTK_Fixed,
                SatelliteCount = 12,
                TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });

            // 10Hz update rate
            await Task.Delay(100, _cts.Token);
        }
    }

    private void UpdatePosition()
    {
        // Simple movement simulation
        const double metersPerDegree = 111320.0;
        var headingRad = _heading * Math.PI / 180.0;

        var deltaLat = (Speed * 0.1 * Math.Cos(headingRad)) / metersPerDegree;
        var deltaLon = (Speed * 0.1 * Math.Sin(headingRad)) / metersPerDegree;

        _latitude += deltaLat;
        _longitude += deltaLon;
    }
}
```

### Example 2: Simple Autosteer Module

Calculates steering based on GPS and guidance line:

```csharp
using AgOpenGPS.ModuleContracts;
using AgOpenGPS.ModuleContracts.Messages;
using Microsoft.Extensions.Logging;

namespace AgOpenGPS.Modules.SimpleAutosteer;

public class SimpleAutosteerModule : IAgModule
{
    public string Name => "Simple Autosteer";
    public Version Version => new Version(1, 0, 0);
    public ModuleCategory Category => ModuleCategory.Control;
    public string[] Dependencies => Array.Empty<string>();

    private IModuleContext? _context;
    private IMessageBus? _messageBus;
    private ILogger? _logger;

    private bool _engaged;
    private double _targetHeading = 45.0;
    private const double Kp = 2.0; // Proportional gain

    public Task InitializeAsync(IModuleContext context)
    {
        _context = context;
        _messageBus = context.MessageBus;
        _logger = context.Logger;

        // Subscribe to GPS positions
        _messageBus.Subscribe<GpsPositionMessage>(OnGpsPosition);

        // Subscribe to guidance line updates
        _messageBus.Subscribe<GuidanceLineMessage>(OnGuidanceLine);

        _logger.LogInformation($"{Name} initialized");
        return Task.CompletedTask;
    }

    public Task StartAsync()
    {
        _engaged = true;
        _logger?.LogInformation($"{Name} started - autosteer engaged");
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        _engaged = false;
        _logger?.LogInformation($"{Name} stopped");
        return Task.CompletedTask;
    }

    public Task ShutdownAsync() => Task.CompletedTask;

    public ModuleHealth GetHealth() => ModuleHealth.Healthy;

    private void OnGuidanceLine(GuidanceLineMessage msg)
    {
        _targetHeading = msg.HeadingDegrees;
        _logger?.LogInformation($"New guidance line: heading {_targetHeading:F1}°");
    }

    private void OnGpsPosition(GpsPositionMessage msg)
    {
        if (!_engaged) return;

        // Calculate heading error
        var headingError = _targetHeading - msg.Heading;

        // Normalize to -180 to +180
        while (headingError > 180) headingError -= 360;
        while (headingError < -180) headingError += 360;

        // Simple proportional control
        var steerAngle = headingError * Kp;

        // Clamp to ±45 degrees
        steerAngle = Math.Clamp(steerAngle, -45, 45);

        // Publish steer command
        _messageBus!.Publish(new SteerCommandMessage
        {
            SteerAngleDegrees = steerAngle,
            SpeedPWM = 128,
            TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        });

        _logger?.LogDebug($"Heading: {msg.Heading:F1}°, Error: {headingError:F1}°, Steer: {steerAngle:F1}°");
    }
}
```

### Example 3: Data Logger Module

Logs all GPS positions to CSV file:

```csharp
using AgOpenGPS.ModuleContracts;
using AgOpenGPS.ModuleContracts.Messages;
using Microsoft.Extensions.Logging;
using System.Text;

namespace AgOpenGPS.Modules.DataLogger;

public class DataLoggerModule : IAgModule
{
    public string Name => "Data Logger";
    public Version Version => new Version(1, 0, 0);
    public ModuleCategory Category => ModuleCategory.Logging;
    public string[] Dependencies => Array.Empty<string>();

    private IModuleContext? _context;
    private IMessageBus? _messageBus;
    private ILogger? _logger;
    private StreamWriter? _logFile;
    private long _messageCount;

    public Task InitializeAsync(IModuleContext context)
    {
        _context = context;
        _messageBus = context.MessageBus;
        _logger = context.Logger;

        // Subscribe to GPS positions
        _messageBus.Subscribe<GpsPositionMessage>(OnGpsPosition);

        _logger.LogInformation($"{Name} initialized");
        return Task.CompletedTask;
    }

    public async Task StartAsync()
    {
        // Create log file
        var filename = $"gps_log_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
        _logFile = new StreamWriter(filename, false, Encoding.UTF8);

        // Write CSV header
        await _logFile.WriteLineAsync("Timestamp,Latitude,Longitude,Altitude,Heading,Speed,FixQuality");

        _logger?.LogInformation($"{Name} started - logging to {filename}");
    }

    public async Task StopAsync()
    {
        if (_logFile != null)
        {
            await _logFile.FlushAsync();
            _logFile.Close();
        }

        _logger?.LogInformation($"{Name} stopped - logged {_messageCount} messages");
    }

    public Task ShutdownAsync()
    {
        _logFile?.Dispose();
        return Task.CompletedTask;
    }

    public ModuleHealth GetHealth() => ModuleHealth.Healthy;

    private async void OnGpsPosition(GpsPositionMessage msg)
    {
        if (_logFile == null) return;

        try
        {
            var line = $"{msg.TimestampMs},{msg.Latitude:F8},{msg.Longitude:F8}," +
                      $"{msg.Altitude:F2},{msg.Heading:F2},{msg.Speed:F2},{msg.FixQuality}";

            await _logFile.WriteLineAsync(line);
            Interlocked.Increment(ref _messageCount);

            // Flush every 10 messages
            if (_messageCount % 10 == 0)
            {
                await _logFile.FlushAsync();
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error writing to log file");
        }
    }
}
```

---

## Testing Your Module

### Unit Tests

Create unit tests to verify module behavior:

```csharp
using Xunit;
using AgOpenGPS.Modules.MyModule;
using AgOpenGPS.ModuleContracts;

public class MyModuleTests
{
    [Fact]
    public async Task Module_ShouldInitialize()
    {
        // Arrange
        var module = new MyModule();
        var context = CreateTestContext();

        // Act
        await module.InitializeAsync(context);

        // Assert
        Assert.Equal("My Custom Module", module.Name);
        Assert.Equal(ModuleHealth.Healthy, module.GetHealth());
    }

    [Fact]
    public void Module_ShouldProcessGpsMessage()
    {
        // Arrange
        var module = new MyModule();
        var context = CreateTestContext();
        await module.InitializeAsync(context);

        var messageBus = context.MessageBus;
        var received = false;

        // Act
        messageBus.Publish(new GpsPositionMessage
        {
            Latitude = 45.5,
            Longitude = -122.6,
            // ...
        });

        // Assert
        Assert.True(received);
    }

    private IModuleContext CreateTestContext()
    {
        // Create mock context for testing
        // ...
    }
}
```

---

## Next Steps

1. **Study existing modules** in the `AgOpenGPS.Modules.*` directories
2. **Run the tests** to see modules in action: `dotnet test`
3. **Create your own module** following this guide
4. **Join the community** and share your modules!

---

## Additional Resources

- [Message Bus Performance](./MESSAGE_BUS_PERFORMANCE.md)
- [Module Hot Reload Guide](./HOT_RELOAD.md)
- [Debugging Modules](./DEBUGGING.md)
- [Architecture Overview](./ARCHITECTURE.md)

---

**Happy Module Building!**
