# Unlimited Speed Async Task Investigation

**Date:** 2025-01-17
**Status:** Work in Progress
**Related Commit:** 6893178

## Executive Summary

Investigation into async task behavior at high TimeScale values (>100x) revealed fundamental design challenges with automatic time advancement in concurrent simulations. While we successfully eliminated real-time bottlenecks, a deeper architectural question remains about how simulated time should advance when multiple async tasks execute concurrently.

## Problem Statement

When running simulations at very high TimeScale (e.g., 10000x or unlimited speed), async tasks using `await timeProvider.Delay()` experience severe performance degradation:

- **Expected:** Multiple concurrent tasks complete in ~10 seconds simulated time
- **Actual:** Tasks complete in 18-30 seconds simulated time (2-3x slower)
- **Impact:** Simulations run 60-80% slower than intended, defeating the purpose of high-speed simulation

### Specific Issues Observed

1. **Task Leapfrogging**: Tasks that should execute concurrently in simulation time instead execute sequentially, with each task observing time advances made by other tasks
2. **No True Deadlock**: Tasks complete eventually, but much slower than expected
3. **Unpredictable Timing**: Simulated time advances depend on task scheduling order, not simulation logic

## Root Cause Analysis

### The Core Problem: Concurrent vs Sequential Ambiguity

When multiple tasks call `await Delay()`, there is no way for the time provider to determine if these delays are:
- **Concurrent** (should all complete at the same simulated time)
- **Sequential** (should advance time for each one)

**Example Scenario:**
```csharp
// Task A
for (int i = 0; i < 10; i++) {
    var time = timeProvider.UtcNow;  // T0, T2, T4, T6...
    await timeProvider.Delay(1 second);
}

// Task B
for (int i = 0; i < 10; i++) {
    var time = timeProvider.UtcNow;  // T1, T3, T5, T7...
    await timeProvider.Delay(1 second);
}
```

