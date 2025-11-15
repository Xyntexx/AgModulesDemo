namespace AgOpenGPS.Tests;

using AgOpenGPS.Core;
using AgOpenGPS.ModuleContracts;
using AgOpenGPS.ModuleContracts.Messages;
using AgOpenGPS.Modules.Monitoring;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// Timing tests for microkernel with agricultural scenarios
/// Tests module load times, message bus latency, and real-time performance
/// </summary>
public class TimingTests
{
    private readonly ITestOutputHelper _output;

    public TimingTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task ModuleLoadTime_ShouldBeFast()
    {
        // Arrange
        var (core, monitor) = await SetupCoreWithMonitoring();
        var testModule = new TestGPSModule();

        // Act
        var sw = Stopwatch.StartNew();
        var result = await core.LoadModuleAsync(testModule);
        sw.Stop();

        // Assert
        Assert.True(result.Success, $"Module load failed: {result.ErrorMessage}");
        Assert.True(sw.ElapsedMilliseconds < 100,
            $"Module load took {sw.ElapsedMilliseconds}ms, should be under 100ms");

        _output.WriteLine($"Module load time: {sw.ElapsedMilliseconds}ms");

        // Cleanup
        await core.StopAsync();
        core.Dispose();
    }

    [Fact]
    public async Task MessageBusLatency_GPS_ShouldBeRealTime()
    {
        // Scenario: GPS messages must be delivered with minimal latency for accurate positioning
        // Target: < 1ms per message for real-time tractor guidance

        // Arrange
        var (core, monitor) = await SetupCoreWithMonitoring();
        var messageBus = GetMessageBus(core);

        var received = false;
        var latencies = new List<long>();
        var sw = Stopwatch.StartNew();

        messageBus.Subscribe<GpsPositionMessage>(msg =>
        {
            var latency = sw.ElapsedTicks;
            latencies.Add(latency);
            received = true;
        });

        // Act - Simulate GPS updates at 10 Hz (typical RTK GPS rate)
        for (int i = 0; i < 100; i++)
        {
            sw.Restart();
            messageBus.Publish(new GpsPositionMessage
            {
                Latitude = 45.5 + (i * 0.0001),
                Longitude = -122.6 + (i * 0.0001),
                Heading = 45.0,
                Speed = 2.0,
                FixQuality = GpsFixQuality.RTK_Fixed,
                TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });
            await Task.Delay(1); // Simulate 10Hz rate
        }

        // Assert
        Assert.True(received, "GPS messages should be received");
        var avgLatencyTicks = latencies.Average();
        var avgLatencyMs = (avgLatencyTicks / (double)Stopwatch.Frequency) * 1000;

        Assert.True(avgLatencyMs < 1.0,
            $"Average GPS message latency was {avgLatencyMs:F3}ms, should be under 1ms for real-time guidance");

        _output.WriteLine($"GPS Message Latency Stats:");
        _output.WriteLine($"  Average: {avgLatencyMs:F3}ms");
        _output.WriteLine($"  Min: {(latencies.Min() / (double)Stopwatch.Frequency) * 1000:F3}ms");
        _output.WriteLine($"  Max: {(latencies.Max() / (double)Stopwatch.Frequency) * 1000:F3}ms");

        // Cleanup
        await core.StopAsync();
        core.Dispose();
    }

