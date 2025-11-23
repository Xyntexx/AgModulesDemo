# Async Task Coordination for Simulations

> **STATUS: Historical Investigation Document (November 2024)**
> This document explores design options that led to the EventScheduler implementation.
> **Current Implementation:** `EventScheduler.RunSimulationAsync()` uses Option 5 (Simplified Pump Loop)
> See: `AgOpenGPS.Core/EventScheduler.cs` and `docs/EVENT_SCHEDULER_MIGRATION.md`

---

## Background

**Problem:** How do we know when to advance time when running async tasks at unlimited speed?

## The Challenge

```csharp
// We need to detect this state:
var task1 = RunModule1();  // Running...
var task2 = RunModule2();  // Running...

// STATE: Both tasks are waiting on Delay()
//        No tasks actively executing
//        Time should advance
//
// But how do we detect this programmatically?
```

## Solutions Considered

### Option 1: Wait Until All Delays Registered (Cooperative)

**Idea:** After tasks start, wait a moment for all initial delays to register, then advance time.

```csharp
// Start tasks
var tasks = new[] {
    Module1.RunAsync(timeProvider),
    Module2.RunAsync(timeProvider)
};

// Give tasks time to reach their first Delay()
await Task.Delay(10); // Short real-time pause

// Now advance simulation
while (!Task.WhenAll(tasks).IsCompleted) {
    if (timeProvider.HasPendingDelays) {
        timeProvider.AdvanceToNextDelay();
        await Task.Delay(1); // Let tasks process
    }
}
```

**Pros:**
- Simple implementation
- Works with existing code

**Cons:**
- ❌ Fragile - depends on timing
- ❌ Real-time delays defeat unlimited speed
- ❌ May miss delays that register late

**Verdict:** ❌ Not reliable

### Option 2: Explicit Quiescence Detection

**Idea:** Track when all tasks are "quiescent" (waiting, not executing).

```csharp
public class SimulatedTimeProvider
{
    private int _activeTaskCount = 0;

    public async Task Delay(TimeSpan duration) {
        Interlocked.Decrement(ref _activeTaskCount); // Entering wait state
        try {
            // ... delay logic ...
        } finally {
            Interlocked.Increment(ref _activeTaskCount); // Leaving wait state
        }
    }

    public bool IsQuiescent => _activeTaskCount == 0 && _pendingDelays.Count > 0;
}

// Usage
while (!allTasksComplete) {
    await Task.Yield();

    if (timeProvider.IsQuiescent) {
        timeProvider.AdvanceToNextDelay();
    }
}
```

**Pros:**
- Detects true quiescence
- No arbitrary delays
- Works at unlimited speed

**Cons:**
- ⚠️ Requires tracking active tasks
- ⚠️ Need to instrument task starts/ends
- ⚠️ Race condition: task might finish between check and advance

**Verdict:** ⚠️ Workable but complex

### Option 3: Synchronization Barrier (Best for Simulations)

**Idea:** Use a barrier to coordinate tasks. All tasks must wait at barrier before time advances.

```csharp
public class SimulatedTimeProvider
{
    private ManualResetEventSlim _timeAdvanceBarrier = new(false);
    private int _waitingTaskCount = 0;
    private int _totalTaskCount = 0;

    public void RegisterSimulationTask() {
        Interlocked.Increment(ref _totalTaskCount);
    }

    public async Task Delay(TimeSpan duration) {
        // Register this delay
        var operation = new DelayOperation { ... };
        _pendingDelays[operation.Id] = operation;

        // Increment waiting count
        var waiting = Interlocked.Increment(ref _waitingTaskCount);

        // If all tasks are waiting, signal coordinator
        if (waiting == _totalTaskCount) {
            _timeAdvanceBarrier.Set();
        }

        // Wait for time advancement
        await operation.CompletionSource.Task;

        // Decrement waiting count
        Interlocked.Decrement(ref _waitingTaskCount);
    }

    public async Task WaitForAllTasksWaiting() {
        await Task.Run(() => _timeAdvanceBarrier.Wait());
        _timeAdvanceBarrier.Reset();
    }
}

// Usage
timeProvider.RegisterSimulationTask(); // For each task
timeProvider.RegisterSimulationTask();

var tasks = new[] {
    Module1.RunAsync(timeProvider),
    Module2.RunAsync(timeProvider)
};

while (!Task.WhenAll(tasks).IsCompleted) {
    await timeProvider.WaitForAllTasksWaiting();  // Blocks until all waiting
    timeProvider.AdvanceToNextDelay();            // Advance time
}
```

