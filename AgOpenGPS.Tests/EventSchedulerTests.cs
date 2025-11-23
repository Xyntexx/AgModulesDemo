using AgOpenGPS.Core;
using Xunit;

namespace AgOpenGPS.Tests;

/// <summary>
/// Tests for the unified EventScheduler that coordinates both:
/// - Rate-based scheduled methods (ticks at fixed Hz)
/// - Time-based async delays (await timeProvider.Delay())
/// </summary>
public class EventSchedulerTests
{
    [Fact]
    public void Schedule_CreatesScheduledMethod()
    {
        // Arrange
        var timeProvider = new SimulatedTimeProvider();
        var scheduler = new EventScheduler(timeProvider);
        var callCount = 0;

        // Act
        var handle = scheduler.Schedule(() => callCount++, 10.0, "TestMethod");

        // Assert
        Assert.NotNull(handle);
        Assert.False(handle.IsPaused);
    }

    [Fact]
    public void GetNextEventTime_ReturnsEarliestEvent()
    {
        // Arrange
        var startTime = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new SimulatedTimeProvider(startTime);
        var scheduler = new EventScheduler(timeProvider);

        // Schedule method at 10Hz (100ms intervals)
        scheduler.Schedule(() => { }, 10.0);

        // Create delay for 50ms
        _ = timeProvider.Delay(TimeSpan.FromMilliseconds(50));

        // Act
        var nextEventTime = scheduler.GetNextEventTime();

        // Assert - delay should come first (50ms < 100ms)
        Assert.NotNull(nextEventTime);
        Assert.Equal(startTime.AddMilliseconds(50), nextEventTime.Value);
    }

    [Fact]
    public async Task RunSimulationAsync_ExecutesScheduledMethodsAndDelays()
    {
        // Test that both scheduled methods and delays execute correctly
        // in simulation mode (unlimited speed)

        // Arrange
        var startTime = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new SimulatedTimeProvider(startTime);
        var scheduler = new EventScheduler(timeProvider);

        var methodCallCount = 0;
        var delayCompleted = false;

        // Schedule method at 10Hz (should execute 10 times in 1 second)
        var handle = scheduler.Schedule(() => methodCallCount++, 10.0, "TestMethod");

        // Create task that delays 1 second
        var delayTask = Task.Run(async () =>
        {
            await timeProvider.Delay(TimeSpan.FromSeconds(1));
            delayCompleted = true;
        });

        // Act - run simulation for 1 second
        await scheduler.RunSimulationAsync(new[] { delayTask });

        // Assert
        Assert.True(delayCompleted, "Delay should have completed");
        Assert.InRange(methodCallCount, 9, 11); // ~10 calls (allow tolerance)

        // Verify simulated time advanced correctly
        var elapsed = timeProvider.UtcNow - startTime;
        Assert.InRange(elapsed.TotalSeconds, 0.9, 1.1);

        // Cleanup
        handle.Dispose();
    }

