# Tick-Based vs Thread Pool Simulation Architecture

**Date:** 2025-01-17
**Context:** Deciding on simulation time advancement strategy for unlimited speed scenarios

## Overview

Two fundamentally different approaches to running simulations with multiple concurrent components:

1. **Tick-Based System** (Deterministic, Sequential)
2. **Thread Pool System** (Parallel, Real-Time)

## Architecture Comparison

### Tick-Based System

```
┌─────────────────────────────────────────┐
│         Simulation Loop (Main Thread)   │
│                                         │
│  while (simulating) {                   │
│      time = GetNextEventTime()          │
│      AdvanceTime(time)                  │
│      ProcessAllEventsAtCurrentTime()    │
│      Tick() all modules                 │
│  }                                      │
└─────────────────────────────────────────┘
            │
            ├─> Module A.Tick()
            ├─> Module B.Tick()
            ├─> Module C.Tick()
            └─> Module D.Tick()

(All modules execute sequentially)
```

**Key Characteristics:**
- Single execution thread
- Discrete time steps
- All modules execute in order per tick
- Time only advances between ticks

**Code Example:**
```csharp
public class TickBasedSimulation
{
    private List<ITickableModule> _modules = new();
    private DateTimeOffset _currentTime;
    private double _tickRate = 0.01; // 10ms ticks

    public void RunSimulation(TimeSpan duration)
    {
        var endTime = _currentTime + duration;

        while (_currentTime < endTime)
        {
            // 1. Advance time by fixed amount
            _currentTime = _currentTime.AddSeconds(_tickRate);

            // 2. Process all modules sequentially
            foreach (var module in _modules)
            {
                module.Tick(_currentTime, _tickRate);
            }

            // 3. Time is consistent for entire tick
            // All modules see same _currentTime
        }
    }
}

public interface ITickableModule
{
    void Tick(DateTimeOffset currentTime, double deltaTime);
}

// Module implementation
public class GPSModule : ITickableModule
{
    public void Tick(DateTimeOffset currentTime, double deltaTime)
    {
        // Read GPS data
        // Update position
        // Publish message (with currentTime)

        // No async/await - everything happens synchronously
    }
}
```

### Thread Pool System (Current AgOpenGPS Architecture)

```
┌─────────────────────────────────────────┐
│         Thread Pool                      │
│                                          │
│  [Thread 1] ─> Module A (async/await)   │
│  [Thread 2] ─> Module B (async/await)   │
│  [Thread 3] ─> Module C (async/await)   │
│  [Thread 4] ─> Module D (async/await)   │
│                                          │
│  All modules run concurrently           │
│  Time advances independently per-module │
└─────────────────────────────────────────┘
```

**Key Characteristics:**
- Multiple threads executing concurrently
- Continuous time (not discrete steps)
- Modules execute independently with async/await
- Time advances whenever a module calls Delay()

**Code Example (Current):**
```csharp
public class ModuleBasedSimulation
{
    private List<IAgModule> _modules = new();
    private ITimeProvider _timeProvider;

    public async Task RunSimulation()
    {
        // Start all modules concurrently
        var tasks = _modules.Select(m => m.RunAsync(_timeProvider)).ToArray();

        // Wait for all to complete
        await Task.WhenAll(tasks);
    }
}

// Module implementation
public class GPSModule : IAgModule
{
    public async Task RunAsync(ITimeProvider timeProvider)
    {
        while (true)
        {
            // Read GPS data
            var position = ReadGPS();

            // Publish with timestamp
            _messageBus.Publish(new GpsPositionMessage {
                // Timestamp added automatically by MessageBus
            });

            // Wait for next cycle
            await timeProvider.Delay(TimeSpan.FromMilliseconds(100)); // 10 Hz
        }
    }
}
```

## Detailed Comparison

### 1. Determinism

**Tick-Based: ✅ Fully Deterministic**
- Same inputs always produce same outputs
- Module execution order is fixed
- Time advances predictably
- Reproducible simulation runs
- Perfect for regression testing

**Thread Pool: ⚠️ Non-Deterministic**
- Thread scheduling affects execution order
- Race conditions possible
- Same inputs may produce different outputs
- Timing depends on system load
- Difficult to reproduce exact behavior

**Example:**

*Tick-Based:*
```
Tick 1: Time=0.00s
  - GPS Module: position=(0,0)
  - Autosteer Module: reads (0,0), computes steer=0°

Tick 2: Time=0.01s
  - GPS Module: position=(0.1,0)
  - Autosteer Module: reads (0.1,0), computes steer=5°

(Always executes in this exact order)
```

