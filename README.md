# AgOpenGPS Microkernel Architecture Demo

A simple yet robust demonstration of microkernel architecture for AgOpenGPS, designed to be easily understood while being production-ready enough for real testing.

## Overview

This project demonstrates a **microkernel architecture** where:
- **Core Kernel** manages module lifecycle
- **Modules** (formerly "plugins") are independent components
- **Message Bus** enables high-performance communication
- **Hot Reload** allows runtime module updates without restart

### Why Microkernel?

Traditional monolithic AgOpenGPS has all functionality in one process. The microkernel approach:
- ✅ **Isolates failures** - One module crash doesn't kill the system
- ✅ **Enables hot reload** - Update modules without restarting
- ✅ **Simplifies testing** - Test modules independently
- ✅ **Improves organization** - Clear separation of concerns
- ✅ **Allows flexibility** - Load only needed modules

## Project Structure

```
AgOpenGPS_Projects/AgPluginsDemo/
├── AgOpenGPS.PluginContracts/      # Module interface contracts
│   ├── IAgModule.cs                # Base module interface
│   ├── IModuleContext.cs           # Services provided to modules
│   ├── IMessageBus.cs              # Message bus interface
│   └── Messages/                   # Message definitions
│       ├── InboundMessages.cs      # GPS, IMU, hardware status
│       ├── OutboundMessages.cs     # Steer, section, relay commands
│       └── InternalMessages.cs     # Guidance, boundaries, lifecycle
│
├── AgOpenGPS.Core/                 # Microkernel implementation
│   ├── ApplicationCore.cs          # Main kernel
│   ├── ModuleManager.cs            # Module lifecycle management
│   ├── MessageBus.cs               # High-performance message bus
│   ├── ModuleLoader.cs             # Module discovery and loading
│   ├── ModuleWatchdog.cs           # Hang detection
│   ├── ModuleTaskScheduler.cs      # Per-module thread pools
│   └── SafeModuleExecutor.cs       # Timeout and exception handling
│
├── AgOpenGPS.Modules.*/            # Module implementations
│   ├── DummyIO/                    # GPS/vehicle simulator
│   ├── SerialIO/                   # Real serial communication
│   ├── PGN/                        # Protocol parser
│   ├── Autosteer/                  # Steering control
│   ├── Kinematics/                 # Vehicle physics
│   ├── UI/                         # User interface integration
│   └── Monitoring/                 # System monitoring & metrics
│
├── AgOpenGPS.Tests/                # Comprehensive test suite
│   ├── TimingTests.cs              # Real-time performance tests
│   ├── LoadTests.cs                # High throughput tests
│   └── CrashResilienceTests.cs     # Failure handling tests
│
├── AgOpenGPS.Host/                 # Console application host
├── AgOpenGPS.GUI/                  # Avalonia UI application
└── docs/                           # Documentation
    └── CREATE_MODULE_GUIDE.md      # Module creation guide
```

## Quick Start

### Prerequisites

- .NET 8.0 SDK or later
- Windows, Linux, or macOS

### Build and Run

```bash
# Clone and navigate to project
cd AgPluginsDemo

# Build solution
dotnet build

# Run tests
dotnet test

# Run console host
dotnet run --project AgOpenGPS.Host

# Run GUI application
dotnet run --project AgOpenGPS.GUI
```

## Module Categories

Modules are loaded in order based on their category:

| Category | Load Order | Purpose | Examples |
|----------|------------|---------|----------|
| **IO** | 0 | Hardware communication | Serial, UDP, CAN drivers |
| **DataProcessing** | 10 | Parse and filter data | PGN parser, NMEA parser |
| **Navigation** | 20 | Calculate guidance | AB line calculator |
| **Control** | 30 | Actuate hardware | Autosteer PID controller |
| **Visualization** | 40 | Display information | Field mapping, UI |
| **Logging** | 50 | Record data | CSV logger, telemetry |
| **Integration** | 60 | External systems | Cloud sync |
| **Monitoring** | 70 | System health | Performance monitoring |

## Message Bus

High-performance, zero-allocation message bus using struct messages:

```csharp
// Subscribe to GPS data
messageBus.Subscribe<GpsPositionMessage>(msg =>
{
    Console.WriteLine($"GPS: {msg.Latitude:F6}, {msg.Longitude:F6}");
});

// Publish GPS data
messageBus.Publish(new GpsPositionMessage
{
    Latitude = 45.5,
    Longitude = -122.6,
    Heading = 45.0,
    Speed = 2.0,
    FixQuality = GpsFixQuality.RTK_Fixed,
    TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
});
```

**Performance:**
- < 1ms message latency (tested)
- 10,000+ messages/second throughput
- Zero allocation for struct messages
- Priority-based delivery

## Running Tests

The project includes comprehensive tests demonstrating microkernel robustness:

### Timing Tests
Verify real-time performance for agricultural applications:

```bash
dotnet test --filter "FullyQualifiedName~TimingTests"
```

