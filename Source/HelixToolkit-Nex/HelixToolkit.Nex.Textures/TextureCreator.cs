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
    /// <param name="generateMipmaps">
    /// When <c>true</c> and the image contains only one mip level, the full mip chain is allocated
    /// and mipmaps are generated on the GPU immediately after upload. Has no effect if the image
    /// already contains multiple mip levels.
    /// </param>
    /// <param name="debugName">Optional debug name for the texture.</param>
    /// <param name="scheduleMipmapGeneration">
    /// Optional callback used to defer GPU mipmap generation instead of invoking
    /// <see cref="IContext.GenerateMipmap(in TextureHandle, out uint)"/> inline. When supplied and
    /// mipmap generation applies, the callback is invoked with the texture handle so the caller can
    /// queue the work to run on the render thread. When <c>null</c>, mipmaps are generated inline.
    /// </param>
    /// <returns>A <see cref="TextureResource"/> representing the created GPU texture.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="image"/>'s format is <see cref="Format.Invalid"/>.
    /// </exception>
    public static TextureResource CreateTexture(
        IContext context,
        Image image,
        bool generateMipmaps = false,
        string? debugName = null,
        Action<TextureHandle>? scheduleMipmapGeneration = null
    )
    {
        if (image.Description.Format == Format.Invalid)
            throw new InvalidOperationException(
                "Cannot create a GPU texture from an image with Format.Invalid"
            );

        var desc = BuildTextureDesc(image, includeData: true, generateMipmaps: generateMipmaps);
        context.CreateTexture(desc, out var texture, debugName).CheckResult();
        if (desc.GenerateMipmaps)
        {
            // Defer generation to the render thread when a scheduler is provided; otherwise
            // generate inline (e.g. when called directly outside the engine render path).
            if (scheduleMipmapGeneration is not null)
                scheduleMipmapGeneration(texture.Handle);
            else
                context.GenerateMipmap(texture.Handle, out _);
        }
        return texture;
    }

    /// <summary>
    /// Loads an image from a stream and creates a GPU texture synchronously.
    /// </summary>
    /// <param name="context">The graphics context.</param>
    /// <param name="stream">The stream containing image data.</param>
    /// <param name="generateMipmaps">
    /// When <c>true</c> and the image contains only one mip level, the full mip chain is allocated
    /// and mipmaps are generated on the GPU immediately after upload.
    /// </param>
    /// <param name="debugName">Optional debug name for the texture.</param>
    /// <param name="scheduleMipmapGeneration">
    /// Optional callback used to defer GPU mipmap generation to the render thread. See
    /// <see cref="CreateTexture"/> for details.
    /// </param>
    /// <returns>A <see cref="TextureResource"/> representing the created GPU texture.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the stream cannot be decoded into an image.</exception>
    public static TextureResource CreateTextureFromStream(
        IContext context,
        Stream stream,
        bool generateMipmaps = false,
        string? debugName = null,
        Action<TextureHandle>? scheduleMipmapGeneration = null
    )
    {
        var image =
            Image.Load(stream)
            ?? throw new InvalidOperationException(
                "Failed to load image from stream: no registered loader could decode the data"
            );
        using (image)
        {
            return CreateTexture(context, image, generateMipmaps, debugName, scheduleMipmapGeneration);
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
    /// <param name="generateMipmaps">
    /// When <c>true</c> and the image contains only one mip level, the full mip chain is allocated
    /// and mipmaps are generated on the GPU after the upload completes.
    /// </param>
    /// <param name="debugName">Optional debug name for the texture.</param>
    /// <param name="scheduleMipmapGeneration">
    /// Optional callback used to defer GPU mipmap generation to the render thread instead of
    /// invoking <see cref="IContext.GenerateMipmap(in TextureHandle, out uint)"/> from the upload
    /// continuation. When supplied, the callback is invoked with the texture handle after the base
    /// upload completes successfully, and the returned handle completes with <see cref="ResultCode.Ok"/>
    /// once the mipmap work has been queued. When <c>null</c>, mipmaps are generated inline in the
    /// continuation.
    /// </param>
    /// <returns>
    /// An <see cref="AsyncUploadHandle{TextureHandle}"/> that completes when both the pixel upload
    /// and, if <paramref name="generateMipmaps"/> is <c>true</c>, mipmap generation have finished.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="image"/>'s format is <see cref="Format.Invalid"/>.
    /// </exception>
    public static AsyncUploadHandle<TextureHandle> CreateTextureAsync(
        IContext context,
        Image image,
        bool generateMipmaps = false,
        string? debugName = null,
        Action<TextureHandle>? scheduleMipmapGeneration = null
    )
    {
        if (image is null)
            throw new InvalidOperationException(
                "Cannot create a GPU texture: the source image is null."
            );

        if (image.Description.Format == Format.Invalid)
            throw new InvalidOperationException(
                "Cannot create a GPU texture from an image with Format.Invalid"
            );

        // Create texture without initial data
        var desc = BuildTextureDesc(image, includeData: false, generateMipmaps: generateMipmaps);
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
                        Offset = new Offset3D(
                            0,
                            0,
                            desc2.Dimension == TextureDimension.Texture3D ? zSlice : 0
                        ),
                        Layer =
                            desc2.Dimension == TextureDimension.Texture3D ? 0u : (uint)arrayIndex,
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
                    {
                        return desc.GenerateMipmaps
                            ? ChainMipmapGeneration(context, uploadHandle, handle, scheduleMipmapGeneration)
                            : uploadHandle;
                    }
                }
                if (depth > 1)
                    depth >>= 1;
            }
        }

        // Fallback: return a completed handle (should not reach here for valid images)
        return AsyncUploadHandle<TextureHandle>.CreateCompleted(ResultCode.Ok, handle);
    }

    /// <summary>
    /// Creates a GPU texture from the given <see cref="Image"/> and returns both the
    /// <see cref="TextureResource"/> and an <see cref="AsyncUploadHandle{TextureHandle}"/> for async pixel upload.
    /// The texture is allocated synchronously; pixel data is uploaded asynchronously.
    /// </summary>
    /// <param name="context">The graphics context.</param>
    /// <param name="image">The CPU-side image.</param>
    /// <param name="generateMipmaps">
    /// When <c>true</c> and the image contains only one mip level, the full mip chain is allocated
    /// and mipmaps are generated on the GPU after the upload completes.
    /// </param>
    /// <param name="debugName">Optional debug name for the texture.</param>
    /// <returns>
    /// A tuple of a <see cref="ResultCode"/> indicating whether GPU allocation succeeded, the
    /// <see cref="TextureResource"/> (GPU memory allocated synchronously) and an
    /// <see cref="AsyncUploadHandle{TextureHandle}"/> that completes when both the pixel upload
    /// and, if <paramref name="generateMipmaps"/> is <c>true</c>, mipmap generation have finished.
    /// When the result is not <see cref="ResultCode.Ok"/>, the texture resource is
    /// <see cref="TextureResource.Null"/> and the upload handle is <c>null</c>; any partially
    /// allocated GPU memory has been released.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="image"/> is <c>null</c> or its format is <see cref="Format.Invalid"/>.
    /// </exception>
    public static (
        ResultCode result,
        TextureResource resource,
        AsyncUploadHandle<TextureHandle> uploadHandle
    ) CreateTextureAsyncWithResource(
        IContext context,
        Image image,
        bool generateMipmaps = false,
        string? debugName = null,
        Action<TextureHandle>? scheduleMipmapGeneration = null
    )
    {
        if (image is null)
            throw new InvalidOperationException(
                "Cannot create a GPU texture: the source image is null."
            );

        if (image.Description.Format == Format.Invalid)
            throw new InvalidOperationException(
                "Cannot create a GPU texture from an image with Format.Invalid"
            );

        // Reject invalid dimensions and mip counts before any GPU allocation. (Req 1.5, 5.3)
        var imageDesc = image.Description;
        if (imageDesc.Width < 1 || imageDesc.Height < 1 || imageDesc.MipLevels < 1)
        {
            return (ResultCode.ArgumentError, TextureResource.Null, null!);
        }

        // Consistency guard: the resolved mip-level count must match the Full_Mip_Chain formula
        // when mipmap generation applies. ResolveMipLevelCount is the single source of truth, so
        // this should never diverge under normal operation; on a mismatch, abort before any GPU
        // allocation and surface a mip-level-count-mismatch result. (Req 7.4)
        bool generatesMipChain = generateMipmaps && imageDesc.MipLevels == 1;
        if (generatesMipChain)
        {
            uint resolvedMipLevels = ResolveMipLevelCount(imageDesc, generateMipmaps);
            uint formulaMipLevels = (uint)PitchCalculator.CountMips(
                imageDesc.Width,
                imageDesc.Height
            );
            if (resolvedMipLevels != formulaMipLevels)
            {
                return (ResultCode.InvalidState, TextureResource.Null, null!);
            }
        }

        // Create texture without initial data (synchronous GPU memory allocation)
        var desc = BuildTextureDesc(image, includeData: false, generateMipmaps: generateMipmaps);
        var createResult = context.CreateTexture(desc, out var texture, debugName);
        if (createResult != ResultCode.Ok)
        {
            // Allocation failed: release any partially allocated GPU memory and surface the
            // failure code without returning a resource or upload handle. (Req 1.5, 5.3)
            texture?.Dispose();
            return (createResult, TextureResource.Null, null!);
        }
        var handle = texture.Handle;

        // Upload pixel data for each array slice and mip level
        var desc2 = image.Description;
        int d = desc2.Depth;
        AsyncUploadHandle<TextureHandle> lastUploadHandle =
            AsyncUploadHandle<TextureHandle>.CreateCompleted(ResultCode.Ok, handle);

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
                        Offset = new Offset3D(
                            0,
                            0,
                            desc2.Dimension == TextureDimension.Texture3D ? zSlice : 0
                        ),
                        Layer =
                            desc2.Dimension == TextureDimension.Texture3D ? 0u : (uint)arrayIndex,
                        NumLayers = 1,
                        MipLevel = (uint)level,
                        NumMipLevels = 1,
                        Dimensions = new Dimensions((uint)pb.Width, (uint)pb.Height, 1),
                    };

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

                    lastUploadHandle = context.UploadAsync<byte>(
                        in handle,
                        range,
                        pixelData,
                        (size_t)pixelData.Length
                    );
                }
                if (depth > 1)
                    depth >>= 1;
            }
        }

        var finalHandle = desc.GenerateMipmaps
            ? ChainMipmapGeneration(context, lastUploadHandle, handle, scheduleMipmapGeneration)
            : lastUploadHandle;

        return (ResultCode.Ok, texture, finalHandle);
    }

    /// <summary>
    /// Loads an image from a stream and creates a GPU texture asynchronously.
    /// </summary>
    /// <param name="context">The graphics context.</param>
    /// <param name="stream">The stream containing image data.</param>
    /// <param name="generateMipmaps">
    /// When <c>true</c> and the image contains only one mip level, the full mip chain is allocated
    /// and mipmaps are generated on the GPU after the upload completes.
    /// </param>
    /// <param name="debugName">Optional debug name for the texture.</param>
    /// <param name="scheduleMipmapGeneration">
    /// Optional callback used to defer GPU mipmap generation to the render thread. See
    /// <see cref="CreateTextureAsync"/> for details.
    /// </param>
    /// <returns>An <see cref="AsyncUploadHandle{TextureHandle}"/> that completes when the upload finishes.</returns>
    public static AsyncUploadHandle<TextureHandle> CreateTextureFromStreamAsync(
        IContext context,
        Stream stream,
        bool generateMipmaps = false,
        string? debugName = null,
        Action<TextureHandle>? scheduleMipmapGeneration = null
    )
    {
        if (stream is null)
        {
            throw new InvalidOperationException(
                "Cannot create a GPU texture from a null stream."
            );
        }

        var image =
            Image.Load(stream)
            ?? throw new InvalidOperationException(
                "Failed to load image from stream: no registered loader could decode the data"
            );
        // Note: image is disposed after the async upload is initiated.
        // The pixel data is copied into managed arrays in CreateTextureAsync before disposal.
        using (image)
        {
            return CreateTextureAsync(context, image, generateMipmaps, debugName, scheduleMipmapGeneration);
        }
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns a new <see cref="AsyncUploadHandle{TextureHandle}"/> that completes only after
    /// <paramref name="uploadHandle"/> finishes <em>and</em> mipmap generation has been submitted
    /// (or queued, when <paramref name="scheduleMipmapGeneration"/> is supplied).
    /// </summary>
    private static AsyncUploadHandle<TextureHandle> ChainMipmapGeneration(
        IContext context,
        AsyncUploadHandle<TextureHandle> uploadHandle,
        TextureHandle handle,
        Action<TextureHandle>? scheduleMipmapGeneration = null
    )
    {
        var chainedHandle = new AsyncUploadHandle<TextureHandle>();
        uploadHandle.Task.ContinueWith(
            t =>
            {
                if (!t.IsCompletedSuccessfully)
                {
                    // Upload task faulted/cancelled before producing a result. (Req 4.4)
                    chainedHandle.Complete(ResultCode.RuntimeError, handle);
                    return;
                }

                var (uploadResult, uploadedHandle) = t.Result;
                if (uploadResult != ResultCode.Ok)
                {
                    // Base upload failed: skip mip-gen, propagate the upload code, no valid handle. (Req 2.3, 2.4, 3.5, 4.2)
                    chainedHandle.Complete(uploadResult, default);
                    return;
                }

                try
                {
                    // (Req 2.1) only after Ok upload. When a scheduler is supplied, defer the GPU
                    // mipmap generation to the render thread instead of calling GenerateMipmap from
                    // this thread-pool continuation (where immediate command-buffer submission is
                    // unsafe relative to the render thread).
                    if (scheduleMipmapGeneration is not null)
                        scheduleMipmapGeneration(uploadedHandle);
                    else
                        context.GenerateMipmap(uploadedHandle, out _);
                    chainedHandle.Complete(ResultCode.Ok, uploadedHandle); // (Req 3.3, 4.1)
                }
                catch
                {
                    chainedHandle.Complete(ResultCode.RuntimeError, uploadedHandle); // (Req 3.6, 4.3)
                }
            },
            TaskScheduler.Default
        );
        return chainedHandle;
    }

    /// <summary>
    /// Resolves the number of mip levels to allocate for a texture.
    /// </summary>
    /// <param name="desc">The source image description.</param>
    /// <param name="generateMipmaps">Whether automatic mipmap generation was requested.</param>
    /// <returns>
    /// The full mip chain count (<c>PitchCalculator.CountMips(width, height)</c>, equal to
    /// <c>1 + floor(log2(max(width, height)))</c>) when <paramref name="generateMipmaps"/> is true
    /// and the source has a single mip level; otherwise the source mip-level count.
    /// </returns>
    internal static uint ResolveMipLevelCount(in ImageDescription desc, bool generateMipmaps)
    {
        bool needsGenerate = generateMipmaps && desc.MipLevels == 1;
        return needsGenerate
            ? (uint)PitchCalculator.CountMips(desc.Width, desc.Height)
            : (uint)desc.MipLevels;
    }

    private static TextureDesc BuildTextureDesc(
        Image image,
        bool includeData,
        bool generateMipmaps = false
    )
    {
        var desc = image.Description;
        bool needsGenerate = generateMipmaps && desc.MipLevels == 1;
        uint mipLevels = ResolveMipLevelCount(desc, generateMipmaps);
        return new TextureDesc
        {
            Type = MapDimension(desc.Dimension),
            Format = desc.Format,
            Dimensions = new Dimensions((uint)desc.Width, (uint)desc.Height, (uint)desc.Depth),
            NumLayers = (uint)desc.ArraySize,
            NumMipLevels = mipLevels,
            Usage = TextureUsageBits.Sampled,
            Storage = StorageType.Device,
            Data = includeData ? image.DataPointer : IntPtr.Zero,
            DataSize = includeData ? (size_t)image.TotalSizeInBytes : 0,
            DataNumMipLevels = includeData ? (uint)desc.MipLevels : 1,
            GenerateMipmaps = needsGenerate,
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
