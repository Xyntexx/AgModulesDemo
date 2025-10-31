namespace AgOpenGPS.PluginContracts;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

/// <summary>
/// Services provided by the core to plugins
/// </summary>
public interface IPluginContext
{
    /// <summary>High-performance message bus for plugin communication</summary>
    IMessageBus MessageBus { get; }

    /// <summary>Dependency injection container</summary>
    IServiceProvider Services { get; }

    /// <summary>Configuration access</summary>
    IConfiguration Configuration { get; }

    /// <summary>Logger for this plugin</summary>
    ILogger Logger { get; }

    /// <summary>Cancellation token signaling application shutdown</summary>
    CancellationToken AppShutdownToken { get; }
}