    [Fact]
    public async Task RunSimulationAsync_MultipleTasksWithMixedEvents()
    {
        // Test complex scenario with multiple tasks, scheduled methods, and delays

        // Arrange
        var startTime = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new SimulatedTimeProvider(startTime);
        var scheduler = new EventScheduler(timeProvider);

        var fastMethodCalls = 0;
        var slowMethodCalls = 0;

        // Fast method: 20Hz (50ms intervals)
        var fastHandle = scheduler.Schedule(() => fastMethodCalls++, 20.0, "FastMethod");

        // Slow method: 5Hz (200ms intervals)
        var slowHandle = scheduler.Schedule(() => slowMethodCalls++, 5.0, "SlowMethod");

        // Task 1: Multiple delays
        var task1Events = new List<DateTimeOffset>();
        var task1 = Task.Run(async () =>
        {
            for (int i = 0; i < 5; i++)
            {
                task1Events.Add(timeProvider.UtcNow);
                await timeProvider.Delay(TimeSpan.FromMilliseconds(100));
            }
        });

        // Task 2: Different delay pattern
        var task2Events = new List<DateTimeOffset>();
        var task2 = Task.Run(async () =>
        {
            for (int i = 0; i < 10; i++)
            {
                task2Events.Add(timeProvider.UtcNow);
                await timeProvider.Delay(TimeSpan.FromMilliseconds(50));
            }
        });

        // Act - run simulation
        await scheduler.RunSimulationAsync(new[] { task1, task2 });

        // Assert - Task durations
        // Task 1: 5 delays × 100ms = 500ms (but starts at 0, so 4 intervals)
        Assert.Equal(5, task1Events.Count);
        var task1Duration = task1Events.Last() - task1Events.First();
        Assert.InRange(task1Duration.TotalMilliseconds, 350, 900); // Wide tolerance for thread scheduling

        // Task 2: 10 delays × 50ms = 500ms (but starts at 0, so 9 intervals)
        Assert.Equal(10, task2Events.Count);
        var task2Duration = task2Events.Last() - task2Events.First();
        Assert.InRange(task2Duration.TotalMilliseconds, 400, 900); // Wide tolerance for thread scheduling

        // Assert - Scheduled method calls
        // Fast method: 20Hz × 0.5s = ~10 calls
        Assert.InRange(fastMethodCalls, 9, 11);

        // Slow method: 5Hz × 0.5s = ~2-3 calls
        Assert.InRange(slowMethodCalls, 2, 4);

        // Verify simulated time
        var elapsed = timeProvider.UtcNow - startTime;
        Assert.InRange(elapsed.TotalMilliseconds, 430, 520);

        // Cleanup
        fastHandle.Dispose();
        slowHandle.Dispose();
    }

    [Fact]
    public async Task RunRealTimeAsync_ExecutesWithTimeScaling()
    {
        // Test that real-time mode respects TimeScale

        // Arrange
        var startTime = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new SimulatedTimeProvider(startTime);
        timeProvider.TimeScale = 10.0; // 10x speed
        var scheduler = new EventScheduler(timeProvider);

        var methodCallCount = 0;
        var handle = scheduler.Schedule(() => methodCallCount++, 10.0, "TestMethod");

        var delayTask = Task.Run(async () =>
        {
            await timeProvider.Delay(TimeSpan.FromSeconds(1)); // 1 second simulated
        });

        var realTimeStart = DateTimeOffset.UtcNow;

        // Act - run in real-time mode
        await scheduler.RunRealTimeAsync(new[] { delayTask });

        var realTimeElapsed = DateTimeOffset.UtcNow - realTimeStart;

        // Assert - simulated time should be 1 second
        var simulatedElapsed = timeProvider.UtcNow - startTime;
        Assert.InRange(simulatedElapsed.TotalSeconds, 0.9, 1.1);

        // Real-time should be ~100ms (1 second / 10x scale)
        Assert.InRange(realTimeElapsed.TotalMilliseconds, 80, 200);

        // Method should have been called ~10 times
        Assert.InRange(methodCallCount, 9, 11);

        // Cleanup
        handle.Dispose();
    }

    [Fact]
    public void PauseResume_ControlsMethodExecution()
    {
        // Arrange
        var timeProvider = new SimulatedTimeProvider();
        var scheduler = new EventScheduler(timeProvider);
        var callCount = 0;

        var handle = scheduler.Schedule(() => callCount++, 10.0, "TestMethod");

        // Act - pause
        handle.Pause();

        // Assert
        Assert.True(handle.IsPaused);

        // Resume
        handle.Resume();
        Assert.False(handle.IsPaused);

        // Cleanup
        handle.Dispose();
    }

