using HelixToolkit.Nex.Textures;

namespace HelixToolkit.Nex.Repository;

/// <summary>
/// Interface for a thread-safe repository that caches GPU texture resources.
/// </summary>
/// <remarks>
/// This interface provides access to texture caching functionality, enabling:
/// <list type="bullet">
/// <item><description>Automatic deduplication of texture resources</description></item>
/// <item><description>LRU eviction policy for memory management</description></item>
/// <item><description>Cache statistics and monitoring</description></item>
/// <item><description>Thread-safe concurrent access</description></item>
/// </list>
/// Textures are keyed by a caller-supplied name (for stream sources) or by the normalized
/// absolute file path (for file-system sources).
/// <para>
/// Callers receive a <see cref="TextureRef"/> wrapper instead of a raw <see cref="TextureResource"/>.
/// The wrapper holds a direct reference to the underlying GPU resource and exposes it via
/// <see cref="TextureRef.GetHandle()"/> as an O(1) property. When a texture is removed from the repository
/// (via <see cref="Remove"/>), the underlying GPU resource is disposed and the ref's
/// <see cref="TextureRef.OnDisposed"/> event fires synchronously, allowing consumers to react
/// (e.g., zero their bindless indices) rather than polling or re-fetching.
/// </para>
/// </remarks>
public interface ITextureRepository : IDisposable
{
    /// <summary>
    /// Gets the number of cached textures.
    /// </summary>
    int Count { get; }

    /// <summary>
    /// Gets or creates a GPU texture from a memory stream, using <paramref name="name"/> as the cache key.
    /// </summary>
    /// <param name="name">
    /// A unique name that identifies this texture in the cache.
    /// If a texture with the same name already exists it is returned directly without re-decoding the stream.
    /// </param>
    /// <param name="stream">The stream containing the encoded image data.</param>
    /// <param name="generateMipmaps">When <c>true</c>, automatically generates the full mip chain on the GPU after upload. Defaults to <c>true</c>.</param>
    /// <param name="debugName">Optional debug name forwarded to the GPU resource.</param>
    /// <returns>
    /// A <see cref="TextureRef"/> whose <see cref="TextureRef.Key"/> matches <paramref name="name"/>
    /// and whose <see cref="TextureRef.Repository"/> is this repository instance.
    /// </returns>
    /// <exception cref="ArgumentException">Thrown if <paramref name="name"/> is null or empty.</exception>
    /// <exception cref="InvalidOperationException">Thrown if the stream cannot be decoded or texture creation fails.</exception>
    /// <exception cref="ObjectDisposedException">Thrown if the repository or context has been disposed.</exception>
    TextureRef GetOrCreateFromStream(string name, Stream stream, bool generateMipmaps = true, string? debugName = null);

    /// <summary>
    /// Gets or creates a GPU texture by loading a file from the file system.
    /// The normalized absolute path is used as the cache key.
    /// </summary>
    /// <param name="filePath">Path to the image file on disk.</param>
    /// <param name="generateMipmaps">When <c>true</c>, automatically generates the full mip chain on the GPU after upload. Defaults to <c>true</c>.</param>
    /// <param name="debugName">
    /// Optional debug name forwarded to the GPU resource.
    /// Defaults to the file name when <c>null</c>.
    /// </param>
    /// <returns>
    /// A <see cref="TextureRef"/> whose <see cref="TextureRef.Key"/> is the normalized absolute path
    /// and whose <see cref="TextureRef.Repository"/> is this repository instance.
    /// </returns>
    /// <exception cref="ArgumentException">Thrown if <paramref name="filePath"/> is null or empty.</exception>
    /// <exception cref="FileNotFoundException">Thrown if the file does not exist.</exception>
    /// <exception cref="InvalidOperationException">Thrown if the file cannot be decoded or texture creation fails.</exception>
    /// <exception cref="ObjectDisposedException">Thrown if the repository or context has been disposed.</exception>
    TextureRef GetOrCreateFromFile(string filePath, bool generateMipmaps = true, string? debugName = null);

    /// <summary>
    /// Gets or creates a GPU texture from an already decoded image, using <paramref name="name"/> as the cache key.
    /// </summary>
    /// <param name="name">A unique name that identifies this texture in the cache.</param>
    /// <param name="image">The decoded image data.</param>
    /// <param name="generateMipmaps">When <c>true</c>, automatically generates the full mip chain on the GPU after upload. Defaults to <c>true</c>.</param>
    /// <returns>
    /// A <see cref="TextureRef"/> whose <see cref="TextureRef.Key"/> matches <paramref name="name"/>
    /// and whose <see cref="TextureRef.Repository"/> is this repository instance.
    /// </returns>
    /// <exception cref="ArgumentException">Thrown if <paramref name="name"/> is null or empty.</exception>
    /// <exception cref="InvalidOperationException">Thrown if the image cannot be used to create a texture.</exception>
    /// <exception cref="ObjectDisposedException">Thrown if the repository or context has been disposed.</exception>
    TextureRef GetOrCreateFromImage(string name, Image image, bool generateMipmaps = true);

