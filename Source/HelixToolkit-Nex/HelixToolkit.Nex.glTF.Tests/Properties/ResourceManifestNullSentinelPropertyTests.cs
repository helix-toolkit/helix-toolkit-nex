using HelixToolkit.Nex.Geometries;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Repository;
using HelixToolkit.Nex.Textures;

namespace HelixToolkit.Nex.glTF.Tests.Properties;

// Feature: gltf-resource-tracking, Property 3: Null Sentinel Exclusion

/// <summary>
/// Property-based tests for Null Sentinel Exclusion (Property 3).
/// Verifies that ResourceManifest collections never contain TextureRef.Null,
/// SamplerRef.Null, or null geometry references regardless of the input sequence.
/// **Validates: Requirements 2.4, 3.3, 5.3**
/// </summary>
[TestClass]
public class ResourceManifestNullSentinelPropertyTests
{
    private static readonly Config FsCheckConfig = Config.Default.WithMaxTest(100);

    /// <summary>
    /// A minimal mock ITextureRepository for creating valid TextureRef instances.
    /// </summary>
    private sealed class StubTextureRepository : ITextureRepository
    {
        public int Count => 0;

        public TextureRef GetOrCreateFromStream(
            string name,
            Stream stream,
            bool generateMipmaps = true,
            string? debugName = null
        ) => new TextureRef(name, this, TextureResource.Null);

        public TextureRef GetOrCreateFromFile(
            string filePath,
            bool generateMipmaps = true,
            string? debugName = null
        ) => new TextureRef(filePath, this, TextureResource.Null);

        public TextureRef GetOrCreateFromImage(
            string name,
            Image image,
            bool generateMipmaps = true
        ) => new TextureRef(name, this, TextureResource.Null);

        public Task<TextureRef> GetOrCreateFromStreamAsync(
            string name,
            Stream stream,
            bool generateMipmaps = true,
            string? debugName = null
        ) => Task.FromResult(new TextureRef(name, this, TextureResource.Null));

        public Task<TextureRef> GetOrCreateFromFileAsync(
            string filePath,
            bool generateMipmaps = true,
            string? debugName = null
        ) => Task.FromResult(new TextureRef(filePath, this, TextureResource.Null));

        public Task<TextureRef> GetOrCreateFromImageAsync(
            string name,
            Image image,
            bool generateMipmaps = true
        ) => Task.FromResult(new TextureRef(name, this, TextureResource.Null));

        public bool Remove(string key) => false;

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
    /// A minimal mock ISamplerRepository for creating valid SamplerRef instances.
    /// </summary>
    private sealed class StubSamplerRepository : ISamplerRepository
    {
        public int Count => 0;

        public SamplerRef GetOrCreate(string key, SamplerStateDesc desc) =>
            new SamplerRef(key, this, SamplerResource.Null);

        public bool Remove(string key) => false;

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

    /// <summary>
    /// Property 3: For any sequence of Add operations that mixes valid resources with
    /// TextureRef.Null, SamplerRef.Null, and null geometries, the manifest collections
    /// SHALL never contain these sentinel/null values, and the count properties SHALL
    /// not reflect them.
    /// **Validates: Requirements 2.4, 3.3, 5.3**
    /// </summary>
    [TestMethod]
    public void NullSentinels_NeverAppearInTextureCollection()
    {
        var stubRepo = new StubTextureRepository();

        // Generate a sequence of booleans: true = add valid texture, false = add TextureRef.Null
        var inputGen = Gen.ListOf(Gen.Elements(true, false));

        Prop.ForAll(
                Arb.From(inputGen),
                (List<bool> sequence) =>
                {
                    var manifest = new ResourceManifest();
                    int validCount = 0;

                    foreach (var isValid in sequence)
                    {
                        if (isValid)
                        {
                            var key = $"texture_{validCount}";
                            var textureRef = new TextureRef(key, stubRepo, TextureResource.Null);
                            manifest.AddTexture(textureRef);
                            validCount++;
                        }
                        else
                        {
                            manifest.AddTexture(TextureRef.Null);
                        }
                    }

                    // Assert: TextureRef.Null never appears in the collection
                    var containsNull = manifest.Textures.Any(t => t == TextureRef.Null);
                    var countMatchesCollection = manifest.TextureCount == manifest.Textures.Count;

                    return !containsNull && countMatchesCollection;
                }
            )
            .Check(FsCheckConfig);
    }

