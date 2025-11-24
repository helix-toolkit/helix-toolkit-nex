namespace HelixToolkit.Nex.Graphics.Vulkan;

internal struct MemoryRegionDesc(uint32_t offset, uint32_t size)
{
    public uint32_t Offset = offset;
    public uint32_t Size = size;
    public SubmitHandle Handle = SubmitHandle.Null;
}

internal sealed class VulkanStagingDevice : IDisposable
{
    private const uint32_t KStagingBufferAlignment = 16; // updated to support BC7 compressed image
    private const uint32_t KMinBufferSize = 4u * 2048u * 2048u;

    private static readonly ILogger _logger = LogManager.Create<VulkanStagingDevice>();

    private readonly VulkanContext _ctx;
    private readonly uint32_t _maxBufferSize;
    private readonly FastList<MemoryRegionDesc> _regions = [];

    private BufferResource _stagingBuffer = BufferResource.Null;
    private uint32_t _stagingBufferSize = 0;
    private uint32_t _stagingBufferCounter = 0;

    public VulkanStagingDevice(in VulkanContext ctx)
    {
        this._ctx = ctx;
        ref readonly var limits = ref this._ctx.GetVkPhysicalDeviceProperties().limits;

        // use default value of 128Mb clamped to the max limits
        _maxBufferSize = Math.Min(limits.maxStorageBufferRange, 128u * 1024u * 1024u);

        HxDebug.Assert(KMinBufferSize <= _maxBufferSize);
    }

    public ResultCode BufferSubData(
        in VulkanBuffer buffer,
        size_t dstOffset,
        size_t size,
        nint data
    )
    {
        HxDebug.Assert(_ctx.Immediate is not null);
        if (buffer.IsMapped)
        {
            buffer.BufferSubData(dstOffset, size, data);
            return ResultCode.Ok;
        }

        var stagingBuffer = _ctx.BuffersPool.Get(this._stagingBuffer);

        HxDebug.Assert(stagingBuffer);

        if (!stagingBuffer)
        {
            _logger.LogError("Staging buffer is not valid, cannot upload data.");
            return ResultCode.InvalidState;
        }

        while (size > 0)
        {
            // get next staging buffer free offset
            MemoryRegionDesc desc = GetNextFreeOffset(size);
            uint32_t chunkSize = Math.Min(size, desc.Size);

            // copy data into staging buffer
            stagingBuffer!.BufferSubData(desc.Offset, chunkSize, data);

            // do the transfer
            VkBufferCopy copy = new()
            {
                srcOffset = desc.Offset,
                dstOffset = dstOffset,
                size = chunkSize,
            };

            var cmdBuf = _ctx.Immediate!.Acquire();
            unsafe
            {
                VK.vkCmdCopyBuffer(
                    cmdBuf.Instance,
                    stagingBuffer.VkBuffer,
                    buffer.VkBuffer,
                    1,
                    &copy
                );

                VkBufferMemoryBarrier barrier = new()
                {
                    srcAccessMask = VK.VK_ACCESS_TRANSFER_WRITE_BIT,
                    dstAccessMask = 0,
                    srcQueueFamilyIndex = VK.VK_QUEUE_FAMILY_IGNORED,
                    dstQueueFamilyIndex = VK.VK_QUEUE_FAMILY_IGNORED,
                    buffer = buffer.VkBuffer,
                    offset = dstOffset,
                    size = chunkSize,
                };
                VkPipelineStageFlags dstMask = VK.VK_PIPELINE_STAGE_ALL_COMMANDS_BIT;
                if (buffer.VkUsageFlags.HasFlag(VK.VK_BUFFER_USAGE_INDIRECT_BUFFER_BIT))
                {
                    dstMask |= VK.VK_PIPELINE_STAGE_DRAW_INDIRECT_BIT;
                    barrier.dstAccessMask |= VK.VK_ACCESS_INDIRECT_COMMAND_READ_BIT;
                }
                if (buffer.VkUsageFlags.HasFlag(VK.VK_BUFFER_USAGE_INDEX_BUFFER_BIT))
                {
                    dstMask |= VK.VK_PIPELINE_STAGE_VERTEX_INPUT_BIT;
                    barrier.dstAccessMask |= VK.VK_ACCESS_INDEX_READ_BIT;
                }
                if (buffer.VkUsageFlags.HasFlag(VK.VK_BUFFER_USAGE_VERTEX_BUFFER_BIT))
                {
                    dstMask |= VK.VK_PIPELINE_STAGE_VERTEX_INPUT_BIT;
                    barrier.dstAccessMask |= VK.VK_ACCESS_VERTEX_ATTRIBUTE_READ_BIT;
                }
                if (
                    buffer.VkUsageFlags.HasFlag(
                        VK.VK_BUFFER_USAGE_ACCELERATION_STRUCTURE_BUILD_INPUT_READ_ONLY_BIT_KHR
                    )
                )
                {
                    dstMask |= VK.VK_PIPELINE_STAGE_ACCELERATION_STRUCTURE_BUILD_BIT_KHR;
                    barrier.dstAccessMask |= VK.VK_ACCESS_MEMORY_READ_BIT;
                }
                VK.vkCmdPipelineBarrier(
                    cmdBuf.Instance,
                    VK.VK_PIPELINE_STAGE_TRANSFER_BIT,
                    dstMask,
                    new VkDependencyFlags { },
                    0,
                    null,
                    1,
                    &barrier,
                    0,
                    null
                );
                desc.Handle = _ctx.Immediate!.Submit(cmdBuf);
                _regions.Add(desc);

                size -= chunkSize;
                data = (nint)((uint8_t*)data + chunkSize);
                dstOffset += chunkSize;
            }
        }
        return ResultCode.Ok;
    }

