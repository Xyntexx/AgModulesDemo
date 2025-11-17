using AgOpenGPS.Core;
using AgOpenGPS.ModuleContracts;
using AgOpenGPS.ModuleContracts.Messages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AgOpenGPS.Tests;

public class TimeProviderTests
{
    [Fact]
    public void SystemTimeProvider_ReturnsRealTime()
    {
        // Arrange
        var timeProvider = new SystemTimeProvider();
        var before = DateTimeOffset.UtcNow;

        // Act
        var providedTime = timeProvider.UtcNow;

        // Assert
        var after = DateTimeOffset.UtcNow;
        Assert.InRange(providedTime, before, after);
    }

    [Fact]
    public async Task SystemTimeProvider_DelayWaitsRealTime()
    {
        // Arrange
        var timeProvider = new SystemTimeProvider();
        var before = DateTimeOffset.UtcNow;

        // Act
        await timeProvider.Delay(TimeSpan.FromMilliseconds(100));

        // Assert
        var after = DateTimeOffset.UtcNow;
        Assert.True((after - before).TotalMilliseconds >= 90); // Allow some tolerance
    }

    [Fact]
    public void SimulatedTimeProvider_StartsAtSpecifiedTime()
    {
        // Arrange
        var startTime = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);

        // Act
        var timeProvider = new SimulatedTimeProvider(startTime);

        // Assert
        Assert.Equal(startTime, timeProvider.UtcNow);
    }

    [Fact]
    public void SimulatedTimeProvider_AdvanceMovesTimeForward()
    {
        // Arrange
        var startTime = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new SimulatedTimeProvider(startTime);

        // Act
        timeProvider.Advance(TimeSpan.FromHours(2));

        // Assert
        var expected = startTime.AddHours(2);
        Assert.Equal(expected, timeProvider.UtcNow);
    }

    [Fact]
    public async Task SimulatedTimeProvider_DelayCompletesInstantlyWhenTimeAdvanced()
    {
        // Arrange
        var timeProvider = new SimulatedTimeProvider();
        var delayTask = timeProvider.Delay(TimeSpan.FromSeconds(10));

        // Act - advance time past the delay deadline
        timeProvider.Advance(TimeSpan.FromSeconds(15));
        await delayTask; // Should complete immediately since time was advanced

        // Assert - if we got here without hanging, the test passed
        Assert.True(delayTask.IsCompleted);
    }

    [Fact]
    public async Task SimulatedTimeProvider_WithTimeScale_DelaysAreScaled()
    {
        // Arrange
        var timeProvider = new SimulatedTimeProvider();
        timeProvider.TimeScale = 10.0; // 10x speed

        var realTimeBefore = DateTimeOffset.UtcNow;

        // Act - request 1 second delay, but with 10x speed should only take ~100ms real time
        await timeProvider.Delay(TimeSpan.FromSeconds(1));

        // Assert
        var realTimeAfter = DateTimeOffset.UtcNow;
        var realElapsed = (realTimeAfter - realTimeBefore).TotalMilliseconds;

        // Should take about 100ms in real time (1000ms / 10x)
        Assert.InRange(realElapsed, 80, 200); // Allow tolerance for scheduling
    }

    [Fact]
    public void SimulatedTimeProvider_FrozenTime_DoesNotAdvanceAutomatically()
    {
        // Arrange
        var startTime = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new SimulatedTimeProvider(startTime);
        timeProvider.TimeScale = 0.0; // Freeze time

        // Act
        var time1 = timeProvider.UtcNow;
        Thread.Sleep(50); // Real time passes
        var time2 = timeProvider.UtcNow;

        // Assert - time should not have changed
        Assert.Equal(time1, time2);
    }

    [Fact]
    public void SimulatedTimeProvider_SetTime_MovesTimeForward()
    {
        // Arrange
        var startTime = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new SimulatedTimeProvider(startTime);
        var newTime = new DateTimeOffset(2024, 1, 2, 14, 30, 0, TimeSpan.Zero);

        // Act
        timeProvider.SetTime(newTime);

        // Assert
        Assert.Equal(newTime, timeProvider.UtcNow);
    }

    [Fact]
    public void SimulatedTimeProvider_SetTime_CannotMoveBackwards()
    {
        // Arrange
        var startTime = new DateTimeOffset(2024, 1, 2, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new SimulatedTimeProvider(startTime);
        var earlierTime = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);

        // Act & Assert
        Assert.Throws<ArgumentException>(() => timeProvider.SetTime(earlierTime));
    }

    [Fact]
    public async Task SimulatedTimeProvider_CompleteAllDelays_CompletesAllPendingDelays()
    {
        // Arrange
        var timeProvider = new SimulatedTimeProvider();
        timeProvider.TimeScale = 0.0; // Freeze time so delays don't complete automatically

        var delay1 = timeProvider.Delay(TimeSpan.FromSeconds(10));
        var delay2 = timeProvider.Delay(TimeSpan.FromSeconds(20));
        var delay3 = timeProvider.Delay(TimeSpan.FromSeconds(30));

        // Act
        timeProvider.CompleteAllDelays();

        // Assert - all delays should be complete
        await Task.WhenAll(delay1, delay2, delay3);
        Assert.True(delay1.IsCompleted);
        Assert.True(delay2.IsCompleted);
        Assert.True(delay3.IsCompleted);
    }
}

