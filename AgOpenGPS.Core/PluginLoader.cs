namespace AgOpenGPS.Core;

using System.Reflection;
using AgOpenGPS.PluginContracts;
using Microsoft.Extensions.Logging;

/// <summary>
/// Discovers and loads plugins from disk
/// </summary>
public class PluginLoader
{
    private readonly ILogger<PluginLoader> _logger;
    private readonly string _pluginDirectory;

    public PluginLoader(string pluginDirectory, ILogger<PluginLoader> logger)
    {
        _pluginDirectory = pluginDirectory;
        _logger = logger;
    }

    public List<IAgPlugin> DiscoverPlugins()
    {
        var plugins = new List<IAgPlugin>();

        if (!Directory.Exists(_pluginDirectory))
        {
            _logger.LogWarning($"Plugin directory not found: {_pluginDirectory}");
            return plugins;
        }

        foreach (var dllPath in Directory.GetFiles(_pluginDirectory, "*.dll"))
        {
            try
            {
                var assembly = Assembly.LoadFrom(dllPath);

                var pluginTypes = assembly.GetTypes()
                    .Where(t => typeof(IAgPlugin).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

                foreach (var type in pluginTypes)
                {
                    var plugin = (IAgPlugin)Activator.CreateInstance(type)!;
                    plugins.Add(plugin);
                    _logger.LogInformation($"Discovered plugin: {plugin.Name} v{plugin.Version}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to load plugin from {dllPath}: {ex.Message}");
            }
        }

        return plugins;
    }

    /// <summary>
    /// Resolve plugin load order based on dependencies and categories
    /// </summary>
    public List<IAgPlugin> ResolveLoadOrder(List<IAgPlugin> plugins)
    {
        // Create a logger for the resolver using the factory
        var resolverLogger = Microsoft.Extensions.Logging.LoggerFactoryExtensions.CreateLogger<PluginDependencyResolver>(
            Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);

        var resolver = new PluginDependencyResolver(resolverLogger);
        return resolver.ResolveLoadOrder(plugins);
    }
}
