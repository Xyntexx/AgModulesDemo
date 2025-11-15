using AgOpenGPS.Core;
using AgOpenGPS.ModuleContracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration((context, config) =>
    {
        config.AddJsonFile("appsettings.json", optional: false);
    })
    .ConfigureServices((context, services) =>
    {
        // Register core services
        // TimeProvider - use SystemTimeProvider for real-time operation
        services.AddSingleton<ITimeProvider, SystemTimeProvider>();

        // MessageBus must be registered as both concrete type and interface
        services.AddSingleton<MessageBus>();
        services.AddSingleton<IMessageBus>(sp => sp.GetRequiredService<MessageBus>());
        services.AddSingleton<ApplicationCore>();
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Debug);
        });
    })
    .Build();

// Get the application core
var core = host.Services.GetRequiredService<ApplicationCore>();

// Setup graceful shutdown
var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
lifetime.ApplicationStopping.Register(() =>
{
    Console.WriteLine("\nShutdown requested...");
    core.StopAsync().GetAwaiter().GetResult();
    core.Dispose();
});

// Start the application
await core.StartAsync();

Console.WriteLine("\nAgOpenGPS running. Press Ctrl+C to exit.\n");

// Wait for shutdown signal
await host.WaitForShutdownAsync();