    public ResultCode ImageData2D(
        in VulkanImage image,
        in VkRect2D imageRegion,
        uint32_t baseMipLevel,
        uint32_t numMipLevels,
        uint32_t layer,
        uint32_t numLayers,
        VkFormat format,
        nint data,
        size_t dataSize
    )
    {
        HxDebug.Assert(numMipLevels <= Constants.MAX_MIP_LEVELS);

        // divide the width and height by 2 until we get to the size of level 'baseMipLevel'
        uint32_t width = image.Extent.width >> (int)baseMipLevel;
        uint32_t height = image.Extent.height >> (int)baseMipLevel;

        var texFormat = format.ToFormat();

        HxDebug.Assert(
            imageRegion.offset.x == 0
                && imageRegion.offset.y == 0
                && imageRegion.extent.width == width
                && imageRegion.extent.height == height,
            "Uploading mip-levels with an image region that is smaller than the base mip level is not supported"
        );

        // find the storage size for all mip-levels being uploaded
        uint32_t layerStorageSize = 0;
        for (uint32_t i = 0; i < numMipLevels; ++i)
        {
            uint32_t mipSize = HxVkUtils.GetTextureBytesPerLayer(
                image.Extent.width,
                image.Extent.height,
                texFormat,
                i
            );
            layerStorageSize += mipSize;
            width = width <= 1 ? 1 : width >> 1;
            height = height <= 1 ? 1 : height >> 1;
        }
        uint32_t storageSize = layerStorageSize * numLayers;
        EnsureStagingBufferSize(storageSize);

        HxDebug.Assert(
            storageSize <= _stagingBufferSize,
            $"Required storage size ({storageSize} is larger than maximum supported staging buffer size ({_stagingBufferSize})."
        );

        var desc = GetNextFreeOffset(storageSize);
        // No support for copying image in multiple smaller chunk sizes. If we get smaller buffer size than storageSize, we will wait for GPU idle
        // and get bigger chunk.
        if (desc.Size < storageSize)
        {
            WaitAndReset();
            desc = GetNextFreeOffset(storageSize);
        }
        HxDebug.Assert(desc.Size >= storageSize);

        var cmdBuf = _ctx.Immediate!.Acquire();

        var stagingBuffer = _ctx.BuffersPool.Get(this._stagingBuffer);

        HxDebug.Assert(stagingBuffer, "Staging buffer is not valid, cannot upload image data.");

        if (!stagingBuffer)
        {
            _logger.LogError("Staging buffer is not valid, cannot upload image data.");
            return ResultCode.InvalidState;
        }

        var result = stagingBuffer!.BufferSubData(desc.Offset, storageSize, data);
        if (result != ResultCode.Ok)
        {
            return result;
        }

        uint32_t offset = 0;

        uint32_t numPlanes = image.ImageFormat.GetNumImagePlanes();

        if (numPlanes > 1)
        {
            HxDebug.Assert(layer == 0 && baseMipLevel == 0);
            HxDebug.Assert(numLayers == 1 && numMipLevels == 1);
            HxDebug.Assert(imageRegion.offset.x == 0 && imageRegion.offset.y == 0);
            HxDebug.Assert(image.ImageType == VK.VK_IMAGE_TYPE_2D);
            HxDebug.Assert(
                image.Extent.width == imageRegion.extent.width
                    && image.Extent.height == imageRegion.extent.height
            );
        }

        VkImageAspectFlags imageAspect = VK.VK_IMAGE_ASPECT_COLOR_BIT;

        if (numPlanes == 2)
        {
            imageAspect = VK.VK_IMAGE_ASPECT_PLANE_0_BIT | VK.VK_IMAGE_ASPECT_PLANE_1_BIT;
        }
        if (numPlanes == 3)
        {
            imageAspect =
                VK.VK_IMAGE_ASPECT_PLANE_0_BIT
                | VK.VK_IMAGE_ASPECT_PLANE_1_BIT
                | VK.VK_IMAGE_ASPECT_PLANE_2_BIT;
        }

        // https://registry.khronos.org/KTX/specs/1.0/ktxspec.v1.html
        for (uint32_t mipLevel = 0; mipLevel < numMipLevels; ++mipLevel)
        {
            for (uint32_t layer1 = 0; layer1 != numLayers; layer1++)
            {
                uint32_t currentMipLevel = baseMipLevel + mipLevel;

                HxDebug.Assert(currentMipLevel < image.NumLevels);
                HxDebug.Assert(mipLevel < image.NumLevels);

                // 1. Transition initial image layout into TRANSFER_DST_OPTIMAL
                cmdBuf.Instance.ImageMemoryBarrier2(
                    image.Image,
                    new StageAccess2
                    {
                        Stage = VkPipelineStageFlags2.TopOfPipe,
                        Access = VkAccessFlags2.None,
                    },
                    new StageAccess2
                    {
                        Stage = VkPipelineStageFlags2.Transfer,
                        Access = VkAccessFlags2.TransferWrite,
                    },
                    VK.VK_IMAGE_LAYOUT_UNDEFINED,
                    VK.VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,
                    new VkImageSubresourceRange(imageAspect, currentMipLevel, 1, layer1, 1)
                );

                // 2. Copy the pixel data from the staging buffer into the image
                uint32_t planeOffset = 0;
                for (uint32_t plane = 0; plane != numPlanes; plane++)
                {
                    var extent = HxVkUtils.GetImagePlaneExtent(
                        new VkExtent2D
                        {
                            width = Math.Max(1u, imageRegion.extent.width >> (int)mipLevel),
                            height = Math.Max(1u, imageRegion.extent.height >> (int)mipLevel),
                        },
                        format.ToFormat(),
                        plane
                    );
                    VkRect2D region = new()
                    {
                        offset = new VkOffset2D()
                        {
                            x = imageRegion.offset.x >> (int)mipLevel,
                            y = imageRegion.offset.y >> (int)mipLevel,
                        },
                        extent = extent,
                    };
                    VkBufferImageCopy copy = new()
                    {
                        // the offset for this level is at the start of all mip-levels plus the size of all previous mip-levels being uploaded
                        bufferOffset = desc.Offset + offset + planeOffset,
                        bufferRowLength = 0,
                        bufferImageHeight = 0,
                        imageSubresource = new VkImageSubresourceLayers
                        {
                            aspectMask =
                                numPlanes > 1
                                    ? (VkImageAspectFlags)(
                                        (uint)VkImageAspectFlags.Plane0 << (int)plane
                                    )
                                    : imageAspect,
                            mipLevel = currentMipLevel,
                            baseArrayLayer = layer1,
                            layerCount = 1,
                        },
                        imageOffset =
                        {
                            x = region.offset.x,
                            y = region.offset.y,
                            z = 0,
                        },
                        imageExtent =
                        {
                            width = region.extent.width,
                            height = region.extent.height,
                            depth = 1u,
                        },
                    };
                    unsafe
                    {
                        VK.vkCmdCopyBufferToImage(
                            cmdBuf.Instance,
                            stagingBuffer.VkBuffer,
                            image.Image,
                            VK.VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,
                            1,
                            &copy
                        );
                    }
                    planeOffset += HxVkUtils.GetTextureBytesPerPlane(
                        imageRegion.extent.width,
                        imageRegion.extent.height,
                        format.ToFormat(),
                        plane
                    );
                }

                // 3. Transition TRANSFER_DST_OPTIMAL into SHADER_READ_ONLY_OPTIMAL
                cmdBuf.Instance.ImageMemoryBarrier2(
                    image.Image,
                    new StageAccess2
                    {
                        Stage = VkPipelineStageFlags2.Transfer,
                        Access = VkAccessFlags2.TransferWrite,
                    },
                    new StageAccess2
                    {
                        Stage = VkPipelineStageFlags2.AllCommands,
                        Access = VkAccessFlags2.MemoryRead | VkAccessFlags2.MemoryWrite,
                    },
                    VK.VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,
                    VK.VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,
                    new VkImageSubresourceRange(imageAspect, currentMipLevel, 1, layer, 1)
                );

                offset += HxVkUtils.GetTextureBytesPerLayer(
                    imageRegion.extent.width,
                    imageRegion.extent.height,
                    texFormat,
                    currentMipLevel
                );
            }
        }

        image.ImageLayout = VK.VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;

        desc.Handle = _ctx.Immediate!.Submit(cmdBuf);
        _regions.Add(desc);
        return ResultCode.Ok;
    }

