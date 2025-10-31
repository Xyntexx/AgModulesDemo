/*
 * HOT RELOAD EXAMPLE
 *
 * This example demonstrates the enhanced microkernel's capability to load,
 * unload, and reload plugins at runtime without stopping the entire application.
 *
 * Key Features Demonstrated:
 * 1. Runtime plugin loading
 * 2. Runtime plugin unloading with dependency checking
 * 3. Plugin hot reload
 * 4. Health monitoring
 * 5. Thread-safe operations
 * 6. Automatic subscription cleanup
 */

using AgOpenGPS.Core;
using AgOpenGPS.PluginContracts;
using AgOpenGPS.PluginContracts.Messages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AgOpenGPS.Examples;

public class HotReloadExample
{
    public static async Task RunAsync()
    {
        // Setup DI and configuration
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Core:PluginDirectory"] = "./plugins"
            })
            .Build();

        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<MessageBus>();
        services.AddSingleton<ApplicationCore>();

        var provider = services.BuildServiceProvider();
        var core = provider.GetRequiredService<ApplicationCore>();
        var logger = provider.GetRequiredService<ILogger<HotReloadExample>>();

        try
        {
            // Start the application with initial plugins
            logger.LogInformation("=== Starting Application with Initial Plugins ===");
            await core.StartAsync();

            // Display loaded plugins
            DisplayPluginStatus(core, logger);

            // Simulate application running
            logger.LogInformation("\n=== Application Running - Press Enter to Continue ===");
            Console.ReadLine();

            // DEMO 1: Load a new plugin at runtime
            logger.LogInformation("\n=== DEMO 1: Loading New Plugin at Runtime ===");
            var newPlugin = new DemoRuntimePlugin();
            var loadResult = await core.LoadPluginAsync(newPlugin);

            if (loadResult.Success)
            {
                logger.LogInformation($"Successfully loaded plugin: {loadResult.PluginId}");
            }
            else
            {
                logger.LogError($"Failed to load plugin: {loadResult.ErrorMessage}");
            }

            DisplayPluginStatus(core, logger);
            Console.ReadLine();

            // DEMO 2: Health Check
            logger.LogInformation("\n=== DEMO 2: Performing Health Check ===");
            var healthResult = await core.PerformHealthCheckAsync();

            foreach (var pluginHealth in healthResult.PluginHealths)
            {
                logger.LogInformation(
                    $"Plugin: {pluginHealth.PluginName} | Health: {pluginHealth.Health} | State: {pluginHealth.State}");
            }
            Console.ReadLine();

            // DEMO 3: Unload a plugin
            logger.LogInformation("\n=== DEMO 3: Unloading Plugin at Runtime ===");
            var pluginToUnload = core.GetLoadedPlugins().FirstOrDefault();

            if (pluginToUnload != null)
            {
                logger.LogInformation($"Attempting to unload: {pluginToUnload.Name}");
                var unloadResult = await core.UnloadPluginAsync(pluginToUnload.PluginId);

                if (unloadResult.Success)
                {
                    logger.LogInformation($"Successfully unloaded plugin: {pluginToUnload.Name}");
                }
                else
                {
                    logger.LogError($"Failed to unload: {unloadResult.ErrorMessage}");
                    if (unloadResult.DependentPlugins != null)
                    {
                        logger.LogError($"Dependent plugins: {string.Join(", ", unloadResult.DependentPlugins)}");
                    }
                }
            }

            DisplayPluginStatus(core, logger);
            Console.ReadLine();

            // DEMO 4: Reload a plugin
            logger.LogInformation("\n=== DEMO 4: Reloading Plugin (Hot Reload) ===");
            var pluginToReload = core.GetLoadedPlugins().FirstOrDefault();

            if (pluginToReload != null)
            {
                logger.LogInformation($"Reloading plugin: {pluginToReload.Name}");
                var reloadResult = await core.ReloadPluginAsync(pluginToReload.PluginId);

                if (reloadResult.Success)
                {
                    logger.LogInformation($"Successfully reloaded plugin: {pluginToReload.Name}");
                }
                else
                {
                    logger.LogError($"Failed to reload: {reloadResult.ErrorMessage}");
                }
            }

            DisplayPluginStatus(core, logger);
            Console.ReadLine();

            // DEMO 5: Message Bus Isolation Test
            logger.LogInformation("\n=== DEMO 5: Testing Message Bus Subscription Cleanup ===");
            logger.LogInformation("Creating temporary plugin with subscriptions...");

            var tempPlugin = new TemporaryMonitorPlugin();
            await core.LoadPluginAsync(tempPlugin);

            logger.LogInformation("Sending test messages...");
            var messageBus = provider.GetRequiredService<MessageBus>();
            messageBus.Publish(new GpsPositionMessage
            {
                Latitude = 45.5,
                Longitude = -122.6,
                TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });

            await Task.Delay(100);

            logger.LogInformation("Unloading temporary plugin...");
            await core.UnloadPluginAsync($"{tempPlugin.Name}:{tempPlugin.Version}".Replace(" ", "_"));

            logger.LogInformation("Sending messages after unload (should not be received by unloaded plugin)...");
            messageBus.Publish(new GpsPositionMessage
            {
                Latitude = 45.6,
                Longitude = -122.7,
                TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });

            await Task.Delay(100);

            // Check message bus stats
            var stats = messageBus.GetStatistics();
            logger.LogInformation($"Message Bus Stats - Types: {stats.MessageTypeCount}, Subscribers: {stats.TotalSubscribers}, Scopes: {stats.ScopeCount}");

            Console.ReadLine();

            // Shutdown
            logger.LogInformation("\n=== Shutting Down Application ===");
            await core.StopAsync();
            core.Dispose();

            logger.LogInformation("Application stopped successfully");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Application error");
        }
    }

    private static void DisplayPluginStatus(ApplicationCore core, ILogger logger)
    {
        logger.LogInformation("\n--- Current Plugin Status ---");
        var plugins = core.GetLoadedPlugins();

        if (!plugins.Any())
        {
            logger.LogInformation("No plugins loaded");
            return;
        }

        foreach (var plugin in plugins)
        {
            logger.LogInformation(
                $"  [{plugin.Category}] {plugin.Name} v{plugin.Version} | State: {plugin.State} | Health: {plugin.Health}");
        }

        logger.LogInformation($"Total: {plugins.Count} plugins\n");
    }
}