    /// <summary>
    /// Property 3: For any sequence of Add operations that mixes valid SamplerRef with
    /// SamplerRef.Null, the manifest Samplers collection SHALL never contain SamplerRef.Null.
    /// **Validates: Requirements 3.3**
    /// </summary>
    [TestMethod]
    public void NullSentinels_NeverAppearInSamplerCollection()
    {
        var stubRepo = new StubSamplerRepository();

        // Generate a sequence of booleans: true = add valid sampler, false = add SamplerRef.Null
        var inputGen = Gen.ListOf(Gen.Elements(true, false));

        Prop.ForAll(
                Arb.From(inputGen),
                (List<bool> sequence) =>
                {
                    var manifest = new ResourceManifest();
                    int validCount = 0;

                    foreach (var isValid in sequence)
                    {
                        if (isValid)
                        {
                            var key = $"sampler_{validCount}";
                            var samplerRef = new SamplerRef(key, stubRepo, SamplerResource.Null);
                            manifest.AddSampler(samplerRef);
                            validCount++;
                        }
                        else
                        {
                            manifest.AddSampler(SamplerRef.Null);
                        }
                    }

                    // Assert: SamplerRef.Null never appears in the collection
                    var containsNull = manifest.Samplers.Any(s => s == SamplerRef.Null);
                    var countMatchesCollection = manifest.SamplerCount == manifest.Samplers.Count;

                    return !containsNull && countMatchesCollection;
                }
            )
            .Check(FsCheckConfig);
    }

    /// <summary>
    /// Property 3: For any sequence of Add operations that mixes valid Geometry with null,
    /// the manifest Geometries collection SHALL never contain null.
    /// **Validates: Requirements 5.3**
    /// </summary>
    [TestMethod]
    public void NullGeometries_NeverAppearInGeometryCollection()
    {
        // Generate a sequence of booleans: true = add valid geometry, false = add null
        var inputGen = Gen.ListOf(Gen.Elements(true, false));

        Prop.ForAll(
                Arb.From(inputGen),
                (List<bool> sequence) =>
                {
                    var manifest = new ResourceManifest();
                    int validCount = 0;

                    foreach (var isValid in sequence)
                    {
                        if (isValid)
                        {
                            var geometry = new Geometry();
                            manifest.AddGeometry(geometry);
                            validCount++;
                        }
                        else
                        {
                            manifest.AddGeometry(null);
                        }
                    }

                    // Assert: null never appears in the collection
                    var containsNull = manifest.Geometries.Any(g => g is null);
                    var countMatchesCollection =
                        manifest.GeometryCount == manifest.Geometries.Count;

                    return !containsNull && countMatchesCollection;
                }
            )
            .Check(FsCheckConfig);
    }

    /// <summary>
    /// Property 3 (combined): For any mixed sequence of Add operations including all resource
    /// types with their null/sentinel counterparts, no collection ever contains sentinel/null values.
    /// **Validates: Requirements 2.4, 3.3, 5.3**
    /// </summary>
    [TestMethod]
    public void MixedSequence_NullSentinels_NeverAppearInAnyCollection()
    {
        var stubTextureRepo = new StubTextureRepository();
        var stubSamplerRepo = new StubSamplerRepository();

        // Generate a list of operations: 0=valid texture, 1=null texture, 2=valid sampler,
        // 3=null sampler, 4=valid geometry, 5=null geometry
        var inputGen = Gen.ListOf(Gen.Choose(0, 5));

        Prop.ForAll(
                Arb.From(inputGen),
                (List<int> operations) =>
                {
                    var manifest = new ResourceManifest();
                    int textureIdx = 0;
                    int samplerIdx = 0;

                    foreach (var op in operations)
                    {
                        switch (op)
                        {
                            case 0: // valid texture
                                var texKey = $"tex_{textureIdx++}";
                                manifest.AddTexture(
                                    new TextureRef(texKey, stubTextureRepo, TextureResource.Null)
                                );
                                break;
                            case 1: // TextureRef.Null
                                manifest.AddTexture(TextureRef.Null);
                                break;
                            case 2: // valid sampler
                                var sampKey = $"samp_{samplerIdx++}";
                                manifest.AddSampler(
                                    new SamplerRef(sampKey, stubSamplerRepo, SamplerResource.Null)
                                );
                                break;
                            case 3: // SamplerRef.Null
                                manifest.AddSampler(SamplerRef.Null);
                                break;
                            case 4: // valid geometry
                                manifest.AddGeometry(new Geometry());
                                break;
                            case 5: // null geometry
                                manifest.AddGeometry(null);
                                break;
                        }
                    }

                    // Assert: no collection contains sentinel/null values
                    var texturesClean = !manifest.Textures.Any(t => t == TextureRef.Null);
                    var samplersClean = !manifest.Samplers.Any(s => s == SamplerRef.Null);
                    var geometriesClean = !manifest.Geometries.Any(g => g is null);

                    // Assert: counts match collection lengths
                    var textureCountValid = manifest.TextureCount == manifest.Textures.Count;
                    var samplerCountValid = manifest.SamplerCount == manifest.Samplers.Count;
                    var geometryCountValid = manifest.GeometryCount == manifest.Geometries.Count;

                    return texturesClean
                        && samplersClean
                        && geometriesClean
                        && textureCountValid
                        && samplerCountValid
                        && geometryCountValid;
                }
            )
            .Check(FsCheckConfig);
    }
}
