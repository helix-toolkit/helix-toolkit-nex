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
    /// Updates all streams by processing pending changes, running compaction where needed,
    /// and rebuilding material-type ordering. Called once per frame before rendering begins.
    /// </summary>
    /// <returns><c>true</c> if all stream updates succeeded; <c>false</c> if any stream failed to update.</returns>
    bool Update();
}
