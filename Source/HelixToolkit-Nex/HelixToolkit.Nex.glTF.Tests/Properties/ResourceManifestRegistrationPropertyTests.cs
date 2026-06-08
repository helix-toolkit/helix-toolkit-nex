using HelixToolkit.Nex.Geometries;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Material;
using HelixToolkit.Nex.Repository;
using HelixToolkit.Nex.Shaders.Frag;
using HelixToolkit.Nex.Textures;

namespace HelixToolkit.Nex.glTF.Tests.Properties;

// Feature: gltf-resource-tracking, Property 1: Resource Registration Completeness

/// <summary>
/// Property-based tests for Resource Registration Completeness (Property 1).
/// Verifies that for any valid (non-null, non-sentinel) resource added to a ResourceManifest,
/// the corresponding read-only collection contains that resource, and the corresponding count
/// property equals the number of unique entries in that collection.
/// **Validates: Requirements 1.1, 1.2, 1.3, 1.4, 4.1, 4.2, 4.3, 5.1, 9.1, 9.2, 9.3, 9.4, 9.6**
/// </summary>
[TestClass]
public class ResourceManifestRegistrationPropertyTests
{
    private static readonly Config FsCheckConfig = Config.Default.WithMaxTest(100);

    #region Mock Infrastructure

    /// <summary>
    /// Minimal ITextureRepository stub for creating TextureRef instances in tests.
    /// </summary>
    private sealed class StubTextureRepository : ITextureRepository
    {
        public static readonly StubTextureRepository Instance = new();
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
    /// Minimal ISamplerRepository stub for creating SamplerRef instances in tests.
    /// </summary>
    private sealed class StubSamplerRepository : ISamplerRepository
    {
        public static readonly StubSamplerRepository Instance = new();
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

    #endregion

    /// <summary>
    /// Property 1: For any sequence of AddTexture calls with unique keys,
    /// each added TextureRef appears in the Textures collection and TextureCount
    /// equals the collection length.
    /// **Validates: Requirements 1.1, 9.1, 9.6**
    /// </summary>
    [TestMethod]
    public void AddTexture_ValidResources_AllAppearInCollection_CountMatchesLength()
    {
        var repo = StubTextureRepository.Instance;

        // Generate a list of unique texture keys (1..20 unique keys)
        var keysGen =
            from count in Gen.Choose(1, 20)
            from keys in Gen.ArrayOf(
                Gen.Elements(
                    "tex_a",
                    "tex_b",
                    "tex_c",
                    "tex_d",
                    "tex_e",
                    "tex_f",
                    "tex_g",
                    "tex_h",
                    "tex_i",
                    "tex_j",
                    "tex_k",
                    "tex_l",
                    "tex_m",
                    "tex_n",
                    "tex_o",
                    "tex_p",
                    "tex_q",
                    "tex_r",
                    "tex_s",
                    "tex_t"
                ),
                count
            )
            select keys.Distinct(StringComparer.Ordinal).ToArray();

        Prop.ForAll(
                Arb.From(keysGen),
                (string[] uniqueKeys) =>
                {
                    var manifest = new ResourceManifest();
                    var addedRefs = new List<TextureRef>();

                    foreach (var key in uniqueKeys)
                    {
                        var textureRef = new TextureRef(key, repo, TextureResource.Null);
                        manifest.AddTexture(textureRef);
                        addedRefs.Add(textureRef);
                    }

                    // Assert: each added resource appears in the collection
                    var allPresent = addedRefs.All(r => manifest.Textures.Contains(r));

                    // Assert: count property equals collection length
                    var countMatchesLength = manifest.TextureCount == manifest.Textures.Count;

                    // Assert: count equals number of unique keys added
                    var countMatchesAdded = manifest.TextureCount == uniqueKeys.Length;

                    return allPresent && countMatchesLength && countMatchesAdded;
                }
            )
            .Check(FsCheckConfig);
    }