*Thread Pool:*
```
Run 1:
  GPS: (0,0) at T=0.000
  Autosteer: reads (0,0), steer=0°
  GPS: (0.1,0) at T=0.015
  Autosteer: reads (0.1,0), steer=5°

Run 2:
  GPS: (0,0) at T=0.000
  GPS: (0.1,0) at T=0.010  ⚠️ Faster this time
  Autosteer: reads (0.1,0), steer=5°  ⚠️ Different sequence!

(Order varies by thread scheduling)
```

### 2. Performance

**Tick-Based: 🐢 Sequential (Slower)**

**Speed Factors:**
- ❌ Single-threaded execution
- ❌ Modules execute sequentially, not in parallel
- ❌ Can't utilize multiple CPU cores effectively
- ✅ Minimal overhead (no thread synchronization)
- ✅ Cache-friendly (sequential access patterns)

**Throughput:**
```
10 modules × 10ms each = 100ms per tick
Max speed: 10 ticks/second
Can simulate: 10 seconds of sim time per second real-time (10x max)
```

**Thread Pool: 🚀 Parallel (Faster)**

**Speed Factors:**
- ✅ Multi-threaded execution
- ✅ Modules run concurrently on multiple cores
- ✅ Can achieve very high simulation speeds (10000x+)
- ❌ Thread synchronization overhead
- ❌ Context switching costs
- ❌ Cache misses from concurrent access

**Throughput:**
```
10 modules × 10ms each (concurrent) = 10ms per cycle
Max speed: 100 cycles/second
Can simulate: Hours per second with unlimited TimeScale
```

### 3. Scalability

**Tick-Based: Poor Horizontal Scaling**

**Characteristics:**
- ❌ Adding more modules linearly increases tick time
- ❌ Cannot distribute across multiple cores effectively
- ❌ Tick time = Sum of all module times
- ✅ Predictable performance degradation
- ✅ Easy to identify bottleneck modules

**Scaling:**
```
1 module:  10ms tick
5 modules: 50ms tick  (5x slower)
10 modules: 100ms tick (10x slower)
50 modules: 500ms tick (50x slower)
```

**Thread Pool: Good Horizontal Scaling**

**Characteristics:**
- ✅ Adding modules doesn't increase cycle time (if cores available)
- ✅ Naturally distributes across CPU cores
- ✅ Bounded by CPU core count, not module count
- ⚠️ Performance depends on system resources
- ⚠️ Harder to predict performance

**Scaling:**
```
1 module:  10ms cycle (on 1 core)
5 modules: 10ms cycle (on 5 cores)
10 modules: 10ms cycle (on 10 cores)
50 modules: 10ms cycle (on 50 cores, if available)
          or 50ms cycle (on 10 cores, 5 modules per core)
```

### 4. Debugging & Testing

**Tick-Based: ✅ Excellent**

**Advantages:**
- ✅ Step through simulation one tick at a time
- ✅ Inspect state between ticks
- ✅ Reproducible failures
- ✅ No race conditions to hunt
- ✅ Breakpoints work reliably
- ✅ Can pause mid-simulation

**Debug Experience:**
```csharp
// Set breakpoint in simulation loop
while (_currentTime < endTime)
{
    _currentTime = _currentTime.AddSeconds(_tickRate);

    foreach (var module in _modules)  // <- BREAKPOINT HERE
    {
        module.Tick(_currentTime, _tickRate);
        // Can inspect every module call, in order
    }
}
```

**Thread Pool: ⚠️ Challenging**

**Challenges:**
- ❌ Multiple threads executing simultaneously
- ❌ Race conditions difficult to reproduce
- ❌ Heisenbugs (problems disappear when debugging)
- ❌ Cannot "step" through parallel execution
- ❌ Breakpoints affect timing
- ❌ Need specialized tools (thread debugging)

**Debug Experience:**
```csharp
// Multiple threads executing concurrently
public async Task RunAsync(ITimeProvider timeProvider)
{
    while (true)
    {
        // When breakpoint hits, other threads keep running!
        // Can't see "global" state easily
        await timeProvider.Delay(TimeSpan.FromMilliseconds(100));
    }
}
```

### 5. Code Complexity

**Tick-Based: ✅ Simpler**

