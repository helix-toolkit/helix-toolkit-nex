using glTFLoader.Schema;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Repository;
using GltfSampler = glTFLoader.Schema.Sampler;

namespace HelixToolkit.Nex.glTF.Internal;

/// <summary>
/// Resolves glTF image references (embedded or external), loads them via
/// ITextureRepository, and maps samplers via ISamplerRepository.
/// </summary>
internal sealed class TextureLoader
{
    private readonly ITextureRepository _textureRepository;
    private readonly ISamplerRepository _samplerRepository;
    private readonly string _baseDirectory;
    private readonly Gltf _model;
    private readonly byte[][] _bufferData;
    private readonly List<ImportDiagnostic> _diagnostics;
    private readonly ResourceManifest _manifest;
    private readonly string _sessionId;

    /// <summary>
    /// Initializes a new instance of the <see cref="TextureLoader"/> class.
    /// </summary>
    /// <param name="textureRepository">The texture repository for creating/caching GPU textures.</param>
    /// <param name="samplerRepository">The sampler repository for creating/caching sampler states.</param>
    /// <param name="baseDirectory">The base directory of the glTF file for resolving relative URIs.</param>
    /// <param name="model">The deserialized glTF model.</param>
    /// <param name="bufferData">The raw binary buffer data arrays for embedded images.</param>
    /// <param name="diagnostics">The diagnostics list to append warnings/errors to.</param>
    /// <param name="manifest">The resource manifest to register loaded textures and samplers with.</param>
    /// <param name="sessionId">The unique session identifier for cache key isolation.</param>
    public TextureLoader(
        ITextureRepository textureRepository,
        ISamplerRepository samplerRepository,
        string baseDirectory,
        Gltf model,
        byte[][] bufferData,
        List<ImportDiagnostic> diagnostics,
        ResourceManifest manifest,
        string sessionId
    )
    {
        _textureRepository =
            textureRepository ?? throw new ArgumentNullException(nameof(textureRepository));
        _samplerRepository =
            samplerRepository ?? throw new ArgumentNullException(nameof(samplerRepository));
        _baseDirectory = baseDirectory ?? throw new ArgumentNullException(nameof(baseDirectory));
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _bufferData = bufferData ?? throw new ArgumentNullException(nameof(bufferData));
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        ArgumentNullException.ThrowIfNull(manifest);
        _manifest = manifest;
        if (string.IsNullOrEmpty(sessionId))
            throw new ArgumentException(
                "Session identifier must not be null or empty.",
                nameof(sessionId)
            );
        _sessionId = sessionId;
    }

    /// <summary>
    /// Builds a session-scoped cache key for an embedded texture.
    /// Format: "{imageIndex}:{sessionId}"
    /// </summary>
    private string BuildEmbeddedTextureKey(int imageIndex) => $"{imageIndex}:{_sessionId}";

    /// <summary>
    /// Builds a session-scoped cache key for an external texture file.
    /// Format: "{normalizedAbsolutePath}:{sessionId}"
    /// </summary>
    private string BuildExternalTextureKey(string absolutePath) => $"{absolutePath}:{_sessionId}";

    /// <summary>
    /// Builds a session-scoped cache key for a sampler.
    /// Format: "{nameOrIndex}:{sessionId}"
    /// Uses sampler name if available, otherwise falls back to array index.
    /// </summary>
    private string BuildSamplerKey(string? samplerName, int samplerIndex) =>
        string.IsNullOrEmpty(samplerName)
            ? $"{samplerIndex}:{_sessionId}"
            : $"{samplerName}:{_sessionId}";

    /// <summary>
    /// Loads a texture from the glTF model by resolving the image source
    /// (embedded buffer view or external URI).
    /// </summary>
    /// <param name="textureIndex">The index of the texture in the glTF model's Textures array.</param>
    /// <returns>
    /// A <see cref="TextureRef"/> for the loaded texture, or <see cref="TextureRef.Null"/>
    /// if the image cannot be resolved or decoded.
    /// </returns>
    public TextureRef LoadTexture(int textureIndex)
    {
        if (_model.Textures == null || textureIndex < 0 || textureIndex >= _model.Textures.Length)
        {
            _diagnostics.Add(
                new ImportDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"Texture index {textureIndex} is out of range.",
                    "Texture",
                    textureIndex
                )
            );
            return TextureRef.Null;
        }

