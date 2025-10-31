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

    /// <summary>Cancellation token signaling application shutdown</summary>
    CancellationToken AppShutdownToken { get; }
}
