using HelixToolkit.Nex.Graphics;

namespace HelixToolkit.Nex.Textures;

/// <summary>
/// Fluent builder that combines up to three PBR source images into a single
/// OMR texture (R = Occlusion, G = Metallic, B = Roughness, A = 255).
/// </summary>
/// <remarks>
/// Each output channel is driven by a <see cref="ChannelSource"/>: either a specific
/// color channel extracted from a source <see cref="Image"/>, or a constant byte value.
/// Unconfigured channels default to a constant value of 0.
/// The builder is not thread-safe; callers must not share an instance across threads.
/// </remarks>
public sealed class OmrTextureCombiner
{
    // ---- Private state ----

    private ChannelSource _occlusion = new ChannelSource.Constant(0);
    private ChannelSource _metallic = new ChannelSource.Constant(0);
    private ChannelSource _roughness = new ChannelSource.Constant(0);

    // ---- Occlusion (R output channel) ----

    /// <summary>
    /// Configures the Occlusion (R) output channel to read from a specific channel of a source image.
    /// </summary>
    /// <param name="source">The source image. Must not be null.</param>
    /// <param name="channel">The color channel to read from the source image.</param>
    /// <returns>This builder instance for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="source"/> is null.</exception>
    public OmrTextureCombiner WithOcclusion(Image source, ChannelComponent channel)
    {
        ArgumentNullException.ThrowIfNull(source);
        _occlusion = new ChannelSource.ImageChannel(source, channel);
        return this;
    }

    /// <summary>
    /// Configures the Occlusion (R) output channel to write a constant byte value to every pixel.
    /// </summary>
    /// <param name="constantValue">The constant byte value in the range [0, 255].</param>
    /// <returns>This builder instance for method chaining.</returns>
    public OmrTextureCombiner WithOcclusion(byte constantValue)
    {
        _occlusion = new ChannelSource.Constant(constantValue);
        return this;
    }

    /// <summary>
    /// Configures the Occlusion (R) output channel to read from a specific channel of a source image loaded from disk.
    /// </summary>
    /// <param name="filePath">The file path of the source image.</param>
    /// <param name="channel">The color channel to read from the source image.</param>
    /// <returns>This builder instance for method chaining.</returns>
    /// <exception cref="ArgumentException">Thrown when the image fails to load from the specified path.</exception>
    public OmrTextureCombiner WithOcclusion(string filePath, ChannelComponent channel)
    {
        var image = Image.Load(filePath);
        return image is null
            ? throw new FileLoadException($"Failed to load image from path: {filePath}", filePath)
            : WithOcclusion(image, channel);
    }

    // ---- Metallic (G output channel) ----

    /// <summary>
    /// Configures the Metallic (G) output channel to read from a specific channel of a source image.
    /// </summary>
    /// <param name="source">The source image. Must not be null.</param>
    /// <param name="channel">The color channel to read from the source image.</param>
    /// <returns>This builder instance for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="source"/> is null.</exception>
    public OmrTextureCombiner WithMetallic(Image source, ChannelComponent channel)
    {
        ArgumentNullException.ThrowIfNull(source);
        _metallic = new ChannelSource.ImageChannel(source, channel);
        return this;
    }

    /// <summary>
    /// Configures the Metallic (G) output channel to write a constant byte value to every pixel.
    /// </summary>
    /// <param name="constantValue">The constant byte value in the range [0, 255].</param>
    /// <returns>This builder instance for method chaining.</returns>
    public OmrTextureCombiner WithMetallic(byte constantValue)
    {
        _metallic = new ChannelSource.Constant(constantValue);
        return this;
    }

    /// <summary>
    /// Adds a metallic texture to the combiner using the specified image file and channel mapping.
    /// </summary>
    /// <param name="filePath">The path to the image file to use as the metallic texture. Cannot be null or empty.</param>
    /// <param name="channel">The channel of the image to use for the metallic component.</param>
    /// <returns>A new instance of <see cref="OmrTextureCombiner"/> with the metallic texture applied.</returns>
    /// <exception cref="FileLoadException">Thrown if the image cannot be loaded from the specified <paramref name="filePath"/>.</exception>
    public OmrTextureCombiner WithMetallic(string filePath, ChannelComponent channel)
    {
        var image = Image.Load(filePath);
        return image is null
            ? throw new FileLoadException($"Failed to load image from path: {filePath}", filePath)
            : WithMetallic(image, channel);
    }

    // ---- Roughness (B output channel) ----

    /// <summary>
    /// Configures the Roughness (B) output channel to read from a specific channel of a source image.
    /// </summary>
    /// <param name="source">The source image. Must not be null.</param>
    /// <param name="channel">The color channel to read from the source image.</param>
    /// <returns>This builder instance for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="source"/> is null.</exception>
    public OmrTextureCombiner WithRoughness(Image source, ChannelComponent channel)
    {
        ArgumentNullException.ThrowIfNull(source);
        _roughness = new ChannelSource.ImageChannel(source, channel);
        return this;
    }

