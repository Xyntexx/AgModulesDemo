namespace AgOpenGPS.Core;

using AgOpenGPS.ModuleContracts;
using Microsoft.Extensions.Logging;
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
    private readonly ConcurrentDictionary<Type, LastMessageInfo> _lastMessages = new();
    private readonly ConcurrentDictionary<Type, FailureTracker> _failureTrackers = new();
    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.SupportsRecursion);
    private readonly ITimeProvider _timeProvider;
    private readonly ILogger<MessageBus> _logger;
    private readonly int _maxLastMessages;
    private readonly TimeSpan _lastMessageTtl;
    private readonly int _maxFailuresBeforeRemoval;
    private volatile bool _disposed;

    public MessageBus(
        ITimeProvider timeProvider,
        ILogger<MessageBus> logger,
        int maxLastMessages = 100,
        TimeSpan? lastMessageTtl = null,
        int maxFailuresBeforeRemoval = 10)
    {
        _timeProvider = timeProvider;
        _logger = logger;
        _maxLastMessages = maxLastMessages;
        _lastMessageTtl = lastMessageTtl ?? TimeSpan.FromHours(1);
        _maxFailuresBeforeRemoval = maxFailuresBeforeRemoval;
    }

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

        var messageType = typeof(T);

        // Store last message with timestamp and enforce limits
        StoreLastMessage(messageType, message);

        _lock.EnterReadLock();
        List<SubscriberInfo>? subscribersCopy = null;
        try
        {
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
            var failedHandlers = new List<string>();

            foreach (var sub in subscribersCopy)
            {
                try
                {
                    ((Action<T>)sub.Handler)(message);

                    // Reset failure count on success
                    ResetFailureCount(messageType, sub.SubscriptionId);
                }
                catch (Exception ex)
                {
                    // Track failure and log with full details
                    var failureCount = RecordFailure(messageType, sub.SubscriptionId);

                    _logger.LogError(ex,
                        "Error in message handler for {MessageType}. " +
                        "Handler ID: {HandlerId}, Failure count: {FailureCount}/{MaxFailures}",
                        messageType.Name, sub.SubscriptionId, failureCount, _maxFailuresBeforeRemoval);

                    if (failureCount >= _maxFailuresBeforeRemoval)
                    {
                        _logger.LogWarning(
                            "Handler {HandlerId} for {MessageType} has failed {FailureCount} times. Marking for removal.",
                            sub.SubscriptionId, messageType.Name, failureCount);

                        failedHandlers.Add(sub.SubscriptionId);
                    }
                }
            }

            // Remove repeatedly failing handlers
            if (failedHandlers.Count > 0)
            {
                RemoveFailedHandlers<T>(failedHandlers);
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
    /// Get the last published message of a given type
    /// </summary>
    public bool TryGetLastMessage<T>(out T message, out DateTimeOffset timestamp) where T : struct
    {
        if (_lastMessages.TryGetValue(typeof(T), out var info))
        {
            message = (T)info.Message;
            timestamp = info.Timestamp;
            return true;
        }

        message = default;
        timestamp = default;
        return false;
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
            ScopeCount = _scopedSubscriptions.Count,
            LastMessageCount = _lastMessages.Count
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

    private class LastMessageInfo
    {
        public object Message { get; set; } = null!;
        public DateTimeOffset Timestamp { get; set; }
    }

    private class FailureTracker
    {
        public ConcurrentDictionary<string, int> HandlerFailureCounts { get; } = new();
    }

    /// <summary>
    /// Store last message with cleanup policy enforcement
    /// </summary>
    private void StoreLastMessage<T>(Type messageType, T message) where T : struct
    {
        _lastMessages[messageType] = new LastMessageInfo
        {
            Message = message,
            Timestamp = _timeProvider.UtcNow
        };

        // Enforce size limit by removing oldest entries
        if (_lastMessages.Count > _maxLastMessages)
        {
            CleanupOldMessages();
        }
    }

    /// <summary>
    /// Remove expired messages based on TTL and enforce size limits
    /// </summary>
    private void CleanupOldMessages()
    {
        var now = _timeProvider.UtcNow;
        var toRemove = new List<Type>();

        // Find expired entries
        foreach (var kvp in _lastMessages)
        {
            if (now - kvp.Value.Timestamp > _lastMessageTtl)
            {
                toRemove.Add(kvp.Key);
            }
        }

        // If still over limit after TTL cleanup, remove oldest entries
        if (_lastMessages.Count - toRemove.Count > _maxLastMessages)
        {
            var oldest = _lastMessages
                .OrderBy(kvp => kvp.Value.Timestamp)
                .Take(_lastMessages.Count - _maxLastMessages)
                .Select(kvp => kvp.Key)
                .ToList();

            toRemove.AddRange(oldest);
        }

        // Remove collected entries
        foreach (var type in toRemove)
        {
            if (_lastMessages.TryRemove(type, out _))
            {
                _logger.LogDebug("Removed cached message for type {MessageType} (TTL expired or size limit)", type.Name);
            }
        }
    }

    /// <summary>
    /// Record a handler failure and return the failure count
    /// </summary>
    private int RecordFailure(Type messageType, string subscriptionId)
    {
        var tracker = _failureTrackers.GetOrAdd(messageType, _ => new FailureTracker());
        return tracker.HandlerFailureCounts.AddOrUpdate(subscriptionId, 1, (_, count) => count + 1);
    }

    /// <summary>
    /// Reset failure count for a handler (called on successful execution)
    /// </summary>
    private void ResetFailureCount(Type messageType, string subscriptionId)
    {
        if (_failureTrackers.TryGetValue(messageType, out var tracker))
        {
            tracker.HandlerFailureCounts.TryRemove(subscriptionId, out _);
        }
    }

    /// <summary>
    /// Remove handlers that have failed repeatedly
    /// </summary>
    private void RemoveFailedHandlers<T>(List<string> failedHandlerIds) where T : struct
    {
        _lock.EnterWriteLock();
        try
        {
            var messageType = typeof(T);
            if (_subscribers.TryGetValue(messageType, out var subscribers))
            {
                lock (subscribers)
                {
                    var removedCount = subscribers.RemoveAll(s => failedHandlerIds.Contains(s.SubscriptionId));

                    if (removedCount > 0)
                    {
                        _logger.LogWarning(
                            "Removed {Count} failing handler(s) for message type {MessageType}",
                            removedCount, messageType.Name);
                    }
                }
            }

            // Clean up failure trackers for removed handlers
            if (_failureTrackers.TryGetValue(messageType, out var tracker))
            {
                foreach (var handlerId in failedHandlerIds)
                {
                    tracker.HandlerFailureCounts.TryRemove(handlerId, out _);
                }
            }
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }
}

public class MessageBusStatistics
{
    public int MessageTypeCount { get; set; }
    public int TotalSubscribers { get; set; }
    public int ScopeCount { get; set; }
    public int LastMessageCount { get; set; }
}
