using HelixToolkit.Nex.ECS;
using HelixToolkit.Nex.Geometries;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Graphics.Mock;
using HelixToolkit.Nex.Material;
using HelixToolkit.Nex.Repository;
using HelixToolkit.Nex.Scene;
using HelixToolkit.Nex.Textures;

namespace HelixToolkit.Nex.glTF.Tests.Properties;

// Feature: gltf-resource-tracking, Property 7: ImportResult.Dispose Delegates Correctly

/// <summary>
/// Property-based tests for ImportResult.Dispose delegation behavior.
/// Verifies that for any ImportResult with a non-null RootNode and a non-empty ResourceManifest,
/// calling Dispose() disposes the RootNode (recursively disposing all children) and calls
/// DisposeAll() on the ResourceManifest, releasing all tracked GPU resources.
/// </summary>
/// <remarks>
/// **Validates: Requirements 7.2, 7.3**
/// </remarks>
[TestClass]
public class ImportResultDisposalPropertyTests
{
    private static readonly Config FsCheckConfig = Config.Default.WithMaxTest(100);

    #region Mock Infrastructure

    /// <summary>
    /// A mock ITextureRepository that tracks Remove call counts.
    /// </summary>
    private sealed class TrackingTextureRepository : ITextureRepository
    {
        public int RemoveCallCount { get; private set; }

        public int Count => 0;

        public bool Remove(string key)
        {
            RemoveCallCount++;
            return true;
        }

        public TextureRef GetOrCreateFromStream(
            string name,
            Stream stream,
            bool generateMipmaps = true,
            string? debugName = null
        ) => TextureRef.Null;

        public TextureRef GetOrCreateFromFile(
            string filePath,
            bool generateMipmaps = true,
            string? debugName = null
        ) => TextureRef.Null;

        public TextureRef GetOrCreateFromImage(
            string name,
            Image image,
            bool generateMipmaps = true
        ) => TextureRef.Null;

        public Task<TextureRef> GetOrCreateFromStreamAsync(
            string name,
            Stream stream,
            bool generateMipmaps = true,
            string? debugName = null
        ) => Task.FromResult(TextureRef.Null);

        public Task<TextureRef> GetOrCreateFromFileAsync(
            string filePath,
            bool generateMipmaps = true,
            string? debugName = null
        ) => Task.FromResult(TextureRef.Null);

        public Task<TextureRef> GetOrCreateFromImageAsync(
            string name,
            Image image,
            bool generateMipmaps = true
        ) => Task.FromResult(TextureRef.Null);

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

    /// <summary>
    /// A mock ISamplerRepository that tracks Remove call counts.
    /// </summary>
    private sealed class TrackingSamplerRepository : ISamplerRepository
    {
        public int RemoveCallCount { get; private set; }

        public int Count => 0;

        public bool Remove(string key)
        {
            RemoveCallCount++;
            return true;
        }

        public SamplerRef GetOrCreate(string key, SamplerStateDesc desc) => SamplerRef.Null;

        public bool TryGet(string cacheKey, out SamplerModuleCacheEntry? entry)
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

    #endregion

    #region Helpers

    /// <summary>
    /// Creates a valid TextureRef with a unique key backed by the given tracking repository.
    /// </summary>
    private static TextureRef CreateTextureRef(string key, TrackingTextureRepository repo)
    {
        var ctx = new MockContext();
        ctx.Initialize();
        ctx.CreateTexture(
            new TextureDesc
            {
                Type = TextureType.Texture2D,
                Format = Format.RGBA_UN8,
                Dimensions = new Dimensions(1, 1, 1),
                NumMipLevels = 1,
                NumLayers = 1,
            },
            out var tex,
            key
        );
        return new TextureRef(key, repo, tex);
    }

    /// <summary>
    /// Creates a valid SamplerRef with a unique key backed by the given tracking repository.
    /// </summary>
    private static SamplerRef CreateSamplerRef(string key, TrackingSamplerRepository repo)
    {
        var ctx = new MockContext();
        ctx.Initialize();
        ctx.CreateSampler(new SamplerStateDesc { }, out var sampler);
        return new SamplerRef(key, repo, sampler);
    }

    #endregion

    // -------------------------------------------------------------------------
    // Property 7: ImportResult.Dispose Delegates Correctly
    // Feature: gltf-resource-tracking, Property 7: ImportResult.Dispose Delegates Correctly
    // Validates: Requirements 7.2, 7.3
    // -------------------------------------------------------------------------