        var texture = _model.Textures[textureIndex];
        int? imageIndex = texture.Source;

        if (imageIndex == null)
        {
            _diagnostics.Add(
                new ImportDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"Texture {textureIndex} has no image source.",
                    "Texture",
                    textureIndex
                )
            );
            return TextureRef.Null;
        }

        var result = LoadImage(imageIndex.Value);
        if (result != TextureRef.Null)
        {
            _manifest.AddTexture(result);
        }
        return result;
    }

    /// <summary>
    /// Asynchronously loads a texture from the glTF model by resolving the image source
    /// (embedded buffer view or external URI).
    /// </summary>
    /// <param name="textureIndex">The index of the texture in the glTF model's Textures array.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A <see cref="TextureRef"/> for the loaded texture, or <see cref="TextureRef.Null"/>
    /// if the image cannot be resolved or decoded.
    /// </returns>
    public async Task<TextureRef> LoadTextureAsync(int textureIndex, CancellationToken ct = default)
    {
        if (_model.Textures == null || textureIndex < 0 || textureIndex >= _model.Textures.Length)
        {
            _diagnostics.Add(
                new ImportDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"Texture index {textureIndex} is out of range.",
                    "Texture",
                    textureIndex
                )
            );
            return TextureRef.Null;
        }

        var texture = _model.Textures[textureIndex];
        int? imageIndex = texture.Source;

        if (imageIndex == null)
        {
            _diagnostics.Add(
                new ImportDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"Texture {textureIndex} has no image source.",
                    "Texture",
                    textureIndex
                )
            );
            return TextureRef.Null;
        }

        var result = await LoadImageAsync(imageIndex.Value, ct).ConfigureAwait(false);
        if (result != TextureRef.Null)
        {
            _manifest.AddTexture(result);
        }
        return result;
    }

    /// <summary>
    /// Loads the sampler referenced by a glTF texture (via the texture's <c>sampler</c> property).
    /// </summary>
    /// <param name="textureIndex">The index of the texture in the glTF model's Textures array.</param>
    /// <returns>
    /// A <see cref="SamplerRef"/> for the texture's sampler, or <see cref="SamplerRef.Null"/> when
    /// the texture index is out of range or the texture does not reference a sampler.
    /// </returns>
    public SamplerRef LoadSamplerForTexture(int textureIndex)
    {
        if (_model.Textures == null || textureIndex < 0 || textureIndex >= _model.Textures.Length)
        {
            return SamplerRef.Null;
        }

        // A glTF texture without a `sampler` reference uses the glTF default sampler (auto
        // filtering, repeat wrapping); LoadSampler returns SamplerRef.Null for a null index.
        return LoadSampler(_model.Textures[textureIndex].Sampler);
    }

    /// <summary>
    /// Loads a sampler from the glTF model by mapping glTF sampler properties
    /// to a <see cref="SamplerStateDesc"/>.
    /// </summary>
    /// <param name="samplerIndex">The index of the sampler in the glTF model's Samplers array, or null for default.</param>
    /// <returns>A <see cref="SamplerRef"/> for the sampler, or <see cref="SamplerRef.Null"/> if no sampler is specified.</returns>
    public SamplerRef LoadSampler(int? samplerIndex)
    {
        if (samplerIndex == null)
        {
            return SamplerRef.Null;
        }

        if (
            _model.Samplers == null
            || samplerIndex.Value < 0
            || samplerIndex.Value >= _model.Samplers.Length
        )
        {
            _diagnostics.Add(
                new ImportDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"Sampler index {samplerIndex.Value} is out of range.",
                    "Sampler",
                    samplerIndex.Value
                )
            );
            return SamplerRef.Null;
        }

        var sampler = _model.Samplers[samplerIndex.Value];
        var desc = MapSamplerToDesc(sampler);
        var result = _samplerRepository.GetOrCreate(
            BuildSamplerKey(sampler.Name, samplerIndex.Value),
            desc
        );
        if (result != SamplerRef.Null)
        {
            _manifest.AddSampler(result);
        }
        return result;
    }

    /// <summary>
    /// Loads an image by index, resolving embedded buffer view or external URI.
    /// </summary>
    private TextureRef LoadImage(int imageIndex)
    {
        if (_model.Images == null || imageIndex < 0 || imageIndex >= _model.Images.Length)
        {
            _diagnostics.Add(
                new ImportDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"Image index {imageIndex} is out of range.",
                    "Image",
                    imageIndex
                )
            );
            return TextureRef.Null;
        }

        var image = _model.Images[imageIndex];

        // Embedded image: has a BufferView reference
        if (image.BufferView.HasValue)
        {
            return LoadEmbeddedImage(imageIndex, image.BufferView.Value);
        }

        // External image: has a URI
        if (!string.IsNullOrEmpty(image.Uri))
        {
            return LoadExternalImage(imageIndex, image.Uri);
        }

        _diagnostics.Add(
            new ImportDiagnostic(
                DiagnosticSeverity.Warning,
                $"Image {imageIndex} has neither a buffer view nor a URI.",
                "Image",
                imageIndex
            )
        );
        return TextureRef.Null;
    }

    /// <summary>
    /// Asynchronously loads an image by index, resolving embedded buffer view or external URI.
    /// </summary>
    private async Task<TextureRef> LoadImageAsync(int imageIndex, CancellationToken ct)
    {
        if (_model.Images == null || imageIndex < 0 || imageIndex >= _model.Images.Length)
        {
            _diagnostics.Add(
                new ImportDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"Image index {imageIndex} is out of range.",
                    "Image",
                    imageIndex
                )
            );
            return TextureRef.Null;
        }

        var image = _model.Images[imageIndex];

        // Embedded image: has a BufferView reference
        if (image.BufferView.HasValue)
        {
            return await LoadEmbeddedImageAsync(imageIndex, image.BufferView.Value, ct)
                .ConfigureAwait(false);
        }

        // External image: has a URI
        if (!string.IsNullOrEmpty(image.Uri))
        {
            return await LoadExternalImageAsync(imageIndex, image.Uri, ct).ConfigureAwait(false);
        }

        _diagnostics.Add(
            new ImportDiagnostic(
                DiagnosticSeverity.Warning,
                $"Image {imageIndex} has neither a buffer view nor a URI.",
                "Image",
                imageIndex
            )
        );
        return TextureRef.Null;
    }

    /// <summary>
    /// Loads an embedded image from buffer data via a buffer view.
    /// </summary>
    private TextureRef LoadEmbeddedImage(int imageIndex, int bufferViewIndex)
    {
        try
        {
            var stream = GetEmbeddedImageStream(imageIndex, bufferViewIndex);
            if (stream == null)
                return TextureRef.Null;

            string cacheKey = BuildEmbeddedTextureKey(imageIndex);
            return _textureRepository.GetOrCreateFromStream(
                cacheKey,
                stream,
                debugName: $"glTF_Image_{imageIndex}"
            );
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _diagnostics.Add(
                new ImportDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"Failed to decode embedded image {imageIndex}: {ex.Message}",
                    "Image",
                    imageIndex
                )
            );
            return TextureRef.Null;
        }
    }

    /// <summary>
    /// Asynchronously loads an embedded image from buffer data via a buffer view.
    /// </summary>
    private async Task<TextureRef> LoadEmbeddedImageAsync(
        int imageIndex,
        int bufferViewIndex,
        CancellationToken ct
    )
    {
        try
        {
            var stream = GetEmbeddedImageStream(imageIndex, bufferViewIndex);
            if (stream == null)
                return TextureRef.Null;

            string cacheKey = BuildEmbeddedTextureKey(imageIndex);
            return await _textureRepository
                .GetOrCreateFromStreamAsync(cacheKey, stream, debugName: $"glTF_Image_{imageIndex}")
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _diagnostics.Add(
                new ImportDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"Failed to decode embedded image {imageIndex}: {ex.Message}",
                    "Image",
                    imageIndex
                )
            );
            return TextureRef.Null;
        }
    }

    /// <summary>
    /// Extracts the embedded image data from the buffer view into a MemoryStream.
    /// </summary>
    private MemoryStream? GetEmbeddedImageStream(int imageIndex, int bufferViewIndex)
    {
        if (
            _model.BufferViews == null
            || bufferViewIndex < 0
            || bufferViewIndex >= _model.BufferViews.Length
        )
        {
            _diagnostics.Add(
                new ImportDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"Image {imageIndex} references invalid buffer view index {bufferViewIndex}.",
                    "Image",
                    imageIndex
                )
            );
            return null;
        }

        var bufferView = _model.BufferViews[bufferViewIndex];

        if (bufferView.Buffer < 0 || bufferView.Buffer >= _bufferData.Length)
        {
            _diagnostics.Add(
                new ImportDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"Image {imageIndex} references invalid buffer index {bufferView.Buffer}.",
                    "Image",
                    imageIndex
                )
            );
            return null;
        }

        var buffer = _bufferData[bufferView.Buffer];
        int byteOffset = bufferView.ByteOffset;
        int byteLength = bufferView.ByteLength;

        if (byteOffset + byteLength > buffer.Length)
        {
            _diagnostics.Add(
                new ImportDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"Image {imageIndex} buffer view data exceeds buffer bounds.",
                    "Image",
                    imageIndex
                )
            );
            return null;
        }

        return new MemoryStream(buffer, byteOffset, byteLength, writable: false);
    }

    /// <summary>
    /// Loads an external image from a file URI resolved relative to the glTF file.
    /// Uses a session-scoped cache key to ensure isolation across imports.
    /// </summary>
    private TextureRef LoadExternalImage(int imageIndex, string uri)
    {
        try
        {
            string absolutePath = ResolveUri(uri);

            if (!File.Exists(absolutePath))
            {
                _diagnostics.Add(
                    new ImportDiagnostic(
                        DiagnosticSeverity.Warning,
                        $"External image file not found for image {imageIndex}: {uri}",
                        "Image",
                        imageIndex
                    )
                );
                return TextureRef.Null;
            }

            using var stream = File.OpenRead(absolutePath);
            return _textureRepository.GetOrCreateFromStream(
                BuildExternalTextureKey(absolutePath),
                stream,
                debugName: $"glTF_Image_{imageIndex}"
            );
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _diagnostics.Add(
                new ImportDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"Failed to load external image {imageIndex} ({uri}): {ex.Message}",
                    "Image",
                    imageIndex
                )
            );
            return TextureRef.Null;
        }
    }

    /// <summary>
    /// Asynchronously loads an external image from a file URI resolved relative to the glTF file.
    /// Uses a session-scoped cache key to ensure isolation across imports.
    /// </summary>
    private async Task<TextureRef> LoadExternalImageAsync(
        int imageIndex,
        string uri,
        CancellationToken ct
    )
    {
        try
        {
            string absolutePath = ResolveUri(uri);

            if (!File.Exists(absolutePath))
            {
                _diagnostics.Add(
                    new ImportDiagnostic(
                        DiagnosticSeverity.Warning,
                        $"External image file not found for image {imageIndex}: {uri}",
                        "Image",
                        imageIndex
                    )
                );
                return TextureRef.Null;
            }

            using var stream = File.OpenRead(absolutePath);
            return await _textureRepository
                .GetOrCreateFromStreamAsync(
                    BuildExternalTextureKey(absolutePath),
                    stream,
                    debugName: $"glTF_Image_{imageIndex}"
                )
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _diagnostics.Add(
                new ImportDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"Failed to load external image {imageIndex} ({uri}): {ex.Message}",
                    "Image",
                    imageIndex
                )
            );
            return TextureRef.Null;
        }
    }

    /// <summary>
    /// Resolves a relative URI to an absolute file path based on the glTF file's base directory.
    /// </summary>
    private string ResolveUri(string uri)
    {
        // Decode percent-encoded characters in the URI
        string decodedUri = Uri.UnescapeDataString(uri);
        return Path.GetFullPath(Path.Combine(_baseDirectory, decodedUri));
    }

    /// <summary>
    /// Maps a glTF sampler to a <see cref="SamplerStateDesc"/>.
    /// </summary>
    private static SamplerStateDesc MapSamplerToDesc(GltfSampler sampler)
    {
        return new SamplerStateDesc
        {
            MagFilter = MapMagFilter(sampler.MagFilter),
            MinFilter = MapMinFilter(sampler.MinFilter),
            MipMap = MapMipFilter(sampler.MinFilter),
            WrapU = MapWrap(sampler.WrapS),
            WrapV = MapWrap(sampler.WrapT),
            DebugName = sampler.Name,
        };
    }

    /// <summary>
    /// Maps glTF magFilter to engine SamplerFilter.
    /// </summary>
    private static SamplerFilter MapMagFilter(GltfSampler.MagFilterEnum? magFilter)
    {
        return magFilter switch
        {
            GltfSampler.MagFilterEnum.NEAREST => SamplerFilter.Nearest,
            GltfSampler.MagFilterEnum.LINEAR => SamplerFilter.Linear,
            _ => SamplerFilter.Linear, // Default to linear
        };
    }

    /// <summary>
    /// Maps glTF minFilter to engine SamplerFilter (base filter only, not mip).
    /// </summary>
    private static SamplerFilter MapMinFilter(GltfSampler.MinFilterEnum? minFilter)
    {
        return minFilter switch
        {
            GltfSampler.MinFilterEnum.NEAREST => SamplerFilter.Nearest,
            GltfSampler.MinFilterEnum.LINEAR => SamplerFilter.Linear,
            GltfSampler.MinFilterEnum.NEAREST_MIPMAP_NEAREST => SamplerFilter.Nearest,
            GltfSampler.MinFilterEnum.LINEAR_MIPMAP_NEAREST => SamplerFilter.Linear,
            GltfSampler.MinFilterEnum.NEAREST_MIPMAP_LINEAR => SamplerFilter.Nearest,
            GltfSampler.MinFilterEnum.LINEAR_MIPMAP_LINEAR => SamplerFilter.Linear,
            _ => SamplerFilter.Linear, // Default to linear
        };
    }

    /// <summary>
    /// Maps glTF minFilter to engine SamplerMip (mipmap mode).
    /// </summary>
    private static SamplerMip MapMipFilter(GltfSampler.MinFilterEnum? minFilter)
    {
        return minFilter switch
        {
            GltfSampler.MinFilterEnum.NEAREST => SamplerMip.Disabled,
            GltfSampler.MinFilterEnum.LINEAR => SamplerMip.Disabled,
            GltfSampler.MinFilterEnum.NEAREST_MIPMAP_NEAREST => SamplerMip.Nearest,
            GltfSampler.MinFilterEnum.LINEAR_MIPMAP_NEAREST => SamplerMip.Nearest,
            GltfSampler.MinFilterEnum.NEAREST_MIPMAP_LINEAR => SamplerMip.Linear,
            GltfSampler.MinFilterEnum.LINEAR_MIPMAP_LINEAR => SamplerMip.Linear,
            _ => SamplerMip.Disabled, // Default to disabled
        };
    }

    /// <summary>
    /// Maps glTF wrap mode to engine SamplerWrap.
    /// </summary>
    private static SamplerWrap MapWrap(GltfSampler.WrapSEnum wrap)
    {
        return wrap switch
        {
            GltfSampler.WrapSEnum.REPEAT => SamplerWrap.Repeat,
            GltfSampler.WrapSEnum.CLAMP_TO_EDGE => SamplerWrap.Clamp,
            GltfSampler.WrapSEnum.MIRRORED_REPEAT => SamplerWrap.MirrorRepeat,
            _ => SamplerWrap.Repeat, // Default to repeat per glTF spec
        };
    }

    /// <summary>
    /// Maps glTF wrap mode (WrapT) to engine SamplerWrap.
    /// </summary>
    private static SamplerWrap MapWrap(GltfSampler.WrapTEnum wrap)
    {
        return wrap switch
        {
            GltfSampler.WrapTEnum.REPEAT => SamplerWrap.Repeat,
            GltfSampler.WrapTEnum.CLAMP_TO_EDGE => SamplerWrap.Clamp,
            GltfSampler.WrapTEnum.MIRRORED_REPEAT => SamplerWrap.MirrorRepeat,
            _ => SamplerWrap.Repeat, // Default to repeat per glTF spec
        };
    }
}
