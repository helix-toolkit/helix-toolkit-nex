namespace HelixToolkit.Nex.ECS.Events;

internal static class Publisher
{
    private readonly struct Subscription<T> : IDisposable
    {
        private readonly int _worldId;
        private readonly Message<T> _handler;

        public Subscription(in int worldId, Message<T> handler)
        {
            Debug.Assert(worldId < byte.MaxValue);
            _worldId = worldId;
            _handler = handler;
        }

        public void Dispose()
        {
            ECSEventBus.Unregister(_worldId, _handler);
        }
    }

    public static IDisposable Subscribe<T>(in int worldId, Message<T> handler)
    {
        Debug.Assert(worldId < byte.MaxValue);
        ECSEventBus.Register<T>(worldId, handler);
        return new Subscription<T>(worldId, handler);
    }

    public static void Publish<T>(in int worldId, T message)
    {
        Debug.Assert(worldId < byte.MaxValue);
        ECSEventBus.Send(worldId, message);
    }
}
