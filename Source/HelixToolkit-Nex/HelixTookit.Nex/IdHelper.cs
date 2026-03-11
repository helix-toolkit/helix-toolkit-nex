using System.Collections.Concurrent;

namespace HelixToolkit.Nex;

public sealed class IdHelper
{
    private int _maxId = 0;
    private readonly ConcurrentStack<int> _freedIds = new ConcurrentStack<int>();

    public int GetNextId()
    {
        return _freedIds.TryPop(out var id) ? id : Interlocked.Increment(ref _maxId);
    }

    public int MaxId => Interlocked.CompareExchange(ref _maxId, 0, 0);

    public int Count => MaxId - _freedIds.Count;

    public void ReleaseId(int id)
    {
        _freedIds.Push(id);
    }
}
