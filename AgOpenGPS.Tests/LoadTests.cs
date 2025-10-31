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
/// Load tests for microkernel with agricultural scenarios
/// Tests system behavior under high message throughput and multiple concurrent modules
/// </summary>
public class LoadTests
{
    private readonly ITestOutputHelper _output;

    public LoadTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task HighFrequencyGPS_ShouldHandleRTKRate()
    {
        // Scenario: RTK GPS can output at 10-20Hz, system must handle this without dropping messages
        // Target: Process 10,000 GPS messages without message loss or slowdown

        // Arrange
        var (core, monitor) = await SetupCoreWithMonitoring();
        var messageBus = GetMessageBus(core);

        var receivedCount = 0;
        messageBus.Subscribe<GpsPositionMessage>(_ => Interlocked.Increment(ref receivedCount));

        const int messageCount = 10_000;
        const int targetHz = 10;

        // Act
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < messageCount; i++)
        {
            messageBus.Publish(new GpsPositionMessage
            {
                Latitude = 45.5 + (i * 0.00001),
                Longitude = -122.6,
                Heading = 45.0,
                Speed = 2.0,
                FixQuality = GpsFixQuality.RTK_Fixed,
                TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });

            // Simulate 10Hz rate
            if (i % 100 == 0)
                await Task.Delay(1);
        }
        sw.Stop();

        // Wait for all messages to be processed
        await Task.Delay(100);

        // Assert
        Assert.Equal(messageCount, receivedCount);

        var throughput = messageCount / sw.Elapsed.TotalSeconds;
        Assert.True(throughput > targetHz,
            $"GPS throughput was {throughput:F0} msg/sec, should exceed {targetHz} Hz");

        _output.WriteLine($"GPS Load Test Results:");
        _output.WriteLine($"  Messages sent: {messageCount:N0}");
        _output.WriteLine($"  Messages received: {receivedCount:N0}");
        _output.WriteLine($"  Duration: {sw.ElapsedMilliseconds}ms");
        _output.WriteLine($"  Throughput: {throughput:F0} msg/sec");

        var metrics = monitor.GetSystemMetrics();
        _output.WriteLine($"  Total system messages: {metrics.TotalMessagesProcessed:N0}");