    public ResultCode ImageData3D(
        in VulkanImage image,
        in VkOffset3D offset,
        in VkExtent3D extent,
        VkFormat format,
        nint data,
        size_t dataSize
    )
    {
        HxDebug.Assert(image.NumLevels == 1, "Can handle only 3D images with exactly 1 mip-level");
        HxDebug.Assert(
            (offset.x == 0) && (offset.y == 0) && (offset.z == 0),
            "Can upload only full-size 3D images"
        );
        uint32_t storageSize =
            extent.width * extent.height * extent.depth * format.GetBytesPerPixel();

        EnsureStagingBufferSize(storageSize);

        HxDebug.Assert(
            storageSize <= _stagingBufferSize,
            "No support for copying image in multiple smaller chunk sizes"
        );

        // get next staging buffer free offset
        MemoryRegionDesc desc = GetNextFreeOffset(storageSize);

        // No support for copying image in multiple smaller chunk sizes.
        // If we get smaller buffer size than storageSize, we will wait for GPU idle and get a bigger chunk.
        if (desc.Size < storageSize)
        {
            WaitAndReset();
            desc = GetNextFreeOffset(storageSize);
        }

        HxDebug.Assert(desc.Size >= storageSize);

        var stagingBuffer = _ctx.BuffersPool.Get(this._stagingBuffer);

        HxDebug.Assert(stagingBuffer, "Staging buffer is not valid, cannot upload image data.");
        if (!stagingBuffer)
        {
            _logger.LogError("Staging buffer is not valid, cannot upload image data.");
            return ResultCode.InvalidState;
        }

        // 1. Copy the pixel data into the host visible staging buffer
        stagingBuffer!.BufferSubData(desc.Offset, storageSize, data);

        var cmdBuf = _ctx.Immediate!.Acquire();
        // 1. Transition initial image layout into TRANSFER_DST_OPTIMAL
        cmdBuf.Instance.ImageMemoryBarrier2(
            image.Image,
            new StageAccess2
            {
                Stage = VkPipelineStageFlags2.TopOfPipe,
                Access = VkAccessFlags2.None,
            },
            new StageAccess2
            {
                Stage = VkPipelineStageFlags2.Transfer,
                Access = VkAccessFlags2.TransferWrite,
            },
            VK.VK_IMAGE_LAYOUT_UNDEFINED,
            VK.VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,
            new VkImageSubresourceRange(VK.VK_IMAGE_ASPECT_COLOR_BIT, 0, 1, 0, 1)
        );

        // 2. Copy the pixel data from the staging buffer into the image
        VkBufferImageCopy copy = new()
        {
            bufferOffset = desc.Offset,
            bufferRowLength = 0,
            bufferImageHeight = 0,
            imageSubresource = new VkImageSubresourceLayers(VK.VK_IMAGE_ASPECT_COLOR_BIT, 0, 0, 1),
            imageOffset = offset,
            imageExtent = extent,
        };
        unsafe
        {
            VK.vkCmdCopyBufferToImage(
                cmdBuf.Instance,
                stagingBuffer.VkBuffer,
                image.Image,
                VK.VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,
                1,
                &copy
            );
        }
        // 3. Transition TRANSFER_DST_OPTIMAL into SHADER_READ_ONLY_OPTIMAL
        cmdBuf.Instance.ImageMemoryBarrier2(
            image.Image,
            new StageAccess2
            {
                Stage = VkPipelineStageFlags2.Transfer,
                Access = VkAccessFlags2.TransferWrite,
            },
            new StageAccess2
            {
                Stage = VkPipelineStageFlags2.AllCommands,
                Access = VkAccessFlags2.MemoryRead | VkAccessFlags2.MemoryWrite,
            },
            VK.VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,
            VK.VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,
            new VkImageSubresourceRange(VK.VK_IMAGE_ASPECT_COLOR_BIT, 0, 1, 0, 1)
        );

        image.ImageLayout = VK.VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;

        desc.Handle = _ctx.Immediate!.Submit(cmdBuf);
        _regions.Add(desc);
        return ResultCode.Ok;
    }

