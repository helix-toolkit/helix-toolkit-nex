namespace HelixToolkit.Nex.Rendering.DrawStreams;

/// <summary>
/// Central registry that owns all <see cref="IDrawStream"/> instances and provides
/// lookup by stream name or category. The registry manages the lifecycle of all streams,
/// coordinates per-frame updates, and supports batch GPU synchronization barriers.
/// </summary>
public interface IDrawStreamRegistry : IInitializable, IDisposable
{
    /// <summary>
    /// Gets a stream by its registered name.
    /// </summary>
    /// <param name="name">The unique name identifying the desired stream.</param>
    /// <returns>The <see cref="IDrawStream"/> registered under the specified name.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown if <paramref name="name"/> does not correspond to a registered stream.
    /// </exception>
    IDrawStream GetStream(DrawStreamName name);

    /// <summary>
    /// Gets all streams whose <see cref="IDrawStream.Categories"/> include the specified category flags.
    /// Returns an empty enumerable if no streams match.
    /// </summary>
    /// <param name="category">The category flag mask to match against.</param>
    /// <returns>All streams that have the specified category flags set.</returns>
    IEnumerable<IDrawStream> GetStreams(DrawStreamCategory category);

    /// <summary>
    /// Gets all registered streams in the registry.
    /// </summary>
    IEnumerable<IDrawStream> AllStreams { get; }

    /// <summary>
    /// Zero-allocation overload for internal/engine callers that know the concrete registry type.
    /// Returns a struct enumerable that avoids the <c>yield return</c> state-machine heap allocation.
    /// </summary>
    MeshDrawStreamEnumerable GetStreamsCore(DrawStreamCategory category);

    /// <summary>
    /// Updates all streams by processing pending changes, running compaction where needed,
    /// and rebuilding material-type ordering. Called once per frame before rendering begins.
    /// </summary>
    /// <returns><c>true</c> if all stream updates succeeded; <c>false</c> if any stream failed to update.</returns>
    bool Update();
}

/// <summary>
/// Zero-allocation struct enumerable over a <see cref="FastList{MeshDrawStream}"/> filtered by
/// <see cref="DrawStreamCategory"/>. Use <see cref="MeshDrawStreamRegistry.GetStreamsCore"/> to obtain one.
/// </summary>
public readonly struct MeshDrawStreamEnumerable(
    FastList<IDrawStream> list,
    DrawStreamCategory category
)
{
    private readonly FastList<IDrawStream> _list = list;
    private readonly DrawStreamCategory _category = category;

    public Enumerator GetEnumerator() => new(_list, _category);

    public struct Enumerator(FastList<IDrawStream> list, DrawStreamCategory category)
    {
        private readonly FastList<IDrawStream> _list = list;
        private readonly DrawStreamCategory _category = category;
        private int _index = -1;

        public IDrawStream Current => _list[_index];

        public bool MoveNext()
        {
            while (++_index < _list.Count)
            {
                if (_list[_index].Categories.HasAnyFlag(_category))
                    return true;
            }
            return false;
        }
    }

    public bool HasAny()
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