// Demo plugin that can be loaded at runtime
public class DemoRuntimePlugin : IAgPlugin
{
    public string Name => "Demo Runtime Plugin";
    public Version Version => new(1, 0, 0);
    public PluginCategory Category => PluginCategory.DataProcessing;
    public string[] Dependencies => Array.Empty<string>();

    private IPluginContext? _context;

    public Task InitializeAsync(IPluginContext context)
    {
        _context = context;
        context.Logger.LogInformation("Demo Runtime Plugin initialized");
        return Task.CompletedTask;
    }

    public Task StartAsync()
    {
        _context?.Logger.LogInformation("Demo Runtime Plugin started");
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        _context?.Logger.LogInformation("Demo Runtime Plugin stopped");
        return Task.CompletedTask;
    }

    public Task ShutdownAsync()
    {
        _context?.Logger.LogInformation("Demo Runtime Plugin shutdown");
        return Task.CompletedTask;
    }

    public PluginHealth GetHealth()
    {
        return PluginHealth.Healthy;
    }
}

// Temporary plugin to test subscription cleanup
public class TemporaryMonitorPlugin : IAgPlugin
{
    public string Name => "Temporary Monitor";
    public Version Version => new(1, 0, 0);
    public PluginCategory Category => PluginCategory.Logging;
    public string[] Dependencies => Array.Empty<string>();

    private IPluginContext? _context;

    public Task InitializeAsync(IPluginContext context)
    {
        _context = context;

        // Subscribe to GPS messages
        context.MessageBus.Subscribe<GpsPositionMessage>(msg =>
        {
            context.Logger.LogInformation(
                $"[TEMP MONITOR] GPS: Lat={msg.Latitude:F6}, Lon={msg.Longitude:F6}");
        });

        context.Logger.LogInformation("Temporary Monitor initialized with subscriptions");
        return Task.CompletedTask;
    }

    public Task StartAsync()
    {
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        return Task.CompletedTask;
    }

    public Task ShutdownAsync()
    {
        _context?.Logger.LogInformation("Temporary Monitor cleaned up");
        return Task.CompletedTask;
    }

    public PluginHealth GetHealth()
    {
        return PluginHealth.Healthy;
    }
}
