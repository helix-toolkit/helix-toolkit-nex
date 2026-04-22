using HelixToolkit.Nex.Graphics;

namespace HelixToolkit.Nex.Textures;

/// <summary>
/// Static utility for creating GPU <see cref="TextureResource"/> objects from CPU-side <see cref="Image"/> data.
/// </summary>
public static class TextureCreator
{
    // -------------------------------------------------------------------------
    // Synchronous path
    // -------------------------------------------------------------------------

    /// <summary>
    /// Creates a GPU texture from the given <see cref="Image"/>, uploading all pixel data synchronously.
    /// </summary>
    /// <param name="context">The graphics context used to create the texture.</param>
    /// <param name="image">The CPU-side image containing pixel data and description.</param>
    /// <param name="debugName">Optional debug name for the texture.</param>
    /// <returns>A <see cref="TextureResource"/> representing the created GPU texture.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="image"/>'s format is <see cref="Format.Invalid"/>.
    /// </exception>
    public static TextureResource CreateTexture(
        IContext context,
        Image image,
        string? debugName = null
    )
    {
        if (image.Description.Format == Format.Invalid)
            throw new InvalidOperationException(
                "Cannot create a GPU texture from an image with Format.Invalid"
            );

        var desc = BuildTextureDesc(image, includeData: true);
        context.CreateTexture(desc, out var texture, debugName).CheckResult();
        return texture;
    }

    /// <summary>
    /// Loads an image from a stream and creates a GPU texture synchronously.
    /// </summary>
    /// <param name="context">The graphics context.</param>
    /// <param name="stream">The stream containing image data.</param>
    /// <param name="debugName">Optional debug name for the texture.</param>
    /// <returns>A <see cref="TextureResource"/> representing the created GPU texture.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the stream cannot be decoded into an image.</exception>
    public static TextureResource CreateTextureFromStream(
        IContext context,
        Stream stream,
        string? debugName = null
    )
    {
        var image =
            Image.Load(stream)
            ?? throw new InvalidOperationException(
                "Failed to load image from stream: no registered loader could decode the data"
            );
        using (image)
        {
            return CreateTexture(context, image, debugName);
        }
    }

    // -------------------------------------------------------------------------
    // Async upload path
    // -------------------------------------------------------------------------

    /// <summary>
    /// Creates a GPU texture from the given <see cref="Image"/> using an asynchronous upload.
    /// The texture is created without initial data, then pixel data is uploaded via
    /// <see cref="IContext.UploadAsync{T}(in TextureHandle, TextureRangeDesc, T[], size_t)"/>.
    /// </summary>
    /// <param name="context">The graphics context.</param>
    /// <param name="image">The CPU-side image.</param>
    /// <param name="debugName">Optional debug name for the texture.</param>
    /// <returns>An <see cref="AsyncUploadHandle{TextureHandle}"/> that completes when the upload finishes.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="image"/>'s format is <see cref="Format.Invalid"/>.
    /// </exception>
    public static AsyncUploadHandle<TextureHandle> CreateTextureAsync(
        IContext context,
        Image image,
        string? debugName = null
    )
    {
        if (image.Description.Format == Format.Invalid)
            throw new InvalidOperationException(
                "Cannot create a GPU texture from an image with Format.Invalid"
            );

        // Create texture without initial data
        var desc = BuildTextureDesc(image, includeData: false);
        context.CreateTexture(desc, out var texture, debugName).CheckResult();
        var handle = texture.Handle;

        // Upload pixel data for each array slice and mip level
        var desc2 = image.Description;
        int d = desc2.Depth;
        for (int arrayIndex = 0; arrayIndex < desc2.ArraySize; arrayIndex++)
        {
            int depth = d;
            for (int level = 0; level < desc2.MipLevels; level++)
            {
                for (int zSlice = 0; zSlice < depth; zSlice++)
                {
                    var pb = image.GetPixelBuffer(
                        desc2.Dimension == TextureDimension.Texture3D ? zSlice : arrayIndex,
                        level
                    );

                    var range = new TextureRangeDesc
                    {
                        Layer = (uint)arrayIndex,
                        NumLayers = 1,
                        MipLevel = (uint)level,
                        NumMipLevels = 1,
                        Dimensions = new Dimensions((uint)pb.Width, (uint)pb.Height, 1),
                    };

                    // Copy pixel buffer data into a managed byte array for the async upload
                    var pixelData = new byte[pb.BufferStride];
                    unsafe
                    {
                        System.Runtime.InteropServices.Marshal.Copy(
                            pb.DataPointer,
                            pixelData,
                            0,
                            pb.BufferStride
                        );
                    }

                    // The last upload returns the handle we care about
                    var uploadHandle = context.UploadAsync<byte>(
                        in handle,
                        range,
                        pixelData,
                        (size_t)pixelData.Length
                    );

                    bool isLast =
                        arrayIndex == desc2.ArraySize - 1
                        && level == desc2.MipLevels - 1
                        && zSlice == depth - 1;

                    if (isLast)
                        return uploadHandle;
                }
                if (depth > 1)
                    depth >>= 1;
            }
        }

        // Fallback: return a completed handle (should not reach here for valid images)
        return AsyncUploadHandle<TextureHandle>.CreateCompleted(ResultCode.Ok, handle);
    }

    /// <summary>
    /// Loads an image from a stream and creates a GPU texture asynchronously.
    /// </summary>
    /// <param name="context">The graphics context.</param>
    /// <param name="stream">The stream containing image data.</param>
    /// <param name="debugName">Optional debug name for the texture.</param>
    /// <returns>An <see cref="AsyncUploadHandle{TextureHandle}"/> that completes when the upload finishes.</returns>
    public static AsyncUploadHandle<TextureHandle> CreateTextureFromStreamAsync(
        IContext context,
        Stream stream,
        string? debugName = null
    )
    {
        var image =
            Image.Load(stream)
            ?? throw new InvalidOperationException(
                "Failed to load image from stream: no registered loader could decode the data"
            );
        // Note: image is disposed after the async upload is initiated.
        // The pixel data is copied into managed arrays in CreateTextureAsync before disposal.
        using (image)
        {
            return CreateTextureAsync(context, image, debugName);
        }
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private static TextureDesc BuildTextureDesc(Image image, bool includeData)
    {
        var desc = image.Description;
        return new TextureDesc
        {
            Type = MapDimension(desc.Dimension),
            Format = desc.Format,
            Dimensions = new Dimensions((uint)desc.Width, (uint)desc.Height, (uint)desc.Depth),
            NumLayers = (uint)desc.ArraySize,
            NumMipLevels = (uint)desc.MipLevels,
            Usage = TextureUsageBits.Sampled,
            Storage = StorageType.Device,
            Data = includeData ? image.DataPointer : IntPtr.Zero,
            DataSize = includeData ? (size_t)image.TotalSizeInBytes : 0,
            DataNumMipLevels = includeData ? (uint)desc.MipLevels : 1,
        };
    }

    private static TextureType MapDimension(TextureDimension dimension) =>
        dimension switch
        {
            TextureDimension.Texture2D => TextureType.Texture2D,
            TextureDimension.Texture3D => TextureType.Texture3D,
            TextureDimension.TextureCube => TextureType.TextureCube,
            _ => throw new InvalidOperationException($"Unsupported texture dimension: {dimension}"),
        };
}