    /// <summary>
    /// Property 1: For any sequence of AddSampler calls with distinct references,
    /// each added SamplerRef appears in the Samplers collection and SamplerCount
    /// equals the collection length.
    /// **Validates: Requirements 1.2, 9.2, 9.6**
    /// </summary>
    [TestMethod]
    public void AddSampler_ValidResources_AllAppearInCollection_CountMatchesLength()
    {
        var repo = StubSamplerRepository.Instance;

        // Generate a count of distinct samplers to add (1..10)
        var countGen = Gen.Choose(1, 10);

        Prop.ForAll(
                Arb.From(countGen),
                (int count) =>
                {
                    var manifest = new ResourceManifest();
                    var addedRefs = new List<SamplerRef>();

                    // Create distinct SamplerRef instances (each new instance is a unique reference)
                    for (int i = 0; i < count; i++)
                    {
                        var samplerRef = new SamplerRef($"sampler_{i}", repo, SamplerResource.Null);
                        manifest.AddSampler(samplerRef);
                        addedRefs.Add(samplerRef);
                    }

                    // Assert: each added resource appears in the collection
                    var allPresent = addedRefs.All(r => manifest.Samplers.Contains(r));

                    // Assert: count property equals collection length
                    var countMatchesLength = manifest.SamplerCount == manifest.Samplers.Count;

                    // Assert: count equals number of distinct samplers added
                    var countMatchesAdded = manifest.SamplerCount == count;

                    return allPresent && countMatchesLength && countMatchesAdded;
                }
            )
            .Check(FsCheckConfig);
    }

    /// <summary>
    /// Property 1: For any sequence of AddMaterial calls,
    /// each added PBRMaterialProperties appears in the Materials collection and MaterialCount
    /// equals the collection length.
    /// **Validates: Requirements 1.3, 4.1, 4.2, 4.3, 9.3, 9.6**
    /// </summary>
    [TestMethod]
    public void AddMaterial_ValidResources_AllAppearInCollection_CountMatchesLength()
    {
        // Generate a count of materials to add (1..15)
        var countGen = Gen.Choose(1, 15);

        Prop.ForAll(
                Arb.From(countGen),
                (int count) =>
                {
                    using var manager = new PBRMaterialPropertyManager();
                    var manifest = new ResourceManifest();
                    var addedMaterials = new List<PBRMaterialProperties>();

                    for (int i = 0; i < count; i++)
                    {
                        var material = manager.Create(PBRShadingMode.PBR);
                        manifest.AddMaterial(material);
                        addedMaterials.Add(material);
                    }

                    // Assert: each added resource appears in the collection
                    var allPresent = addedMaterials.All(m => manifest.Materials.Contains(m));

                    // Assert: count property equals collection length
                    var countMatchesLength = manifest.MaterialCount == manifest.Materials.Count;

                    // Assert: count equals number of materials added
                    var countMatchesAdded = manifest.MaterialCount == count;

                    // Cleanup
                    foreach (var mat in addedMaterials)
                    {
                        mat.Dispose();
                    }

                    return allPresent && countMatchesLength && countMatchesAdded;
                }
            )
            .Check(FsCheckConfig);
    }

    /// <summary>
    /// Property 1: For any sequence of AddGeometry calls with valid Geometry instances,
    /// each added Geometry appears in the Geometries collection and GeometryCount
    /// equals the collection length.
    /// **Validates: Requirements 1.4, 5.1, 9.4, 9.6**
    /// </summary>
    [TestMethod]
    public void AddGeometry_ValidResources_AllAppearInCollection_CountMatchesLength()
    {
        // Generate a count of geometries to add (1..15)
        var countGen = Gen.Choose(1, 15);

        Prop.ForAll(
                Arb.From(countGen),
                (int count) =>
                {
                    var manifest = new ResourceManifest();
                    var addedGeometries = new List<Geometry>();

                    for (int i = 0; i < count; i++)
                    {
                        var geometry = new Geometry();
                        manifest.AddGeometry(geometry);
                        addedGeometries.Add(geometry);
                    }

                    // Assert: each added resource appears in the collection
                    var allPresent = addedGeometries.All(g => manifest.Geometries.Contains(g));

                    // Assert: count property equals collection length
                    var countMatchesLength = manifest.GeometryCount == manifest.Geometries.Count;

                    // Assert: count equals number of geometries added
                    var countMatchesAdded = manifest.GeometryCount == count;

                    return allPresent && countMatchesLength && countMatchesAdded;
                }
            )
            .Check(FsCheckConfig);
    }

