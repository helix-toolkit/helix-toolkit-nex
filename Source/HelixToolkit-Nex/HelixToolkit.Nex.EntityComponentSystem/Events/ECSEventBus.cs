namespace HelixToolkit.Nex.ECS.Events;

internal static class ECSEventBus
{
    private static class Subscriptions<T>
    {
        public static readonly SubscriptionHandler<T>[] Containers = new SubscriptionHandler<T>[
            Limits.MaxWorldId + 1
        ];

        static Subscriptions()
        {
            for (var i = 0; i < Containers.Length; ++i)
            {
                Containers[i] = new SubscriptionHandler<T>();
                Register<WorldDisposedEvent>(i, OnWorldDisposed);
            }
        }

        private static void OnWorldDisposed(World world, WorldDisposedEvent msg)
        {
            Containers[world.Id].Clear();
        }
    }

    public static void Send<TMessage>(World world, TMessage message)
    {
        Subscriptions<TMessage>.Containers[world.Id].Publish(world, message);
    }

    public static void Send<TMessage>(int worldId, TMessage message)
    {
        var world = World.GetWorldById(worldId);
        if (world == null)
        {
            return;
        }
        Subscriptions<TMessage>.Containers[worldId].Publish(world, message);
    }

    public static Subscription Register<TMessage>(World world, Action<World, TMessage> action)
    {
        return Subscriptions<TMessage>.Containers[world.Id].Subscribe(action);
    }

    public static Subscription Register<TMessage>(int worldId, Action<World, TMessage> action)
    {
        Debug.Assert(
            worldId >= 0 && worldId <= Limits.MaxWorldId,
            $"World ID {worldId} is out of bounds."
        );
        return Subscriptions<TMessage>.Containers[worldId].Subscribe(action);
    }

    /// <summary>
    /// A high-performance, array-based subscription handler with sender context that avoids heap allocations
    /// during publish operations. Supports struct-based subscription handles for easy unsubscription.
    /// </summary>
    /// <typeparam name="TMessage">The type of message to handle.</typeparam>
    public sealed class SubscriptionHandler<TMessage>
    {
        private Action<World, TMessage>?[] _handlers = [];
        private int _count;
        private readonly object _lock = new();

        /// <summary>
        /// Gets the number of active subscriptions.
        /// </summary>
        public int Count => _count;

        /// <summary>
        /// Subscribes an action to receive messages.
        /// </summary>
        /// <param name="action">The action to invoke when a message is received.</param>
        /// <returns>A subscription handle that can be disposed to unsubscribe.</returns>
        public Subscription Subscribe(Action<World, TMessage> action)
        {
            ArgumentNullException.ThrowIfNull(action);

            lock (_lock)
            {
                // Find an empty slot or expand
                for (var i = 0; i < _handlers.Length; i++)
                {
                    if (_handlers[i] is null)
                    {
                        _handlers[i] = action;
                        _count++;
                        return new Subscription.SubscriptionT<TMessage>(this, i);
                    }
                }

                // No empty slot, expand array
                var newIndex = _handlers.Length;
                var newSize = Math.Max(4, _handlers.Length * 2);
                Array.Resize(ref _handlers, newSize);
                _handlers[newIndex] = action;
                _count++;
                return new Subscription.SubscriptionT<TMessage>(this, newIndex);
            }
        }

        /// <summary>
        /// Publishes a message to all subscribed handlers.
        /// </summary>
        /// <param name="sender">The sender of the message.</param>
        /// <param name="message">The message to publish.</param>
        public void Publish(World sender, TMessage message)
        {
            // Take a local reference to avoid issues if array is resized during iteration
            var handlers = _handlers;
            for (var i = 0; i < handlers.Length; i++)
            {
                handlers[i]?.Invoke(sender, message);
            }
        }

        /// <summary>
        /// Removes all subscriptions.
        /// </summary>
        public void Clear()
        {
            lock (_lock)
            {
                Array.Clear(_handlers, 0, _handlers.Length);
                _count = 0;
            }
        }

        internal void Unsubscribe(int index)
        {
            lock (_lock)
            {
                if (index >= 0 && index < _handlers.Length && _handlers[index] is not null)
                {
                    _handlers[index] = null;
                    _count--;
                }
            }
        }
    }
}

/// <summary>
/// A struct-based subscription handle that can be disposed to unsubscribe.
/// </summary>
public abstract class Subscription : IDisposable
{
    private readonly int _index;
    private bool _disposed = false;

    internal Subscription(int index)
    {
        _index = index;
    }

    protected abstract void OnDispose();

    /// <summary>
    /// Unsubscribes from the subscription handler.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        OnDispose();
        _disposed = true;
    }

    internal sealed class SubscriptionT<TMessage> : Subscription
    {
        private readonly ECSEventBus.SubscriptionHandler<TMessage> _handler;

        internal SubscriptionT(ECSEventBus.SubscriptionHandler<TMessage> handler, int index)
            : base(index)
        {
            _handler = handler;
        }

        protected override void OnDispose()
        {
            _handler.Unsubscribe(_index);
        }
    }
}
