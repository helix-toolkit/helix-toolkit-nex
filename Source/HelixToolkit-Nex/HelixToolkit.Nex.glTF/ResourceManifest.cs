using HelixToolkit.Nex.Geometries;
using HelixToolkit.Nex.Material;
using HelixToolkit.Nex.Repository;

namespace HelixToolkit.Nex.glTF;

/// <summary>
/// Aggregates all GPU resources created during a single glTF import operation.
/// Provides bulk disposal for deterministic cleanup when a scene is no longer needed.
/// </summary>
public class ResourceManifest : IDisposable
{
    private readonly List<TextureRef> _textures = [];
    private readonly List<SamplerRef> _samplers = [];
    private readonly List<PBRMaterialProperties> _materials = [];
    private readonly List<Geometry> _geometries = [];

    // Deduplication sets
    private readonly HashSet<string> _textureKeys = new(StringComparer.Ordinal);
    private readonly HashSet<SamplerRef> _samplerRefs = new(ReferenceEqualityComparer.Instance);

    private bool _disposed;

    /// <summary>
    /// The session identifier for this import. Empty string for the Empty sentinel.
    /// </summary>
    public string SessionId { get; }

    /// <summary>
    /// Creates a new <see cref="ResourceManifest"/> with the specified session identifier.
    /// </summary>
    /// <param name="sessionId">A non-null session identifier for this import.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sessionId"/> is null.</exception>
    internal ResourceManifest(string sessionId)
    {
        SessionId = sessionId ?? throw new ArgumentNullException(nameof(sessionId));
    }

    /// <summary>
    /// Creates a new <see cref="ResourceManifest"/> with an auto-generated session identifier (GUID).
    /// Exists to preserve existing call sites during transition.
    /// </summary>
    internal ResourceManifest()
        : this(Guid.NewGuid().ToString("D")) { }

    // --- Read-only accessors ---

    /// <summary>Gets the tracked texture references as a read-only collection.</summary>
    public IReadOnlyList<TextureRef> Textures => _textures;

    /// <summary>Gets the tracked sampler references as a read-only collection.</summary>
    public IReadOnlyList<SamplerRef> Samplers => _samplers;

    /// <summary>Gets the tracked material properties as a read-only collection.</summary>
    public IReadOnlyList<PBRMaterialProperties> Materials => _materials;

    /// <summary>Gets the tracked geometry instances as a read-only collection.</summary>
    public IReadOnlyList<Geometry> Geometries => _geometries;

    // --- Count properties ---

    /// <summary>Gets the number of tracked texture references.</summary>
    public int TextureCount => _textures.Count;

    /// <summary>Gets the number of tracked sampler references.</summary>
    public int SamplerCount => _samplers.Count;

    /// <summary>Gets the number of tracked material properties.</summary>
    public int MaterialCount => _materials.Count;

    /// <summary>Gets the number of tracked geometry instances.</summary>
    public int GeometryCount => _geometries.Count;

    /// <summary>
    /// Returns a task that completes when every tracked texture is fully GPU-ready, including any
    /// mipmap generation that was deferred to the render thread.
    /// </summary>
    /// <remarks>
    /// Base pixel uploads are already complete by the time a texture is tracked (the async upload
    /// path awaits them before returning). This method additionally waits for the render-thread
    /// mipmap pass (driven by the engine each frame via
    /// <see cref="ITextureRepository.ProcessPendingMipmapGeneration"/>). For textures that requested
    /// no mipmaps, or whose mipmaps have already been generated, the corresponding wait is already
    /// complete. Returns a completed task when no textures are tracked.
    /// </remarks>
    public Task WhenTexturesReadyAsync()
    {
        if (_textures.Count == 0)
        {
            return Task.CompletedTask;
        }

        var tasks = new List<Task>(_textures.Count);
        foreach (var texture in _textures)
        {
            tasks.Add(texture.Repository.WhenMipmapReadyAsync(texture.GetHandle()));
        }
        return Task.WhenAll(tasks);
    }

    // --- Registration methods (internal, called by pipeline stages) ---

    /// <summary>
    /// Registers a texture. Deduplicates by <see cref="TextureRef.Key"/>.
    /// Skips <see cref="TextureRef.Null"/>.
    /// </summary>
    internal virtual void AddTexture(TextureRef textureRef)
    {
        if (textureRef == TextureRef.Null)
            return;
        if (_textureKeys.Add(textureRef.Key))
        {
            _textures.Add(textureRef);
        }
    }

    /// <summary>
    /// Registers a sampler. Deduplicates by reference identity.
    /// Skips <see cref="SamplerRef.Null"/>.
    /// </summary>
    internal virtual void AddSampler(SamplerRef samplerRef)
    {
        if (samplerRef == SamplerRef.Null)
            return;
        if (_samplerRefs.Add(samplerRef))
        {
            _samplers.Add(samplerRef);
        }
    }

    /// <summary>
    /// Registers a material. No deduplication — each created instance is tracked.
    /// </summary>
    internal virtual void AddMaterial(PBRMaterialProperties material)
    {
        _materials.Add(material);
    }

    /// <summary>
    /// Registers a geometry. No deduplication — each created instance is tracked.
    /// Skips null.
    /// </summary>
    internal virtual void AddGeometry(Geometry? geometry)
    {
        if (geometry is null)
            return;
        _geometries.Add(geometry);
    }

    // --- Disposal ---

    /// <summary>
    /// Disposes all tracked resources in the correct order:
    /// <list type="number">
    /// <item>Materials (unsubscribes from texture/sampler events)</item>
    /// <item>Geometries (releases GPU buffers)</item>
    /// <item>Textures (removes from repository)</item>
    /// <item>Samplers (removes from repository)</item>
    /// </list>
    /// Idempotent: subsequent calls are no-ops.
    /// </summary>
    public void DisposeAll()
    {
        if (_disposed)
            return;
        _disposed = true;

        // Phase 1: Dispose materials first (they hold event subscriptions to textures/samplers)
        foreach (var material in _materials)
        {
            material.Dispose();
        }

        // Phase 2: Dispose geometries (releases GPU vertex/index buffers)
        foreach (var geometry in _geometries)
        {
            geometry.Dispose();
        }

        // Phase 3: Remove textures from repository (disposes GPU texture resources)
        foreach (var texture in _textures)
        {
            texture.Repository.Remove(texture.Key);
        }

        // Phase 4: Remove samplers from repository (disposes GPU sampler resources)
        foreach (var sampler in _samplers)
        {
            sampler.Repository.Remove(sampler.Key);
        }

        // Clear collections so count properties return 0
        _materials.Clear();
        _geometries.Clear();
        _textures.Clear();
        _samplers.Clear();
        _textureKeys.Clear();
        _samplerRefs.Clear();
    }

    /// <summary>
    /// Disposes all tracked resources. Delegates to <see cref="DisposeAll"/>.
    /// </summary>
    public void Dispose() => DisposeAll();

    /// <summary>
    /// A sentinel empty manifest for failed imports. Add methods are no-ops.
    /// </summary>
    public static readonly ResourceManifest Empty = new EmptyResourceManifest();

    private sealed class EmptyResourceManifest : ResourceManifest
    {
        internal EmptyResourceManifest()
            : base(string.Empty) { }

        internal override void AddTexture(TextureRef _) { }

        internal override void AddSampler(SamplerRef _) { }

        internal override void AddMaterial(PBRMaterialProperties _) { }

        internal override void AddGeometry(Geometry? _) { }
    }
}
