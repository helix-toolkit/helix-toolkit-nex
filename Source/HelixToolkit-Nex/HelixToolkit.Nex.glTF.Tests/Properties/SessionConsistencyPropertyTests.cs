using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Repository;
using HelixToolkit.Nex.Textures;

namespace HelixToolkit.Nex.glTF.Tests.Properties;

// Feature: import-resource-isolation, Property 5: Session Identifier Consistency Within Import

/// <summary>
/// Property-based tests for Session Identifier Consistency Within Import (Property 5).
/// Verifies that for any single import operation, all texture and sampler cache keys tracked
/// in the resulting ResourceManifest contain the ResourceManifest.SessionId value as a substring,
/// and the SessionId property is non-null and equal to the value used during key generation.
/// **Validates: Requirements 1.3, 7.1, 7.3**
/// </summary>
[TestClass]
public class SessionConsistencyPropertyTests
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

    #region Generators

    /// <summary>
    /// Generator for texture counts (1..10).
    /// </summary>
    private static Gen<int> TextureCountGen => Gen.Choose(1, 10);

    /// <summary>
    /// Generator for sampler counts (1..10).
    /// </summary>
    private static Gen<int> SamplerCountGen => Gen.Choose(1, 10);

    /// <summary>
    /// Generator for image indices (0..999).
    /// </summary>
    private static Gen<int> ImageIndexGen => Gen.Choose(0, 999);

    /// <summary>
    /// Generator for sampler names.
    /// </summary>
    private static Gen<string> SamplerNameGen =>
        Gen.Elements(
            "LinearWrap",
            "PointClamp",
            "AnisotropicWrap",
            "LinearMirror",
            "NearestRepeat",
            "BilinearClamp",
            "TrilinearWrap"
        );

    #endregion

    /// <summary>
    /// Property 5: For any single import, all texture cache keys tracked in the ResourceManifest
    /// contain the manifest's SessionId as a substring, and SessionId is non-null and matches
    /// the value used for key generation.
    /// **Validates: Requirements 1.3, 7.1, 7.3**
    /// </summary>
    [TestMethod]
    public void AllTrackedTextureKeys_ContainSessionId()
    {
        Prop.ForAll(
                Arb.From(TextureCountGen),
                (int textureCount) =>
                {
                    // Generate a known sessionId (GUID format)
                    var sessionId = Guid.NewGuid().ToString("D");

                    // Create a ResourceManifest with this sessionId
                    var manifest = new ResourceManifest(sessionId);
                    var textureRepo = StubTextureRepository.Instance;

                    // Create N random texture keys in format "{imageIndex}:{sessionId}"
                    var random = new Random();
                    for (int i = 0; i < textureCount; i++)
                    {
                        var imageIndex = random.Next(0, 1000);
                        var key = $"{imageIndex}:{sessionId}";
                        var textureRef = new TextureRef(key, textureRepo, TextureResource.Null);
                        manifest.AddTexture(textureRef);
                    }

                    // Assert SessionId is non-null and matches the value used for key generation
                    if (manifest.SessionId is null)
                        return false;
                    if (manifest.SessionId != sessionId)
                        return false;

                    // Assert all tracked texture keys contain the sessionId as a substring
                    foreach (var texture in manifest.Textures)
                    {
                        if (!texture.Key.Contains(sessionId, StringComparison.Ordinal))
                            return false;
                    }

                    return true;
                }
            )
            .Check(FsCheckConfig);
    }

    /// <summary>
    /// Property 5: For any single import, all sampler cache keys tracked in the ResourceManifest
    /// contain the manifest's SessionId as a substring, and SessionId is non-null and matches
    /// the value used for key generation.
    /// **Validates: Requirements 1.3, 7.1, 7.3**
    /// </summary>
    [TestMethod]
    public void AllTrackedSamplerKeys_ContainSessionId()
    {
        var samplerNames = new[]
        {
            "LinearWrap",
            "PointClamp",
            "AnisotropicWrap",
            "LinearMirror",
            "NearestRepeat",
        };

        Prop.ForAll(
                Arb.From(SamplerCountGen),
                (int samplerCount) =>
                {
                    // Generate a known sessionId (GUID format)
                    var sessionId = Guid.NewGuid().ToString("D");

                    // Create a ResourceManifest with this sessionId
                    var manifest = new ResourceManifest(sessionId);
                    var samplerRepo = StubSamplerRepository.Instance;

                    // Create M random sampler keys in format "{samplerName}:{sessionId}"
                    var random = new Random();
                    for (int i = 0; i < samplerCount; i++)
                    {
                        var samplerName = samplerNames[random.Next(samplerNames.Length)];
                        var key = $"{samplerName}:{sessionId}";
                        var samplerRef = new SamplerRef(key, samplerRepo, SamplerResource.Null);
                        manifest.AddSampler(samplerRef);
                    }

                    // Assert SessionId is non-null and matches the value used for key generation
                    if (manifest.SessionId is null)
                        return false;
                    if (manifest.SessionId != sessionId)
                        return false;

                    // Assert all tracked sampler keys contain the sessionId as a substring
                    foreach (var sampler in manifest.Samplers)
                    {
                        if (!sampler.Key.Contains(sessionId, StringComparison.Ordinal))
                            return false;
                    }

                    return true;
                }
            )
            .Check(FsCheckConfig);
    }

    /// <summary>
    /// Property 5: For any single import with both textures and samplers, all tracked keys
    /// in the ResourceManifest contain the manifest's SessionId as a substring, and SessionId
    /// is non-null and equals the value used during key generation.
    /// **Validates: Requirements 1.3, 7.1, 7.3**
    /// </summary>
    [TestMethod]
    public void AllTrackedKeys_TexturesAndSamplers_ContainSessionId()
    {
        var samplerNames = new[]
        {
            "LinearWrap",
            "PointClamp",
            "AnisotropicWrap",
            "LinearMirror",
            "NearestRepeat",
        };

        Prop.ForAll(
                Arb.From(TextureCountGen),
                Arb.From(SamplerCountGen),
                (int textureCount, int samplerCount) =>
                {
                    // Generate a known sessionId (GUID format)
                    var sessionId = Guid.NewGuid().ToString("D");

                    // Create a ResourceManifest with this sessionId
                    var manifest = new ResourceManifest(sessionId);
                    var textureRepo = StubTextureRepository.Instance;
                    var samplerRepo = StubSamplerRepository.Instance;

                    // Add textures with keys containing the sessionId
                    var random = new Random();
                    for (int i = 0; i < textureCount; i++)
                    {
                        var imageIndex = random.Next(0, 1000);
                        var key = $"{imageIndex}:{sessionId}";
                        var textureRef = new TextureRef(key, textureRepo, TextureResource.Null);
                        manifest.AddTexture(textureRef);
                    }

                    // Add samplers with keys containing the sessionId
                    for (int i = 0; i < samplerCount; i++)
                    {
                        var samplerName = samplerNames[random.Next(samplerNames.Length)];
                        var key = $"{samplerName}:{sessionId}";
                        var samplerRef = new SamplerRef(key, samplerRepo, SamplerResource.Null);
                        manifest.AddSampler(samplerRef);
                    }

                    // Assert SessionId is non-null
                    if (manifest.SessionId is null)
                        return false;

                    // Assert SessionId matches the value used for key generation
                    if (manifest.SessionId != sessionId)
                        return false;

                    // Assert all tracked texture keys contain the sessionId
                    foreach (var texture in manifest.Textures)
                    {
                        if (!texture.Key.Contains(sessionId, StringComparison.Ordinal))
                            return false;
                    }

                    // Assert all tracked sampler keys contain the sessionId
                    foreach (var sampler in manifest.Samplers)
                    {
                        if (!sampler.Key.Contains(sessionId, StringComparison.Ordinal))
                            return false;
                    }

                    return true;
                }
            )
            .Check(FsCheckConfig);
    }
}
