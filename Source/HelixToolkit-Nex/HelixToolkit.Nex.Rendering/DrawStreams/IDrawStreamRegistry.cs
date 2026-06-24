namespace HelixToolkit.Nex.Rendering.DrawStreams;

/// <summary>
/// Central registry that owns all <see cref="IDrawStream{DRAW_TYPE}"/> instances and provides
/// lookup by stream name or category. The registry manages the lifecycle of all streams,
/// coordinates per-frame updates, and supports batch GPU synchronization barriers.
/// </summary>
public interface IDrawStreamRegistry<DRAW_TYPE> : IInitializable, IDisposable
    where DRAW_TYPE : unmanaged
{
    /// <summary>
    /// Gets a stream by its registered name.
    /// </summary>
    /// <param name="type">The type of the draw stream.</param>
    /// <param name="name">The unique name identifying the desired stream.</param>
    /// <returns>The <see cref="IDrawStream{DRAW_TYPE}"/> registered under the specified name.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown if <paramref name="name"/> does not correspond to a registered stream.
    /// </exception>
    IDrawStream<DRAW_TYPE>? GetStream(DrawStreamType type, DrawStreamName name);

    /// <summary>
    /// Gets all streams whose <see cref="IDrawStream{DRAW_TYPE}.StreamType"/> include the specified variant flags.
    /// Returns an empty enumerable if no streams match.
    /// </summary>
    /// <param name="type">The type of the draw stream.</param>
    /// <param name="variants">The variant flag mask to match against.</param>
    /// <returns>All streams that have the specified variant flags set.</returns>
    DrawStreamEnumerable<DRAW_TYPE> GetStreams(DrawStreamType type, DrawStreamVariants variants);

    /// <summary>
    /// Gets all registered streams in the registry.
    /// Returns a zero-allocation struct enumerable.
    /// </summary>
    AllStreamsEnumerable<DRAW_TYPE> AllStreams { get; }

    /// <summary>
    /// Zero-allocation overload for internal/engine callers that know the concrete registry type.
    /// Returns a struct enumerable that avoids the <c>yield return</c> state-machine heap allocation.
    /// </summary>
    DrawStreamEnumerable<DRAW_TYPE> GetStreams(DrawStreamType type);

    /// <summary>
    /// Updates all streams by processing pending changes, running compaction where needed,
    /// and rebuilding material-type ordering. Called once per frame before rendering begins.
    /// </summary>
    /// <returns><c>true</c> if all stream updates succeeded; <c>false</c> if any stream failed to update.</returns>
    bool Update();
}

/// <summary>
/// Zero-allocation struct enumerable over a <see cref="FastList{IDrawStream}"/> filtered by
/// <see cref="DrawStreamVariants"/>. 
/// </summary>
public readonly struct DrawStreamEnumerable<DRAW_TYPE>(
    FastList<IDrawStream<DRAW_TYPE>?> list,
    DrawStreamVariants? category
)
    where DRAW_TYPE : unmanaged
{
    private readonly FastList<IDrawStream<DRAW_TYPE>?> _list = list;
    private readonly DrawStreamVariants? _category = category;

    public readonly Enumerator GetEnumerator() => new(_list, _category);

    public struct Enumerator(FastList<IDrawStream<DRAW_TYPE>?> list, DrawStreamVariants? category)
    {
        private readonly FastList<IDrawStream<DRAW_TYPE>?> _list = list;
        private readonly DrawStreamVariants? _category = category;
        private int _index = -1;

        public IDrawStream<DRAW_TYPE> Current => _list[_index]!;

        public bool MoveNext()
        {
            while (++_index < _list.Count)
            {
                if (_list[_index] == null)
                {
                    continue;
                }
                if (_category == null || _list[_index]!.Variants.HasAllFlags(_category.Value))
                {
                    return true;
                }
            }
            return false;
        }
    }

    public readonly bool HasAny()
    {
        foreach (var stream in this)
        {
            if (stream.Count > 0)
            {
                return true;
            }
        }
        return false;
    }
}

/// <summary>
/// Zero-allocation struct enumerable over all streams in a registry.
/// Supports both flat lists and nested lists of draw streams.
/// </summary>
public readonly struct AllStreamsEnumerable<DRAW_TYPE>
    where DRAW_TYPE : unmanaged
{
    private readonly FastList<IDrawStream<DRAW_TYPE>?>? _flatList;
    private readonly FastList<FastList<IDrawStream<DRAW_TYPE>>>? _nestedList;

    /// <summary>
    /// Creates an enumerable over a flat list of streams.
    /// </summary>
    public AllStreamsEnumerable(FastList<IDrawStream<DRAW_TYPE>?> list)
    {
        _flatList = list;
        _nestedList = null;
    }

    /// <summary>
    /// Creates an enumerable over a nested list of streams.
    /// </summary>
    public AllStreamsEnumerable(FastList<FastList<IDrawStream<DRAW_TYPE>>> nestedList)
    {
        _flatList = null;
        _nestedList = nestedList;
    }

    public readonly Enumerator GetEnumerator() => new(_flatList, _nestedList);

    public struct Enumerator
    {
        private readonly FastList<IDrawStream<DRAW_TYPE>?>? _flatList;
        private readonly FastList<FastList<IDrawStream<DRAW_TYPE>>>? _nestedList;
        private int _outerIndex;
        private int _innerIndex;
        private IDrawStream<DRAW_TYPE>? _current;

        public Enumerator(
            FastList<IDrawStream<DRAW_TYPE>?>? flatList,
            FastList<FastList<IDrawStream<DRAW_TYPE>>>? nestedList
        )
        {
            _flatList = flatList;
            _nestedList = nestedList;
            _outerIndex = -1;
            _innerIndex = -1;
            _current = null;
        }

        public readonly IDrawStream<DRAW_TYPE> Current => _current!;

        public bool MoveNext()
        {
            if (_flatList is not null)
            {
                return MoveNextFlat();
            }
            else if (_nestedList is not null)
            {
                return MoveNextNested();
            }
            return false;
        }

        private bool MoveNextFlat()
        {
            while (++_outerIndex < _flatList!.Count)
            {
                if (_flatList[_outerIndex] is not null)
                {
                    _current = _flatList[_outerIndex];
                    return true;
                }
            }
            return false;
        }

        private bool MoveNextNested()
        {
            while (true)
            {
                // Try to advance within the current inner list
                if (_outerIndex >= 0 && _outerIndex < _nestedList!.Count)
                {
                    var innerList = _nestedList[_outerIndex];
                    if (innerList is not null)
                    {
                        while (++_innerIndex < innerList.Count)
                        {
                            var stream = innerList[_innerIndex];
                            if (stream is not null)
                            {
                                _current = stream;
                                return true;
                            }
                        }
                    }
                }

                // Move to the next outer list
                ++_outerIndex;
                _innerIndex = -1;

                if (_outerIndex >= _nestedList!.Count)
                {
                    return false;
                }
            }
        }
    }
}