/// <summary>
/// Integration tests demonstrating time control in message bus
/// </summary>
public class MessageBusTimeTests
{
    [Fact]
    public void MessageBus_UsesTimeProvider_ForMessageTimestamps()
    {
        // Arrange
        var startTime = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new SimulatedTimeProvider(startTime);
        var logger = NullLogger<MessageBus>.Instance;
        var messageBus = new MessageBus(timeProvider, logger);

        var testMessage = new GpsPositionMessage
        {
            Latitude = 40.7128,
            Longitude = -74.0060,
            Speed = 5.0,
            Heading = 90.0,
            FixQuality = GpsFixQuality.RTK_Fixed,
            Timestamp = TimestampMetadata.CreateMonotonicOnly(timeProvider, 0)
        };

        // Act
        messageBus.Publish(in testMessage);

        // Assert - verify timestamp was stored correctly
        var retrieved = messageBus.TryGetLastMessage<GpsPositionMessage>(out var msg, out var timestamp);
        Assert.True(retrieved);
        Assert.Equal(startTime, timestamp);
    }

    [Fact]
    public void MessageBus_AdvanceTime_UpdatesTimestamps()
    {
        // Arrange
        var startTime = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new SimulatedTimeProvider(startTime);
        var logger = NullLogger<MessageBus>.Instance;
        var messageBus = new MessageBus(timeProvider, logger);

        var testMessage = new GpsPositionMessage
        {
            Latitude = 40.7128,
            Longitude = -74.0060,
            Speed = 5.0,
            Heading = 90.0,
            FixQuality = GpsFixQuality.RTK_Fixed,
            Timestamp = TimestampMetadata.CreateMonotonicOnly(timeProvider, 0)
        };

        // Act
        messageBus.Publish(in testMessage);
        timeProvider.Advance(TimeSpan.FromHours(1));
        messageBus.Publish(in testMessage);

        // Assert - second message should have later timestamp
        var retrieved = messageBus.TryGetLastMessage<GpsPositionMessage>(out var msg, out var timestamp);
        Assert.True(retrieved);
        Assert.Equal(startTime.AddHours(1), timestamp);
    }

