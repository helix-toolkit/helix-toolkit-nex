using System.Collections.Concurrent;

namespace HelixToolkit.Nex;

public sealed class ObjectPool<T>(Func<T> objectGenerator, int maxCapacity = int.MaxValue / 2)
{
    private readonly ConcurrentBag<T> _objects = [];
    public readonly int MaxCapacity = maxCapacity;

    public int Count
    {
        get { return _objects.Count; }
    }

    public T GetObject()
    {
        if (_objects.TryTake(out var item))
            return item;
        return objectGenerator();
    }

    public void PutObject(T item)
    {
        if (Count < MaxCapacity)
            _objects.Add(item);
    }
}
