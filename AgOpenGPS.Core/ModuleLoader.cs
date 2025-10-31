namespace AgOpenGPS.Core;

using System.Reflection;
using AgOpenGPS.ModuleContracts;
using Microsoft.Extensions.Logging;

/// <summary>
/// Discovers and loads modules from disk
/// </summary>
public class ModuleLoader
{
    private readonly ILogger<ModuleLoader> _logger;
    private readonly string _moduleDirectory;

    public ModuleLoader(string moduleDirectory, ILogger<ModuleLoader> logger)
    {
        _moduleDirectory = moduleDirectory;
        _logger = logger;
    }

    public List<IAgModule> DiscoverModules()
    {
        var modules = new List<IAgModule>();

        if (!Directory.Exists(_moduleDirectory))
        {
            _logger.LogWarning($"Module directory not found: {_moduleDirectory}");
            return modules;
        }

        foreach (var dllPath in Directory.GetFiles(_moduleDirectory, "*.dll"))
        {
            try
            {
                var assembly = Assembly.LoadFrom(dllPath);

                var moduleTypes = assembly.GetTypes()
                    .Where(t => typeof(IAgModule).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

                foreach (var type in moduleTypes)
                {
                    var module = (IAgModule)Activator.CreateInstance(type)!;
                    modules.Add(module);
                    _logger.LogInformation($"Discovered module: {module.Name} v{module.Version}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to load module from {dllPath}: {ex.Message}");
            }
        }

        return modules;
    }

    /// <summary>
    /// Resolve module load order based on dependencies and categories
    /// </summary>
    public List<IAgModule> ResolveLoadOrder(List<IAgModule> plugins)
    {
        // Create a logger for the resolver using the factory
        var resolverLogger = Microsoft.Extensions.Logging.LoggerFactoryExtensions.CreateLogger<ModuleDependencyResolver>(
            Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);

        var resolver = new ModuleDependencyResolver(resolverLogger);
        return resolver.ResolveLoadOrder(plugins);
    }
}
