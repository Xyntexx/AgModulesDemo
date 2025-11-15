namespace AgOpenGPS.ModuleContracts;

/// <summary>
/// High-performance in-process message bus for real-time communication
/// Optimized for zero-allocation with struct messages
/// </summary>
public interface IMessageBus
{
    /// <summary>
    /// Subscribe to messages of type T
    /// Returns IDisposable to unsubscribe
    /// </summary>
    IDisposable Subscribe<T>(Action<T> handler) where T : struct;

    /// <summary>
    /// Subscribe with priority (higher = called first)
    /// Use for time-critical handlers
    /// </summary>
    IDisposable Subscribe<T>(Action<T> handler, int priority) where T : struct;

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
