using AgOpenGPS.Core;
using AgOpenGPS.ModuleContracts;
using AgOpenGPS.ModuleContracts.Messages;
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
        var messageBus = new MessageBus(timeProvider);

        var testMessage = new GpsPositionMessage
        {
            Latitude = 40.7128,
            Longitude = -74.0060,
            Speed = 5.0,
            Heading = 90.0,
            FixQuality = GpsFixQuality.RTK_Fixed,
            TimestampMs = 0
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
        var messageBus = new MessageBus(timeProvider);

        var testMessage = new GpsPositionMessage
        {
            Latitude = 40.7128,
            Longitude = -74.0060,
            Speed = 5.0,
            Heading = 90.0,
            FixQuality = GpsFixQuality.RTK_Fixed,
            TimestampMs = 0
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
        // This test demonstrates how to run a 1-hour simulation in under 1 second

        // Arrange
        var startTime = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new SimulatedTimeProvider(startTime);
        timeProvider.TimeScale = 3600.0; // 3600x speed (1 hour per second)

        var messageBus = new MessageBus(timeProvider);
        var messageCount = 0;

        // Subscribe to count messages
        messageBus.Subscribe<GpsPositionMessage>(msg => messageCount++);

        var realTimeBefore = DateTimeOffset.UtcNow;

        // Act - simulate publishing messages every second for 1 hour (simulated)
        var simulationTask = Task.Run(async () =>
        {
            for (int i = 0; i < 3600; i++) // 1 hour worth of seconds
            {
                var testMessage = new GpsPositionMessage
                {
                    Latitude = 40.7128 + (i * 0.0001),
                    Longitude = -74.0060 + (i * 0.0001),
                    Speed = 5.0,
                    Heading = 90.0,
                    FixQuality = GpsFixQuality.RTK_Fixed,
                    TimestampMs = timeProvider.UtcNow.ToUnixTimeMilliseconds()
                };

                messageBus.Publish(in testMessage);
                await timeProvider.Delay(TimeSpan.FromSeconds(1));
            }
        });

        await simulationTask;

        // Assert
        var realTimeAfter = DateTimeOffset.UtcNow;
        var realElapsed = (realTimeAfter - realTimeBefore).TotalSeconds;

        // Should have taken about 1 second in real time (3600 seconds / 3600x speed)
        Assert.InRange(realElapsed, 0.8, 2.0); // Allow tolerance

        // Should have simulated 1 hour
        var simulatedElapsed = timeProvider.UtcNow - startTime;
        Assert.InRange(simulatedElapsed.TotalHours, 0.99, 1.01);

        // Should have received 3600 messages
        Assert.Equal(3600, messageCount);
    }
}
