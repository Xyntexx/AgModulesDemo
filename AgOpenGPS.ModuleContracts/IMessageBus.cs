namespace AgOpenGPS.ModuleContracts;

/// <summary>
/// High-performance in-process message bus for real-time communication
/// Optimized for zero-allocation with struct messages
/// </summary>
public interface IMessageBus
{
    /// <summary>
    /// Subscribe to messages of type T (immediate/synchronous execution)
    /// Handler runs on publisher's thread - use for fast, stateless operations
    /// Returns IDisposable to unsubscribe
    /// </summary>
    IDisposable Subscribe<T>(Action<T> handler) where T : struct;

    /// <summary>
    /// Subscribe with priority (higher = called first)
    /// Use for time-critical handlers
    /// </summary>
    IDisposable Subscribe<T>(Action<T> handler, int priority) where T : struct;

    /// <summary>
    /// Subscribe to messages with deferred/queued execution
    /// Messages are queued and processed during module's Tick() call
    /// Use for stateful operations that should run in module's thread context
    /// </summary>
    IDisposable SubscribeQueued<T>(Action<T> handler, IMessageQueue queue) where T : struct;

    /// <summary>
    /// Publish message to all subscribers synchronously
    /// Uses 'in' parameter to avoid copying struct
    /// </summary>
    void Publish<T>(in T message) where T : struct;

    /// <summary>
    /// Async publish for non-time-critical messages
    /// </summary>
    Task PublishAsync<T>(T message) where T : struct;

    /// <summary>
    /// Get the last published message of a given type with timestamp
    /// Returns true if a message was previously published, false otherwise
    /// </summary>
    bool TryGetLastMessage<T>(out T message, out DateTimeOffset timestamp) where T : struct;
}

/// <summary>
/// Message queue for deferred message processing
/// Tickable modules should create one and process it during Tick()
/// </summary>
public interface IMessageQueue
{
    /// <summary>
    /// Enqueue a message for later processing
    /// </summary>
    void Enqueue<T>(in T message) where T : struct;

    /// <summary>
    /// Process all queued messages by calling their handlers
    /// Call this during your module's Tick() method
    /// </summary>
    void ProcessQueue();

    /// <summary>
    /// Get number of messages waiting in queue
    /// </summary>
    int QueuedCount { get; }

    /// <summary>
    /// Clear all queued messages without processing
    /// </summary>
    void Clear();
}
