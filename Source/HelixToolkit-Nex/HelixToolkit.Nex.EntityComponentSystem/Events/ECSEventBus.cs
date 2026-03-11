namespace HelixToolkit.Nex.ECS.Events;

public delegate void Message<in T>(int worldId, T message);

internal static class ECSEventBus
{
    private struct MessageContainer<T>
    {
        public Message<T>? Handlers;
    }

    private static class Subscriptions<T>
    {
        public static readonly MessageContainer<T>[] Containers = new MessageContainer<T>[
            byte.MaxValue
        ];

        static Subscriptions()
        {
            for (var i = 0; i < Containers.Length; ++i)
            {
                Register<WorldDisposedEvent>(i, OnWorldDisposed);
            }
        }

        private static void OnWorldDisposed(int worldId, WorldDisposedEvent msg)
        {
            Debug.Assert(worldId < byte.MaxValue);
            Containers[worldId].Handlers = null;
        }
    }

    public static void Send<TMessage>(int worldId, TMessage message)
    {
        Debug.Assert(worldId < byte.MaxValue);
        Subscriptions<TMessage>.Containers[worldId].Handlers?.Invoke(worldId, message);
    }

    public static void Register<TMessage>(int worldId, Message<TMessage> action)
    {
        Debug.Assert(worldId < byte.MaxValue);
        Subscriptions<TMessage>.Containers[worldId].Handlers += action;
    }

    public static void Unregister<TMessage>(int worldId, Message<TMessage> action)
    {
        Debug.Assert(worldId < byte.MaxValue);
        Subscriptions<TMessage>.Containers[worldId].Handlers -= action;
    }
}