    /// <summary>
    /// Configures the Roughness (B) output channel to write a constant byte value to every pixel.
    /// </summary>
    /// <param name="constantValue">The constant byte value in the range [0, 255].</param>
    /// <returns>This builder instance for method chaining.</returns>
    public OmrTextureCombiner WithRoughness(byte constantValue)
    {
        _roughness = new ChannelSource.Constant(constantValue);
        return this;
    }

    /// <summary>
    /// Configures the Roughness (B) output channel to read from a specific channel of a source image loaded from disk.
    /// </summary>
    /// <param name="filePath">The path to the image file to use as the roughness texture. Cannot be null or empty.</param>
    /// <param name="channel">The channel of the image to use for the roughness component.</param>
    /// <returns>A new instance of <see cref="OmrTextureCombiner"/> with the roughness texture applied.</returns>
    /// <exception cref="FileLoadException">Thrown if the image cannot be loaded from the specified <paramref name="filePath"/>.</exception>
    public OmrTextureCombiner WithRoughness(string filePath, ChannelComponent channel)
    {
        var image = Image.Load(filePath);
        return image is null
            ? throw new FileLoadException($"Failed to load image from path: {filePath}", filePath)
            : WithRoughness(image, channel);
    }

    /// <summary>
    /// Configures the Roughness (B) output channel to read from a specific channel of a gloss map image,
    /// applying the inversion transform <c>(byte)(255 - rawValue)</c> to convert gloss to roughness.
    /// </summary>
    /// <param name="source">The gloss map source image. Must not be null.</param>
    /// <param name="channel">The color channel to read from the source image.</param>
    /// <returns>This builder instance for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="source"/> is null.</exception>
    public OmrTextureCombiner WithRoughnessFromGloss(Image source, ChannelComponent channel)
    {
        ArgumentNullException.ThrowIfNull(source);
        _roughness = new ChannelSource.InvertedImageChannel(source, channel);
        return this;
    }

    /// <summary>
    /// Configures the Roughness (B) output channel to read from a specific channel of a gloss map image
    /// loaded from disk, applying the inversion transform <c>(byte)(255 - rawValue)</c> to convert gloss to roughness.
    /// </summary>
    /// <param name="filePath">The file path of the gloss map source image.</param>
    /// <param name="channel">The color channel to read from the source image.</param>
    /// <returns>This builder instance for method chaining.</returns>
    /// <exception cref="FileLoadException">Thrown when the image fails to load from the specified path.</exception>
    public OmrTextureCombiner WithRoughnessFromGloss(string filePath, ChannelComponent channel)
    {
        var image = Image.Load(filePath);
        return image is null
            ? throw new FileLoadException($"Failed to load image from path: {filePath}", filePath)
            : WithRoughnessFromGloss(image, channel);
    }

    // ---- Execute ----

    /// <summary>
    /// Executes the combine operation. Output dimensions are inferred from the source images.
    /// </summary>
    /// <returns>A new <see cref="Image"/> with format <c>RGBA_UN8</c>, mip level 1, array size 1.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when all three channels are constant-value mappings (no dimensions can be inferred).
    /// </exception>
    public Image Combine()
    {
        static bool IsConstant(ChannelSource s) => s is ChannelSource.Constant;
        bool allConstant =
            IsConstant(_occlusion) && IsConstant(_metallic) && IsConstant(_roughness);

        if (allConstant)
        {
            throw new ArgumentException(
                "Output dimensions must be supplied explicitly when all channel mappings are constant values."
            );
        }

        // Dimension resolution will override the zeros with source image dimensions.
        return Combine(0, 0);
    }

    /// <summary>
    /// Executes the combine operation with explicit output dimensions.
    /// Required when all three channels are constant-value mappings.
    /// </summary>
    /// <param name="width">The output image width. Overridden by source image dimensions when image sources are present.</param>
    /// <param name="height">The output image height. Overridden by source image dimensions when image sources are present.</param>
    /// <returns>A new <see cref="Image"/> with format <c>RGBA_UN8</c>, mip level 1, array size 1.</returns>
    /// <exception cref="ArgumentNullException">Thrown when a source image is null.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when a source image has been disposed.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when a source image has an unsupported format, or when source image dimensions are inconsistent.
    /// </exception>
    public Image Combine(int width, int height)
    {
        // ---- Validation phase (before any allocation) ----

        ValidateSource(_occlusion, "Occlusion");
        ValidateSource(_metallic, "Metallic");
        ValidateSource(_roughness, "Roughness");

        // Dimension consistency check
        (int w, int h, string name)? firstDim = null;
        CheckDimension(_occlusion, "Occlusion", ref firstDim, ref width, ref height);
        CheckDimension(_metallic, "Metallic", ref firstDim, ref width, ref height);
        CheckDimension(_roughness, "Roughness", ref firstDim, ref width, ref height);

        // ---- Pixel loop (with exception safety) ----

        // Pre-fetch pixel buffers for image channel sources
        PixelBuffer? occlusionPb = GetPixelBuffer(_occlusion);
        PixelBuffer? metallicPb = GetPixelBuffer(_metallic);
        PixelBuffer? roughnessPb = GetPixelBuffer(_roughness);

        Image? output = null;
        try
        {
            output = Image.New2D(width, height, 1, Format.RGBA_UN8);
            var outPb = output.GetPixelBuffer(0, 0);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    byte r = SampleSource(_occlusion, occlusionPb, x, y);
                    byte b = SampleSource(_metallic, metallicPb, x, y);
                    byte g = SampleSource(_roughness, roughnessPb, x, y);

                    outPb.SetPixel<Rgba8Pixel>(
                        x,
                        y,
                        new Rgba8Pixel
                        {
                            R = r,
                            G = g,
                            B = b,
                            A = 255,
                        }
                    );
                }
            }