    /// <summary>
    /// Gets or creates a cubemap GPU texture from six already decoded face images, using
    /// <paramref name="name"/> as the cache key.
    /// </summary>
    /// <param name="name">A unique name that identifies this cubemap in the cache.</param>
    /// <param name="faces">
    /// Exactly six square, equally sized face images in the standard cubemap order:
    /// index 0 = +X, 1 = -X, 2 = +Y, 3 = -Y, 4 = +Z, 5 = -Z.
    /// </param>
    /// <param name="generateMipmaps">When <c>true</c>, generates the full mip chain on the GPU after upload. Defaults to <c>true</c>.</param>
    /// <returns>A <see cref="TextureRef"/> for the resulting <c>TextureCube</c>.</returns>
    /// <exception cref="ArgumentException">Thrown if <paramref name="name"/> is null/empty or the faces are invalid (not six, not square, mismatched size/format).</exception>
    /// <exception cref="ObjectDisposedException">Thrown if the repository or context has been disposed.</exception>
    /// <remarks>
    /// The default implementation assembles the faces into a cube <see cref="Image"/> via
    /// <see cref="Image.NewCube(IReadOnlyList{Image})"/> and forwards to
    /// <see cref="GetOrCreateFromImage"/>; <see cref="TextureRepository"/> overrides it for caching.
    /// </remarks>
    TextureRef GetOrCreateCubeFromImages(
        string name,
        IReadOnlyList<Image> faces,
        bool generateMipmaps = true
    )
    {
        using var cube = Image.NewCube(faces);
        return GetOrCreateFromImage(name, cube, generateMipmaps);
    }

    /// <summary>
    /// Gets or creates a cubemap GPU texture by loading six face image files from disk. If debug name is not provided,
    /// A deterministic composite of the six normalized absolute paths is used as the cache key.
    /// </summary>
    /// <param name="filePaths">
    /// Exactly six image file paths in the standard cubemap order:
    /// index 0 = +X, 1 = -X, 2 = +Y, 3 = -Y, 4 = +Z, 5 = -Z. Faces must decode to square,
    /// equally sized images of the same format (PNG/JPG/BMP/TGA/… decode to RGBA8).
    /// </param>
    /// <param name="generateMipmaps">When <c>true</c>, generates the full mip chain on the GPU after upload. Defaults to <c>true</c>.</param>
    /// <param name="cubeMapName">Optional name as key used for storing the cubemap. Defaults to <c>null</c>.</param>
    /// <returns>A <see cref="TextureRef"/> for the resulting <c>TextureCube</c>.</returns>
    /// <exception cref="ArgumentException">Thrown if there are not exactly six paths, or a path is null/empty.</exception>
    /// <exception cref="FileNotFoundException">Thrown if any face file does not exist.</exception>
    /// <exception cref="InvalidOperationException">Thrown if any face cannot be decoded or texture creation fails.</exception>
    /// <exception cref="ObjectDisposedException">Thrown if the repository or context has been disposed.</exception>
    /// <remarks>
    /// The default implementation decodes each face and forwards to
    /// <see cref="GetOrCreateCubeFromImages"/>; <see cref="TextureRepository"/> overrides it to key
    /// the cache by the six normalized paths and to validate file existence up front.
    /// </remarks>
    TextureRef GetOrCreateCubeFromFiles(
        IReadOnlyList<string> filePaths,
        bool generateMipmaps = true,
        string? cubeMapName = null
    )
    {
        ArgumentNullException.ThrowIfNull(filePaths);

        var faces = filePaths
            .Select(p =>
                Image.Load(p)
                ?? throw new InvalidOperationException($"Failed to decode cube face image: '{p}'")
            )
            .ToArray();
        try
        {
            var name = string.IsNullOrEmpty(cubeMapName)
                ? string.Join("|", filePaths.Select(Path.GetFullPath))
                : cubeMapName;
            return GetOrCreateCubeFromImages(name, faces, generateMipmaps);
        }
        finally
        {
            foreach (var face in faces)
                face?.Dispose();
        }
    }

    /// <summary>
    /// Gets or creates a GPU texture from a memory stream asynchronously, using <paramref name="name"/> as the cache key.
    /// </summary>
    /// <param name="name">
    /// A unique name that identifies this texture in the cache.
    /// If a texture with the same name already exists it is returned directly without re-decoding the stream.
    /// </param>
    /// <param name="stream">The stream containing the encoded image data.</param>
    /// <param name="generateMipmaps">When <c>true</c>, automatically generates the full mip chain on the GPU after upload. Defaults to <c>true</c>.</param>
    /// <param name="debugName">Optional debug name forwarded to the GPU resource.</param>
    /// <returns>
    /// A <see cref="TextureRef"/> whose <see cref="TextureRef.Key"/> matches <paramref name="name"/>
    /// and whose <see cref="TextureRef.Repository"/> is this repository instance.
    /// </returns>
    Task<TextureRef> GetOrCreateFromStreamAsync(
        string name,
        Stream stream,
        bool generateMipmaps = true,
        string? debugName = null
    );