    public ResultCode GetBufferData(
        in VulkanBuffer buffer,
        size_t offset,
        nint outData,
        size_t outDataSize
    )
    {
        HxDebug.Assert(outDataSize + offset <= buffer.BufferSize);
        EnsureStagingBufferSize(outDataSize);
        HxDebug.Assert(outDataSize <= _stagingBufferSize);
        // get next staging buffer free offset
        MemoryRegionDesc desc = GetNextFreeOffset(outDataSize);
        // No support for copying image in multiple smaller chunk sizes.
        // If we get smaller buffer size than storageSize, we will wait for GPU idle and get a bigger chunk.
        if (desc.Size < outDataSize)
        {
            WaitAndReset();
            desc = GetNextFreeOffset(outDataSize);
        }

        HxDebug.Assert(desc.Size >= outDataSize);

        var stagingBuffer = _ctx.BuffersPool.Get(this._stagingBuffer);

        HxDebug.Assert(stagingBuffer, "Staging buffer is not valid, cannot upload image data.");
        if (!stagingBuffer)
        {
            _logger.LogError("Staging buffer is not valid, cannot upload image data.");
            return ResultCode.InvalidState;
        }

        var cmdBuf = _ctx.Immediate!.Acquire();

        // 1. Transition to VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL
        cmdBuf.Instance.BufferBarrier2(
            buffer,
            VkPipelineStageFlags2.BottomOfPipe,
            VkPipelineStageFlags2.Transfer
        );

        VkBufferCopy copy = new()
        {
            srcOffset = offset,
            dstOffset = desc.Offset,
            size = outDataSize,
        };

        unsafe
        {
            VK.vkCmdCopyBuffer(cmdBuf.Instance, buffer.VkBuffer, stagingBuffer!.VkBuffer, 1, &copy);
        }

        desc.Handle = _ctx.Immediate!.Submit(cmdBuf);
        _regions.Add(desc);

        WaitAndReset();

        if (!stagingBuffer.IsCoherentMemory)
        {
            stagingBuffer.InvalidateMappedMemory(desc.Offset, desc.Size);
        }

        // 3. Copy data from staging buffer into data
        NativeHelper.MemoryCopy(
            outData,
            (nint)(stagingBuffer.MappedPtr + desc.Offset),
            outDataSize
        );

        // 4. Transition back to the initial image layout
        var cmdBuf2 = _ctx.Immediate!.Acquire();

        cmdBuf2.Instance.BufferBarrier2(
            buffer,
            VkPipelineStageFlags2.Transfer,
            VkPipelineStageFlags2.TopOfPipe
        );

        _ctx.Immediate!.Wait(_ctx.Immediate!.Submit(cmdBuf2));
        return ResultCode.Ok;
    }

