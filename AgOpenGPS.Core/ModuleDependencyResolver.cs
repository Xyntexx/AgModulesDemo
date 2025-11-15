namespace AgOpenGPS.Core;

using AgOpenGPS.ModuleContracts;
using Microsoft.Extensions.Logging;

/// <summary>
/// Resolves module dependencies using topological sort
/// </summary>
public class ModuleDependencyResolver
{
    private readonly ILogger<ModuleDependencyResolver> _logger;

    public ModuleDependencyResolver(ILogger<ModuleDependencyResolver> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Resolve module load order based on dependencies and categories
    /// Throws if circular dependencies detected
    /// </summary>
    public List<IAgModule> ResolveLoadOrder(List<IAgModule> modules)
    {
        if (!modules.Any())
        {
            return new List<IAgModule>();
        }

        // Build dependency graph
        var graph = BuildDependencyGraph(modules);

        // Detect circular dependencies
        var cycles = DetectCycles(graph);
        if (cycles.Any())
        {
            var cycleStr = string.Join(", ", cycles.Select(c => string.Join(" -> ", c)));
            throw new InvalidOperationException($"Circular dependencies detected: {cycleStr}");
        }

        // Topological sort
        var sorted = TopologicalSort(graph);

        // Secondary sort by category within dependency order
        var result = sorted
            .OrderBy(p => GetDependencyLevel(p, graph))
            .ThenBy(p => (int)p.Category)
            .ToList();

        _logger.LogInformation($"Resolved module load order: {string.Join(" -> ", result.Select(p => p.Name))}");

        return result;
    }

    private Dictionary<string, DependencyNode> BuildDependencyGraph(List<IAgModule> modules)
    {
        var graph = new Dictionary<string, DependencyNode>(StringComparer.OrdinalIgnoreCase);

        // Create nodes
        foreach (var module in modules)
        {
            graph[module.Name] = new DependencyNode
            {
                Module = module,
                Dependencies = new List<string>(module.Dependencies)
            };
        }

        // Validate all dependencies exist
        foreach (var node in graph.Values)
        {
            foreach (var dep in node.Dependencies)
            {
                if (!graph.ContainsKey(dep))
                {
                    throw new InvalidOperationException(
                        $"Module '{node.Module.Name}' depends on '{dep}', but '{dep}' is not available");
                }
            }
        }

        return graph;
    }

    private List<List<string>> DetectCycles(Dictionary<string, DependencyNode> graph)
    {
        var visited = new HashSet<string>();
        var recursionStack = new HashSet<string>();
        var cycles = new List<List<string>>();

        foreach (var node in graph.Keys)
        {
            if (!visited.Contains(node))
            {
                DetectCyclesDFS(node, graph, visited, recursionStack, new List<string>(), cycles);
            }
        }

        return cycles;
    }

    private void DetectCyclesDFS(
        string node,
        Dictionary<string, DependencyNode> graph,
        HashSet<string> visited,
        HashSet<string> recursionStack,
        List<string> path,
        List<List<string>> cycles)
    {
        visited.Add(node);
        recursionStack.Add(node);
        path.Add(node);

        foreach (var dep in graph[node].Dependencies)
        {
            if (!visited.Contains(dep))
            {
                DetectCyclesDFS(dep, graph, visited, recursionStack, path, cycles);
            }
            else if (recursionStack.Contains(dep))
            {
                // Found cycle
                var cycleStart = path.IndexOf(dep);
                var cycle = path.Skip(cycleStart).Append(dep).ToList();
                cycles.Add(cycle);
            }
        }

        path.RemoveAt(path.Count - 1);
        recursionStack.Remove(node);
    }

    private List<IAgModule> TopologicalSort(Dictionary<string, DependencyNode> graph)
    {
        var sorted = new List<IAgModule>();
        var visited = new HashSet<string>();

        foreach (var node in graph.Keys)
        {
            if (!visited.Contains(node))
            {
                TopologicalSortDFS(node, graph, visited, sorted);
            }
        }

        return sorted;
    }

    private void TopologicalSortDFS(
        string node,
        Dictionary<string, DependencyNode> graph,
        HashSet<string> visited,
        List<IAgModule> sorted)
    {
        visited.Add(node);

        // Visit dependencies first
        foreach (var dep in graph[node].Dependencies)
        {
            if (!visited.Contains(dep))
            {
                TopologicalSortDFS(dep, graph, visited, sorted);
            }
        }

        // Add to sorted list after all dependencies
        sorted.Add(graph[node].Module);
    }

    private int GetDependencyLevel(IAgModule module, Dictionary<string, DependencyNode> graph)
    {
        if (!graph.TryGetValue(module.Name, out var node))
        {
            return 0;
        }

        if (node.Dependencies.Count == 0)
        {
            return 0;
        }

        var maxDepLevel = 0;
        foreach (var dep in node.Dependencies)
        {
            if (graph.TryGetValue(dep, out var depNode))
            {
                maxDepLevel = Math.Max(maxDepLevel, GetDependencyLevel(depNode.Module, graph) + 1);
            }
        }

        return maxDepLevel;
    }

    private class DependencyNode
    {
        public required IAgModule Module { get; set; }
        public required List<string> Dependencies { get; set; }
    }
}