    /// <summary>
    /// Gets or creates a GPU texture by loading a file from the file system asynchronously.
    /// The normalized absolute path is used as the cache key.
    /// </summary>
    /// <param name="filePath">Path to the image file on disk.</param>
    /// <param name="generateMipmaps">When <c>true</c>, automatically generates the full mip chain on the GPU after upload. Defaults to <c>true</c>.</param>
    /// <param name="debugName">
    /// Optional debug name forwarded to the GPU resource.
    /// Defaults to the file name when <c>null</c>.
    /// </param>
    /// <returns>
    /// A <see cref="TextureRef"/> whose <see cref="TextureRef.Key"/> is the normalized absolute path
    /// and whose <see cref="TextureRef.Repository"/> is this repository instance.
    /// </returns>
    Task<TextureRef> GetOrCreateFromFileAsync(string filePath, bool generateMipmaps = true, string? debugName = null);

    /// <summary>
    /// Gets or creates a GPU texture from an already decoded image asynchronously, using <paramref name="name"/> as the cache key.
    /// </summary>
    /// <param name="name">A unique name that identifies this texture in the cache.</param>
    /// <param name="image">The decoded image data.</param>
    /// <param name="generateMipmaps">When <c>true</c>, automatically generates the full mip chain on the GPU after upload. Defaults to <c>true</c>.</param>
    /// <returns>
    /// A <see cref="TextureRef"/> whose <see cref="TextureRef.Key"/> matches <paramref name="name"/>
    /// and whose <see cref="TextureRef.Repository"/> is this repository instance.
    /// </returns>
    Task<TextureRef> GetOrCreateFromImageAsync(string name, Image image, bool generateMipmaps = true);

    /// <summary>
    /// Removes and disposes the texture stored under <paramref name="key"/>.
    /// </summary>
    /// <param name="key">The cache key of the texture to remove.</param>
    /// <returns>
    /// <c>true</c> if the texture was found and removed; <c>false</c> if the key did not exist.
    /// </returns>
    /// <remarks>
    /// After this call, any <see cref="TextureRef"/> previously returned for <paramref name="key"/>
    /// will return an invalid handle on the next <see cref="TextureRef.GetHandle()"/> call.
    /// </remarks>
    bool Remove(string key);

    /// <summary>
    /// Drains the queue of pending GPU mipmap-generation requests, generating mipmaps for each
    /// texture whose asynchronous (or deferred synchronous) upload has completed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Mipmap generation issues an immediate GPU command-buffer submission and therefore must run
    /// on the engine render thread. Texture-creation paths only <em>enqueue</em> the request; the
    /// engine calls this method once per frame on the render thread to perform the actual work.
    /// </para>
    /// <para>
    /// Safe to call when the queue is empty (no-op) and after the underlying context is disposed
    /// (queued requests are discarded).
    /// </para>
    /// </remarks>
    void ProcessPendingMipmapGeneration() { }

    /// <summary>
    /// Returns a task that completes when the queued GPU mipmap generation for the given texture
    /// handle has run on the render thread (via <see cref="ProcessPendingMipmapGeneration"/>).
    /// </summary>
    /// <param name="handle">The texture handle to await mipmap readiness for.</param>
    /// <returns>
    /// A task that completes when mipmaps for <paramref name="handle"/> have been generated. If no
    /// generation is pending for the handle (already generated, or none was requested), a completed
    /// task is returned.
    /// </returns>
    Task WhenMipmapReadyAsync(TextureHandle handle) => Task.CompletedTask;

    /// <summary>
    /// Attempts to retrieve a cached texture entry by its cache key.
    /// </summary>
    /// <param name="cacheKey">The cache key (name or normalized file path).</param>
    /// <param name="entry">The cached texture entry if found. Access the texture via <see cref="TextureCacheEntry.Ref"/>.</param>
    /// <returns><c>true</c> if the texture was found in cache; otherwise, <c>false</c>.</returns>
    bool TryGet(string cacheKey, out TextureCacheEntry? entry);

    /// <summary>
    /// Clears all cached textures and disposes of them.
    /// </summary>
    void Clear();

    /// <summary>
    /// Removes and disposes expired texture entries.
    /// </summary>
    /// <returns>The number of entries removed.</returns>
    int CleanupExpired();

    /// <summary>
    /// Gets cache statistics for monitoring and debugging.
    /// </summary>
    /// <returns>Statistics about the texture repository cache.</returns>
    RepositoryStatistics GetStatistics();
}