    public ResultCode GetImageData(
        in VulkanImage image,
        in VkOffset3D offset,
        in VkExtent3D extent,
        in VkImageSubresourceRange range,
        VkFormat format,
        nint outData,
        size_t outDataSize
    )
    {
        HxDebug.Assert(image.ImageLayout != VK.VK_IMAGE_LAYOUT_UNDEFINED);
        HxDebug.Assert(range.layerCount == 1);

        uint32_t storageSize =
            extent.width * extent.height * extent.depth * format.GetBytesPerPixel();

        EnsureStagingBufferSize(storageSize);

        HxDebug.Assert(storageSize <= _stagingBufferSize);

        // get next staging buffer free offset
        MemoryRegionDesc desc = GetNextFreeOffset(storageSize);

        // No support for copying image in multiple smaller chunk sizes.
        // If we get smaller buffer size than storageSize, we will wait for GPU idle and get a bigger chunk.
        if (desc.Size < storageSize)
        {
            WaitAndReset();
            desc = GetNextFreeOffset(storageSize);
        }

        HxDebug.Assert(desc.Size >= storageSize);

        var stagingBuffer = _ctx.BuffersPool.Get(this._stagingBuffer);

        HxDebug.Assert(stagingBuffer, "Staging buffer is not valid, cannot upload image data.");
        if (!stagingBuffer)
        {
            _logger.LogError("Staging buffer is not valid, cannot upload image data.");
            return ResultCode.InvalidState;
        }

        var cmdBuf = _ctx.Immediate!.Acquire();

        // 1. Transition to VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL
        cmdBuf.Instance.ImageMemoryBarrier2(
            image.Image,
            new StageAccess2
            {
                Stage = VkPipelineStageFlags2.BottomOfPipe,
                Access = VkAccessFlags2.MemoryRead | VkAccessFlags2.MemoryWrite,
            },
            new StageAccess2
            {
                Stage = VkPipelineStageFlags2.Transfer,
                Access = VkAccessFlags2.TransferRead,
            },
            image.ImageLayout,
            VK.VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,
            range
        );

        // 2.  Copy the pixel data from the image into the staging buffer
        VkBufferImageCopy copy = new()
        {
            bufferOffset = desc.Offset,
            bufferRowLength = 0,
            bufferImageHeight = extent.height,
            imageSubresource = new VkImageSubresourceLayers
            {
                aspectMask = range.aspectMask,
                mipLevel = range.baseMipLevel,
                baseArrayLayer = range.baseArrayLayer,
                layerCount = range.layerCount,
            },
            imageOffset = offset,
            imageExtent = extent,
        };
        unsafe
        {
            VK.vkCmdCopyImageToBuffer(
                cmdBuf.Instance,
                image.Image,
                VK.VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,
                stagingBuffer!.VkBuffer,
                1,
                &copy
            );
        }

        desc.Handle = _ctx.Immediate!.Submit(cmdBuf);
        _regions.Add(desc);

        WaitAndReset();

        if (!stagingBuffer.IsCoherentMemory)
        {
            stagingBuffer.InvalidateMappedMemory(desc.Offset, desc.Size);
        }

        // 3. Copy data from staging buffer into data
        NativeHelper.MemoryCopy(
            outData,
            (nint)(stagingBuffer.MappedPtr + desc.Offset),
            storageSize
        );

        // 4. Transition back to the initial image layout
        var cmdBuf2 = _ctx.Immediate!.Acquire();

        cmdBuf2.Instance.ImageMemoryBarrier2(
            image.Image,
            new StageAccess2
            {
                Stage = VkPipelineStageFlags2.Transfer,
                Access = VkAccessFlags2.TransferRead,
            },
            new StageAccess2
            {
                Stage = VkPipelineStageFlags2.TopOfPipe,
                Access = VkAccessFlags2.MemoryRead | VkAccessFlags2.MemoryWrite,
            },
            VK.VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,
            image.ImageLayout,
            range
        );

        _ctx.Immediate!.Wait(_ctx.Immediate!.Submit(cmdBuf2));
        return ResultCode.Ok;
    }