    /// <summary>
    /// Property 7: For any ImportResult with a non-null RootNode and a non-empty ResourceManifest,
    /// calling Dispose() disposes the RootNode and calls DisposeAll on the manifest.
    /// **Validates: Requirements 7.2, 7.3**
    /// </summary>
    [TestMethod]
    public void Property7_Dispose_DisposesRootNodeAndManifest()
    {
        // Generate varying resource counts and whether RootNode is present
        var gen =
            from hasRootNode in Gen.Elements(true, false)
            from textureCount in Gen.Choose(0, 3)
            from samplerCount in Gen.Choose(0, 3)
            from materialCount in Gen.Choose(0, 3)
            from geometryCount in Gen.Choose(0, 3)
            from childCount in Gen.Choose(0, 3)
            select (
                hasRootNode,
                textureCount,
                samplerCount,
                materialCount,
                geometryCount,
                childCount
            );

        Prop.ForAll(
                Arb.From(gen),
                (
                    (
                        bool hasRootNode,
                        int textureCount,
                        int samplerCount,
                        int materialCount,
                        int geometryCount,
                        int childCount
                    ) input
                ) =>
                {
                    var (
                        hasRootNode,
                        textureCount,
                        samplerCount,
                        materialCount,
                        geometryCount,
                        childCount
                    ) = input;

                    var textureRepo = new TrackingTextureRepository();
                    var samplerRepo = new TrackingSamplerRepository();
                    using var materialManager = new PBRMaterialPropertyManager();

                    // Build the ResourceManifest with generated resources
                    var manifest = new ResourceManifest();

                    for (int i = 0; i < textureCount; i++)
                    {
                        manifest.AddTexture(CreateTextureRef($"tex_{i}", textureRepo));
                    }

                    for (int i = 0; i < samplerCount; i++)
                    {
                        manifest.AddSampler(CreateSamplerRef($"samp_{i}", samplerRepo));
                    }

                    for (int i = 0; i < materialCount; i++)
                    {
                        manifest.AddMaterial(
                            materialManager.Create(Shaders.Frag.PBRShadingMode.PBR.ToString())
                        );
                    }

                    for (int i = 0; i < geometryCount; i++)
                    {
                        manifest.AddGeometry(new Geometry());
                    }

                    // Build the RootNode (or null)
                    World? world = null;
                    Node? rootNode = null;
                    var childNodes = new List<Node>();

                    if (hasRootNode)
                    {
                        world = World.CreateWorld();
                        rootNode = new Node(world, "Root");

                        // Add child nodes to verify recursive disposal
                        for (int i = 0; i < childCount; i++)
                        {
                            var child = new Node(world, $"Child_{i}");
                            rootNode.AddChild(child);
                            childNodes.Add(child);
                        }
                    }

                    // Create ImportResult
                    var importResult = new ImportResult
                    {
                        RootNode = rootNode,
                        Resources = manifest,
                    };

                    // Act: Dispose the ImportResult
                    importResult.Dispose();

                    // Assert: ResourceManifest was disposed (all counts zero)
                    bool manifestDisposed =
                        manifest.TextureCount == 0
                        && manifest.SamplerCount == 0
                        && manifest.MaterialCount == 0
                        && manifest.GeometryCount == 0;

                    // Assert: Repository Remove was called for each texture and sampler
                    bool textureRemoveCorrect = textureRepo.RemoveCallCount == textureCount;
                    bool samplerRemoveCorrect = samplerRepo.RemoveCallCount == samplerCount;

                    // Assert: RootNode was disposed (if it existed)
                    bool rootNodeDisposed;
                    if (hasRootNode)
                    {
                        // After disposal, the node's entity is no longer alive in the world
                        rootNodeDisposed = !rootNode!.Alive;

                        // All children should also be disposed (recursive disposal)
                        bool childrenDisposed = childNodes.All(c => !c.Alive);
                        rootNodeDisposed = rootNodeDisposed && childrenDisposed;
                    }
                    else
                    {
                        // No RootNode — no exception should have been thrown (null-safe)
                        rootNodeDisposed = true;
                    }

                    // Cleanup: dispose the world if we created one
                    world?.Dispose();

                    return manifestDisposed
                        && textureRemoveCorrect
                        && samplerRemoveCorrect
                        && rootNodeDisposed;
                }
            )
            .Check(FsCheckConfig);
    }

    /// <summary>
    /// Property 7 (null RootNode variant): For any ImportResult with a null RootNode,
    /// calling Dispose() does not throw and still calls DisposeAll on the manifest.
    /// **Validates: Requirements 7.2, 7.3**
    /// </summary>
    [TestMethod]
    public void Property7_Dispose_NullRootNode_StillDisposesManifest()
    {
        // Generate varying resource counts with null RootNode
        var gen =
            from textureCount in Gen.Choose(0, 5)
            from samplerCount in Gen.Choose(0, 5)
            from materialCount in Gen.Choose(0, 5)
            from geometryCount in Gen.Choose(0, 5)
            select (textureCount, samplerCount, materialCount, geometryCount);

        Prop.ForAll(
                Arb.From(gen),
                (
                    (int textureCount, int samplerCount, int materialCount, int geometryCount) input
                ) =>
                {
                    var (textureCount, samplerCount, materialCount, geometryCount) = input;

                    var textureRepo = new TrackingTextureRepository();
                    var samplerRepo = new TrackingSamplerRepository();
                    using var materialManager = new PBRMaterialPropertyManager();

                    var manifest = new ResourceManifest();

                    for (int i = 0; i < textureCount; i++)
                    {
                        manifest.AddTexture(CreateTextureRef($"tex_{i}", textureRepo));
                    }

                    for (int i = 0; i < samplerCount; i++)
                    {
                        manifest.AddSampler(CreateSamplerRef($"samp_{i}", samplerRepo));
                    }

                    for (int i = 0; i < materialCount; i++)
                    {
                        manifest.AddMaterial(
                            materialManager.Create(Shaders.Frag.PBRShadingMode.PBR.ToString())
                        );
                    }

                    for (int i = 0; i < geometryCount; i++)
                    {
                        manifest.AddGeometry(new Geometry());
                    }

                    // Create ImportResult with null RootNode
                    var importResult = new ImportResult { RootNode = null, Resources = manifest };

                    // Act: Dispose should not throw
                    importResult.Dispose();

                    // Assert: Manifest was still disposed
                    bool manifestDisposed =
                        manifest.TextureCount == 0
                        && manifest.SamplerCount == 0
                        && manifest.MaterialCount == 0
                        && manifest.GeometryCount == 0;

                    bool textureRemoveCorrect = textureRepo.RemoveCallCount == textureCount;
                    bool samplerRemoveCorrect = samplerRepo.RemoveCallCount == samplerCount;

                    return manifestDisposed && textureRemoveCorrect && samplerRemoveCorrect;
                }
            )
            .Check(FsCheckConfig);
    }
}
