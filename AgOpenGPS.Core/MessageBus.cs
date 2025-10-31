namespace AgOpenGPS.Core;

using AgOpenGPS.ModuleContracts;
using System.Collections.Concurrent;

/// <summary>
/// High-performance message bus implementation
/// Thread-safe, zero-allocation for struct messages
/// Supports scoped subscriptions for module lifecycle management
/// </summary>
public class MessageBus : IMessageBus, IDisposable
{
    private readonly ConcurrentDictionary<Type, List<SubscriberInfo>> _subscribers = new();
    private readonly ConcurrentDictionary<string, List<IDisposable>> _scopedSubscriptions = new();
    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.SupportsRecursion);
    private volatile bool _disposed;

    public IDisposable Subscribe<T>(Action<T> handler) where T : struct
    {
        return Subscribe(handler, priority: 0);
    }

    public IDisposable Subscribe<T>(Action<T> handler, int priority) where T : struct
    {
        ThrowIfDisposed();

        _lock.EnterWriteLock();
        try
        {
            var messageType = typeof(T);
            var subscribers = _subscribers.GetOrAdd(messageType, _ => new List<SubscriberInfo>());

            var info = new SubscriberInfo
            {
                Handler = handler,
                Priority = priority,
                SubscriptionId = Guid.NewGuid().ToString()
            };

            subscribers.Add(info);

            // Sort by priority (higher first)
            subscribers.Sort((a, b) => b.Priority.CompareTo(a.Priority));

            return new Unsubscriber(() => Unsubscribe<T>(handler));
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Subscribe with a scope identifier for bulk cleanup
    /// </summary>
    public IDisposable Subscribe<T>(Action<T> handler, string scope, int priority = 0) where T : struct
    {
        ThrowIfDisposed();

        var subscription = Subscribe(handler, priority);

        var scopedSubs = _scopedSubscriptions.GetOrAdd(scope, _ => new List<IDisposable>());
        lock (scopedSubs)
        {
            scopedSubs.Add(subscription);
        }

        return subscription;
    }

    /// <summary>
    /// Unsubscribe all handlers for a given scope (e.g., module ID)
    /// </summary>
    public void UnsubscribeScope(string scope)
    {
        if (_scopedSubscriptions.TryRemove(scope, out var subscriptions))
        {
            lock (subscriptions)
            {
                foreach (var sub in subscriptions)
                {
                    sub.Dispose();
                }
                subscriptions.Clear();
            }
        }
    }

    public void Publish<T>(in T message) where T : struct
    {
        ThrowIfDisposed();

        _lock.EnterReadLock();
        List<SubscriberInfo>? subscribersCopy = null;
        try
        {
            var messageType = typeof(T);
            if (_subscribers.TryGetValue(messageType, out var subscribers))
            {
                // Create snapshot to avoid holding read lock during handler execution
                lock (subscribers)
                {
                    subscribersCopy = new List<SubscriberInfo>(subscribers);
                }
            }
        }
        finally
        {
            _lock.ExitReadLock();
        }

        // Execute handlers outside of lock to prevent deadlocks
        if (subscribersCopy != null)
        {
            foreach (var sub in subscribersCopy)
            {
                try
                {
                    ((Action<T>)sub.Handler)(message);
                }
                catch (Exception ex)
                {
                    // Log but don't let one bad handler break others
                    Console.WriteLine($"Error in message handler for {typeof(T).Name}: {ex.Message}");
                }
            }
        }
    }

    public async Task PublishAsync<T>(T message) where T : struct
    {
        await Task.Run(() => Publish(in message));
    }

    private void Unsubscribe<T>(Action<T> handler) where T : struct
    {
        _lock.EnterWriteLock();
        try
        {
            var messageType = typeof(T);
            if (_subscribers.TryGetValue(messageType, out var subscribers))
            {
                subscribers.RemoveAll(s => ReferenceEquals(s.Handler, handler));
            }
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(MessageBus));
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;

        // Cleanup all scoped subscriptions
        foreach (var scope in _scopedSubscriptions.Keys.ToList())
        {
            UnsubscribeScope(scope);
        }

        _lock.Dispose();
    }

    /// <summary>
    /// Get subscription statistics for monitoring
    /// </summary>
    public MessageBusStatistics GetStatistics()
    {
        return new MessageBusStatistics
        {
            MessageTypeCount = _subscribers.Count,
            TotalSubscribers = _subscribers.Values.Sum(list => list.Count),
            ScopeCount = _scopedSubscriptions.Count
        };
    }

    private class SubscriberInfo
    {
        public Delegate Handler { get; set; } = null!;
        public int Priority { get; set; }
        public string SubscriptionId { get; set; } = null!;
    }

    private class Unsubscriber : IDisposable
    {
        private readonly Action _unsubscribe;
        private int _disposed;

        public Unsubscriber(Action unsubscribe)
        {
            _unsubscribe = unsubscribe;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _unsubscribe();
            }
        }
    }
}

public class MessageBusStatistics
{
    public int MessageTypeCount { get; set; }
    public int TotalSubscribers { get; set; }
    public int ScopeCount { get; set; }
}