**Pros:**
- ✅ Deterministic synchronization
- ✅ No race conditions
- ✅ Perfect for simulations
- ✅ No arbitrary delays

**Cons:**
- ⚠️ Requires registration of task count
- ⚠️ More complex implementation
- ❌ Doesn't work if tasks complete (need handling)

**Verdict:** ✅ Best for controlled simulations

### Option 4: Event-Driven Coordinator (Most General)

**Idea:** Let the time provider notify when it needs coordination.

```csharp
public class SimulatedTimeProvider
{
    public event EventHandler? QuiescenceReached;

    public async Task Delay(TimeSpan duration) {
        // ... register delay ...

        // Check if this was the last active task
        if (AllTasksWaiting()) {
            QuiescenceReached?.Invoke(this, EventArgs.Empty);
        }

        await operation.CompletionSource.Task;
    }

    private bool AllTasksWaiting() {
        // Check if all registered tasks are in pending delays
        // This requires tracking task IDs
    }
}

// Usage
var coordinator = new SimulationCoordinator(timeProvider);

timeProvider.QuiescenceReached += (s, e) => {
    timeProvider.AdvanceToNextDelay();
};

await coordinator.RunAsync(tasks);
```

**Pros:**
- ✅ Event-driven, reactive
- ✅ Can handle dynamic task creation
- ✅ Decoupled design

**Cons:**
- ⚠️ More complex architecture
- ⚠️ Need task tracking mechanism
- ⚠️ Events might fire at wrong times

**Verdict:** ✅ Good for complex scenarios

### Option 5: Simplified Pump Loop (Pragmatic)

**Idea:** Simple loop that repeatedly advances time until all tasks complete.

```csharp
public static async Task RunSimulation(
    SimulatedTimeProvider timeProvider,
    params Task[] tasks)
{
    var allTasks = Task.WhenAll(tasks);

    while (!allTasks.IsCompleted)
    {
        // Yield to let tasks execute
        await Task.Yield();

        // If there are pending delays and no task is actively running
        if (timeProvider.HasPendingDelays)
        {
            // Advance to next event
            timeProvider.AdvanceToNextDelay();
        }

        // Small delay to prevent tight loop (if needed)
        if (timeProvider.PendingDelayCount == 0)
        {
            await Task.Delay(1); // Wait for tasks to register delays
        }
    }
}

// Usage (conceptual - see EventScheduler for actual implementation)
await scheduler.RunSimulationAsync(new[] {
    Module1.RunAsync(timeProvider),
    Module2.RunAsync(timeProvider)
});
```

**Pros:**
- ✅ Simple to implement
- ✅ No registration required
- ✅ Works with existing code
- ✅ Handles task completion naturally

**Cons:**
- ⚠️ Small real-time delay needed (1ms)
- ⚠️ Not 100% deterministic
- ⚠️ Might advance time too early/late

**Verdict:** ✅ Good pragmatic solution

## Recommended Implementation: Hybrid Approach

Combine Options 3 and 5 for best results:

