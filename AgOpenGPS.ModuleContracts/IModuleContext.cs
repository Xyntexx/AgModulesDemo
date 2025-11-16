namespace AgOpenGPS.ModuleContracts;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

/// <summary>
/// Services provided by the core to modules
/// </summary>
public interface IModuleContext
{
    /// <summary>High-performance message bus for module communication</summary>
    IMessageBus MessageBus { get; }

    /// <summary>Dependency injection container</summary>
    IServiceProvider Services { get; }

    /// <summary>Configuration access</summary>
    IConfiguration Configuration { get; }

    /// <summary>Logger for this module</summary>
    ILogger Logger { get; }

    /// <summary>Time provider for timestamps and delays (supports simulation)</summary>
    ITimeProvider TimeProvider { get; }

    /// <summary>Cancellation token signaling application shutdown</summary>
    CancellationToken AppShutdownToken { get; }

    /// <summary>
    /// Create a new message queue for deferred message processing.
    /// Tickable modules should call this during initialization and process the queue in Tick().
    /// </summary>
    IMessageQueue CreateMessageQueue();

    /// <summary>
    /// Get access to the scheduler for registering multiple scheduled methods.
    /// Returns null if scheduler is disabled.
    /// </summary>
    IScheduler? Scheduler { get; }
}
