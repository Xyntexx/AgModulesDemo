namespace AgOpenGPS.Core;

using AgOpenGPS.ModuleContracts;
using System.Collections.Concurrent;

/// <summary>
/// Thread-safe message queue for deferred message processing.
/// Allows modules to process messages during their Tick() in their own thread context.
/// </summary>
public class MessageQueue : IMessageQueue
{
    private readonly ConcurrentQueue<QueuedMessage> _queue = new();
    private int _queuedCount = 0;

    public int QueuedCount => _queuedCount;

    /// <summary>
    /// Enqueue a message for later processing
    /// </summary>
    public void Enqueue<T>(in T message) where T : struct
    {
        _queue.Enqueue(new QueuedMessage
        {
            Message = message,
            Handler = null!  // Handler set during SubscribeQueued
        });
        Interlocked.Increment(ref _queuedCount);
    }

    /// <summary>
    /// Internal enqueue that includes the handler
    /// </summary>
    internal void EnqueueWithHandler<T>(in T message, Action<T> handler) where T : struct
    {
        _queue.Enqueue(new QueuedMessage
        {
            Message = message,
            Handler = handler
        });
        Interlocked.Increment(ref _queuedCount);
    }

    /// <summary>
    /// Process all queued messages
    /// Should be called during module's Tick()
    /// </summary>
    public void ProcessQueue()
    {
        while (_queue.TryDequeue(out var queued))
        {
            Interlocked.Decrement(ref _queuedCount);

            try
            {
                // Call the handler with the message
                var messageType = queued.Message.GetType();
                var handlerType = queued.Handler.GetType();
                var invokeMethod = handlerType.GetMethod("Invoke");
                invokeMethod?.Invoke(queued.Handler, new object[] { queued.Message });
            }
            catch (Exception ex)
            {
                // Log but continue processing
                // TODO: Inject logger for better error reporting
                Console.Error.WriteLine($"Error processing queued message: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Clear all queued messages without processing
    /// </summary>
    public void Clear()
    {
        while (_queue.TryDequeue(out _))
        {
            Interlocked.Decrement(ref _queuedCount);
        }
    }

    private class QueuedMessage
    {
        public object Message { get; set; } = null!;
        public Delegate Handler { get; set; } = null!;
    }
}