```csharp
public class SimulationCoordinator
{
    private readonly SimulatedTimeProvider _timeProvider;
    private readonly List<Task> _tasks = new();
    private int _activeTaskCount = 0;

    public SimulationCoordinator(SimulatedTimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
        _timeProvider.TaskEnteringDelay += OnTaskEnteringDelay;
        _timeProvider.TaskLeavingDelay += OnTaskLeavingDelay;
    }

    public void RegisterTask(Task task)
    {
        _tasks.Add(task);
        Interlocked.Increment(ref _activeTaskCount);
    }

    private void OnTaskEnteringDelay(object? sender, EventArgs e)
    {
        Interlocked.Decrement(ref _activeTaskCount);
    }

    private void OnTaskLeavingDelay(object? sender, EventArgs e)
    {
        Interlocked.Increment(ref _activeTaskCount);
    }

    public async Task RunAsync()
    {
        var allTasks = Task.WhenAll(_tasks);

        while (!allTasks.IsCompleted)
        {
            // Wait for quiescence
            while (_activeTaskCount > 0)
            {
                await Task.Yield();
            }

            // All tasks are waiting - advance time
            if (_timeProvider.HasPendingDelays)
            {
                _timeProvider.AdvanceToNextDelay();

                // Give tasks a chance to process
                await Task.Yield();
            }
            else
            {
                // No pending delays and no active tasks
                // Either all done or deadlock
                if (!allTasks.IsCompleted)
                {
                    throw new InvalidOperationException(
                        "Simulation deadlock: no pending delays but tasks not complete");
                }
            }
        }
    }
}

// Usage
var coordinator = new SimulationCoordinator(timeProvider);
coordinator.RegisterTask(Module1.RunAsync(timeProvider));
coordinator.RegisterTask(Module2.RunAsync(timeProvider));
await coordinator.RunAsync();
```

## Implementation Plan

### Phase 1: Add Events to SimulatedTimeProvider

```csharp
public class SimulatedTimeProvider
{
    public event EventHandler? TaskEnteringDelay;
    public event EventHandler? TaskLeavingDelay;

    public int PendingDelayCount => _pendingDelays.Count;
    public bool HasPendingDelays => _pendingDelays.Count > 0;

    public async Task Delay(TimeSpan duration) {
        TaskEnteringDelay?.Invoke(this, EventArgs.Empty);
        try {
            // ... existing delay logic ...
        } finally {
            TaskLeavingDelay?.Invoke(this, EventArgs.Empty);
        }
    }
}
```

### Phase 2: Create SimulationCoordinator

```csharp
public class SimulationCoordinator
{
    // As shown above
}
```

### Phase 3: Update Tests

```csharp
[Fact]
public async Task UnlimitedSpeed_WithCoordinator_CompletesCorrectly()
{
    var timeProvider = new SimulatedTimeProvider();
    timeProvider.TimeScale = double.MaxValue;

    var coordinator = new SimulationCoordinator(timeProvider);

    // Register tasks
    coordinator.RegisterTask(Task.Run(async () => {
        for (int i = 0; i < 10; i++) {
            await timeProvider.Delay(TimeSpan.FromSeconds(1));
        }
    }));

    coordinator.RegisterTask(Task.Run(async () => {
        for (int i = 0; i < 20; i++) {
            await timeProvider.Delay(TimeSpan.FromMilliseconds(500));
        }
    }));

    // Run simulation
    await coordinator.RunAsync();

    // Assert
    var elapsed = timeProvider.UtcNow - startTime;
    Assert.InRange(elapsed.TotalSeconds, 9.5, 10.5);
}
```

## Alternative: Use Existing CompleteAllDelays()

**Simplest approach** - use what we already have:

```csharp
// Start tasks (don't await yet)
var task1 = Module1.RunAsync(timeProvider);
var task2 = Module2.RunAsync(timeProvider);

// Give tasks a moment to start
await Task.Yield();

// Fast-forward through all delays
timeProvider.CompleteAllDelays();

// Now wait for completion
await Task.WhenAll(task1, task2);
```

**Problem:** `CompleteAllDelays()` advances all the way to the end immediately. This works for simple cases but doesn't allow observing intermediate states.

## Recommendation

**✅ IMPLEMENTED:** EventScheduler uses Option 5 (Simplified Pump Loop)
- See: `AgOpenGPS.Core/EventScheduler.cs` - `RunSimulationAsync()` method
- Combines rate-based scheduling with time-based delays
- Works with both SystemTimeProvider and SimulatedTimeProvider

**For simple test cases:** Use `EventScheduler.RunSimulationAsync()`

**For time-scaled tests:** Use `EventScheduler.RunRealTimeAsync()` with `TimeScale`

**For production:** Use `EventScheduler.Start()` (background thread mode)

---

**Generated with Claude Code**
