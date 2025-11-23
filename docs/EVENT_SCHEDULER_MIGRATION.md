# EventScheduler Migration Guide

## Overview

The **EventScheduler** is a unified event scheduler that replaces the legacy RateScheduler, providing a modern approach that combines:

1. **Rate-based scheduled methods** (fixed Hz ticks)
2. **Time-based async delays** (await timeProvider.Delay())

## Key Advantages

### 1. Unified Event System
The scheduler determines the next event (whichever comes first - rate-based or time-based) and handles timing automatically.

**Before (RateScheduler + manual coordination):**
```csharp
// Rate-based events
scheduler.Schedule(method, 10.0); // 10Hz

// Time-based events (manual coordination required)
await timeProvider.Delay(TimeSpan.FromSeconds(1));
timeProvider.Advance(...); // Manual time management!
```

**After (EventScheduler):**
```csharp
// Both types work together automatically
scheduler.Schedule(method, 10.0);  // Rate-based

await timeProvider.Delay(TimeSpan.FromSeconds(1)); // Time-based
// Scheduler automatically handles both!
```

### 2. Works with Both Real-Time and Simulated Time

**SystemTimeProvider** (Production):
- Real-time execution
- Background thread
- Normal wall-clock time

**SimulatedTimeProvider** (Testing/Offline):
- Controlled time advancement
- Unlimited speed simulation
- Deterministic testing

### 3. Three Execution Modes

#### Mode 1: Background Thread (Production)
```csharp
var scheduler = new EventScheduler(systemTimeProvider);
scheduler.Start();  // Runs in background thread
// ... application runs ...
scheduler.Stop();
```

#### Mode 2: Real-Time Async (Time-scaled)
```csharp
var scheduler = new EventScheduler(simTimeProvider);
simTimeProvider.TimeScale = 10.0; // 10x speed

await scheduler.RunRealTimeAsync(new[] { tasks });
```

#### Mode 3: Simulation (Unlimited Speed)
```csharp
var scheduler = new EventScheduler(simTimeProvider);

await scheduler.RunSimulationAsync(new[] { tasks });
// Runs as fast as possible, advancing time instantly
```

## Configuration

### Enable EventScheduler in ApplicationCore

Add to `appsettings.json`:
```json
{
  "Core": {
    "UseScheduler": true
  }
}
```

EventScheduler is now the only scheduler implementation. RateScheduler has been removed.

## API Compatibility

EventScheduler implements `IScheduler`, providing compatibility with the existing module system:

```csharp
// Both methods supported
IScheduledMethod Schedule(Action<long, long> method, double rateHz, string? name = null);
void Unschedule(IScheduledMethod handle);
SchedulerStatistics GetStatistics();
```

### Additional Features

```csharp
// Simple action (no tick parameters)
var handle = scheduler.Schedule(() =>
{
    Console.WriteLine("Hello!");
}, 10.0, "MyMethod");

// Pause/Resume
handle.Pause();
handle.Resume();

// Dispose to unschedule
handle.Dispose();
```

## Migration Checklist

- [x] EventScheduler works with ITimeProvider
- [x] Implements IScheduler interface
- [x] Supports Start/Stop for background thread
- [x] ApplicationCore uses EventScheduler exclusively
- [x] Simplified configuration (single UseScheduler flag)
- [x] RateScheduler removed
- [x] SimulationRunner removed
- [x] All existing tests pass
- [ ] Production validation

## Architecture

### Clean Separation of Concerns

**SimulatedTimeProvider.Delay():**
- ONLY creates pending delays
- Waits for external time advancement
- No automatic time progression

**EventScheduler:**
- Handles ALL time advancement
- Coordinates rate-based and time-based events
- Determines next event automatically

### Event Loop Logic

```
while (running):
    1. Find next event (min of scheduled methods and pending delays)
    2. Wait/advance to that time
    3. Execute all methods at that time
    4. Repeat
```

## Performance Considerations

### EventScheduler
- **Pros**: Unified event system, cleaner architecture, better testability, works with both real and simulated time
- **Cons**: Slightly more overhead due to event coordination logic compared to dedicated rate-only scheduler
- **Use Cases**: All scenarios - production, testing, and simulation

## Examples

See `EXAMPLES/EventSchedulerExample.cs` for comprehensive demos showing:
1. Basic rate-based scheduling
2. Mixed rate-based and time-based events
3. Simulation mode (unlimited speed)
4. Real-time mode (time-scaled)
5. Pause/resume functionality

## Troubleshooting

### EventScheduler not starting
Check that `Core:UseScheduler` is `true` in configuration.

### "requires SimulatedTimeProvider" error
`RunSimulationAsync()` only works with SimulatedTimeProvider. Use `RunRealTimeAsync()` or `Start()` for SystemTimeProvider.

### Background thread not stopping
Ensure you call `Stop()` before disposing. The Stop() method waits up to 5 seconds for graceful shutdown.

## Future Enhancements

- [ ] Module name tracking in statistics
- [ ] Jitter/latency metrics
- [ ] Adaptive rate scheduling based on load
- [ ] Support for priority-based scheduling
- [ ] Integration with async/await patterns for modules