    public MemoryRegionDesc GetNextFreeOffset(uint32_t size)
    {
        uint32_t requestedAlignedSize = Alignment.GetAlignedSize(size, KStagingBufferAlignment);

        EnsureStagingBufferSize(requestedAlignedSize);

        HxDebug.Assert(_regions.Count > 0);

        // if we can't find an available region that is big enough to store requestedAlignedSize, return whatever we could find, which will be
        // stored in bestNextIt
        int bestNextRegion = _regions.Count;
        uint32_t unusedSize = 0;
        uint32_t unusedOffset = 0;

        for (int i = 0; i < _regions.Count; ++i)
        {
            ref var region = ref _regions.GetInternalArray()[i];
            if (_ctx.Immediate!.IsReady(region.Handle))
            {
                // This region is free, but is it big enough?
                if (region.Size >= requestedAlignedSize)
                {
                    // It is big enough!
                    unusedSize = region.Size - requestedAlignedSize;
                    unusedOffset = region.Offset + requestedAlignedSize;

                    // Return this region and add the remaining unused size to the regions_ deque
                    using var scope = new Scope(() =>
                    {
                        _regions.RemoveAt(i);
                        if (unusedSize > 0)
                        {
                            _regions.Insert(
                                0,
                                new MemoryRegionDesc { Offset = unusedOffset, Size = unusedSize }
                            );
                        }
                    });

                    return new MemoryRegionDesc
                    {
                        Offset = region.Offset,
                        Size = requestedAlignedSize,
                    };
                }
                // cache the largest available region that isn't as big as the one we're looking for
                if (region.Size > _regions[bestNextRegion].Size)
                {
                    bestNextRegion = i;
                }
            }
        }

        // we found a region that is available that is smaller than the requested size. It's the best we can do
        if (
            bestNextRegion != _regions.Count
            && _ctx.Immediate!.IsReady(_regions[bestNextRegion].Handle)
        )
        {
            var region = _regions[bestNextRegion];
            using var scope = new Scope(() =>
            {
                _regions.RemoveAt(bestNextRegion);
            });

            return region;
            ;
        }

        // nothing was available. Let's wait for the entire staging buffer to become free
        WaitAndReset();

        // waitAndReset() adds a region that spans the entire buffer. Since we'll be using part of it, we need to replace it with a used block and
        // an unused portion
        _regions.Clear();

        // store the unused size in the deque first...
        unusedSize =
            _stagingBufferSize > requestedAlignedSize
                ? _stagingBufferSize - requestedAlignedSize
                : 0;

        if (unusedSize > 0)
        {
            unusedOffset = _stagingBufferSize - unusedSize;
            _regions.Insert(0, new MemoryRegionDesc { Offset = unusedOffset, Size = unusedSize });
        }

        // ...and then return the smallest free region that can hold the requested size
        return new MemoryRegionDesc { Offset = 0, Size = _stagingBufferSize - unusedSize };
    }