**Characteristics:**
- ✅ Synchronous code (no async/await)
- ✅ No race conditions to worry about
- ✅ Clear execution order
- ✅ Easier to reason about
- ✅ Simpler module interface
- ❌ Need to manage state between ticks

**Code Simplicity:**
```csharp
// Simple, synchronous
public class GPSModule : ITickableModule
{
    private Position _position;

    public void Tick(DateTimeOffset currentTime, double deltaTime)
    {
        // Update position
        _position = CalculateNewPosition(deltaTime);

        // Publish
        _messageBus.Publish(new GpsPositionMessage {
            Position = _position
        });

        // Done! No async, no awaits, no Tasks
    }
}
```

**Thread Pool: ⚠️ More Complex**

**Characteristics:**
- ⚠️ Async/await everywhere
- ⚠️ Must handle race conditions
- ⚠️ Concurrent access to shared state
- ⚠️ Thread-safety considerations
- ⚠️ More complex module interface
- ✅ Natural fit for I/O operations

**Code Complexity:**
```csharp
// More complex with async/await
public class GPSModule : IAgModule
{
    private Position _position;
    private readonly SemaphoreSlim _lock = new(1);

    public async Task RunAsync(ITimeProvider timeProvider)
    {
        while (!_shutdown)
        {
            // Need to handle concurrency
            await _lock.WaitAsync();
            try
            {
                _position = await CalculateNewPositionAsync();

                _messageBus.Publish(new GpsPositionMessage {
                    Position = _position
                });
            }
            finally
            {
                _lock.Release();
            }

            await timeProvider.Delay(TimeSpan.FromMilliseconds(100));
        }
    }
}
```

### 6. Real-World Fit

**Tick-Based: Best for Offline Simulations**

**Use Cases:**
- ✅ Testing and validation
- ✅ Batch processing
- ✅ Replay and analysis
- ✅ Monte Carlo simulations
- ✅ Model validation
- ❌ Real-time hardware interaction
- ❌ Live sensor data

**Example:** Simulating 8 hours of field operation to validate algorithms

**Thread Pool: Best for Real-Time Systems**

**Use Cases:**
- ✅ Production operation
- ✅ Real-time hardware control
- ✅ Live sensor processing
- ✅ Responsive UI
- ✅ Network I/O
- ⚠️ Testing (non-deterministic)
- ❌ Exact reproducibility

**Example:** Running on actual tractor with live GPS and steering

### 7. Time Control

**Tick-Based: ✅ Perfect Control**

**Capabilities:**
- ✅ Fixed time steps (10ms, 100ms, etc.)
- ✅ Variable time steps (event-driven)
- ✅ Pause/resume trivial
- ✅ Rewind possible (if state saved)
- ✅ Slow motion (increase tick time)
- ✅ Fast forward (decrease tick time)
- ✅ Step-by-step execution

**Example:**
```csharp
// Perfect time control
sim.TickSize = TimeSpan.FromMilliseconds(10);  // 10ms ticks
sim.Run(1000 ticks);  // Exactly 10 seconds simulated

sim.Pause();
sim.Step();  // Advance by exactly 1 tick
sim.Step();  // Another tick
sim.Resume();
```

**Thread Pool: ⚠️ Limited Control**

**Capabilities:**
- ⚠️ TimeScale approximation only
- ⚠️ Cannot guarantee exact timing
- ⚠️ Pause requires stopping all threads
- ❌ Rewind not possible
- ⚠️ Fast forward has limits (task scheduling overhead)
- ❌ Step-by-step not meaningful

**Example:**
```csharp
// Approximate time control
timeProvider.TimeScale = 10.0;  // Try for 10x speed
// Actual speed depends on thread scheduling

// Can't do:
// - Exact time steps
// - Rewind
// - Step through concurrent execution
```

## Hybrid Approaches

### Option 1: Tick-Based Core + Async Peripherals

```csharp
public class HybridSimulation
{
    // Core simulation runs on ticks
    public void Tick(DateTimeOffset currentTime, double deltaTime)
    {
        // Deterministic, sequential core
        UpdatePhysics(deltaTime);
        UpdateVehicleState(deltaTime);
        UpdateControl(deltaTime);
    }

    // Async I/O runs independently
    private async Task HandlePeripheralsAsync()
    {
        await Task.WhenAll(
            ReadGPSAsync(),
            WriteSteeringAsync(),
            UpdateUIAsync()
        );
    }
}
```

**Pros:**
- Deterministic core simulation
- Responsive I/O and UI
- Best of both worlds

