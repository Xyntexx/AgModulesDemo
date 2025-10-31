namespace AgOpenGPS.Core;

using AgOpenGPS.PluginContracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

public class PluginContext : IPluginContext
{
    public IMessageBus MessageBus { get; }
    public IServiceProvider Services { get; }
    public IConfiguration Configuration { get; }
    public ILogger Logger { get; }
    public CancellationToken AppShutdownToken { get; }

    public PluginContext(
        IMessageBus messageBus,
        IServiceProvider services,
        IConfiguration configuration,
        ILogger logger,
        CancellationToken appShutdownToken)
    {
        MessageBus = messageBus;
        Services = services;
        Configuration = configuration;
        Logger = logger;
        AppShutdownToken = appShutdownToken;
    }
}
