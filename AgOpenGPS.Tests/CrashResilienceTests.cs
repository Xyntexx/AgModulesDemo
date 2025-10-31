namespace AgOpenGPS.Tests;

using AgOpenGPS.Core;
using AgOpenGPS.ModuleContracts;
using AgOpenGPS.ModuleContracts.Messages;
using AgOpenGPS.Modules.Monitoring;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// Crash and resilience tests for microkernel
/// Tests system behavior when modules crash, hang, or misbehave
/// Critical for agricultural applications where reliability is paramount
/// </summary>
public class CrashResilienceTests
{
    private readonly ITestOutputHelper _output;

    public CrashResilienceTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task CrashedModule_ShouldNotAffectOtherModules()
    {
        // Scenario: Logging module crashes but GPS and autosteer must continue
        // Target: System remains operational after module crash

        // Arrange
        var (core, monitor) = await SetupCoreWithMonitoring();

        var stableModule = new StableModule("GPS IO");
        var crashingModule = new CrashingModule("Data Logger");

        await core.LoadModuleAsync(stableModule);
        await core.LoadModuleAsync(crashingModule);

        // Act - Trigger crash in logging module
        var messageBus = GetMessageBus(core);
        var receivedAfterCrash = 0;

        messageBus.Subscribe<GpsPositionMessage>(_ => Interlocked.Increment(ref receivedAfterCrash));

        // This will cause the crashing module to crash
        messageBus.Publish(new GpsPositionMessage
        {
            Latitude = 45.5,
            Longitude = -122.6,
            Heading = 45.0,
            Speed = 2.0,
            FixQuality = GpsFixQuality.RTK_Fixed,
            TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        });

        await Task.Delay(100);

        // Continue sending messages
        for (int i = 0; i < 10; i++)
        {
            messageBus.Publish(new GpsPositionMessage
            {
                Latitude = 45.5 + i * 0.00001,
                Longitude = -122.6,
                Heading = 45.0,
                Speed = 2.0,
                FixQuality = GpsFixQuality.RTK_Fixed,
                TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });
        }

        await Task.Delay(100);

        // Assert
        Assert.True(receivedAfterCrash >= 10,
            $"System should continue processing messages after module crash. Received: {receivedAfterCrash}");

        _output.WriteLine($"Crash Resilience Test:");
        _output.WriteLine($"  Messages received after crash: {receivedAfterCrash}");
        _output.WriteLine($"  System continued operation: {receivedAfterCrash >= 10}");

        // Cleanup
        await core.StopAsync();
        core.Dispose();
    }

    [Fact]
    public async Task SlowModule_ShouldNotBlockOthers()
    {
        // Scenario: Section control module is slow, but autosteer must remain responsive
        // Target: Fast modules not blocked by slow modules

        // Arrange
        var (core, monitor) = await SetupCoreWithMonitoring();

        var fastModule = new FastModule("Autosteer");
        var slowModule = new SlowModule("Section Control");

        await core.LoadModuleAsync(fastModule);
        await core.LoadModuleAsync(slowModule);

        // Act
        var messageBus = GetMessageBus(core);
        var fastModuleReceived = 0;
        var slowModuleReceived = 0;

        messageBus.Subscribe<GpsPositionMessage>(msg =>
        {
            Interlocked.Increment(ref fastModuleReceived);
        });

        messageBus.Subscribe<SteerCommandMessage>(msg =>
        {
            Thread.Sleep(100); // Simulate slow processing
            Interlocked.Increment(ref slowModuleReceived);
        });

        // Send messages rapidly
        for (int i = 0; i < 20; i++)
        {
            messageBus.Publish(new GpsPositionMessage
            {
                Latitude = 45.5,
                Longitude = -122.6,
                Heading = 45.0,
                Speed = 2.0,
                FixQuality = GpsFixQuality.RTK_Fixed,
                TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });

            messageBus.Publish(new SteerCommandMessage
            {
                SteerAngleDegrees = 5.0,
                SpeedPWM = 128,
                TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });

            await Task.Delay(10);
        }

        await Task.Delay(200);

        // Assert
        Assert.Equal(20, fastModuleReceived);
        _output.WriteLine($"Slow Module Test:");
        _output.WriteLine($"  Fast module received: {fastModuleReceived}/20");
        _output.WriteLine($"  Slow module received: {slowModuleReceived}/20");
        _output.WriteLine($"  Fast module not blocked: {fastModuleReceived == 20}");

        // Cleanup
        await core.StopAsync();
        core.Dispose();
    }

