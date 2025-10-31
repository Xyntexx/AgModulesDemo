using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using AgOpenGPS.Core;
using AgOpenGPS.PluginContracts;
using System;
using System.IO;
using System.Threading;

namespace AgOpenGPS.GUI;

public partial class App : Application
{
    private IHost? _host;
    private ApplicationCore? _core;
    private CancellationTokenSource? _cts;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _cts = new CancellationTokenSource();

            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            _host = Host.CreateDefaultBuilder()
                .ConfigureServices((context, services) =>
                {
                    services.AddSingleton<IConfiguration>(configuration);
                    services.AddSingleton<IMessageBus, MessageBus>();
                    services.AddSingleton<ApplicationCore>();
                    services.AddLogging(builder =>
                    {
                        builder.AddConsole();
                        builder.SetMinimumLevel(LogLevel.Information);
                    });
                })
                .Build();

            _core = _host.Services.GetRequiredService<ApplicationCore>();
            var messageBus = _host.Services.GetRequiredService<IMessageBus>();

            _core.StartAsync();

            var mainWindow = new MainWindow();
            mainWindow.ViewModel.Initialize(_core, messageBus);

            desktop.MainWindow = mainWindow;
            desktop.ShutdownRequested += OnShutdownRequested;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        _cts?.Cancel();
        _core?.StopAsync().Wait();
        _host?.Dispose();
    }
}