    [Fact]
    public async Task Dispose_StopsMethodExecution()
    {
        // Arrange
        var startTime = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new SimulatedTimeProvider(startTime);
        var scheduler = new EventScheduler(timeProvider);

        var callCount = 0;
        var handle = scheduler.Schedule(() => callCount++, 10.0, "TestMethod");

        // Create a task that delays briefly
        var delayTask = Task.Run(async () =>
        {
            await timeProvider.Delay(TimeSpan.FromMilliseconds(100));
        });

        // Dispose immediately
        handle.Dispose();

        // Act - run simulation
        await scheduler.RunSimulationAsync(new[] { delayTask });

        // Assert - method should not have been called (disposed before execution)
        Assert.Equal(0, callCount);
    }

    [Fact]
    public async Task ScheduledMethod_ExecutesAtCorrectIntervals()
    {
        // Verify that scheduled methods execute at precise intervals

        // Arrange
        var startTime = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new SimulatedTimeProvider(startTime);
        var scheduler = new EventScheduler(timeProvider);

        var executionTimes = new List<DateTimeOffset>();
        var handle = scheduler.Schedule(() =>
        {
            executionTimes.Add(timeProvider.UtcNow);
        }, 10.0, "TestMethod"); // 10Hz = 100ms intervals

        // Create task that runs for 1 second
        var delayTask = Task.Run(async () =>
        {
            await timeProvider.Delay(TimeSpan.FromSeconds(1));
        });

        // Act
        await scheduler.RunSimulationAsync(new[] { delayTask });

        // Assert - should have ~10 executions
        Assert.InRange(executionTimes.Count, 9, 11);

        // Verify intervals are ~100ms apart
        for (int i = 1; i < executionTimes.Count; i++)
        {
            var interval = (executionTimes[i] - executionTimes[i - 1]).TotalMilliseconds;
            Assert.InRange(interval, 95, 105); // 100ms ± 5ms tolerance
        }

        // Cleanup
        handle.Dispose();
    }

    [Fact]
    public async Task ConcurrentScheduledMethods_ExecuteIndependently()
    {
        // Test that multiple scheduled methods at different rates execute correctly

        // Arrange
        var startTime = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new SimulatedTimeProvider(startTime);
        var scheduler = new EventScheduler(timeProvider);

        var method1Calls = 0;
        var method2Calls = 0;
        var method3Calls = 0;

        var handle1 = scheduler.Schedule(() => method1Calls++, 10.0);  // 10Hz
        var handle2 = scheduler.Schedule(() => method2Calls++, 20.0);  // 20Hz
        var handle3 = scheduler.Schedule(() => method3Calls++, 5.0);   // 5Hz

        // Create task that runs for 1 second
        var delayTask = Task.Run(async () =>
        {
            await timeProvider.Delay(TimeSpan.FromSeconds(1));
        });

        // Act
        await scheduler.RunSimulationAsync(new[] { delayTask });

        // Assert
        Assert.InRange(method1Calls, 9, 12);   // ~10 calls (allow extra due to timing)
        Assert.InRange(method2Calls, 19, 22);  // ~20 calls (allow extra due to timing)
        Assert.InRange(method3Calls, 4, 7);    // ~5 calls (allow extra due to timing)

        // Cleanup
        handle1.Dispose();
        handle2.Dispose();
        handle3.Dispose();
    }

    [Fact]
    public async Task PausedMethod_DoesNotExecute()
    {
        // Arrange
        var startTime = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new SimulatedTimeProvider(startTime);
        var scheduler = new EventScheduler(timeProvider);

        var callCount = 0;
        var handle = scheduler.Schedule(() => callCount++, 10.0);

        // Pause immediately
        handle.Pause();

        // Create task that runs for 1 second
        var delayTask = Task.Run(async () =>
        {
            await timeProvider.Delay(TimeSpan.FromSeconds(1));
        });

        // Act
        await scheduler.RunSimulationAsync(new[] { delayTask });

        // Assert - method should not have been called while paused
        Assert.Equal(0, callCount);

        // Cleanup
        handle.Dispose();
    }
}
