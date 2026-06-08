using glTFLoader.Schema;
using HelixToolkit.Nex.glTF.Internal;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Repository;
using GltfTexture = glTFLoader.Schema.Texture;
using NexImage = HelixToolkit.Nex.Textures.Image;

namespace HelixToolkit.Nex.glTF.Tests.Properties;

// Feature: gltf-importer, Property 11: Texture deduplication

/// <summary>
/// Property-based tests for texture deduplication (Property 11).
/// Verifies that when N materials reference the same glTF texture index,
/// the ITextureRepository handles caching and all callers receive the same TextureRef instance.
/// **Validates: Requirements 5.8**
/// </summary>
[TestClass]
public class TexturePropertyTests
{
    private static readonly Config FsCheckConfig = Config.Default.WithMaxTest(100);

    /// <summary>
    /// A mock ITextureRepository that tracks calls to GetOrCreateFromStream
    /// and returns the same TextureRef for the same cache key (simulating caching behavior).
    /// </summary>
    private sealed class MockTextureRepository : ITextureRepository
    {
        private readonly Dictionary<string, TextureRef> _cache = new();
        private int _getOrCreateFromStreamCallCount;

        /// <summary>Gets the total number of times GetOrCreateFromStream was called.</summary>
        public int GetOrCreateFromStreamCallCount => _getOrCreateFromStreamCallCount;

        public int Count => _cache.Count;

        public TextureRef GetOrCreateFromStream(
            string name,
            Stream stream,
            bool generateMipmaps = true,
            string? debugName = null
        )
        {
            _getOrCreateFromStreamCallCount++;

            if (_cache.TryGetValue(name, out var existing))
                return existing;

            // Create a new TextureRef with a null resource (sufficient for testing identity)
            var textureRef = new TextureRef(name, this, TextureResource.Null);
            _cache[name] = textureRef;
            return textureRef;
        }

        public TextureRef GetOrCreateFromFile(
            string filePath,
            bool generateMipmaps = true,
            string? debugName = null
        ) => TextureRef.Null;

        public TextureRef GetOrCreateFromImage(
            string name,
            NexImage image,
            bool generateMipmaps = true
        ) => TextureRef.Null;

        public Task<TextureRef> GetOrCreateFromStreamAsync(
            string name,
            Stream stream,
            bool generateMipmaps = true,
            string? debugName = null
        ) => Task.FromResult(GetOrCreateFromStream(name, stream, generateMipmaps, debugName));

        public Task<TextureRef> GetOrCreateFromFileAsync(
            string filePath,
            bool generateMipmaps = true,
            string? debugName = null
        ) => Task.FromResult(TextureRef.Null);

        public Task<TextureRef> GetOrCreateFromImageAsync(
            string name,
            NexImage image,
            bool generateMipmaps = true
        ) => Task.FromResult(TextureRef.Null);

        public bool Remove(string key) => _cache.Remove(key);

        public bool TryGet(string cacheKey, out TextureCacheEntry? entry)
        {
            entry = null;
            return false;
        }

        public void Clear() => _cache.Clear();

        public int CleanupExpired() => 0;

        public RepositoryStatistics GetStatistics() =>
            new()
            {
                TotalEntries = _cache.Count,
                MaxEntries = 0,
                TotalHits = 0,
                TotalMisses = 0,
            };

        public void Dispose() { }
    }

    /// <summary>
    /// A minimal mock ISamplerRepository (not exercised in this test).
    /// </summary>
    private sealed class MockSamplerRepository : ISamplerRepository
    {
        public int Count => 0;

        public SamplerRef GetOrCreate(string key, SamplerStateDesc desc) => SamplerRef.Null;

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
    /// Property 11: For any set of N (2..10) calls to LoadTexture with the same texture index,
    /// the ITextureRepository.GetOrCreateFromStream is called N times (TextureLoader delegates
    /// every call), and the repository's caching ensures all returned TextureRef values are
    /// the same instance.
    /// **Validates: Requirements 5.8**
    /// </summary>
    [TestMethod]
    public void LoadTexture_SameIndex_NTimes_ReturnsIdenticalTextureRef()
    {
        // Generate N (2..10) calls with the same texture index
        var inputGen = from n in Gen.Choose(2, 10) select n;

        Prop.ForAll(
                Arb.From(inputGen),
                (int callCount) =>
                {
                    // Arrange: Create a glTF model with one texture referencing one embedded image
                    // The image is stored in a buffer view (embedded)
                    var imageData = new byte[] { 0x89, 0x50, 0x4E, 0x47 }; // Minimal fake image bytes

                    var model = new Gltf
                    {
                        Textures =
                        [
                            new GltfTexture
                            {
                                Source = 0, // References image index 0
                                Sampler = null,
                            },
                        ],
                        Images =
                        [
                            new glTFLoader.Schema.Image
                            {
                                BufferView = 0,
                                MimeType = glTFLoader.Schema.Image.MimeTypeEnum.image_png,
                            },
                        ],
                        BufferViews =
                        [
                            new BufferView
                            {
                                Buffer = 0,
                                ByteOffset = 0,
                                ByteLength = imageData.Length,
                            },
                        ],
                        Buffers = [new glTFLoader.Schema.Buffer { ByteLength = imageData.Length }],
                    };

                    var mockTextureRepo = new MockTextureRepository();
                    var mockSamplerRepo = new MockSamplerRepository();
                    var diagnostics = new List<ImportDiagnostic>();

                    var textureLoader = new TextureLoader(
                        mockTextureRepo,
                        mockSamplerRepo,
                        baseDirectory: "C:\\fake",
                        model: model,
                        bufferData: [imageData],
                        diagnostics: diagnostics,
                        manifest: new ResourceManifest(),
                        sessionId: Guid.NewGuid().ToString("D")
                    );

                    // Act: Call LoadTexture N times with the same texture index (0)
                    var results = new TextureRef[callCount];
                    for (int i = 0; i < callCount; i++)
                    {
                        results[i] = textureLoader.LoadTexture(0);
                    }

                    // Assert 1: GetOrCreateFromStream was called N times
                    // (TextureLoader delegates every call; the repository handles caching)
                    if (mockTextureRepo.GetOrCreateFromStreamCallCount != callCount)
                        return false;

                    // Assert 2: All returned TextureRef values are the same instance
                    var firstRef = results[0];
                    for (int i = 1; i < callCount; i++)
                    {
                        if (!ReferenceEquals(firstRef, results[i]))
                            return false;
                    }

                    return true;
                }
            )
            .Check(FsCheckConfig);
    }
}