**What Happens:**
1. Task A records time = 0s
2. Task A delays 1s → advances time to 1s
3. Task B records time = 1s ⚠️ (sees A's advancement!)
4. Task B delays 1s → advances time to 2s
5. Task A records time = 2s ⚠️ (sees B's advancement!)
6. Pattern repeats...

**Result:** Both tasks take 20 seconds simulated time instead of 10 seconds

### Technical Details

#### Original Implementation (TimeScale 1-100x)
```csharp
var scaledDuration = TimeSpan.FromTicks(duration.Ticks / TimeScale);
await Task.Delay(scaledDuration);  // Real-time delay
Advance(duration);
```

**Problem at High TimeScale:**
- At 10000x, a 1-second simulated delay = 0.1ms real-time
- Windows `Task.Delay()` has ~15ms minimum resolution
- Real delays are 150x slower than intended
- Task scheduling overhead dominates execution time

#### Current Implementation (TimeScale > 100x)
```csharp
lock (_lock) {
    if (operation.Deadline > _wallClockTime) {
        _monotonicMs += (long)timeToAdvance.TotalMilliseconds;
        _wallClockTime = operation.Deadline;
    }
}
await Task.Yield();  // Instant, no real-time delay
```

**Improvements:**
✅ Eliminated `Task.Delay()` bottleneck
✅ Atomic time advancement (thread-safe)
✅ Instant task switching via `Task.Yield()`

**Remaining Issues:**
⚠️ Tasks still leapfrog each other
⚠️ Simulated time advances 2-3x more than expected
⚠️ No way to distinguish concurrent from sequential operations

## Test Results

### Test 1: UnlimitedSpeed_MultipleAsyncTasks_ExecuteInCorrectOrder

**Setup:** 3 concurrent tasks with different delay patterns
- Task 1: 10 iterations × 1 second = 10 seconds
- Task 2: 20 iterations × 0.5 seconds = 10 seconds
- Task 3: 5 iterations × 2 seconds = 10 seconds

**Expected Result:** All complete in ~10 seconds simulated time

**Actual Results:**
| Implementation | Simulated Time | Status |
|---------------|----------------|--------|
| Original (<100x) | 30 seconds | ❌ FAIL (3x slower) |
| First Fix | 20 seconds | ❌ FAIL (2x slower) |
| Current | 18.5 seconds | ❌ FAIL (1.85x slower) |

### Test 2: UnlimitedSpeed_TaskStarvation_NoDeadlock

**Setup:** 10 concurrent tasks, 5 iterations each × 100ms = 500ms

**Expected Result:** ~500ms simulated time (all tasks run concurrently)

**Actual Results:**
| Implementation | Simulated Time | Status |
|---------------|----------------|--------|
| Original (<100x) | 5000ms | ❌ FAIL (10x slower) |
| First Fix | 3800ms | ❌ FAIL (7.6x slower) |
| Current | 3200ms | ❌ FAIL (6.4x slower) |

**Progress:** 36% improvement, but still 6x slower than expected

## Proposed Solutions

### Option A: Independent Time Advancement (Current)

**Description:** Each task independently advances time when it delays

**Pros:**
- Simple implementation
- No coordination required between tasks
- Works for single-task simulations

**Cons:**
- Concurrent tasks appear sequential in simulated time
- Simulated time advances unpredictably
- Performance degrades with more concurrent tasks

**Verdict:** ❌ Not suitable for multi-task simulations at unlimited speed

### Option B: Explicit Simulation Steps ⭐ RECOMMENDED

**Description:** At very high speeds (>1000x), disable automatic time advancement. Require explicit control via:
- `timeProvider.AdvanceToNextDelay()` - Advance to next pending deadline
- `timeProvider.Advance(duration)` - Manual time control
- `timeProvider.CompleteAllDelays()` - Fast-forward through all pending delays

**Implementation:**
```csharp
if (TimeScale > 1000) {
    // Time frozen for Delay() - no automatic advancement
    _pendingDelays[operation.Id] = operation;
    await operation.CompletionSource.Task;  // Wait for external Advance()
} else if (TimeScale > 100) {
    // Current yield-based approach
    // ...
}
```

**Example Usage:**
```csharp
var timeProvider = new SimulatedTimeProvider();
timeProvider.TimeScale = double.MaxValue;  // Unlimited speed

// Start tasks
var task1 = RunSimulation1(timeProvider);
var task2 = RunSimulation2(timeProvider);

// Explicitly advance simulation
while (AnyTasksRunning()) {
    timeProvider.AdvanceToNextDelay();  // Step to next event
    await Task.Yield();
}
```

**Pros:**
- Clear separation: simulation logic controls time
- Predictable timing behavior
- Handles concurrency correctly
- No ambiguity about concurrent vs sequential

**Cons:**
- Requires explicit time management at high speeds
- More complex for users
- Different behavior at different TimeScale values

**Verdict:** ✅ Best for deterministic, high-speed simulations

### Option C: Batched Time Advancement

**Description:** Collect all pending delays, advance to earliest deadline, complete all expired delays simultaneously

**Implementation Concept:**
```csharp
// When any Delay() is called at unlimited speed:
1. Register the delay in pending queue
2. Don't advance time yet
3. Yield to let other tasks register delays
4. After all tasks yield, find minimum deadline
5. Advance time to that deadline atomically
6. Complete all delays that expired
```

**Pros:**
- Handles concurrency automatically
- No manual time management required
- Correct concurrent execution semantics

**Cons:**
- Complex implementation
- Requires sophisticated coordination mechanism
- Need to detect when "all tasks have registered"
- May still have edge cases with dynamic task creation

**Verdict:** ⚠️ Theoretically ideal, but complex and may not be achievable

### Option D: Hybrid Approach

**Description:** Combine approaches based on TimeScale:

- **0 < TimeScale ≤ 100x:** Current scaled real-time delays (works well)
- **100 < TimeScale ≤ 1000x:** Yield-based with automatic advancement (reasonable trade-off)
- **TimeScale > 1000x:** Explicit control required (Option B)

**Configuration:**
```csharp
timeProvider.TimeScale = double.MaxValue;  // Requires explicit control
// OR
timeProvider.UnlimitedSpeed = true;  // Clearer API
```

**Pros:**
- Best of both worlds
- Clear behavioral boundaries
- Optimized for each use case

**Cons:**
- Multiple modes to understand
- Documentation complexity

**Verdict:** ✅ Practical compromise

## Recommendations

### Immediate Action (Short Term)

1. **Implement Option B** for TimeScale > 1000x
   - Disable automatic time advancement
   - Require `AdvanceToNextDelay()` or `CompleteAllDelays()`
   - Document clearly in API

2. **Update Tests** to use explicit time control
   ```csharp
   timeProvider.TimeScale = double.MaxValue;

   // Run tasks
   var tasks = StartAllTasks(timeProvider);

   // Advance simulation explicitly
   while (!AllTasksComplete(tasks)) {
       timeProvider.AdvanceToNextDelay();
   }
   ```

3. **Add Documentation** explaining three modes:
   - Real-time mode (TimeScale = 1x)
   - Fast-forward mode (100x < TimeScale ≤ 1000x)
   - Unlimited speed mode (TimeScale > 1000x, explicit control)

### Long-Term Considerations

1. **API Enhancement:** Consider adding explicit modes
   ```csharp
   public enum SimulationMode {
       RealTime,        // TimeScale 1-100x, real delays
       FastForward,     // TimeScale 100-1000x, yield-based
       Unlimited        // Manual time control required
   }
   ```

2. **Simulation Framework:** Build higher-level abstractions for common patterns
   ```csharp
   var sim = new Simulation(timeProvider);
   sim.Run(startTime, endTime, tasks);  // Handles time advancement automatically
   ```

3. **Monitoring Tools:** Add diagnostics for time advancement
   ```csharp
   timeProvider.OnTimeAdvanced += (oldTime, newTime, source) => {
       Console.WriteLine($"Time: {oldTime} -> {newTime} (by {source})");
   };
   ```

## Related Files

- `AgOpenGPS.Core/SimulatedTimeProvider.cs` - Time provider implementation
- `AgOpenGPS.Tests/TimeProviderTests.cs` - Test suite with failing tests
- Lines 284-432: New unlimited speed tests (currently failing)

## References

- **SRS R-22-002:** SimClock Alignment requirements
- **ADR-23-002:** Temporal Architecture decision record
- **Task.Delay Resolution:** Windows timer resolution is ~15.6ms minimum

## Conclusion

The investigation revealed that automatic time advancement at unlimited speeds has fundamental ambiguity around concurrent vs sequential operations. The recommended solution is to require explicit time control at very high speeds (>1000x), providing clear semantics and predictable behavior while maintaining automatic advancement at moderate speeds where it works well.

**Next Steps:**
1. Implement explicit control for TimeScale > 1000x
2. Update tests to use explicit advancement
3. Document the three operational modes
4. Consider long-term API enhancements

---

**Generated with Claude Code**
