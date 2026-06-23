using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Repository;
using HelixToolkit.Nex.Textures;

namespace HelixToolkit.Nex.glTF.Tests.Mocks;

// Feature: consolidate-gltf-test-mocks (Task 3.1)
//
// Shared, reusable test double for ITextureRepository. This consolidates the several drifted
// `private sealed class StubTextureRepository` variants that were inlined across the test project
// into a single configurable type. The behavior of each variant is preserved exactly via the
// StubTextureRepositoryMode selector (see the preservation oracle in
// MockVariantPreservationPropertyTests for the golden behavior each mode reproduces):
//
//   - Minimal        : Count=0, GetOrCreate*=>TextureRef.Null, Remove=>false, TryGet out=null =>false,
//                      CleanupExpired=>0, empty RepositoryStatistics, no-op Clear/Dispose.
//   - RemoveTracking : as Minimal, but Remove(key) appends to RemovedKeys and returns true.
//   - Instance       : GetOrCreateFrom*(name/filePath) => new TextureRef(name, this,
//                      TextureResource.Null); Remove=>false (use the shared static Instance field).
//
// The Image parameters target the interface's HelixToolkit.Nex.Textures.Image type, so existing
// `NexImage`/`Image` aliases at call sites still bind.

/// <summary>Selects the behavioral variant reproduced by <see cref="StubTextureRepository"/>.</summary>
internal enum StubTextureRepositoryMode
{
    /// <summary>All members return sentinels; <c>Remove</c> returns <c>false</c>.</summary>
    Minimal,

    /// <summary><c>Remove</c> records the key in <see cref="StubTextureRepository.RemovedKeys"/> and returns <c>true</c>.</summary>
    RemoveTracking,

    /// <summary><c>GetOrCreateFrom*</c> create real <see cref="TextureRef"/> values from their key.</summary>
    Instance,
}

/// <summary>
/// Shared configurable <see cref="ITextureRepository"/> stub. The <see cref="StubTextureRepositoryMode"/>
/// passed to the constructor selects which inlined variant's observable behavior is reproduced.
/// </summary>
internal sealed class StubTextureRepository : ITextureRepository
{
    /// <summary>
    /// Shared singleton in <see cref="StubTextureRepositoryMode.Instance"/> mode, replacing the
    /// per-file <c>static readonly StubTextureRepository Instance</c> ref-creating variant.
    /// </summary>
    public static readonly StubTextureRepository Instance = new(StubTextureRepositoryMode.Instance);

    private readonly StubTextureRepositoryMode _mode;

    /// <summary>
    /// Creates a stub in the given <paramref name="mode"/>. Defaults to
    /// <see cref="StubTextureRepositoryMode.Minimal"/> so existing minimal call sites need no argument.
    /// </summary>
    public StubTextureRepository(StubTextureRepositoryMode mode = StubTextureRepositoryMode.Minimal)
    {
        _mode = mode;
    }

    public int Count => 0;

    /// <summary>Keys passed to <see cref="Remove"/> in <see cref="StubTextureRepositoryMode.RemoveTracking"/> mode.</summary>
    public List<string> RemovedKeys { get; } = [];

    public TextureRef GetOrCreateFromStream(
        string name,
        Stream stream,
        bool generateMipmaps = true,
        string? debugName = null
    ) =>
        _mode == StubTextureRepositoryMode.Instance
            ? new TextureRef(name, this, TextureResource.Null)
            : TextureRef.Null;

    public TextureRef GetOrCreateFromFile(
        string filePath,
        bool generateMipmaps = true,
        string? debugName = null
    ) =>
        _mode == StubTextureRepositoryMode.Instance
            ? new TextureRef(filePath, this, TextureResource.Null)
            : TextureRef.Null;

    public TextureRef GetOrCreateFromImage(string name, Image image, bool generateMipmaps = true) =>
        _mode == StubTextureRepositoryMode.Instance
            ? new TextureRef(name, this, TextureResource.Null)
            : TextureRef.Null;

    public Task<TextureRef> GetOrCreateFromStreamAsync(
        string name,
        Stream stream,
        bool generateMipmaps = true,
        string? debugName = null
    ) =>
        Task.FromResult(
            _mode == StubTextureRepositoryMode.Instance
                ? new TextureRef(name, this, TextureResource.Null)
                : TextureRef.Null
        );

    public Task<TextureRef> GetOrCreateFromFileAsync(
        string filePath,
        bool generateMipmaps = true,
        string? debugName = null
    ) =>
        Task.FromResult(
            _mode == StubTextureRepositoryMode.Instance
                ? new TextureRef(filePath, this, TextureResource.Null)
                : TextureRef.Null
        );

    public Task<TextureRef> GetOrCreateFromImageAsync(
        string name,
        Image image,
        bool generateMipmaps = true
    ) =>
        Task.FromResult(
            _mode == StubTextureRepositoryMode.Instance
                ? new TextureRef(name, this, TextureResource.Null)
                : TextureRef.Null
        );

    public bool Remove(string key)
    {
        if (_mode == StubTextureRepositoryMode.RemoveTracking)
        {
            RemovedKeys.Add(key);
            return true;
        }

        return false;
    }

    public bool TryGet(string cacheKey, out TextureCacheEntry? entry)
    {
        entry = null;
        return false;
    }

    public void Clear() { }

    public int CleanupExpired() => 0;

    public RepositoryStatistics GetStatistics() =>
        new()
        {
            TotalEntries = 0,
            MaxEntries = 0,
            TotalHits = 0,
            TotalMisses = 0,
        };

    public void Dispose() { }
}