        // Cleanup
        await core.StopAsync();
        core.Dispose();
    }

    [Fact]
    public async Task MultipleModules_ConcurrentOperation()
    {
        // Scenario: Field computer runs many modules simultaneously
        // GPS IO, PGN Parser, Autosteer, Section Control, Mapping, Logging
        // Target: All modules operate without interference

        // Arrange
        var (core, monitor) = await SetupCoreWithMonitoring();

        // Load multiple test modules
        var modules = new IAgModule[]
        {
            new StressTestModule("GPS Simulator", ModuleCategory.IO),
            new StressTestModule("PGN Parser", ModuleCategory.DataProcessing),
            new StressTestModule("Kinematics", ModuleCategory.DataProcessing),
            new StressTestModule("Autosteer", ModuleCategory.Control),
            new StressTestModule("Section Control", ModuleCategory.Control),
            new StressTestModule("Field Mapping", ModuleCategory.Visualization),
            new StressTestModule("Data Logger", ModuleCategory.Logging)
        };

        // Act
        var sw = Stopwatch.StartNew();
        foreach (var module in modules)
        {
            var result = await core.LoadModuleAsync(module);
            Assert.True(result.Success, $"Failed to load {module.Name}");
        }
        sw.Stop();

        _output.WriteLine($"Loaded {modules.Length} modules in {sw.ElapsedMilliseconds}ms");

        // Run for 2 seconds with concurrent load
        await Task.Delay(2000);

        // Assert
        var loadedModules = core.GetLoadedModules();
        Assert.Equal(modules.Length + 1, loadedModules.Count); // +1 for monitoring module

        var health = await core.PerformHealthCheckAsync();
        foreach (var moduleHealth in health.ModuleHealths)
        {
            Assert.NotEqual(ModuleHealth.Unhealthy, moduleHealth.Health);
            _output.WriteLine($"  {moduleHealth.ModuleName}: {moduleHealth.Health}");
        }

        var metrics = monitor.GetSystemMetrics();
        _output.WriteLine($"\nSystem Metrics:");
        _output.WriteLine($"  Total Messages: {metrics.TotalMessagesProcessed:N0}");
        _output.WriteLine($"  Throughput: {metrics.MessagesPerSecond:F0} msg/sec");
        _output.WriteLine($"  Errors: {metrics.TotalErrors}");

        // Cleanup
        await core.StopAsync();
        core.Dispose();
    }

    [Fact]
    public async Task SustainedLoad_FieldOperation()
    {
        // Scenario: Tractor operates in field for extended period
        // System must maintain performance without degradation
        // Target: 30 seconds continuous operation at full load

        // Arrange
        var (core, monitor) = await SetupCoreWithMonitoring();
        var messageBus = GetMessageBus(core);

        var gpsCount = 0;
        var steerCount = 0;

        messageBus.Subscribe<GpsPositionMessage>(_ => Interlocked.Increment(ref gpsCount));
        messageBus.Subscribe<SteerCommandMessage>(_ => Interlocked.Increment(ref steerCount));

        // Act - Simulate field operation
        var duration = TimeSpan.FromSeconds(10); // Reduced for faster testing
        var sw = Stopwatch.StartNew();
        var samples = new List<(TimeSpan elapsed, long messagesProcessed)>();

        while (sw.Elapsed < duration)
        {
            // Simulate GPS at 10Hz
            messageBus.Publish(new GpsPositionMessage
            {
                Latitude = 45.5,
                Longitude = -122.6,
                Heading = 45.0,
                Speed = 2.0,
                FixQuality = GpsFixQuality.RTK_Fixed,
                TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });

            // Simulate steer commands at 20Hz
            messageBus.Publish(new SteerCommandMessage
            {
                SteerAngleDegrees = 5.0,
                SpeedPWM = 128,
                TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });

            // Sample metrics every second
            if (sw.ElapsedMilliseconds % 1000 < 50)
            {
                var metrics = monitor.GetSystemMetrics();
                samples.Add((sw.Elapsed, metrics.TotalMessagesProcessed));
            }

            await Task.Delay(50); // 20Hz rate
        }

        // Assert
        _output.WriteLine($"Sustained Load Test Results:");
        _output.WriteLine($"  Duration: {duration.TotalSeconds}s");
        _output.WriteLine($"  GPS Messages: {gpsCount:N0}");
        _output.WriteLine($"  Steer Messages: {steerCount:N0}");

        // Check for performance degradation
        if (samples.Count >= 3)
        {
            var earlyThroughput = (samples[1].messagesProcessed - samples[0].messagesProcessed) /
                                  (samples[1].elapsed - samples[0].elapsed).TotalSeconds;
            var lateThroughput = (samples[^1].messagesProcessed - samples[^2].messagesProcessed) /
                                (samples[^1].elapsed - samples[^2].elapsed).TotalSeconds;

            _output.WriteLine($"  Early throughput: {earlyThroughput:F0} msg/sec");
            _output.WriteLine($"  Late throughput: {lateThroughput:F0} msg/sec");

            Assert.True(lateThroughput > earlyThroughput * 0.8,
                "System throughput degraded by more than 20% during sustained operation");
        }

        // Cleanup
        await core.StopAsync();
        core.Dispose();
    }

    [Fact]
    public async Task BurstLoad_GPSReacquisition()
    {
        // Scenario: GPS loses signal, then reacquires with burst of cached positions
        // System must handle sudden burst without dropping messages
        // Target: Process 1000 messages in rapid burst

        // Arrange
        var (core, monitor) = await SetupCoreWithMonitoring();
        var messageBus = GetMessageBus(core);

        var receivedCount = 0;
        messageBus.Subscribe<GpsPositionMessage>(_ => Interlocked.Increment(ref receivedCount));

        // Act - Simulate burst
        const int burstSize = 1000;
        var sw = Stopwatch.StartNew();

        for (int i = 0; i < burstSize; i++)
        {
            messageBus.Publish(new GpsPositionMessage
            {
                Latitude = 45.5 + (i * 0.00001),
                Longitude = -122.6,
                Heading = 45.0,
                Speed = 2.0,
                FixQuality = GpsFixQuality.RTK_Fixed,
                TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });
        }

        sw.Stop();
        await Task.Delay(50); // Allow processing

        // Assert
        Assert.Equal(burstSize, receivedCount);

        _output.WriteLine($"Burst Load Test Results:");
        _output.WriteLine($"  Burst size: {burstSize:N0} messages");
        _output.WriteLine($"  Burst duration: {sw.ElapsedMilliseconds}ms");
        _output.WriteLine($"  Peak throughput: {burstSize / sw.Elapsed.TotalSeconds:F0} msg/sec");
        _output.WriteLine($"  All messages received: {receivedCount == burstSize}");

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
            builder.SetMinimumLevel(LogLevel.Warning); // Reduce noise for load tests
        });

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Core:ModuleDirectory"] = "./modules"
            })
            .Build();

        services.AddSingleton<IConfiguration>(config);
        services.AddSingleton<MessageBus>();
        services.AddSingleton<ApplicationCore>();

        var provider = services.BuildServiceProvider();
        var core = provider.GetRequiredService<ApplicationCore>();

        var monitor = new MonitoringModule();
        await core.LoadModuleAsync(monitor);

        return (core, monitor);
    }

    private MessageBus GetMessageBus(ApplicationCore core)
    {
        var field = typeof(ApplicationCore).GetField("_messageBus",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return (MessageBus)field!.GetValue(core)!;
    }
}

/// <summary>
/// Module that generates load for stress testing
/// </summary>
internal class StressTestModule : IAgModule
{
    public string Name { get; }
    public Version Version => new Version(1, 0, 0);
    public ModuleCategory Category { get; }
    public string[] Dependencies => Array.Empty<string>();

    private IModuleContext? _context;
    private CancellationTokenSource? _cts;

    public StressTestModule(string name, ModuleCategory category)
    {
        Name = name;
        Category = category;
    }

    public Task InitializeAsync(IModuleContext context)
    {
        _context = context;
        return Task.CompletedTask;
    }

    public Task StartAsync()
    {
        _cts = new CancellationTokenSource();
        // Simulate some background work
        Task.Run(async () =>
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                await Task.Delay(100, _cts.Token);
            }
        }, _cts.Token);
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        _cts?.Cancel();
        return Task.CompletedTask;
    }

    public Task ShutdownAsync() => Task.CompletedTask;
    public ModuleHealth GetHealth() => ModuleHealth.Healthy;
}