    public void EnsureStagingBufferSize(uint32_t sizeNeeded)
    {
        uint32_t alignedSize = Math.Max(
            Alignment.GetAlignedSize(sizeNeeded, KStagingBufferAlignment),
            KMinBufferSize
        );

        sizeNeeded = alignedSize < _maxBufferSize ? alignedSize : _maxBufferSize;

        if (!_stagingBuffer.Empty)
        {
            bool isEnoughSize = sizeNeeded <= _stagingBufferSize;
            bool isMaxSize = _stagingBufferSize == _maxBufferSize;

            if (isEnoughSize || isMaxSize)
            {
                return;
            }
        }

        WaitAndReset();

        // deallocate the previous staging buffer
        _stagingBuffer = BufferResource.Null;

        // if the combined size of the new staging buffer and the existing one is larger than the limit imposed by some architectures on buffers
        // that are device and host visible, we need to wait for the current buffer to be destroyed before we can allocate a new one
        if ((sizeNeeded + _stagingBufferSize) > _maxBufferSize)
        {
            _ctx.WaitDeferredTasks();
        }

        _stagingBufferSize = sizeNeeded;

        string debugName = $"Buffer: staging buffer {_stagingBufferCounter++}";
        var ret = _ctx.CreateBuffer(
            _stagingBufferSize,
            VK.VK_BUFFER_USAGE_TRANSFER_SRC_BIT | VK.VK_BUFFER_USAGE_TRANSFER_DST_BIT,
            VK.VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT,
            out var newBuf,
            debugName
        );
        if (ret.HasError())
        {
            _logger.LogError(
                "Failed to create staging buffer of size {SIZE} bytes.",
                _stagingBufferSize
            );
            return;
        }

        _stagingBuffer = new(_ctx, newBuf);
        HxDebug.Assert(_stagingBuffer.Valid);

        _regions.Clear();
        _regions.Add(new MemoryRegionDesc() { Offset = 0, Size = _stagingBufferSize });
    }

    public void WaitAndReset()
    {
        foreach (var r in _regions)
        {
            _ctx.Immediate!.Wait(r.Handle);
        }
        ;

        _regions.Clear();
        _regions.Add(new MemoryRegionDesc(0, _stagingBufferSize));
    }

    #region Dispose
    private bool _disposedValue;

    private void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                _stagingBuffer.Dispose();
                _stagingBuffer = BufferResource.Null;
            }

            // TODO: free unmanaged resources (unmanaged objects) and override finalizer
            // TODO: set large fields to null
            _disposedValue = true;
        }
    }

    // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
    // ~VulkanStagingDevice()
    // {
    //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
    //     Dispose(disposing: false);
    // }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
    }
    #endregion
};
