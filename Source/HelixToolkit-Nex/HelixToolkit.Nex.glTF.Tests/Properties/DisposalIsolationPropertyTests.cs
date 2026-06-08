using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Graphics.Mock;
using HelixToolkit.Nex.Repository;
using HelixToolkit.Nex.Textures;

namespace HelixToolkit.Nex.glTF.Tests.Properties;

// Feature: import-resource-isolation, Property 6: Disposal Isolation

/// <summary>
/// Property-based tests for Disposal Isolation (Property 6).
/// Verifies that for any two ResourceManifest instances A and B with disjoint key sets
/// (guaranteed by distinct session IDs), calling DisposeAll() on A SHALL call Remove on
/// the repository only for keys tracked in A, leaving all keys tracked in B present in
/// the repository.
/// </summary>
/// <remarks>
/// **Validates: Requirements 4.1, 4.2, 4.3, 4.4**
/// </remarks>
[TestClass]
public class DisposalIsolationPropertyTests
{
    private static readonly Config FsCheckConfig = Config.Default.WithMaxTest(100);

    #region Mock Infrastructure

    /// <summary>
    /// A mock ITextureRepository that tracks which keys have been added and removed.
    /// Allows checking which keys are still "present" after disposal operations.
    /// </summary>
    private sealed class KeyTrackingTextureRepository : ITextureRepository
    {
        private readonly HashSet<string> _presentKeys = new(StringComparer.Ordinal);
        private readonly HashSet<string> _removedKeys = new(StringComparer.Ordinal);

        public int Count => _presentKeys.Count;

        /// <summary>Gets the set of keys that have been removed via Remove calls.</summary>
        public IReadOnlySet<string> RemovedKeys => _removedKeys;

        /// <summary>Adds a key to the set of present keys (simulates repository population).</summary>
        public void AddKey(string key) => _presentKeys.Add(key);

        /// <summary>Checks if a key is still present (not removed).</summary>
        public bool ContainsKey(string key) => _presentKeys.Contains(key);

