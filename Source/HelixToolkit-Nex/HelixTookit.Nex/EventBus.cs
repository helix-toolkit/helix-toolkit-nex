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
/// Thread-safe event bus implementation supporting generic event types with synchronous publishing
/// </summary>
public sealed class EventBus : IDisposable
{
    private readonly ConcurrentDictionary<Type, object> _subscribers = new();
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the EventBus class
    /// </summary>
    public EventBus() { }

    /// <summary>
    /// Subscribes to events of type TEvent
    /// </summary>
    /// <typeparam name="TEvent">The event type to subscribe to</typeparam>
    /// <param name="handler">The handler to invoke when an event is published</param>
    /// <returns>A subscription handle that can be disposed to unsubscribe</returns>
    public IEventSubscription Subscribe<TEvent>(Action<TEvent> handler)
        where TEvent : IEvent
    {
        if (handler == null)
            throw new ArgumentNullException(nameof(handler));

        ObjectDisposedException.ThrowIf(_disposed, this);

        var subscriberList =
            (SubscriberList<TEvent>)
                _subscribers.GetOrAdd(typeof(TEvent), _ => new SubscriberList<TEvent>());

        var subscription = new EventSubscription<TEvent>(this, handler, subscriberList);
        subscriberList.Add(subscription);

        return subscription;
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

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
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

        public EventSubscription(
            EventBus eventBus,
            Action<TEvent> handler,
            SubscriberList<TEvent> subscriberList
        )
        {
            _eventBus = eventBus;
            Handler = handler;
            _subscriberList = subscriberList;
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

    private static class DefaultEventBusHolder
    {
        internal static readonly EventBus Instance = new();
    }

    public static EventBus Instance => DefaultEventBusHolder.Instance;
}