**Cons:**
- More complex architecture
- Synchronization between tick and async needed

### Option 2: Event-Driven Tick System

```csharp
public class EventDrivenSimulation
{
    private PriorityQueue<SimulationEvent> _eventQueue;

    public void Run()
    {
        while (_eventQueue.Count > 0)
        {
            var evt = _eventQueue.Dequeue();

            // Jump time to next event
            _currentTime = evt.Time;

            // Process event
            evt.Execute();

            // Event may schedule more events
        }
    }
}
```

**Pros:**
- Efficient (no unnecessary ticks)
- Still deterministic
- Good for sparse events

**Cons:**
- More complex event management
- Harder to reason about continuous systems

## Recommendations for AgOpenGPS

### Current Architecture Analysis

**Current:** Thread Pool System
- ✅ Good for real-time operation on tractor
- ✅ Responsive to hardware
- ✅ Good performance
- ❌ Non-deterministic testing
- ❌ Concurrent time advancement issues at high TimeScale

### Recommendation by Use Case

#### Use Case 1: Production Operation (Real Tractor)
**Use:** Thread Pool (Current Architecture) ✅

**Rationale:**
- Real hardware requires concurrent processing
- Need responsive control loops
- Live sensor data is inherently async
- Determinism less critical (real world isn't deterministic)

**Keep:**
- Async/await module architecture
- MessageBus
- Concurrent execution
- Real-time TimeProvider

#### Use Case 2: High-Speed Simulation Testing
**Use:** Tick-Based System or Hybrid 📊

**Rationale:**
- Need deterministic results
- Want reproducible tests
- Simulation speed critical
- Can sacrifice real-time responsiveness

**Options:**

**Option A: Add Tick-Based Mode**
```csharp
public interface IAgModule
{
    // Existing
    Task RunAsync(ITimeProvider timeProvider);

    // New for simulation
    void Tick(DateTimeOffset currentTime, double deltaTime);
}

// Enable tick mode
core.RunMode = SimulationMode.TickBased;
```

**Option B: Fix Unlimited Speed with Explicit Control**
```csharp
// Keep async/await, but require explicit time advancement
timeProvider.TimeScale = double.MaxValue;

while (simulation.Running)
{
    timeProvider.AdvanceToNextDelay();
    await Task.Yield();
}
```

#### Use Case 3: Unit Testing Individual Modules
**Use:** Frozen Time (TimeScale = 0) ✅

**Already Supported:**
```csharp
var timeProvider = new SimulatedTimeProvider();
timeProvider.TimeScale = 0;  // Frozen

// Test can control time precisely
await module.StartAsync();
timeProvider.Advance(TimeSpan.FromSeconds(1));
// Assert expected behavior
```

## Performance Comparison Table

| Metric | Tick-Based | Thread Pool (Current) |
|--------|------------|----------------------|
| **Max Speed** | 10-100x | 10000x+ (unlimited) |
| **CPU Usage** | Low (single thread) | High (multi-thread) |
| **Determinism** | ✅ Perfect | ❌ None |
| **Scalability** | Poor (linear) | Good (parallel) |
| **Debugging** | ✅ Easy | ⚠️ Hard |
| **Code Complexity** | ✅ Simple | ⚠️ Complex |
| **Real-time HW** | ❌ Difficult | ✅ Natural |
| **Testing** | ✅ Excellent | ⚠️ Tricky |
| **Reproducibility** | ✅ Perfect | ❌ None |

## Conclusion

### For AgOpenGPS:

**✅ Keep Thread Pool Architecture for Production**
- Essential for real-time hardware control
- Good performance for live operation
- Natural fit for async I/O

**➕ Add Tick-Based Simulation Mode for Testing**
- Optional mode for deterministic testing
- Activated via configuration
- Modules implement both `RunAsync()` and `Tick()`

**🔧 Fix Unlimited Speed Issues**
- Implement explicit time control for TimeScale > 1000x
- Use `AdvanceToNextDelay()` for test scenarios
- Document the three modes clearly

### Architecture Recommendation:

```csharp
public enum SimulationMode
{
    RealTime,      // Thread pool, real hardware (production)
    FastForward,   // Thread pool, TimeScale 1-1000x (testing)
    TickBased,     // Single thread, deterministic (validation)
    Unlimited      // Manual time control (special cases)
}
```

This gives you the best of both worlds: responsive real-time operation for production, and deterministic tick-based simulation for testing and validation.

---

**Generated with Claude Code**
