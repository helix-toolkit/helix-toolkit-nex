using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Graphics.Mock;
using HelixToolkit.Nex.Repository;
using HelixToolkit.Nex.Textures;

namespace HelixToolkit.Nex.glTF.Tests.Properties;

// Feature: import-resource-isolation, Property 7: Complete Cleanup After Full Disposal

/// <summary>
/// Property-based tests for Complete Cleanup After Full Disposal (Property 7).
/// Verifies that for any set of N (2..10) ResourceManifest instances that collectively
/// own all entries for a given set of base keys in the repository, disposing all N manifests
/// results in zero repository entries for any of the tracked keys.
/// **Validates: Requirements 4.6**
/// </summary>
[TestClass]
public class CompleteCleanupPropertyTests
{
    private static readonly Config FsCheckConfig = Config.Default.WithMaxTest(100);

    #region Mock Infrastructure

    /// <summary>
    /// A mock ITextureRepository that tracks a set of "live" keys.
    /// Keys are added when textures are created and removed when Remove is called.
    /// </summary>
    private sealed class LiveKeyTextureRepository : ITextureRepository
    {
        private readonly HashSet<string> _liveKeys = new(StringComparer.Ordinal);

        public int Count => _liveKeys.Count;

        /// <summary>
        /// Checks whether a specific key is still live in the repository.
        /// </summary>
        public bool ContainsKey(string key) => _liveKeys.Contains(key);

        /// <summary>
        /// Checks whether any of the specified keys are still live.
        /// </summary>
        public bool ContainsAny(IEnumerable<string> keys) => keys.Any(_liveKeys.Contains);

        /// <summary>
        /// Adds a key to the live set (simulates resource creation).
        /// </summary>
        public void AddKey(string key) => _liveKeys.Add(key);

        public bool Remove(string key) => _liveKeys.Remove(key);

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

        public void Clear() => _liveKeys.Clear();

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
    /// A mock ISamplerRepository that tracks a set of "live" keys.
    /// Keys are added when samplers are created and removed when Remove is called.
    /// </summary>
    private sealed class LiveKeySamplerRepository : ISamplerRepository
    {
        private readonly HashSet<string> _liveKeys = new(StringComparer.Ordinal);

        public int Count => _liveKeys.Count;

        /// <summary>
        /// Checks whether a specific key is still live in the repository.
        /// </summary>
        public bool ContainsKey(string key) => _liveKeys.Contains(key);

        /// <summary>
        /// Checks whether any of the specified keys are still live.
        /// </summary>
        public bool ContainsAny(IEnumerable<string> keys) => keys.Any(_liveKeys.Contains);

        /// <summary>
        /// Adds a key to the live set (simulates resource creation).
        /// </summary>
        public void AddKey(string key) => _liveKeys.Add(key);

        public bool Remove(string key) => _liveKeys.Remove(key);

        public SamplerRef GetOrCreate(string key, SamplerStateDesc desc) => SamplerRef.Null;

        public bool TryGet(string cacheKey, out SamplerModuleCacheEntry? entry)
        {
            entry = null;
            return false;
        }

        public void Clear() => _liveKeys.Clear();

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
    /// Creates a TextureRef with the given key backed by the specified repository.
    /// Also registers the key as "live" in the repository.
    /// </summary>
    private static TextureRef CreateTextureRef(string key, LiveKeyTextureRepository repo)
    {
        repo.AddKey(key);
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
    /// Creates a SamplerRef with the given key backed by the specified repository.
    /// Also registers the key as "live" in the repository.
    /// </summary>
    private static SamplerRef CreateSamplerRef(string key, LiveKeySamplerRepository repo)
    {
        repo.AddKey(key);
        var ctx = new MockContext();
        ctx.Initialize();
        ctx.CreateSampler(new SamplerStateDesc { }, out var sampler);
        return new SamplerRef(key, repo, sampler);
    }

    #endregion

    // -------------------------------------------------------------------------
    // Property 7: Complete Cleanup After Full Disposal
    // Feature: import-resource-isolation, Property 7: Complete Cleanup After Full Disposal
    // Validates: Requirements 4.6
    // -------------------------------------------------------------------------

    /// <summary>
    /// Property 7: For any set of N (2..10) ResourceManifest instances that collectively
    /// own all entries for a given set of base keys in the repository, disposing all N
    /// manifests SHALL result in zero repository entries for any of the keys that were
    /// tracked exclusively by those disposed manifests.
    /// **Validates: Requirements 4.6**
    /// </summary>
    [TestMethod]
    public void Property7_DisposingAllManifests_ClearsAllTrackedKeys()
    {
        var gen =
            from n in Gen.Choose(2, 10)
            from baseKeyCount in Gen.Choose(1, 5)
            from baseKeys in Gen.ListOf<string>(
                Gen.Elements("img_0", "img_1", "tex_path_A", "tex_path_B", "sampler_linear"),
                baseKeyCount
            )
            select (n, baseKeys: baseKeys.Distinct().ToList());

        Prop.ForAll(
                Arb.From(gen),
                ((int n, List<string> baseKeys) input) =>
                {
                    var (n, baseKeys) = input;

                    // Use shared repositories across all manifests (simulates shared GPU repos)
                    var textureRepo = new LiveKeyTextureRepository();
                    var samplerRepo = new LiveKeySamplerRepository();

                    // Generate N distinct session IDs
                    var sessionIds = Enumerable
                        .Range(0, n)
                        .Select(_ => Guid.NewGuid().ToString("D"))
                        .ToList();

                    // Track all keys we create across all manifests
                    var allTextureKeys = new List<string>();
                    var allSamplerKeys = new List<string>();

                    // Create N manifests, each with session-scoped keys for the same base keys
                    var manifests = new List<ResourceManifest>();
                    for (int i = 0; i < n; i++)
                    {
                        var sessionId = sessionIds[i];
                        var manifest = new ResourceManifest(sessionId);

                        // For each base key, create a full session-scoped key
                        foreach (var baseKey in baseKeys)
                        {
                            // Add as texture
                            var textureKey = $"{baseKey}:{sessionId}";
                            allTextureKeys.Add(textureKey);
                            manifest.AddTexture(CreateTextureRef(textureKey, textureRepo));

                            // Add as sampler
                            var samplerKey = $"{baseKey}_sampler:{sessionId}";
                            allSamplerKeys.Add(samplerKey);
                            manifest.AddSampler(CreateSamplerRef(samplerKey, samplerRepo));
                        }

                        manifests.Add(manifest);
                    }

                    // Verify all keys are live before disposal
                    var allKeysPresent =
                        allTextureKeys.All(k => textureRepo.ContainsKey(k))
                        && allSamplerKeys.All(k => samplerRepo.ContainsKey(k));

                    if (!allKeysPresent)
                        return false;

                    // Act: Dispose ALL N manifests
                    foreach (var manifest in manifests)
                    {
                        manifest.DisposeAll();
                    }

                    // Assert: Repository contains zero entries for any of the tracked keys
                    var noTextureKeysRemain = !textureRepo.ContainsAny(allTextureKeys);
                    var noSamplerKeysRemain = !samplerRepo.ContainsAny(allSamplerKeys);
                    var repoTextureCountZero = textureRepo.Count == 0;
                    var repoSamplerCountZero = samplerRepo.Count == 0;

                    return noTextureKeysRemain
                        && noSamplerKeysRemain
                        && repoTextureCountZero
                        && repoSamplerCountZero;
                }
            )
            .Check(FsCheckConfig);
    }
}
