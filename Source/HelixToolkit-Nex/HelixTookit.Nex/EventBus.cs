using System.Collections.Concurrent;

namespace HelixToolkit.Nex;

/// <summary>
/// Base interface for all events that can be published through the EventBus
/// </summary>
public interface IEvent { }

/// <summary>
/// Event subscription handle that can be disposed to unsubscribe
/// </summary>
public interface IEventSubscription : IDisposable { }

/// <summary>
/// Thread-safe event bus implementation supporting generic event types with async publishing
/// and main thread subscriber dispatch
/// </summary>
public sealed class EventBus : IDisposable
{
    private readonly ConcurrentDictionary<Type, object> _subscribers = new();
    private readonly SynchronizationContext? _mainThreadContext;
    private readonly ConcurrentQueue<Action> _eventQueue = new();
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly Thread _publishThread;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the EventBus class
    /// </summary>
    /// <param name="captureMainThreadContext">If true, captures the current synchronization context as the main thread context</param>
    public EventBus(bool captureMainThreadContext = true)
    {
        if (captureMainThreadContext)
        {
            _mainThreadContext = SynchronizationContext.Current;
        }

        _publishThread = new Thread(ProcessEventQueue)
        {
            Name = "EventBus Publisher Thread",
            IsBackground = true,
        };
        _publishThread.Start();
    }

    /// <summary>
    /// Subscribes to events of type TEvent
    /// </summary>
    /// <typeparam name="TEvent">The event type to subscribe to</typeparam>
    /// <param name="handler">The handler to invoke when an event is published</param>
    /// <param name="dispatchOnMainThread">If true, handler will be invoked on the main thread (default: true)</param>
    /// <returns>A subscription handle that can be disposed to unsubscribe</returns>
    public IEventSubscription Subscribe<TEvent>(
        Action<TEvent> handler,
        bool dispatchOnMainThread = true
    )
        where TEvent : IEvent
    {
        if (handler == null)
            throw new ArgumentNullException(nameof(handler));

        ObjectDisposedException.ThrowIf(_disposed, this);

        var subscriberList =
            (SubscriberList<TEvent>)
                _subscribers.GetOrAdd(typeof(TEvent), _ => new SubscriberList<TEvent>());

        var subscription = new EventSubscription<TEvent>(
            this,
            handler,
            subscriberList,
            dispatchOnMainThread
        );
        subscriberList.Add(subscription);

        return subscription;
    }

    /// <summary>
    /// Publishes an event asynchronously in a thread-safe manner
    /// </summary>
    /// <typeparam name="TEvent">The event type to publish</typeparam>
    /// <param name="eventData">The event data to publish</param>
    public void PublishAsync<TEvent>(TEvent eventData)
        where TEvent : IEvent
    {
        if (eventData == null)
            throw new ArgumentNullException(nameof(eventData));

        ObjectDisposedException.ThrowIf(_disposed, this);

        _eventQueue.Enqueue(() => PublishInternal(eventData));
    }

    /// <summary>
    /// Publishes an event synchronously on the calling thread
    /// </summary>
    /// <typeparam name="TEvent">The event type to publish</typeparam>
    /// <param name="eventData">The event data to publish</param>
    public void Publish<TEvent>(TEvent eventData)
        where TEvent : IEvent
    {
        if (eventData == null)
            throw new ArgumentNullException(nameof(eventData));

        ObjectDisposedException.ThrowIf(_disposed, this);

        PublishInternal(eventData);
    }

    /// <summary>
    /// Gets the number of subscribers for a specific event type
    /// </summary>
    /// <typeparam name="TEvent">The event type</typeparam>
    /// <returns>The number of subscribers</returns>
    public int GetSubscriberCount<TEvent>()
        where TEvent : IEvent
    {
        if (_subscribers.TryGetValue(typeof(TEvent), out var list))
        {
            return ((SubscriberList<TEvent>)list).Count;
        }
        return 0;
    }

    private void PublishInternal<TEvent>(TEvent eventData)
        where TEvent : IEvent
    {
        if (!_subscribers.TryGetValue(typeof(TEvent), out var subscribersObj))
            return;

        var subscribers = (SubscriberList<TEvent>)subscribersObj;
        var subscriptionsSnapshot = subscribers.GetSnapshot();

        foreach (var subscription in subscriptionsSnapshot)
        {
            if (subscription.DispatchOnMainThread && _mainThreadContext != null)
            {
                // Dispatch to main thread
                _mainThreadContext.Post(
                    _ =>
                    {
                        try
                        {
                            subscription.Handler(eventData);
                        }
                        catch (Exception ex)
                        {
                            // Log error but don't crash the event bus
                            System.Diagnostics.Debug.WriteLine($"Error in event handler: {ex}");
                        }
                    },
                    null
                );
            }
            else
            {
                // Invoke directly on the current thread
                try
                {
                    subscription.Handler(eventData);
                }
                catch (Exception ex)
                {
                    // Log error but don't crash the event bus
                    System.Diagnostics.Debug.WriteLine($"Error in event handler: {ex}");
                }
            }
        }
    }

    private void ProcessEventQueue()
    {
        while (!_cancellationTokenSource.Token.IsCancellationRequested)
        {
            try
            {
                if (_eventQueue.TryDequeue(out var action))
                {
                    action();
                }
                else
                {
                    // Sleep briefly if queue is empty to avoid spinning
                    Thread.Sleep(1);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error processing event queue: {ex}");
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _cancellationTokenSource.Cancel();

        // Wait for the publish thread to complete
        if (!_publishThread.Join(TimeSpan.FromSeconds(5)))
        {
            System.Diagnostics.Debug.WriteLine(
                "EventBus publish thread did not terminate gracefully"
            );
        }

        _cancellationTokenSource.Dispose();
        _subscribers.Clear();
    }

    /// <summary>
    /// Internal subscription implementation
    /// </summary>
    private sealed class EventSubscription<TEvent> : IEventSubscription
        where TEvent : IEvent
    {
        private readonly EventBus _eventBus;
        private readonly SubscriberList<TEvent> _subscriberList;
        private bool _disposed;

        public Action<TEvent> Handler { get; }
        public bool DispatchOnMainThread { get; }

        public EventSubscription(
            EventBus eventBus,
            Action<TEvent> handler,
            SubscriberList<TEvent> subscriberList,
            bool dispatchOnMainThread
        )
        {
            _eventBus = eventBus;
            Handler = handler;
            _subscriberList = subscriberList;
            DispatchOnMainThread = dispatchOnMainThread;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _subscriberList.Remove(this);
        }
    }

    /// <summary>
    /// Thread-safe list of event subscribers
    /// </summary>
    private sealed class SubscriberList<TEvent>
        where TEvent : IEvent
    {
        private readonly object _lock = new();
        private readonly List<EventSubscription<TEvent>> _subscriptions = new();

        public int Count
        {
            get
            {
                lock (_lock)
                {
                    return _subscriptions.Count;
                }
            }
        }

        public void Add(EventSubscription<TEvent> subscription)
        {
            lock (_lock)
            {
                _subscriptions.Add(subscription);
            }
        }

        public void Remove(EventSubscription<TEvent> subscription)
        {
            lock (_lock)
            {
                _subscriptions.Remove(subscription);
            }
        }

        public List<EventSubscription<TEvent>> GetSnapshot()
        {
            lock (_lock)
            {
                return [.. _subscriptions];
            }
        }
    }
}
