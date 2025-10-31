namespace AgOpenGPS.Core;

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

/// <summary>
/// Dedicated task scheduler that ensures each module runs on its own thread pool
/// preventing blocking operations in one module from affecting others
/// </summary>
public class ModuleTaskScheduler
{
    private readonly ConcurrentDictionary<string, ModuleThreadPool> _moduleThreadPools = new();
    private readonly ILogger<ModuleTaskScheduler> _logger;

    public ModuleTaskScheduler(ILogger<ModuleTaskScheduler> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Execute a module operation on its dedicated thread pool
    /// </summary>
    public async Task<T> ExecuteOnModuleThreadAsync<T>(
        string moduleId,
        Func<Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        var threadPool = _moduleThreadPools.GetOrAdd(moduleId, id => new ModuleThreadPool(id, _logger));

        var tcs = new TaskCompletionSource<T>();

        // Queue work on plugin's dedicated thread pool
        threadPool.QueueWork(async () =>
        {
            try
            {
                var result = await operation();
                tcs.TrySetResult(result);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });

        // Wait with cancellation support
        using (cancellationToken.Register(() => tcs.TrySetCanceled()))
        {
            return await tcs.Task;
        }
    }

    /// <summary>
    /// Execute a module operation on its dedicated thread pool (void return)
    /// </summary>
    public async Task ExecuteOnModuleThreadAsync(
        string moduleId,
        Func<Task> operation,
        CancellationToken cancellationToken = default)
    {
        await ExecuteOnModuleThreadAsync<object>(moduleId, async () =>
        {
            await operation();
            return null!;
        }, cancellationToken);
    }

    /// <summary>
    /// Execute a synchronous module operation on its dedicated thread
    /// </summary>
    public async Task<T> ExecuteOnModuleThread<T>(
        string moduleId,
        Func<T> operation,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteOnModuleThreadAsync(moduleId, () => Task.FromResult(operation()), cancellationToken);
    }

    /// <summary>
    /// Cleanup thread pool for a plugin
    /// </summary>
    public void CleanupModule(string moduleId)
    {
        if (_moduleThreadPools.TryRemove(moduleId, out var threadPool))
        {
            threadPool.Dispose();
            _logger.LogDebug($"Cleaned up thread pool for module {moduleId}");
        }
    }

    /// <summary>
    /// Get statistics for monitoring
    /// </summary>
    public Dictionary<string, ModuleThreadPoolStats> GetStatistics()
    {
        return _moduleThreadPools.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.GetStats());
    }
}

/// <summary>
/// Dedicated thread pool for a single plugin
/// </summary>
internal class ModuleThreadPool : IDisposable
{
    private readonly string _moduleId;
    private readonly ILogger _logger;
    private readonly BlockingCollection<Func<Task>> _workQueue;
    private readonly List<Thread> _threads;
    private readonly CancellationTokenSource _cts;
    private readonly int _threadCount;
    private long _completedTasks;
    private long _failedTasks;
    private volatile bool _disposed;

    public ModuleThreadPool(string moduleId, ILogger logger, int threadCount = 2)
    {
        _moduleId = moduleId;
        _logger = logger;
        _threadCount = threadCount;
        _workQueue = new BlockingCollection<Func<Task>>(new ConcurrentQueue<Func<Task>>());
        _threads = new List<Thread>();
        _cts = new CancellationTokenSource();

        // Start dedicated threads for this plugin
        for (int i = 0; i < threadCount; i++)
        {
            var thread = new Thread(WorkerThread)
            {
                Name = $"Plugin-{_moduleId}-Worker-{i}",
                IsBackground = true,
                Priority = ThreadPriority.Normal
            };
            thread.Start();
            _threads.Add(thread);
        }

        _logger.LogDebug($"Created thread pool with {threadCount} threads for module {_moduleId}");
    }

    public void QueueWork(Func<Task> work)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ModuleThreadPool));
        }

        _workQueue.Add(work);
    }

    private void WorkerThread()
    {
        _logger.LogTrace($"Worker thread started for module {_moduleId}");

        try
        {
            foreach (var work in _workQueue.GetConsumingEnumerable(_cts.Token))
            {
                try
                {
                    // Execute the work on this dedicated thread
                    work().GetAwaiter().GetResult();
                    Interlocked.Increment(ref _completedTasks);
                }
                catch (OperationCanceledException)
                {
                    // Expected during shutdown
                    break;
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref _failedTasks);
                    _logger.LogError(ex, $"Error executing task for module {_moduleId}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }

        _logger.LogTrace($"Worker thread stopped for module {_moduleId}");
    }

    public ModuleThreadPoolStats GetStats()
    {
        return new ModuleThreadPoolStats
        {
            ModuleId = _moduleId,
            ThreadCount = _threadCount,
            QueuedTasks = _workQueue.Count,
            CompletedTasks = Interlocked.Read(ref _completedTasks),
            FailedTasks = Interlocked.Read(ref _failedTasks),
            IsHealthy = !_disposed && _threads.All(t => t.IsAlive)
        };
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        _cts.Cancel();
        _workQueue.CompleteAdding();

        // Give threads a moment to finish current work
        foreach (var thread in _threads)
        {
            thread.Join(TimeSpan.FromSeconds(2));
        }

        _workQueue.Dispose();
        _cts.Dispose();
    }
}

public class ModuleThreadPoolStats
{
    public required string ModuleId { get; set; }
    public int ThreadCount { get; set; }
    public int QueuedTasks { get; set; }
    public long CompletedTasks { get; set; }
    public long FailedTasks { get; set; }
    public bool IsHealthy { get; set; }
}