            return output;
        }
        catch
        {
            output?.Dispose();
            throw;
        }
    }

    // ---- Private helpers ----

    private static readonly Format[] SupportedFormats =
    [
        Format.RGBA_UN8,
        Format.R_UN8,
        Format.BGRA_UN8,
    ];

    /// <summary>
    /// Validates a channel source: null check, disposed check, and format check.
    /// </summary>
    private static void ValidateSource(ChannelSource source, string channelName)
    {
        Image? image = source switch
        {
            ChannelSource.ImageChannel ic => ic.Source,
            ChannelSource.InvertedImageChannel ic => ic.Source,
            _ => null,
        };

        if (image is not null)
            ValidateImageSource(image, channelName);
    }

    /// <summary>
    /// Validates a source image: null check, disposed check, and format check.
    /// </summary>
    private static void ValidateImageSource(Image image, string channelName)
    {
        // Null check (defensive — ImageChannel/InvertedImageChannel constructors already guard this)
        if (image is null)
        {
            throw new ArgumentNullException(
                channelName,
                $"Source image for {channelName} channel must not be null."
            );
        }

        // Disposed check
        if (image.DataPointer == IntPtr.Zero)
        {
            throw new ObjectDisposedException(
                channelName,
                $"Source image for {channelName} channel has been disposed."
            );
        }

        // Format check
        var format = image.Description.Format;
        if (!Array.Exists(SupportedFormats, f => f == format))
        {
            throw new ArgumentException(
                $"Source image format '{format}' for {channelName} channel is not supported by OmrTextureCombiner. "
                    + $"Supported formats: {Format.RGBA_UN8}, {Format.R_UN8}, {Format.BGRA_UN8}.",
                channelName
            );
        }
    }

    /// <summary>
    /// Checks dimension consistency and resolves output dimensions from image sources.
    /// </summary>
    private static void CheckDimension(
        ChannelSource source,
        string channelName,
        ref (int w, int h, string name)? firstDim,
        ref int width,
        ref int height
    )
    {
        Image? image = source switch
        {
            ChannelSource.ImageChannel ic => ic.Source,
            ChannelSource.InvertedImageChannel ic => ic.Source,
            _ => null,
        };

        if (image is null)
            return;

        int srcW = image.Description.Width;
        int srcH = image.Description.Height;

        if (firstDim is null)
        {
            firstDim = (srcW, srcH, channelName);
            width = srcW;
            height = srcH;
        }
        else if (firstDim.Value.w != srcW || firstDim.Value.h != srcH)
        {
            throw new ArgumentException(
                $"Source image dimensions are inconsistent: {firstDim.Value.name} is {firstDim.Value.w}×{firstDim.Value.h} "
                    + $"but {channelName} is {srcW}×{srcH}."
            );
        }
    }

    /// <summary>
    /// Pre-fetches the pixel buffer for an image channel source (mip 0, array 0).
    /// Returns null for constant sources.
    /// </summary>
    private static PixelBuffer? GetPixelBuffer(ChannelSource source) =>
        source switch
        {
            ChannelSource.ImageChannel ic => ic.Source.GetPixelBuffer(0, 0),
            ChannelSource.InvertedImageChannel ic => ic.Source.GetPixelBuffer(0, 0),
            _ => null,
        };

    /// <summary>
    /// Samples a byte value from the given channel source at pixel (x, y).
    /// </summary>
    private static byte SampleSource(ChannelSource source, PixelBuffer? pb, int x, int y)
    {
        return source switch
        {
            ChannelSource.Constant constant => constant.Value,
            ChannelSource.ImageChannel imageChannel => PixelSampler.Sample(
                pb!,
                imageChannel.Source.Description.Format,
                imageChannel.Channel,
                x,
                y
            ),
            ChannelSource.InvertedImageChannel ic => (byte)(
                255 - PixelSampler.Sample(pb!, ic.Source.Description.Format, ic.Channel, x, y)
            ),
            _ => throw new InvalidOperationException(
                $"Unknown ChannelSource type: {source.GetType()}"
            ),
        };
    }
}