    [Fact]
    public async Task FastForwardSimulation_RunsQuickly()
    {
        // This test verifies that TimeScale allows fast simulation:
        // - Simulated time advances correctly (1 hour in simulation)
        // - All delays complete in order
        // - All messages are processed
        // - Real-world time is irrelevant - only simulated time matters

        // Arrange
        var startTime = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new SimulatedTimeProvider(startTime);
        timeProvider.TimeScale = 3600.0; // 3600x speed - doesn't matter how fast it actually runs

        var logger = NullLogger<MessageBus>.Instance;
        var messageBus = new MessageBus(timeProvider, logger);
        var messageCount = 0;

        // Subscribe to count messages
        messageBus.Subscribe<GpsPositionMessage>(msg => messageCount++);

        // Act - simulate publishing messages every second for 1 hour (simulated)
        for (int i = 0; i < 3600; i++) // 1 hour worth of seconds
        {
            var testMessage = new GpsPositionMessage
            {
                Latitude = 40.7128 + (i * 0.0001),
                Longitude = -74.0060 + (i * 0.0001),
                Speed = 5.0,
                Heading = 90.0,
                FixQuality = GpsFixQuality.RTK_Fixed,
                Timestamp = TimestampMetadata.CreateMonotonicOnly(timeProvider, 0)
            };

            messageBus.Publish(in testMessage);
            await timeProvider.Delay(TimeSpan.FromSeconds(1));
        }

        // Assert - verify simulated time progression and message delivery
        // Should have simulated exactly 1 hour
        var simulatedElapsed = timeProvider.UtcNow - startTime;
        Assert.InRange(simulatedElapsed.TotalHours, 0.99, 1.01);

        // Should have received all 3600 messages
        Assert.Equal(3600, messageCount);

        // Verify final simulated time
        Assert.Equal(new DateTimeOffset(2024, 1, 1, 13, 0, 0, TimeSpan.Zero), timeProvider.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task UnlimitedSpeed_MultipleAsyncTasks_ExecuteInCorrectOrder()
    {
        // This test verifies that at very high TimeScale (unlimited speed):
        // - Multiple concurrent async tasks make progress
        // - Tasks execute in the correct logical order based on simulated time
        // - Real-world time is irrelevant - only simulated time matters
        // - No task starvation or deadlocks occur

        // Arrange
        var startTime = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new SimulatedTimeProvider(startTime);
        timeProvider.TimeScale = 10000.0; // 10000x speed - "unlimited" for practical purposes

        var task1Events = new List<(DateTimeOffset time, string action)>();
        var task2Events = new List<(DateTimeOffset time, string action)>();
        var task3Events = new List<(DateTimeOffset time, string action)>();

        var realTimeStart = DateTimeOffset.UtcNow;

        // Act - Run 3 concurrent tasks with different delay patterns
        var task1 = Task.Run(async () =>
        {
            for (int i = 0; i < 10; i++)
            {
                task1Events.Add((timeProvider.UtcNow, $"Iteration {i}"));
                await timeProvider.Delay(TimeSpan.FromSeconds(1)); // 1 second simulated
            }
            task1Events.Add((timeProvider.UtcNow, "Completed"));
        });

        var task2 = Task.Run(async () =>
        {
            for (int i = 0; i < 20; i++)
            {
                task2Events.Add((timeProvider.UtcNow, $"Iteration {i}"));
                await timeProvider.Delay(TimeSpan.FromMilliseconds(500)); // 0.5 seconds simulated
            }
            task2Events.Add((timeProvider.UtcNow, "Completed"));
        });

        var task3 = Task.Run(async () =>
        {
            for (int i = 0; i < 5; i++)
            {
                task3Events.Add((timeProvider.UtcNow, $"Iteration {i}"));
                await timeProvider.Delay(TimeSpan.FromSeconds(2)); // 2 seconds simulated
            }
            task3Events.Add((timeProvider.UtcNow, "Completed"));
        });

        // Wait for all tasks with timeout
        var allTasks = Task.WhenAll(task1, task2, task3);
        var timeout = Task.Delay(TimeSpan.FromSeconds(5)); // 5 seconds real-time timeout

        var completedTask = await Task.WhenAny(allTasks, timeout);

        var realTimeElapsed = DateTimeOffset.UtcNow - realTimeStart;

        // Assert
        Assert.True(completedTask == allTasks,
            $"Tasks did not complete within 5 seconds real-time. " +
            $"Real time elapsed: {realTimeElapsed.TotalSeconds:F2}s. " +
            $"Task1 events: {task1Events.Count}, Task2 events: {task2Events.Count}, Task3 events: {task3Events.Count}");

        // All tasks should complete
        Assert.Equal(11, task1Events.Count); // 10 iterations + completed
        Assert.Equal(21, task2Events.Count); // 20 iterations + completed
        Assert.Equal(6, task3Events.Count);  // 5 iterations + completed

        // Verify simulated time progression
        // Task 1: 10 * 1s = 10 seconds
        // Task 2: 20 * 0.5s = 10 seconds
        // Task 3: 5 * 2s = 10 seconds
        // All should complete around 10 seconds simulated time

        var task1Duration = task1Events.Last().time - task1Events.First().time;
        var task2Duration = task2Events.Last().time - task2Events.First().time;
        var task3Duration = task3Events.Last().time - task3Events.First().time;

        Assert.InRange(task1Duration.TotalSeconds, 9.5, 10.5); // ~10 seconds
        Assert.InRange(task2Duration.TotalSeconds, 9.5, 10.5); // ~10 seconds
        Assert.InRange(task3Duration.TotalSeconds, 9.5, 10.5); // ~10 seconds

        // Verify real-world time was much faster than simulated time
        // At 10000x, 10 seconds simulated should take ~1ms real-time
        // Allow up to 2 seconds real-time for thread scheduling overhead
        Assert.True(realTimeElapsed.TotalSeconds < 2.0,
            $"Real-time execution ({realTimeElapsed.TotalSeconds:F2}s) should be much faster than simulated time (10s)");

        // Verify task events are in monotonic order (simulated time never goes backward)
        for (int i = 1; i < task1Events.Count; i++)
        {
            Assert.True(task1Events[i].time >= task1Events[i - 1].time,
                $"Task1 time went backward: {task1Events[i - 1].time} -> {task1Events[i].time}");
        }
    }

    [Fact]
    public async Task UnlimitedSpeed_TaskStarvation_NoDeadlock()
    {
        // This test specifically checks for task starvation:
        // - Many tasks competing for execution
        // - All should make progress even at extreme speeds
        // - No task should be starved indefinitely

        // Arrange
        var startTime = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new SimulatedTimeProvider(startTime);
        timeProvider.TimeScale = double.MaxValue; // Truly unlimited - as fast as possible

        var taskCompletionCounts = new int[10];
        var tasks = new List<Task>();

        // Act - Create 10 concurrent tasks
        for (int taskId = 0; taskId < 10; taskId++)
        {
            var id = taskId; // Capture for closure
            tasks.Add(Task.Run(async () =>
            {
                for (int i = 0; i < 5; i++)
                {
                    await timeProvider.Delay(TimeSpan.FromMilliseconds(100));
                    Interlocked.Increment(ref taskCompletionCounts[id]);
                }
            }));
        }

        var allTasks = Task.WhenAll(tasks);
        var timeout = Task.Delay(TimeSpan.FromSeconds(10)); // 10 seconds real-time timeout

        var completedTask = await Task.WhenAny(allTasks, timeout);

        // Assert - All tasks should complete, none starved
        Assert.True(completedTask == allTasks,
            $"Tasks timed out. Completion counts: [{string.Join(", ", taskCompletionCounts)}]");

        for (int i = 0; i < 10; i++)
        {
            Assert.True(taskCompletionCounts[i] == 5,
                $"Task {i} did not complete all iterations. Completed: {taskCompletionCounts[i]}/5");
        }

        // Verify simulated time advanced correctly
        // 10 tasks * 5 iterations * 100ms = but they run concurrently, so ~500ms simulated
        var simulatedElapsed = timeProvider.UtcNow - startTime;
        Assert.InRange(simulatedElapsed.TotalMilliseconds, 400, 600);
    }
}