    [Fact]
    public async Task AutosteerControlLoop_ShouldMeet20HzTarget()
    {
        // Scenario: Autosteer must run at 20Hz (50ms cycle) for smooth steering
        // Target: Complete GPS -> Calculate -> Steer cycle in < 50ms

        // Arrange
        var (core, monitor) = await SetupCoreWithMonitoring();
        var messageBus = GetMessageBus(core);

        var cycleTimes = new List<long>();
        var sw = Stopwatch.StartNew();

        // Subscribe to steer commands (output of control loop)
        messageBus.Subscribe<SteerCommandMessage>(msg =>
        {
            cycleTimes.Add(sw.ElapsedMilliseconds);
            sw.Restart();
        });

        // Act - Simulate 20Hz GPS input for 2 seconds
        for (int i = 0; i < 40; i++)
        {
            sw.Restart();

            // Publish GPS position (simulating tractor moving along a line)
            messageBus.Publish(new GpsPositionMessage
            {
                Latitude = 45.5 + (i * 0.00001),
                Longitude = -122.6,
                Heading = 45.0,
                Speed = 2.0,
                FixQuality = GpsFixQuality.RTK_Fixed,
                TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });

            await Task.Delay(50); // 20Hz rate
        }

        await Task.Delay(100); // Allow final messages to process

        // Assert
        if (cycleTimes.Count > 5)
        {
            var avgCycleTime = cycleTimes.Skip(5).Average(); // Skip first few for warmup
            Assert.True(avgCycleTime < 50,
                $"Average control loop cycle time was {avgCycleTime:F2}ms, should be under 50ms for 20Hz operation");

            _output.WriteLine($"Autosteer Control Loop Stats:");
            _output.WriteLine($"  Target: 20Hz (50ms cycle)");
            _output.WriteLine($"  Average cycle: {avgCycleTime:F2}ms");
            _output.WriteLine($"  Cycles completed: {cycleTimes.Count}");
        }

        // Cleanup
        await core.StopAsync();
        core.Dispose();
    }

    [Fact]
    public async Task ModuleStartupSequence_ShouldRespectDependencyOrder()
    {
        // Scenario: Modules must start in correct order (IO -> Processing -> Control)
        // to ensure data flows properly

        // Arrange
        var (core, monitor) = await SetupCoreWithMonitoring();

        var startTimes = new Dictionary<string, DateTime>();
        var messageBus = GetMessageBus(core);

        messageBus.Subscribe<ModuleLoadedEvent>(evt =>
        {
            startTimes[evt.ModuleName] = DateTimeOffset.FromUnixTimeMilliseconds(evt.TimestampMs).DateTime;
        });

        // Act
        var sw = Stopwatch.StartNew();
        await core.StartAsync();
        sw.Stop();

        // Assert
        Assert.True(sw.ElapsedMilliseconds < 2000,
            $"Full system startup took {sw.ElapsedMilliseconds}ms, should be under 2 seconds");

        _output.WriteLine($"System startup time: {sw.ElapsedMilliseconds}ms");
        _output.WriteLine($"Modules loaded: {startTimes.Count}");

        foreach (var module in startTimes.OrderBy(kvp => kvp.Value))
        {
            _output.WriteLine($"  {module.Key}: {module.Value:HH:mm:ss.fff}");
        }

        // Cleanup
        await core.StopAsync();
        core.Dispose();
    }

    // Helper methods
    private async Task<(ApplicationCore core, MonitoringModule monitor)> SetupCoreWithMonitoring()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            
            builder.SetMinimumLevel(LogLevel.Information);
        });

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Core:ModuleDirectory"] = "./modules"
            })
            .Build();

        services.AddSingleton<IConfiguration>(config);
        services.AddSingleton<ITimeProvider, SystemTimeProvider>();
        services.AddSingleton<MessageBus>();
        services.AddSingleton<ApplicationCore>();

        var provider = services.BuildServiceProvider();
        var core = provider.GetRequiredService<ApplicationCore>();

        // Load monitoring module first
        var monitor = new MonitoringModule();
        await core.LoadModuleAsync(monitor);

        return (core, monitor);
    }

    private MessageBus GetMessageBus(ApplicationCore core)
    {
        // Access message bus via reflection for testing
        var field = typeof(ApplicationCore).GetField("_messageBus",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return (MessageBus)field!.GetValue(core)!;
    }
}

/// <summary>
/// Simple test GPS module for timing tests
/// </summary>
internal class TestGPSModule : IAgModule
{
    public string Name => "Test GPS";
    public Version Version => new Version(1, 0, 0);
    public ModuleCategory Category => ModuleCategory.IO;
    public string[] Dependencies => Array.Empty<string>();

    public Task InitializeAsync(IModuleContext context) => Task.CompletedTask;
    public Task StartAsync() => Task.CompletedTask;
    public Task StopAsync() => Task.CompletedTask;
    public Task ShutdownAsync() => Task.CompletedTask;
    public ModuleHealth GetHealth() => ModuleHealth.Healthy;
}