    [Fact]
    public async Task ModuleInitializationFailure_ShouldBeHandled()
    {
        // Scenario: Hardware module fails to initialize (e.g., serial port not available)
        // Target: System logs error but continues with other modules

        // Arrange
        var (core, monitor) = await SetupCoreWithMonitoring();

        var goodModule = new StableModule("GPS Simulator");
        var failingModule = new FailingInitModule("Serial Hardware");

        // Act
        var goodResult = await core.LoadModuleAsync(goodModule);
        var badResult = await core.LoadModuleAsync(failingModule);

        // Assert
        Assert.True(goodResult.Success, "Good module should load successfully");
        Assert.False(badResult.Success, "Failing module should fail to load");

        var loadedModules = core.GetLoadedModules();
        Assert.Contains(loadedModules, m => m.Name == "GPS Simulator");
        Assert.DoesNotContain(loadedModules, m => m.Name == "Serial Hardware");

        _output.WriteLine($"Module Initialization Failure Test:");
        _output.WriteLine($"  Good module loaded: {goodResult.Success}");
        _output.WriteLine($"  Failing module handled: {!badResult.Success}");
        _output.WriteLine($"  Error message: {badResult.ErrorMessage}");
        _output.WriteLine($"  System operational: {loadedModules.Count >= 2}"); // Monitor + good module

        // Cleanup
        await core.StopAsync();
        core.Dispose();
    }

    [Fact]
    public async Task HotReload_DuringFieldOperation()
    {
        // Scenario: Update autosteer module while tractor is operating
        // Target: Reload module without losing GPS signal or crashing

        // Arrange
        var (core, monitor) = await SetupCoreWithMonitoring();
        var messageBus = GetMessageBus(core);

        var module = new StableModule("Autosteer v1");
        var loadResult = await core.LoadModuleAsync(module);
        Assert.True(loadResult.Success);

        var messageCount = 0;
        messageBus.Subscribe<GpsPositionMessage>(_ => Interlocked.Increment(ref messageCount));

        // Start GPS stream
        var cts = new CancellationTokenSource();
        var gpsTask = Task.Run(async () =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                messageBus.Publish(new GpsPositionMessage
                {
                    Latitude = 45.5,
                    Longitude = -122.6,
                    Heading = 45.0,
                    Speed = 2.0,
                    FixQuality = GpsFixQuality.RTK_Fixed,
                    TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                });
                await Task.Delay(100); // 10Hz
            }
        }, cts.Token);

        await Task.Delay(500); // Let GPS run
        var countBeforeReload = messageCount;

        // Act - Hot reload module
        var moduleId = $"{module.Name}:{module.Version}".Replace(" ", "_");
        var reloadResult = await core.ReloadModuleAsync(moduleId);

        await Task.Delay(500); // Let GPS continue
        cts.Cancel();

        await gpsTask;

        // Assert
        Assert.True(reloadResult.Success, $"Hot reload failed: {reloadResult.ErrorMessage}");
        Assert.True(messageCount > countBeforeReload + 3,
            "GPS messages should continue during and after reload");

        _output.WriteLine($"Hot Reload Test:");
        _output.WriteLine($"  Messages before reload: {countBeforeReload}");
        _output.WriteLine($"  Total messages: {messageCount}");
        _output.WriteLine($"  Reload successful: {reloadResult.Success}");
        _output.WriteLine($"  System remained operational: {messageCount > countBeforeReload}");

        // Cleanup
        await core.StopAsync();
        core.Dispose();
    }

    [Fact]
    public async Task DependentModule_UnloadShouldFail()
    {
        // Scenario: Try to unload GPS module while autosteer depends on it
        // Target: System prevents unsafe unload

        // Arrange
        var (core, monitor) = await SetupCoreWithMonitoring();

        var gpsModule = new StableModule("GPS IO");
        var dependentModule = new DependentModule("Autosteer", new[] { "GPS IO" });

        await core.LoadModuleAsync(gpsModule);
        await core.LoadModuleAsync(dependentModule);

        // Act
        var gpsModuleId = $"{gpsModule.Name}:{gpsModule.Version}".Replace(" ", "_");
        var unloadResult = await core.UnloadModuleAsync(gpsModuleId);

        // Assert
        Assert.False(unloadResult.Success, "Should not allow unloading module with dependents");
        Assert.NotNull(unloadResult.DependentPlugins);
        Assert.NotEmpty(unloadResult.DependentPlugins);

        _output.WriteLine($"Dependent Module Test:");
        _output.WriteLine($"  Unload prevented: {!unloadResult.Success}");
        _output.WriteLine($"  Error: {unloadResult.ErrorMessage}");
        _output.WriteLine($"  Dependents: {string.Join(", ", unloadResult.DependentPlugins ?? new List<string>())}");

        // Cleanup
        await core.StopAsync();
        core.Dispose();
    }

    [Fact]
    public async Task MessageBusException_ShouldNotCrashSystem()
    {
        // Scenario: Subscriber throws exception when processing message
        // Target: Other subscribers still receive message

        // Arrange
        var (core, monitor) = await SetupCoreWithMonitoring();
        var messageBus = GetMessageBus(core);

        var goodSubscriberCount = 0;
        var badSubscriberCalled = false;

        // Subscribe with handler that throws
        messageBus.Subscribe<GpsPositionMessage>(msg =>
        {
            badSubscriberCalled = true;
            throw new Exception("Subscriber crashed!");
        });

        // Subscribe with good handler
        messageBus.Subscribe<GpsPositionMessage>(msg =>
        {
            Interlocked.Increment(ref goodSubscriberCount);
        });

        // Act
        for (int i = 0; i < 10; i++)
        {
            messageBus.Publish(new GpsPositionMessage
            {
                Latitude = 45.5,
                Longitude = -122.6,
                Heading = 45.0,
                Speed = 2.0,
                FixQuality = GpsFixQuality.RTK_Fixed,
                TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });
        }

        await Task.Delay(100);

        // Assert
        Assert.True(badSubscriberCalled, "Bad subscriber should have been called");
        Assert.Equal(10, goodSubscriberCount);

        _output.WriteLine($"Message Bus Exception Test:");
        _output.WriteLine($"  Bad subscriber called: {badSubscriberCalled}");
        _output.WriteLine($"  Good subscriber received: {goodSubscriberCount}/10");
        _output.WriteLine($"  System isolated exceptions: {goodSubscriberCount == 10}");

        // Cleanup
        await core.StopAsync();
        core.Dispose();
    }

    // Helper methods and test modules
    private async Task<(ApplicationCore core, MonitoringModule monitor)> SetupCoreWithMonitoring()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Warning);
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