    /// <summary>
    /// Property 1 (combined): For any random sequence of Add calls mixing all resource types,
    /// each added resource appears in its corresponding collection and all count properties
    /// equal their respective collection lengths.
    /// **Validates: Requirements 1.1, 1.2, 1.3, 1.4, 4.1, 4.2, 4.3, 5.1, 9.1, 9.2, 9.3, 9.4, 9.6**
    /// </summary>
    [TestMethod]
    public void MixedAddCalls_AllResourcesAppearInCollections_CountsMatchLengths()
    {
        var textureRepo = StubTextureRepository.Instance;
        var samplerRepo = StubSamplerRepository.Instance;

        // Generate a list of operations: 0=texture, 1=sampler, 2=material, 3=geometry
        var opsGen = Gen.NonEmptyListOf(Gen.Choose(0, 3));

        Prop.ForAll(
                Arb.From(opsGen),
                (List<int> operations) =>
                {
                    using var manager = new PBRMaterialPropertyManager();
                    var manifest = new ResourceManifest();

                    var addedTextures = new List<TextureRef>();
                    var addedSamplers = new List<SamplerRef>();
                    var addedMaterials = new List<PBRMaterialProperties>();
                    var addedGeometries = new List<Geometry>();

                    int textureIdx = 0;
                    int samplerIdx = 0;

                    foreach (var op in operations)
                    {
                        switch (op)
                        {
                            case 0: // Add texture (unique key each time)
                                var texKey = $"tex_{textureIdx++}";
                                var texRef = new TextureRef(
                                    texKey,
                                    textureRepo,
                                    TextureResource.Null
                                );
                                manifest.AddTexture(texRef);
                                addedTextures.Add(texRef);
                                break;

                            case 1: // Add sampler (unique reference each time)
                                var sampRef = new SamplerRef(
                                    $"samp_{samplerIdx++}",
                                    samplerRepo,
                                    SamplerResource.Null
                                );
                                manifest.AddSampler(sampRef);
                                addedSamplers.Add(sampRef);
                                break;

                            case 2: // Add material
                                var material = manager.Create(PBRShadingMode.PBR);
                                manifest.AddMaterial(material);
                                addedMaterials.Add(material);
                                break;

                            case 3: // Add geometry
                                var geometry = new Geometry();
                                manifest.AddGeometry(geometry);
                                addedGeometries.Add(geometry);
                                break;
                        }
                    }

                    // Assert: all textures present
                    var texturesPresent = addedTextures.All(t => manifest.Textures.Contains(t));

                    // Assert: all samplers present
                    var samplersPresent = addedSamplers.All(s => manifest.Samplers.Contains(s));

                    // Assert: all materials present
                    var materialsPresent = addedMaterials.All(m => manifest.Materials.Contains(m));

                    // Assert: all geometries present
                    var geometriesPresent = addedGeometries.All(g =>
                        manifest.Geometries.Contains(g)
                    );

                    // Assert: count properties equal collection lengths
                    var textureCountValid = manifest.TextureCount == manifest.Textures.Count;
                    var samplerCountValid = manifest.SamplerCount == manifest.Samplers.Count;
                    var materialCountValid = manifest.MaterialCount == manifest.Materials.Count;
                    var geometryCountValid = manifest.GeometryCount == manifest.Geometries.Count;

                    // Assert: counts match expected values
                    var textureCountCorrect = manifest.TextureCount == addedTextures.Count;
                    var samplerCountCorrect = manifest.SamplerCount == addedSamplers.Count;
                    var materialCountCorrect = manifest.MaterialCount == addedMaterials.Count;
                    var geometryCountCorrect = manifest.GeometryCount == addedGeometries.Count;

                    // Cleanup materials
                    foreach (var mat in addedMaterials)
                    {
                        mat.Dispose();
                    }

                    return texturesPresent
                        && samplersPresent
                        && materialsPresent
                        && geometriesPresent
                        && textureCountValid
                        && samplerCountValid
                        && materialCountValid
                        && geometryCountValid
                        && textureCountCorrect
                        && samplerCountCorrect
                        && materialCountCorrect
                        && geometryCountCorrect;
                }
            )
            .Check(FsCheckConfig);
    }
}
