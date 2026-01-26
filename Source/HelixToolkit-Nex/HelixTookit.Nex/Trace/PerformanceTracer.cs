using System.Runtime.CompilerServices;

namespace HelixToolkit.Nex.Trace;

/// <summary>
/// High-performance tracer implementation with object pooling and thread-safe operations.
/// </summary>
public sealed class PerformanceTracer : ITracer
{
    private readonly FastList<TraceEntry> _entries;
    private readonly ReaderWriterLockSlim _lock;
    private volatile bool _isEnabled;
    private readonly int _maxEntries;

    /// <summary>
    /// Event raised when a new trace entry is recorded.
    /// </summary>
    public event EventHandler<TraceEventArgs>? TraceRecorded;

    /// <summary>
    /// Initializes a new instance of the PerformanceTracer class.
    /// </summary>
    /// <param name="initialCapacity">The initial capacity for trace entries.</param>
    /// <param name="maxEntries">Maximum number of entries to keep (0 for unlimited).</param>
    public PerformanceTracer(int initialCapacity = 1024, int maxEntries = 0)
    {
        _entries = new FastList<TraceEntry>(initialCapacity);
        _lock = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);
        _isEnabled = true;
        _maxEntries = maxEntries;
    }

    /// <inheritdoc/>
    public bool IsEnabled
    {
        get => _isEnabled;
        set => _isEnabled = value;
    }

    /// <summary>
    /// Gets the current count of trace entries.
    /// </summary>
    public int Count
    {
        get
        {
            _lock.EnterReadLock();
            try
            {
                return _entries.Count;
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TraceScope BeginScope(string name)
    {
        return new TraceScope(_isEnabled ? this : null, name, null);
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TraceScope BeginScope(string name, string category)
    {
        return new TraceScope(_isEnabled ? this : null, name, category);
    }

    /// <summary>
    /// Internal method called by TraceScope to end a scope.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void EndScope(
        string name,
        string? category,
        long startTicks,
        long endTicks,
        int threadId
    )
    {
        if (!_isEnabled)
            return;

        var entry = TraceEntry.CreateScope(name, category, startTicks, endTicks, threadId);
        AddEntry(entry);
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void TraceEvent(string name, double value)
    {
        if (!_isEnabled)
            return;

        var entry = TraceEntry.CreateEvent(
            name,
            null,
            value,
            Stopwatch.GetTimestamp(),
            Environment.CurrentManagedThreadId
        );

        AddEntry(entry);
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void TraceEvent(string name, string category, double value)
    {
        if (!_isEnabled)
            return;

        var entry = TraceEntry.CreateEvent(
            name,
            category,
            value,
            Stopwatch.GetTimestamp(),
            Environment.CurrentManagedThreadId
        );

        AddEntry(entry);
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void TraceMarker(string name)
    {
        if (!_isEnabled)
            return;

        var entry = TraceEntry.CreateMarker(
            name,
            null,
            Stopwatch.GetTimestamp(),
            Environment.CurrentManagedThreadId
        );

        AddEntry(entry);
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void TraceMarker(string name, string category)
    {
        if (!_isEnabled)
            return;

        var entry = TraceEntry.CreateMarker(
            name,
            category,
            Stopwatch.GetTimestamp(),
            Environment.CurrentManagedThreadId
        );

        AddEntry(entry);
    }

    /// <summary>
    /// Adds a trace entry to the collection.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AddEntry(in TraceEntry entry)
    {
        _lock.EnterWriteLock();
        try
        {
            // If max entries is set, remove oldest entries when limit is reached
            if (_maxEntries > 0 && _entries.Count >= _maxEntries)
            {
                _entries.RemoveAt(0);
            }

            _entries.Add(entry);
        }
        finally
        {
            _lock.ExitWriteLock();
        }

        // Raise event outside the lock to avoid potential deadlocks
        TraceRecorded?.Invoke(this, new TraceEventArgs(entry));
    }

    /// <inheritdoc/>
    public IReadOnlyList<TraceEntry> GetTraceEntries()
    {
        _lock.EnterReadLock();
        try
        {
            return _entries.ToArray();
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <summary>
    /// Gets trace entries filtered by category.
    /// </summary>
    public IReadOnlyList<TraceEntry> GetTraceEntries(string category)
    {
        _lock.EnterReadLock();
        try
        {
            var filtered = new List<TraceEntry>(_entries.Count);
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].Category == category)
                {
                    filtered.Add(_entries[i]);
                }
            }
            return filtered;
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <summary>
    /// Gets trace entries of a specific type.
    /// </summary>
    public IReadOnlyList<TraceEntry> GetTraceEntries(TraceEntryType entryType)
    {
        _lock.EnterReadLock();
        try
        {
            var filtered = new List<TraceEntry>(_entries.Count);
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].EntryType == entryType)
                {
                    filtered.Add(_entries[i]);
                }
            }
            return filtered;
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <inheritdoc/>
    public void Clear()
    {
        _lock.EnterWriteLock();
        try
        {
            _entries.Clear();
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Gets statistics for a specific trace name.
    /// </summary>
    public TraceStatistics GetStatistics(string name)
    {
        _lock.EnterReadLock();
        try
        {
            var stats = new TraceStatistics(name);
            for (int i = 0; i < _entries.Count; i++)
            {
                var entry = _entries[i];
                if (entry.Name == name && entry.EntryType == TraceEntryType.Scope)
                {
                    stats.AddSample(entry.DurationMs);
                }
            }
            return stats;
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <summary>
    /// Disposes the tracer and releases resources.
    /// </summary>
    public void Dispose()
    {
        _lock.Dispose();
    }
}
