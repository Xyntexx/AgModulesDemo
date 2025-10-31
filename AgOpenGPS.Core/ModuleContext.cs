namespace AgOpenGPS.Core;

using AgOpenGPS.ModuleContracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

public class ModuleContext : IModuleContext
{
    public IMessageBus MessageBus { get; }
    public IServiceProvider Services { get; }
    public IConfiguration Configuration { get; }
    public ILogger Logger { get; }
    public CancellationToken AppShutdownToken { get; }

    public ModuleContext(
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
