/*
 * EVENT SCHEDULER EXAMPLE
 *
 * This example demonstrates the unified EventScheduler that combines:
 * 1. Rate-based scheduled methods (fixed Hz ticks)
 * 2. Time-based async delays (await timeProvider.Delay())
 *
 * The scheduler determines the next event (whichever comes first) and advances
 * time appropriately, then executes all events at that time.
 *
 * Key Features Demonstrated:
 * 1. Scheduling methods at specific rates (Hz)
 * 2. Mixing rate-based and time-based events
 * 3. Simulation mode (unlimited speed)
 * 4. Real-time mode (time-scaled execution)
 * 5. Pause/resume functionality
 * 6. Event coordination and timing
 */

using AgOpenGPS.Core;
using Microsoft.Extensions.Logging;

namespace AgOpenGPS.Examples;

public class EventSchedulerExample
{
    public static async Task RunAsync()
    {
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });
        var logger = loggerFactory.CreateLogger<EventSchedulerExample>();

        logger.LogInformation("=== Event Scheduler Demo ===\n");

        // Run all demos
        await Demo1_BasicScheduling(logger);
        await Demo2_MixedEvents(logger);
        await Demo3_SimulationMode(logger);
        await Demo4_RealTimeMode(logger);
        await Demo5_PauseResume(logger);

        logger.LogInformation("\n=== All Demos Complete ===");
    }

    /// <summary>
    /// DEMO 1: Basic rate-based scheduling
    /// Shows how to schedule methods at fixed rates (Hz)
    /// </summary>
    private static async Task Demo1_BasicScheduling(ILogger logger)
    {
        logger.LogInformation("\n--- DEMO 1: Basic Rate-Based Scheduling ---");
        logger.LogInformation("Scheduling methods at 1Hz, 2Hz, and 5Hz...\n");

        var startTime = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new SimulatedTimeProvider(startTime);
        var scheduler = new EventScheduler(timeProvider);

        var count1Hz = 0;
        var count2Hz = 0;
        var count5Hz = 0;

        // Schedule three methods at different rates
        var handle1Hz = scheduler.Schedule(() =>
        {
            count1Hz++;
            logger.LogInformation($"[{timeProvider.UtcNow:HH:mm:ss.fff}] 1Hz method executed (count: {count1Hz})");
        }, 1.0, "Method_1Hz");

        var handle2Hz = scheduler.Schedule(() =>
        {
            count2Hz++;
            logger.LogInformation($"[{timeProvider.UtcNow:HH:mm:ss.fff}] 2Hz method executed (count: {count2Hz})");
        }, 2.0, "Method_2Hz");

        var handle5Hz = scheduler.Schedule(() =>
        {
            count5Hz++;
            logger.LogInformation($"[{timeProvider.UtcNow:HH:mm:ss.fff}] 5Hz method executed (count: {count5Hz})");
        }, 5.0, "Method_5Hz");

        // Run for 2 seconds
        var demoTask = Task.Run(async () =>
        {
            await Task.Delay(100); // Let scheduler run
            await timeProvider.Delay(TimeSpan.FromSeconds(2));
        });

        await scheduler.RunSimulationAsync(new[] { demoTask });

        logger.LogInformation($"\nResults after 2 seconds:");
        logger.LogInformation($"  1Hz: {count1Hz} executions (expected: ~2)");
        logger.LogInformation($"  2Hz: {count2Hz} executions (expected: ~4)");
        logger.LogInformation($"  5Hz: {count5Hz} executions (expected: ~10)");

        // Cleanup
        handle1Hz.Dispose();
        handle2Hz.Dispose();
        handle5Hz.Dispose();

        logger.LogInformation("\nPress Enter to continue...");
        Console.ReadLine();
    }

    /// <summary>
    /// DEMO 2: Mixed rate-based and time-based events
    /// Shows the unified scheduler coordinating both types of events
    /// </summary>
    private static async Task Demo2_MixedEvents(ILogger logger)
    {
        logger.LogInformation("\n--- DEMO 2: Mixed Rate-Based and Time-Based Events ---");
        logger.LogInformation("Combining scheduled methods with async delays...\n");

        var startTime = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new SimulatedTimeProvider(startTime);
        var scheduler = new EventScheduler(timeProvider);

        var sensorReadings = 0;

        // Schedule a sensor reading at 10Hz
        var sensorHandle = scheduler.Schedule(() =>
        {
            sensorReadings++;
            logger.LogInformation($"[{timeProvider.UtcNow:HH:mm:ss.fff}] Sensor reading #{sensorReadings}");
        }, 10.0, "SensorReader");

        // Task that does processing with delays
        var processingTask = Task.Run(async () =>
        {
            logger.LogInformation($"[{timeProvider.UtcNow:HH:mm:ss.fff}] Starting data processing...");

            await timeProvider.Delay(TimeSpan.FromMilliseconds(250));
            logger.LogInformation($"[{timeProvider.UtcNow:HH:mm:ss.fff}] Processing step 1 complete");

            await timeProvider.Delay(TimeSpan.FromMilliseconds(250));
            logger.LogInformation($"[{timeProvider.UtcNow:HH:mm:ss.fff}] Processing step 2 complete");

            await timeProvider.Delay(TimeSpan.FromMilliseconds(500));
            logger.LogInformation($"[{timeProvider.UtcNow:HH:mm:ss.fff}] Processing step 3 complete");

            logger.LogInformation($"[{timeProvider.UtcNow:HH:mm:ss.fff}] All processing complete!");
        });

        await scheduler.RunSimulationAsync(new[] { processingTask });

        var elapsed = timeProvider.UtcNow - startTime;
        logger.LogInformation($"\nCompleted in {elapsed.TotalMilliseconds:F0}ms simulated time");
        logger.LogInformation($"Sensor readings: {sensorReadings} (expected: ~10 in 1 second)");

        sensorHandle.Dispose();

        logger.LogInformation("\nPress Enter to continue...");
        Console.ReadLine();
    }

    /// <summary>
    /// DEMO 3: Simulation mode (unlimited speed)
    /// Demonstrates instant time advancement for testing/simulation
    /// </summary>
    private static async Task Demo3_SimulationMode(ILogger logger)
    {
        logger.LogInformation("\n--- DEMO 3: Simulation Mode (Unlimited Speed) ---");
        logger.LogInformation("Running 10 seconds of simulated time as fast as possible...\n");

        var startTime = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new SimulatedTimeProvider(startTime);
        var scheduler = new EventScheduler(timeProvider);

        var eventCount = 0;

        // Schedule at 5Hz
        var handle = scheduler.Schedule(() =>
        {
            eventCount++;
        }, 5.0, "FastMethod");

        // Measure real-world time
        var realStartTime = DateTimeOffset.UtcNow;

        // Run 10 seconds of simulated time
        var task = Task.Run(async () =>
        {
            await timeProvider.Delay(TimeSpan.FromSeconds(10));
        });

        await scheduler.RunSimulationAsync(new[] { task });

        var realElapsed = DateTimeOffset.UtcNow - realStartTime;
        var simElapsed = timeProvider.UtcNow - startTime;

        logger.LogInformation($"Simulated time: {simElapsed.TotalSeconds:F1} seconds");
        logger.LogInformation($"Real-world time: {realElapsed.TotalMilliseconds:F0} milliseconds");
        logger.LogInformation($"Speed-up factor: {simElapsed.TotalMilliseconds / realElapsed.TotalMilliseconds:F0}x");
        logger.LogInformation($"Events executed: {eventCount}");

        handle.Dispose();

        logger.LogInformation("\nPress Enter to continue...");
        Console.ReadLine();
    }

    /// <summary>
    /// DEMO 4: Real-time mode with time scaling
    /// Demonstrates time-scaled execution (e.g., 10x speed)
    /// </summary>
    private static async Task Demo4_RealTimeMode(ILogger logger)
    {
        logger.LogInformation("\n--- DEMO 4: Real-Time Mode with 10x Time Scaling ---");
        logger.LogInformation("Running at 10x real-time speed...\n");

        var startTime = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new SimulatedTimeProvider(startTime);
        timeProvider.TimeScale = 10.0; // 10x speed

        var scheduler = new EventScheduler(timeProvider);

        var tickCount = 0;

        // Schedule at 2Hz
        var handle = scheduler.Schedule(() =>
        {
            tickCount++;
            logger.LogInformation($"[{timeProvider.UtcNow:HH:mm:ss.fff}] Tick #{tickCount}");
        }, 2.0, "RealTimeMethod");

        // Measure real-world time
        var realStartTime = DateTimeOffset.UtcNow;

        // Run 2 seconds of simulated time at 10x speed
        var task = Task.Run(async () =>
        {
            await timeProvider.Delay(TimeSpan.FromSeconds(2));
        });

        await scheduler.RunRealTimeAsync(new[] { task });

        var realElapsed = DateTimeOffset.UtcNow - realStartTime;
        var simElapsed = timeProvider.UtcNow - startTime;

        logger.LogInformation($"\nSimulated time: {simElapsed.TotalSeconds:F1} seconds");
        logger.LogInformation($"Real-world time: {realElapsed.TotalMilliseconds:F0} milliseconds");
        logger.LogInformation($"Expected real-world time: ~200ms (2s / 10x)");
        logger.LogInformation($"Ticks executed: {tickCount}");

        handle.Dispose();

        logger.LogInformation("\nPress Enter to continue...");
        Console.ReadLine();
    }

    /// <summary>
    /// DEMO 5: Pause and resume functionality
    /// Demonstrates runtime control over scheduled methods
    /// </summary>
    private static async Task Demo5_PauseResume(ILogger logger)
    {
        logger.LogInformation("\n--- DEMO 5: Pause/Resume Functionality ---");
        logger.LogInformation("Demonstrating runtime control over scheduled methods...\n");

        var startTime = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new SimulatedTimeProvider(startTime);
        var scheduler = new EventScheduler(timeProvider);

        var countBeforePause = 0;
        var countAfterResume = 0;

        // Schedule at 5Hz
        var handle = scheduler.Schedule(() =>
        {
            if (countBeforePause < 5)
                countBeforePause++;
            else
                countAfterResume++;

            logger.LogInformation($"[{timeProvider.UtcNow:HH:mm:ss.fff}] Method executed (before: {countBeforePause}, after: {countAfterResume})");
        }, 5.0, "ControlledMethod");

        var task = Task.Run(async () =>
        {
            // Run for 1 second
            await timeProvider.Delay(TimeSpan.FromSeconds(1));

            // Pause
            logger.LogInformation($"\n[{timeProvider.UtcNow:HH:mm:ss.fff}] PAUSING method...\n");
            handle.Pause();

            // Wait while paused (time advances but method doesn't execute)
            await timeProvider.Delay(TimeSpan.FromSeconds(1));
            logger.LogInformation($"[{timeProvider.UtcNow:HH:mm:ss.fff}] Time advanced 1 second while paused\n");

            // Resume
            logger.LogInformation($"[{timeProvider.UtcNow:HH:mm:ss.fff}] RESUMING method...\n");
            handle.Resume();

            // Run for another second
            await timeProvider.Delay(TimeSpan.FromSeconds(1));
        });

        await scheduler.RunSimulationAsync(new[] { task });

        logger.LogInformation($"\nResults:");
        logger.LogInformation($"  Executions before pause: {countBeforePause} (expected: ~5)");
        logger.LogInformation($"  Executions after resume: {countAfterResume} (expected: ~5)");
        logger.LogInformation($"  Total simulated time: 3 seconds");

        handle.Dispose();

        logger.LogInformation("\nPress Enter to finish...");
        Console.ReadLine();
    }
}
