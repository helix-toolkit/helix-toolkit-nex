using glTFLoader.Schema;
using HelixToolkit.Nex.glTF.Internal;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Repository;
using GltfSampler = glTFLoader.Schema.Sampler;
using GltfTexture = glTFLoader.Schema.Texture;
using NexImage = HelixToolkit.Nex.Textures.Image;

namespace HelixToolkit.Nex.glTF.Tests.Unit;

/// <summary>
/// Unit tests for TextureLoader session ID validation and cache key format.
/// Validates: Requirements 2.1, 2.2, 2.6, 3.1, 3.4
/// </summary>
[TestClass]
public class TextureLoaderSessionTests
{
    /// <summary>
    /// A mock ITextureRepository that captures the cache key passed to GetOrCreateFromStream.
    /// </summary>
    private sealed class KeyCapturingTextureRepository : ITextureRepository
    {
        public List<string> CapturedKeys { get; } = [];
        public int Count => 0;

        public TextureRef GetOrCreateFromStream(
            string name,
            Stream stream,
            bool generateMipmaps = true,
            string? debugName = null
        )
        {
            CapturedKeys.Add(name);
            return new TextureRef(name, this, TextureResource.Null);
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

        public bool Remove(string key) => true;

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
    /// A mock ISamplerRepository that captures the cache key passed to GetOrCreate.
    /// </summary>
    private sealed class KeyCapturingSamplerRepository : ISamplerRepository
    {
        public List<string> CapturedKeys { get; } = [];
        public int Count => 0;

        public SamplerRef GetOrCreate(string key, SamplerStateDesc desc)
        {
            CapturedKeys.Add(key);
            return new SamplerRef(key, this, SamplerResource.Null);
        }

        public bool Remove(string key) => true;

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

    // =========================================================================
    // Session ID validation — Validates: Requirement 2.6
    // =========================================================================

    [TestMethod]
    public void Constructor_NullSessionId_ThrowsArgumentException()
    {
        var textureRepo = new KeyCapturingTextureRepository();
        var samplerRepo = new KeyCapturingSamplerRepository();
        var model = new Gltf();

        Assert.ThrowsException<ArgumentException>(() =>
            new TextureLoader(
                textureRepo,
                samplerRepo,
                baseDirectory: @"C:\fake",
                model: model,
                bufferData: [],
                diagnostics: new List<ImportDiagnostic>(),
                manifest: new ResourceManifest(),
                sessionId: null!
            )
        );
    }

    [TestMethod]
    public void Constructor_EmptySessionId_ThrowsArgumentException()
    {
        var textureRepo = new KeyCapturingTextureRepository();
        var samplerRepo = new KeyCapturingSamplerRepository();
        var model = new Gltf();

        Assert.ThrowsException<ArgumentException>(() =>
            new TextureLoader(
                textureRepo,
                samplerRepo,
                baseDirectory: @"C:\fake",
                model: model,
                bufferData: [],
                diagnostics: new List<ImportDiagnostic>(),
                manifest: new ResourceManifest(),
                sessionId: string.Empty
            )
        );
    }

    // =========================================================================
    // Embedded texture key format — Validates: Requirement 2.1
    // =========================================================================

    [TestMethod]
    public void LoadTexture_EmbeddedImage_KeyFormat_IsImageIndexColonSessionId()
    {
        // Arrange
        var sessionId = "a1b2c3d4-e5f6-7890-abcd-ef1234567890";
        var imageData = new byte[] { 0x89, 0x50, 0x4E, 0x47 }; // Minimal fake image bytes

        var textureRepo = new KeyCapturingTextureRepository();
        var samplerRepo = new KeyCapturingSamplerRepository();

        var model = new Gltf
        {
            Textures = [new GltfTexture { Source = 0, Sampler = null }],
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

        var loader = new TextureLoader(
            textureRepo,
            samplerRepo,
            baseDirectory: @"C:\fake",
            model: model,
            bufferData: [imageData],
            diagnostics: new List<ImportDiagnostic>(),
            manifest: new ResourceManifest(),
            sessionId: sessionId
        );

        // Act
        loader.LoadTexture(0);

        // Assert: key format is "{imageIndex}:{sessionId}"
        Assert.AreEqual(1, textureRepo.CapturedKeys.Count);
        Assert.AreEqual($"0:{sessionId}", textureRepo.CapturedKeys[0]);
    }

    // =========================================================================
    // External texture key format — Validates: Requirement 2.2
    // =========================================================================

    [TestMethod]
    public void LoadTexture_ExternalImage_KeyFormat_IsAbsolutePathColonSessionId()
    {
        // Arrange
        var sessionId = "b2c3d4e5-f6a7-8901-bcde-f12345678901";
        var baseDirectory = Path.GetTempPath();

        // Create a temporary image file so LoadExternalImage finds it
        var tempFile = Path.Combine(baseDirectory, "test_texture.png");
        File.WriteAllBytes(tempFile, [0x89, 0x50, 0x4E, 0x47]);

        try
        {
            var textureRepo = new KeyCapturingTextureRepository();
            var samplerRepo = new KeyCapturingSamplerRepository();

            var model = new Gltf
            {
                Textures = [new GltfTexture { Source = 0, Sampler = null }],
                Images = [new glTFLoader.Schema.Image { Uri = "test_texture.png" }],
            };

            var loader = new TextureLoader(
                textureRepo,
                samplerRepo,
                baseDirectory: baseDirectory,
                model: model,
                bufferData: [],
                diagnostics: new List<ImportDiagnostic>(),
                manifest: new ResourceManifest(),
                sessionId: sessionId
            );

            // Act
            loader.LoadTexture(0);

            // Assert: key format is "{absolutePath}:{sessionId}"
            var expectedAbsolutePath = Path.GetFullPath(tempFile);
            Assert.AreEqual(1, textureRepo.CapturedKeys.Count);
            Assert.AreEqual($"{expectedAbsolutePath}:{sessionId}", textureRepo.CapturedKeys[0]);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    // =========================================================================
    // Named sampler key format — Validates: Requirement 3.1
    // =========================================================================

    [TestMethod]
    public void LoadSampler_NamedSampler_KeyFormat_IsSamplerNameColonSessionId()
    {
        // Arrange
        var sessionId = "c3d4e5f6-a7b8-9012-cdef-123456789012";
        var samplerName = "LinearWrap";

        var textureRepo = new KeyCapturingTextureRepository();
        var samplerRepo = new KeyCapturingSamplerRepository();

        var model = new Gltf { Samplers = [new GltfSampler { Name = samplerName }] };

        var loader = new TextureLoader(
            textureRepo,
            samplerRepo,
            baseDirectory: @"C:\fake",
            model: model,
            bufferData: [],
            diagnostics: new List<ImportDiagnostic>(),
            manifest: new ResourceManifest(),
            sessionId: sessionId
        );

        // Act
        loader.LoadSampler(0);

        // Assert: key format is "{samplerName}:{sessionId}"
        Assert.AreEqual(1, samplerRepo.CapturedKeys.Count);
        Assert.AreEqual($"{samplerName}:{sessionId}", samplerRepo.CapturedKeys[0]);
    }

    // =========================================================================
    // Unnamed sampler (null name) falls back to index — Validates: Requirement 3.4
    // =========================================================================

    [TestMethod]
    public void LoadSampler_UnnamedSampler_KeyFormat_IsSamplerIndexColonSessionId()
    {
        // Arrange
        var sessionId = "d4e5f6a7-b8c9-0123-def0-234567890123";

        var textureRepo = new KeyCapturingTextureRepository();
        var samplerRepo = new KeyCapturingSamplerRepository();

        var model = new Gltf
        {
            Samplers =
            [
                new GltfSampler { Name = null }, // Unnamed sampler at index 0
            ],
        };

        var loader = new TextureLoader(
            textureRepo,
            samplerRepo,
            baseDirectory: @"C:\fake",
            model: model,
            bufferData: [],
            diagnostics: new List<ImportDiagnostic>(),
            manifest: new ResourceManifest(),
            sessionId: sessionId
        );

        // Act
        loader.LoadSampler(0);

        // Assert: key format is "{samplerIndex}:{sessionId}" when name is null
        Assert.AreEqual(1, samplerRepo.CapturedKeys.Count);
        Assert.AreEqual($"0:{sessionId}", samplerRepo.CapturedKeys[0]);
    }
}