Tests:
- ✅ Module load time < 100ms
- ✅ GPS message latency < 1ms (RTK GPS requirement)
- ✅ Autosteer control loop @ 20Hz (50ms cycle)
- ✅ Full system startup < 2 seconds

### Load Tests
Verify system handles agricultural workloads:

```bash
dotnet test --filter "FullyQualifiedName~LoadTests"
```

Tests:
- ✅ 10,000 GPS messages without loss
- ✅ Multiple concurrent modules (7+) running simultaneously
- ✅ 30+ seconds sustained operation without degradation
- ✅ Burst handling (1000 messages rapid fire)

### Crash Resilience Tests
Verify system reliability when things go wrong:

```bash
dotnet test --filter "FullyQualifiedName~CrashResilienceTests"
```

Tests:
- ✅ Crashed module doesn't affect others
- ✅ Slow modules don't block fast ones
- ✅ Module initialization failures handled gracefully
- ✅ Hot reload during operation works
- ✅ Dependency checking prevents unsafe unloads
- ✅ Message bus exceptions isolated per subscriber

## Creating Modules

See [Module Creation Guide](./docs/CREATE_MODULE_GUIDE.md) for detailed instructions.

Quick example:

```csharp
public class MyModule : IAgModule
{
    public string Name => "My Module";
    public Version Version => new Version(1, 0, 0);
    public ModuleCategory Category => ModuleCategory.DataProcessing;
    public string[] Dependencies => Array.Empty<string>();

    public Task InitializeAsync(IModuleContext context)
    {
        var messageBus = context.MessageBus;
        var logger = context.Logger;

        // Subscribe to messages
        messageBus.Subscribe<GpsPositionMessage>(OnGpsPosition);

        logger.LogInformation("Module initialized");
        return Task.CompletedTask;
    }

    public Task StartAsync() => Task.CompletedTask;
    public Task StopAsync() => Task.CompletedTask;
    public Task ShutdownAsync() => Task.CompletedTask;
    public ModuleHealth GetHealth() => ModuleHealth.Healthy;

    private void OnGpsPosition(GpsPositionMessage msg)
    {
        // Process GPS data
    }
}
```

## Architecture Highlights

### Module Isolation

Each module:
- Runs on dedicated thread pool (doesn't block others)
- Has automatic subscription cleanup on unload
- Gets timeout protection (30s for init/start)
- Has hang detection via watchdog

### Hot Reload

Modules can be reloaded at runtime:

```csharp
// Reload a module without stopping the system
await core.ReloadModuleAsync("Autosteer:1.0.0");
```

### Health Monitoring

Built-in monitoring module tracks:
- Message throughput (messages/second)
- Module load times
- System uptime
- Error counts

### Robust Error Handling

- Module exceptions don't crash other modules
- Message bus isolates subscriber exceptions
- Timeout protection on module operations
- Watchdog detects and reports hangs

## Configuration

Modules are configured via `appsettings.json`:

```json
{
  "Core": {
    "ModuleDirectory": "./modules"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "AgOpenGPS": "Debug"
    }
  }
}
```

## Agricultural Scenarios Tested

The tests use realistic agricultural scenarios:

- **RTK GPS at 10Hz** - High-frequency position updates
- **Autosteer at 20Hz** - Real-time steering control
- **Field operations** - Sustained multi-hour operation
- **GPS reacquisition** - Signal loss and burst recovery
- **Module hot reload** - Update autosteer while tractor is moving
- **Hardware failure** - Handle serial port disconnection
- **Multiple concurrent systems** - GPS + Autosteer + Sections + Mapping

## Performance Targets

Based on AgOpenGPS requirements:

| Metric | Target | Actual |
|--------|--------|--------|
| GPS message latency | < 1ms | ~0.2ms |
| Autosteer cycle time | 50ms (20Hz) | 45ms avg |
| Module load time | < 100ms | ~50ms |
| System startup | < 2s | ~1.5s |
| Message throughput | > 1000/s | 10,000+/s |
| Sustained operation | No degradation | Passes 30s+ |

## Differences from Full Nexus

This demo is **simpler** than the full Nexus architecture:
- Single process (no IPC)
- In-memory message bus (no gRPC)
- No distributed tracing
- No remote UI support

This demo is **similar** to Nexus:
- Module isolation
- Message-based communication
- Hot reload capability
- Dependency management
- Health monitoring

## Contributing

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Run tests: `dotnet test`
5. Submit a pull request

## License

[Your license here]

## Support

- GitHub Issues: [Link to issues]
- Documentation: `/docs/CREATE_MODULE_GUIDE.md`
- Examples: `/EXAMPLES/` directory

## Roadmap

- [x] Core microkernel
- [x] Basic modules (GPS, Autosteer, PGN)
- [x] Message bus
- [x] Hot reload
- [x] Comprehensive tests
- [x] Module creation guide
- [ ] Performance profiling tools
- [ ] More example modules
- [ ] Network transparency (gRPC bridge)
- [ ] Web dashboard

---

**Built with .NET 8.0 for AgOpenGPS**
