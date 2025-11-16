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
    public ITimeProvider TimeProvider { get; }
    public CancellationToken AppShutdownToken { get; }
    public IScheduler? Scheduler { get; }

    public ModuleContext(
        IMessageBus messageBus,
        IServiceProvider services,
        IConfiguration configuration,
        ILogger logger,
        ITimeProvider timeProvider,
        CancellationToken appShutdownToken,
        IScheduler? scheduler = null)
    {
        MessageBus = messageBus;
        Services = services;
        Configuration = configuration;
        Logger = logger;
        TimeProvider = timeProvider;
        AppShutdownToken = appShutdownToken;
        Scheduler = scheduler;
    }

    public IMessageQueue CreateMessageQueue()
    {
        return new MessageQueue();
    }
}