// Test modules for resilience testing
internal class CrashingModule : IAgModule
{
    public string Name { get; }
    public Version Version => new Version(1, 0, 0);
    public ModuleCategory Category => ModuleCategory.Logging;
    public string[] Dependencies => Array.Empty<string>();

    private bool _hasCrashed;

    public CrashingModule(string name) => Name = name;

    public Task InitializeAsync(IModuleContext context)
    {
        context.MessageBus.Subscribe<GpsPositionMessage>(msg =>
        {
            if (!_hasCrashed)
            {
                _hasCrashed = true;
                throw new Exception("Module crashed!");
            }
        });
        return Task.CompletedTask;
    }

    public Task StartAsync() => Task.CompletedTask;
    public Task StopAsync() => Task.CompletedTask;
    public Task ShutdownAsync() => Task.CompletedTask;
    public ModuleHealth GetHealth() => _hasCrashed ? ModuleHealth.Unhealthy : ModuleHealth.Healthy;
}

internal class SlowModule : IAgModule
{
    public string Name { get; }
    public Version Version => new Version(1, 0, 0);
    public ModuleCategory Category => ModuleCategory.Control;
    public string[] Dependencies => Array.Empty<string>();

    public SlowModule(string name) => Name = name;

    public Task InitializeAsync(IModuleContext context) => Task.CompletedTask;
    public Task StartAsync() => Task.CompletedTask;
    public Task StopAsync() => Task.CompletedTask;
    public Task ShutdownAsync() => Task.CompletedTask;
    public ModuleHealth GetHealth() => ModuleHealth.Healthy;
}

internal class FastModule : IAgModule
{
    public string Name { get; }
    public Version Version => new Version(1, 0, 0);
    public ModuleCategory Category => ModuleCategory.Control;
    public string[] Dependencies => Array.Empty<string>();

    public FastModule(string name) => Name = name;

    public Task InitializeAsync(IModuleContext context) => Task.CompletedTask;
    public Task StartAsync() => Task.CompletedTask;
    public Task StopAsync() => Task.CompletedTask;
    public Task ShutdownAsync() => Task.CompletedTask;
    public ModuleHealth GetHealth() => ModuleHealth.Healthy;
}

internal class StableModule : IAgModule
{
    public string Name { get; }
    public Version Version => new Version(1, 0, 0);
    public ModuleCategory Category => ModuleCategory.IO;
    public string[] Dependencies => Array.Empty<string>();

    public StableModule(string name) => Name = name;

    public Task InitializeAsync(IModuleContext context) => Task.CompletedTask;
    public Task StartAsync() => Task.CompletedTask;
    public Task StopAsync() => Task.CompletedTask;
    public Task ShutdownAsync() => Task.CompletedTask;
    public ModuleHealth GetHealth() => ModuleHealth.Healthy;
}

internal class FailingInitModule : IAgModule
{
    public string Name { get; }
    public Version Version => new Version(1, 0, 0);
    public ModuleCategory Category => ModuleCategory.IO;
    public string[] Dependencies => Array.Empty<string>();

    public FailingInitModule(string name) => Name = name;

    public Task InitializeAsync(IModuleContext context)
    {
        throw new Exception("Hardware not available - COM3 not found");
    }

    public Task StartAsync() => Task.CompletedTask;
    public Task StopAsync() => Task.CompletedTask;
    public Task ShutdownAsync() => Task.CompletedTask;
    public ModuleHealth GetHealth() => ModuleHealth.Unhealthy;
}

internal class DependentModule : IAgModule
{
    public string Name { get; }
    public Version Version => new Version(1, 0, 0);
    public ModuleCategory Category => ModuleCategory.Control;
    public string[] Dependencies { get; }

    public DependentModule(string name, string[] dependencies)
    {
        Name = name;
        Dependencies = dependencies;
    }

    public Task InitializeAsync(IModuleContext context) => Task.CompletedTask;
    public Task StartAsync() => Task.CompletedTask;
    public Task StopAsync() => Task.CompletedTask;
    public Task ShutdownAsync() => Task.CompletedTask;
    public ModuleHealth GetHealth() => ModuleHealth.Healthy;
}