        public bool Remove(string key)
        {
            _removedKeys.Add(key);
            _presentKeys.Remove(key);
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
    /// A mock ISamplerRepository that tracks which keys have been added and removed.
    /// Allows checking which keys are still "present" after disposal operations.
    /// </summary>
    private sealed class KeyTrackingSamplerRepository : ISamplerRepository
    {
        private readonly HashSet<string> _presentKeys = new(StringComparer.Ordinal);
        private readonly HashSet<string> _removedKeys = new(StringComparer.Ordinal);

        public int Count => _presentKeys.Count;

        /// <summary>Gets the set of keys that have been removed via Remove calls.</summary>
        public IReadOnlySet<string> RemovedKeys => _removedKeys;

        /// <summary>Adds a key to the set of present keys (simulates repository population).</summary>
        public void AddKey(string key) => _presentKeys.Add(key);

        /// <summary>Checks if a key is still present (not removed).</summary>
        public bool ContainsKey(string key) => _presentKeys.Contains(key);

        public bool Remove(string key)
        {
            _removedKeys.Add(key);
            _presentKeys.Remove(key);
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
    /// Creates a TextureRef with the given key backed by the given tracking repository.
    /// </summary>
    private static TextureRef CreateTextureRef(string key, KeyTrackingTextureRepository repo)
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
    /// Creates a SamplerRef with the given key backed by the given tracking repository.
    /// </summary>
    private static SamplerRef CreateSamplerRef(string key, KeyTrackingSamplerRepository repo)
    {
        var ctx = new MockContext();
        ctx.Initialize();
        ctx.CreateSampler(new SamplerStateDesc { }, out var sampler);
        return new SamplerRef(key, repo, sampler);
    }

    #endregion

    // -------------------------------------------------------------------------
    // Property 6: Disposal Isolation
    // Feature: import-resource-isolation, Property 6: Disposal Isolation
    // Validates: Requirements 4.1, 4.2, 4.3, 4.4
    // -------------------------------------------------------------------------

    /// <summary>
    /// Property 6: For any two ResourceManifest instances A and B with disjoint key sets
    /// (guaranteed by distinct session IDs), calling DisposeAll() on A SHALL call Remove
    /// on the repository only for keys tracked in A, leaving all keys tracked in B present
    /// in the repository.
    /// **Validates: Requirements 4.1, 4.2, 4.3, 4.4**
    /// </summary>
    [TestMethod]
    public void Property6_DisposingManifestA_PreservesManifestB_RepositoryEntries()
    {
        var gen =
            from baseKeyCount in Gen.Choose(1, 5)
            from baseKeys in Gen.ListOf(
                Gen.Elements("img0", "img1", "img2", "tex_path", "sampler_linear", "sampler_wrap"),
                baseKeyCount
            )
            from textureKeyCount in Gen.Choose(1, baseKeyCount)
            from samplerKeyCount in Gen.Choose(0, Math.Max(0, baseKeyCount - textureKeyCount))
            select (baseKeys: baseKeys.Distinct().ToList(), textureKeyCount, samplerKeyCount);

        Prop.ForAll(
                Arb.From(gen),
                ((List<string> baseKeys, int textureKeyCount, int samplerKeyCount) input) =>
                {
                    var baseKeys = input.baseKeys;
                    if (baseKeys.Count == 0)
                        return true; // vacuously true

                    // Clamp counts to available base keys
                    var texCount = Math.Min(input.textureKeyCount, baseKeys.Count);
                    var sampCount = Math.Min(input.samplerKeyCount, baseKeys.Count - texCount);

                    // Generate two distinct session IDs
                    var sessionA = Guid.NewGuid().ToString("D");
                    var sessionB = Guid.NewGuid().ToString("D");

                    // Create session-scoped keys (disjoint because session IDs differ)
                    var textureBaseKeys = baseKeys.Take(texCount).ToList();
                    var samplerBaseKeys = baseKeys.Skip(texCount).Take(sampCount).ToList();

                    var textureKeysA = textureBaseKeys.Select(k => $"{k}:{sessionA}").ToList();
                    var textureKeysB = textureBaseKeys.Select(k => $"{k}:{sessionB}").ToList();
                    var samplerKeysA = samplerBaseKeys.Select(k => $"{k}:{sessionA}").ToList();
                    var samplerKeysB = samplerBaseKeys.Select(k => $"{k}:{sessionB}").ToList();

                    // Create shared mock repositories and populate with all keys
                    var textureRepo = new KeyTrackingTextureRepository();
                    var samplerRepo = new KeyTrackingSamplerRepository();

                    foreach (var key in textureKeysA.Concat(textureKeysB))
                        textureRepo.AddKey(key);
                    foreach (var key in samplerKeysA.Concat(samplerKeysB))
                        samplerRepo.AddKey(key);

                    // Create manifest A with session-scoped keys
                    var manifestA = new ResourceManifest(sessionA);
                    foreach (var key in textureKeysA)
                        manifestA.AddTexture(CreateTextureRef(key, textureRepo));
                    foreach (var key in samplerKeysA)
                        manifestA.AddSampler(CreateSamplerRef(key, samplerRepo));

                    // Create manifest B with session-scoped keys
                    var manifestB = new ResourceManifest(sessionB);
                    foreach (var key in textureKeysB)
                        manifestB.AddTexture(CreateTextureRef(key, textureRepo));
                    foreach (var key in samplerKeysB)
                        manifestB.AddSampler(CreateSamplerRef(key, samplerRepo));

                    // Act: Dispose manifest A only
                    manifestA.DisposeAll();

                    // Assert: Only A's texture keys were removed
                    bool allATextureKeysRemoved = textureKeysA.All(k =>
                        textureRepo.RemovedKeys.Contains(k)
                    );

                    // Assert: Only A's sampler keys were removed
                    bool allASamplerKeysRemoved = samplerKeysA.All(k =>
                        samplerRepo.RemovedKeys.Contains(k)
                    );

                    // Assert: None of B's texture keys were removed
                    bool noBTextureKeysRemoved = textureKeysB.All(k =>
                        !textureRepo.RemovedKeys.Contains(k)
                    );

                    // Assert: None of B's sampler keys were removed
                    bool noBSamplerKeysRemoved = samplerKeysB.All(k =>
                        !samplerRepo.RemovedKeys.Contains(k)
                    );

                    // Assert: All of B's keys are still present in the repositories
                    bool allBTextureKeysPresent = textureKeysB.All(k => textureRepo.ContainsKey(k));
                    bool allBSamplerKeysPresent = samplerKeysB.All(k => samplerRepo.ContainsKey(k));

                    return allATextureKeysRemoved
                        && allASamplerKeysRemoved
                        && noBTextureKeysRemoved
                        && noBSamplerKeysRemoved
                        && allBTextureKeysPresent
                        && allBSamplerKeysPresent;
                }
            )
            .Check(FsCheckConfig);
    }
}
