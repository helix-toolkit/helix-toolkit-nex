using System;
using static System.Net.Mime.MediaTypeNames;

namespace HelixToolkit.Nex.Graphics.Vulkan;

internal struct MemoryRegionDesc(uint32_t offset, uint32_t size)
{
    public uint32_t offset = offset;
    public uint32_t size = size;
    public SubmitHandle handle = SubmitHandle.Null;
}

internal sealed class VulkanStagingDevice : IDisposable
{
    const uint32_t kStagingBufferAlignment = 16; // updated to support BC7 compressed image
    const uint32_t kMinBufferSize = 4u * 2048u * 2048u;

    static readonly ILogger logger = LogManager.Create<VulkanStagingDevice>();

    readonly VulkanContext ctx;
    readonly uint32_t maxBufferSize;
    readonly FastList<MemoryRegionDesc> regions = [];

    BufferHolder stagingBuffer = BufferHolder.Null;
    uint32_t stagingBufferSize = 0;
    uint32_t stagingBufferCounter = 0;


    public VulkanStagingDevice(in VulkanContext ctx)
    {
        this.ctx = ctx;
        ref readonly var limits = ref this.ctx.GetVkPhysicalDeviceProperties().limits;

        // use default value of 128Mb clamped to the max limits
        maxBufferSize = Math.Min(limits.maxStorageBufferRange, 128u * 1024u * 1024u);

        HxDebug.Assert(kMinBufferSize <= maxBufferSize);
    }

    public ResultCode BufferSubData(in VulkanBuffer buffer, size_t dstOffset, size_t size, nint data)
    {
        HxDebug.Assert(ctx.Immediate is not null);
        if (buffer.IsMapped)
        {
            buffer.BufferSubData(dstOffset, size, data);
            return ResultCode.Ok;
        }

        var stagingBuffer = ctx.BuffersPool.Get(this.stagingBuffer);

        HxDebug.Assert(stagingBuffer);

        if (!stagingBuffer)
        {
            logger.LogError("Staging buffer is not valid, cannot upload data.");
            return ResultCode.InvalidState;
        }

        while (size > 0)
        {
            // get next staging buffer free offset
            MemoryRegionDesc desc = GetNextFreeOffset(size);
            uint32_t chunkSize = Math.Min(size, desc.size);

            // copy data into staging buffer
            stagingBuffer!.BufferSubData(desc.offset, chunkSize, data);

            // do the transfer
            VkBufferCopy copy = new()
            {
                srcOffset = desc.offset,
                dstOffset = dstOffset,
                size = chunkSize,
            };

            ref VulkanImmediateCommands.CommandBufferWrapper wrapper = ref ctx.Immediate!.Acquire();
            unsafe
            {
                VK.vkCmdCopyBuffer(wrapper.cmdBuf, stagingBuffer.VkBuffer, buffer.VkBuffer, 1, &copy);


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
                if (buffer.vkUsageFlags_.HasFlag(VK.VK_BUFFER_USAGE_INDIRECT_BUFFER_BIT))
                {
                    dstMask |= VK.VK_PIPELINE_STAGE_DRAW_INDIRECT_BIT;
                    barrier.dstAccessMask |= VK.VK_ACCESS_INDIRECT_COMMAND_READ_BIT;
                }
                if (buffer.vkUsageFlags_.HasFlag(VK.VK_BUFFER_USAGE_INDEX_BUFFER_BIT))
                {
                    dstMask |= VK.VK_PIPELINE_STAGE_VERTEX_INPUT_BIT;
                    barrier.dstAccessMask |= VK.VK_ACCESS_INDEX_READ_BIT;
                }
                if (buffer.vkUsageFlags_.HasFlag(VK.VK_BUFFER_USAGE_VERTEX_BUFFER_BIT))
                {
                    dstMask |= VK.VK_PIPELINE_STAGE_VERTEX_INPUT_BIT;
                    barrier.dstAccessMask |= VK.VK_ACCESS_VERTEX_ATTRIBUTE_READ_BIT;
                }
                if (buffer.vkUsageFlags_.HasFlag(VK.VK_BUFFER_USAGE_ACCELERATION_STRUCTURE_BUILD_INPUT_READ_ONLY_BIT_KHR))
                {
                    dstMask |= VK.VK_PIPELINE_STAGE_ACCELERATION_STRUCTURE_BUILD_BIT_KHR;
                    barrier.dstAccessMask |= VK.VK_ACCESS_MEMORY_READ_BIT;
                }
                VK.vkCmdPipelineBarrier(
                    wrapper.cmdBuf, VK.VK_PIPELINE_STAGE_TRANSFER_BIT, dstMask, new VkDependencyFlags { }, 0, null, 1, &barrier, 0, null);
                desc.handle = ctx.Immediate!.Submit(ref wrapper);
                regions.Add(desc);

                size -= chunkSize;
                data = (nint)((uint8_t*)data + chunkSize);
                dstOffset += chunkSize;
            }
        }
        return ResultCode.Ok;
    }

    public ResultCode ImageData2D(in VulkanImage image, in VkRect2D imageRegion, uint32_t baseMipLevel, uint32_t numMipLevels,
        uint32_t layer, uint32_t numLayers, VkFormat format, nint data, size_t dataSize)
    {
        HxDebug.Assert(numMipLevels <= Constants.LVK_MAX_MIP_LEVELS);

        // divide the width and height by 2 until we get to the size of level 'baseMipLevel'
        uint32_t width = image.Extent.width >> (int)baseMipLevel;
        uint32_t height = image.Extent.height >> (int)baseMipLevel;

        var texFormat = format.ToFormat();

        HxDebug.Assert(imageRegion.offset.x == 0 && imageRegion.offset.y == 0 && imageRegion.extent.width == width && imageRegion.extent.height == height,
                       "Uploading mip-levels with an image region that is smaller than the base mip level is not supported");

        // find the storage size for all mip-levels being uploaded
        uint32_t layerStorageSize = 0;
        for (uint32_t i = 0; i < numMipLevels; ++i)
        {
            uint32_t mipSize = HxVkUtils.GetTextureBytesPerLayer(image.Extent.width, image.Extent.height, texFormat, i);
            layerStorageSize += mipSize;
            width = width <= 1 ? 1 : width >> 1;
            height = height <= 1 ? 1 : height >> 1;
        }
        uint32_t storageSize = layerStorageSize * numLayers;
        EnsureStagingBufferSize(storageSize);

        HxDebug.Assert(storageSize <= stagingBufferSize);

        var desc = GetNextFreeOffset(storageSize);
        // No support for copying image in multiple smaller chunk sizes. If we get smaller buffer size than storageSize, we will wait for GPU idle
        // and get bigger chunk.
        if (desc.size < storageSize)
        {
            WaitAndReset();
            desc = GetNextFreeOffset(storageSize);
        }
        HxDebug.Assert(desc.size >= storageSize);

        ref var wrapper = ref ctx.Immediate!.Acquire();
        ref var cmdBuf = ref wrapper.cmdBuf;

        var stagingBuffer = ctx.BuffersPool.Get(this.stagingBuffer);

        HxDebug.Assert(stagingBuffer, "Staging buffer is not valid, cannot upload image data.");

        if (!stagingBuffer)
        {
            logger.LogError("Staging buffer is not valid, cannot upload image data.");
            return ResultCode.InvalidState;
        }

        var result = stagingBuffer!.BufferSubData(desc.offset, storageSize, data);
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
            HxDebug.Assert(image.Extent.width == imageRegion.extent.width && image.Extent.height == imageRegion.extent.height);
        }

        VkImageAspectFlags imageAspect = VK.VK_IMAGE_ASPECT_COLOR_BIT;

        if (numPlanes == 2)
        {
            imageAspect = VK.VK_IMAGE_ASPECT_PLANE_0_BIT | VK.VK_IMAGE_ASPECT_PLANE_1_BIT;
        }
        if (numPlanes == 3)
        {
            imageAspect = VK.VK_IMAGE_ASPECT_PLANE_0_BIT | VK.VK_IMAGE_ASPECT_PLANE_1_BIT | VK.VK_IMAGE_ASPECT_PLANE_2_BIT;
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
                cmdBuf.ImageMemoryBarrier2(image.Image, new StageAccess2
                {
                    stage = VkPipelineStageFlags2.TopOfPipe,
                    access = VkAccessFlags2.None
                },
                new StageAccess2
                {
                    stage = VkPipelineStageFlags2.Transfer,
                    access = VkAccessFlags2.TransferWrite
                },
                VK.VK_IMAGE_LAYOUT_UNDEFINED,
                VK.VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,
                new VkImageSubresourceRange(imageAspect, currentMipLevel, 1, layer1, 1));

                // 2. Copy the pixel data from the staging buffer into the image
                uint32_t planeOffset = 0;
                for (uint32_t plane = 0; plane != numPlanes; plane++)
                {
                    var extent = HxVkUtils.GetImagePlaneExtent(new VkExtent2D
                    {
                        width = Math.Max(1u, imageRegion.extent.width >> (int)mipLevel),
                        height = Math.Max(1u, imageRegion.extent.height >> (int)mipLevel),
                    }, format.ToFormat(), plane);
                    VkRect2D region = new()
                    {
                        offset = new VkOffset2D() { x = imageRegion.offset.x >> (int)mipLevel, y = imageRegion.offset.y >> (int)mipLevel },
                        extent = extent,
                    };
                    VkBufferImageCopy copy = new()
                    {
                        // the offset for this level is at the start of all mip-levels plus the size of all previous mip-levels being uploaded
                        bufferOffset = desc.offset + offset + planeOffset,
                        bufferRowLength = 0,
                        bufferImageHeight = 0,
                        imageSubresource =
                            new VkImageSubresourceLayers
                            {
                                aspectMask = numPlanes > 1 ? (VkImageAspectFlags)((uint)VkImageAspectFlags.Plane0 << (int)plane) : imageAspect,
                                mipLevel = currentMipLevel,
                                baseArrayLayer = layer1,
                                layerCount = 1
                            },
                        imageOffset = { x = region.offset.x, y = region.offset.y, z = 0 },
                        imageExtent = { width = region.extent.width, height = region.extent.height, depth = 1u },
                    };
                    unsafe
                    {
                        VK.vkCmdCopyBufferToImage(wrapper.cmdBuf, stagingBuffer.VkBuffer, image.Image, VK.VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, 1, &copy);
                    }
                    planeOffset += HxVkUtils.GetTextureBytesPerPlane(imageRegion.extent.width, imageRegion.extent.height, format.ToFormat(), plane);
                }

                // 3. Transition TRANSFER_DST_OPTIMAL into SHADER_READ_ONLY_OPTIMAL
                cmdBuf.ImageMemoryBarrier2(image.Image, new StageAccess2
                {
                    stage = VkPipelineStageFlags2.Transfer,
                    access = VkAccessFlags2.TransferWrite
                }, new StageAccess2
                {
                    stage = VkPipelineStageFlags2.AllCommands,
                    access = VkAccessFlags2.MemoryRead | VkAccessFlags2.MemoryWrite
                }, VK.VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, VK.VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,
                new VkImageSubresourceRange
                (
                    imageAspect, currentMipLevel, 1, layer, 1
                ));

                offset += HxVkUtils.GetTextureBytesPerLayer(imageRegion.extent.width, imageRegion.extent.height, texFormat, currentMipLevel);
            }
        }

        image.ImageLayout = VK.VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;

        desc.handle = ctx.Immediate!.Submit(ref wrapper);
        regions.Add(desc);
        return ResultCode.Ok;
    }

    public ResultCode ImageData3D(in VulkanImage image, in VkOffset3D offset, in VkExtent3D extent, VkFormat format, nint data, size_t dataSize)
    {
        HxDebug.Assert(image.NumLevels == 1, "Can handle only 3D images with exactly 1 mip-level");
        HxDebug.Assert((offset.x == 0) && (offset.y == 0) && (offset.z == 0), "Can upload only full-size 3D images");
        uint32_t storageSize = extent.width * extent.height * extent.depth * format.GetBytesPerPixel();

        EnsureStagingBufferSize(storageSize);

        HxDebug.Assert(storageSize <= stagingBufferSize, "No support for copying image in multiple smaller chunk sizes");

        // get next staging buffer free offset
        MemoryRegionDesc desc = GetNextFreeOffset(storageSize);

        // No support for copying image in multiple smaller chunk sizes.
        // If we get smaller buffer size than storageSize, we will wait for GPU idle and get a bigger chunk.
        if (desc.size < storageSize)
        {
            WaitAndReset();
            desc = GetNextFreeOffset(storageSize);
        }

        HxDebug.Assert(desc.size >= storageSize);

        var stagingBuffer = ctx.BuffersPool.Get(this.stagingBuffer);

        HxDebug.Assert(stagingBuffer, "Staging buffer is not valid, cannot upload image data.");
        if (!stagingBuffer)
        {
            logger.LogError("Staging buffer is not valid, cannot upload image data.");
            return ResultCode.InvalidState;
        }

        // 1. Copy the pixel data into the host visible staging buffer
        stagingBuffer!.BufferSubData(desc.offset, storageSize, data);

        ref var wrapper = ref ctx.Immediate!.Acquire();
        ref var cmdBuf = ref wrapper.cmdBuf;
        // 1. Transition initial image layout into TRANSFER_DST_OPTIMAL
        cmdBuf.ImageMemoryBarrier2(image.Image, new StageAccess2
        {
            stage = VkPipelineStageFlags2.TopOfPipe,
            access = VkAccessFlags2.None
        },
        new StageAccess2
        {
            stage = VkPipelineStageFlags2.Transfer,
            access = VkAccessFlags2.TransferWrite
        },
        VK.VK_IMAGE_LAYOUT_UNDEFINED,
        VK.VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,
        new VkImageSubresourceRange(VK.VK_IMAGE_ASPECT_COLOR_BIT, 0, 1, 0, 1));

        // 2. Copy the pixel data from the staging buffer into the image
        VkBufferImageCopy copy = new()
        {
            bufferOffset = desc.offset,
            bufferRowLength = 0,
            bufferImageHeight = 0,
            imageSubresource = new VkImageSubresourceLayers(VK.VK_IMAGE_ASPECT_COLOR_BIT, 0, 0, 1),
            imageOffset = offset,
            imageExtent = extent,
        };
        unsafe
        {
            VK.vkCmdCopyBufferToImage(wrapper.cmdBuf, stagingBuffer.VkBuffer, image.Image, VK.VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, 1, &copy);
        }
        // 3. Transition TRANSFER_DST_OPTIMAL into SHADER_READ_ONLY_OPTIMAL
        cmdBuf.ImageMemoryBarrier2(image.Image, new StageAccess2
        {
            stage = VkPipelineStageFlags2.Transfer,
            access = VkAccessFlags2.TransferWrite
        }, new StageAccess2
        {
            stage = VkPipelineStageFlags2.AllCommands,
            access = VkAccessFlags2.MemoryRead | VkAccessFlags2.MemoryWrite
        }, VK.VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, VK.VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, new VkImageSubresourceRange(VK.VK_IMAGE_ASPECT_COLOR_BIT, 0, 1, 0, 1));

        image.ImageLayout = VK.VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;

        desc.handle = ctx.Immediate!.Submit(ref wrapper);
        regions.Add(desc);
        return ResultCode.Ok;
    }

    public ResultCode GetBufferData(in VulkanBuffer buffer, size_t offset, nint outData, size_t outDataSize)
    {
        HxDebug.Assert(outDataSize + offset <= buffer.BufferSize);
        EnsureStagingBufferSize(outDataSize);
        HxDebug.Assert(outDataSize <= stagingBufferSize);
        // get next staging buffer free offset
        MemoryRegionDesc desc = GetNextFreeOffset(outDataSize);
        // No support for copying image in multiple smaller chunk sizes.
        // If we get smaller buffer size than storageSize, we will wait for GPU idle and get a bigger chunk.
        if (desc.size < outDataSize)
        {
            WaitAndReset();
            desc = GetNextFreeOffset(outDataSize);
        }

        HxDebug.Assert(desc.size >= outDataSize);

        var stagingBuffer = ctx.BuffersPool.Get(this.stagingBuffer);

        HxDebug.Assert(stagingBuffer, "Staging buffer is not valid, cannot upload image data.");
        if (!stagingBuffer)
        {
            logger.LogError("Staging buffer is not valid, cannot upload image data.");
            return ResultCode.InvalidState;
        }

        ref var wrapper = ref ctx.Immediate!.Acquire();
        ref var cmdBuf = ref wrapper.cmdBuf;

        // 1. Transition to VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL
        cmdBuf.BufferBarrier2(buffer, VkPipelineStageFlags2.BottomOfPipe, VkPipelineStageFlags2.Transfer);

        VkBufferCopy copy = new()
        {
            srcOffset = offset,
            dstOffset = desc.offset,
            size = outDataSize,
        };

        unsafe
        {
            VK.vkCmdCopyBuffer(wrapper.cmdBuf, buffer.VkBuffer, stagingBuffer!.VkBuffer, 1, &copy);
        }

        desc.handle = ctx.Immediate!.Submit(ref wrapper);
        regions.Add(desc);

        WaitAndReset();

        if (!stagingBuffer.IsCoherentMemory)
        {
            stagingBuffer.InvalidateMappedMemory(desc.offset, desc.size);
        }

        // 3. Copy data from staging buffer into data
        NativeHelper.MemoryCopy(outData, (nint)(stagingBuffer.MappedPtr + desc.offset), outDataSize);

        // 4. Transition back to the initial image layout
        ref var wrapper2 = ref ctx.Immediate!.Acquire();
        ref var cmdBuf2 = ref wrapper2.cmdBuf;

        cmdBuf2.BufferBarrier2(buffer, VkPipelineStageFlags2.Transfer, VkPipelineStageFlags2.TopOfPipe);

        ctx.Immediate!.Wait(ctx.Immediate!.Submit(ref wrapper2));
        return ResultCode.Ok;
    }

    public ResultCode GetImageData(in VulkanImage image, in VkOffset3D offset, in VkExtent3D extent, in VkImageSubresourceRange range, VkFormat format, nint outData, size_t outDataSize)
    {
        HxDebug.Assert(image.ImageLayout != VK.VK_IMAGE_LAYOUT_UNDEFINED);
        HxDebug.Assert(range.layerCount == 1);

        uint32_t storageSize = extent.width * extent.height * extent.depth * format.GetBytesPerPixel();

        EnsureStagingBufferSize(storageSize);

        HxDebug.Assert(storageSize <= stagingBufferSize);

        // get next staging buffer free offset
        MemoryRegionDesc desc = GetNextFreeOffset(storageSize);

        // No support for copying image in multiple smaller chunk sizes.
        // If we get smaller buffer size than storageSize, we will wait for GPU idle and get a bigger chunk.
        if (desc.size < storageSize)
        {
            WaitAndReset();
            desc = GetNextFreeOffset(storageSize);
        }

        HxDebug.Assert(desc.size >= storageSize);

        var stagingBuffer = ctx.BuffersPool.Get(this.stagingBuffer);

        HxDebug.Assert(stagingBuffer, "Staging buffer is not valid, cannot upload image data.");
        if (!stagingBuffer)
        {
            logger.LogError("Staging buffer is not valid, cannot upload image data.");
            return ResultCode.InvalidState;
        }

        ref var wrapper = ref ctx.Immediate!.Acquire();
        ref var cmdBuf = ref wrapper.cmdBuf;

        // 1. Transition to VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL
        cmdBuf.ImageMemoryBarrier2(image.Image, new StageAccess2
        {
            stage = VkPipelineStageFlags2.BottomOfPipe,
            access = VkAccessFlags2.MemoryRead | VkAccessFlags2.MemoryWrite
        }, new StageAccess2
        {
            stage = VkPipelineStageFlags2.Transfer,
            access = VkAccessFlags2.TransferRead
        }, image.ImageLayout, VK.VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL, range);

        // 2.  Copy the pixel data from the image into the staging buffer
        VkBufferImageCopy copy = new()
        {
            bufferOffset = desc.offset,
            bufferRowLength = 0,
            bufferImageHeight = extent.height,
            imageSubresource = new VkImageSubresourceLayers
            {
                aspectMask = range.aspectMask,
                mipLevel = range.baseMipLevel,
                baseArrayLayer = range.baseArrayLayer,
                layerCount = range.layerCount
            },
            imageOffset = offset,
            imageExtent = extent,
        };
        unsafe
        {
            VK.vkCmdCopyImageToBuffer(wrapper.cmdBuf, image.Image, VK.VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL, stagingBuffer!.VkBuffer, 1, &copy);
        }

        desc.handle = ctx.Immediate!.Submit(ref wrapper);
        regions.Add(desc);

        WaitAndReset();

        if (!stagingBuffer.IsCoherentMemory)
        {
            stagingBuffer.InvalidateMappedMemory(desc.offset, desc.size);
        }

        // 3. Copy data from staging buffer into data
        NativeHelper.MemoryCopy(outData, (nint)(stagingBuffer.MappedPtr + desc.offset), storageSize);

        // 4. Transition back to the initial image layout
        ref var wrapper2 = ref ctx.Immediate!.Acquire();
        ref var cmdBuf2 = ref wrapper2.cmdBuf;

        cmdBuf2.ImageMemoryBarrier2(image.Image,
            new StageAccess2
            {
                stage = VkPipelineStageFlags2.Transfer,
                access = VkAccessFlags2.TransferRead
            }, new StageAccess2
            {
                stage = VkPipelineStageFlags2.TopOfPipe,
                access = VkAccessFlags2.MemoryRead | VkAccessFlags2.MemoryWrite
            }, VK.VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL, image.ImageLayout, range);

        ctx.Immediate!.Wait(ctx.Immediate!.Submit(ref wrapper2));
        return ResultCode.Ok;
    }

    public MemoryRegionDesc GetNextFreeOffset(uint32_t size)
    {
        uint32_t requestedAlignedSize = Alignment.GetAlignedSize(size, kStagingBufferAlignment);

        EnsureStagingBufferSize(requestedAlignedSize);

        HxDebug.Assert(regions.Count > 0);

        // if we can't find an available region that is big enough to store requestedAlignedSize, return whatever we could find, which will be
        // stored in bestNextIt
        int bestNextRegion = regions.Count;
        uint32_t unusedSize = 0;
        uint32_t unusedOffset = 0;

        for (int i = 0; i < regions.Count; ++i)
        {
            ref var region = ref regions.GetInternalArray()[i];
            if (ctx.Immediate!.IsReady(region.handle))
            {
                // This region is free, but is it big enough?
                if (region.size >= requestedAlignedSize)
                {
                    // It is big enough!
                    unusedSize = region.size - requestedAlignedSize;
                    unusedOffset = region.offset + requestedAlignedSize;

                    // Return this region and add the remaining unused size to the regions_ deque
                    using var scope = new Scope(() =>
                    {
                        regions.RemoveAt(i);
                        if (unusedSize > 0)
                        {
                            regions.Insert(0, new MemoryRegionDesc { offset = unusedOffset, size = unusedSize });
                        }
                    });

                    return new MemoryRegionDesc { offset = region.offset, size = requestedAlignedSize };
                }
                // cache the largest available region that isn't as big as the one we're looking for
                if (region.size > regions[bestNextRegion].size)
                {
                    bestNextRegion = i;
                }
            }
        }

        // we found a region that is available that is smaller than the requested size. It's the best we can do
        if (bestNextRegion != regions.Count && ctx.Immediate!.IsReady(regions[bestNextRegion].handle))
        {
            var region = regions[bestNextRegion];
            using var scope = new Scope(() =>
            {
                regions.RemoveAt(bestNextRegion);
            });

            return region;
            ;
        }

        // nothing was available. Let's wait for the entire staging buffer to become free
        WaitAndReset();

        // waitAndReset() adds a region that spans the entire buffer. Since we'll be using part of it, we need to replace it with a used block and
        // an unused portion
        regions.Clear();

        // store the unused size in the deque first...
        unusedSize = stagingBufferSize > requestedAlignedSize ? stagingBufferSize - requestedAlignedSize : 0;

        if (unusedSize > 0)
        {
            unusedOffset = stagingBufferSize - unusedSize;
            regions.Insert(0, new MemoryRegionDesc { offset = unusedOffset, size = unusedSize });
        }

        // ...and then return the smallest free region that can hold the requested size
        return new MemoryRegionDesc
        {
            offset = 0,
            size = stagingBufferSize - unusedSize,
        };
    }

    public void EnsureStagingBufferSize(uint32_t sizeNeeded)
    {
        uint32_t alignedSize = Math.Max(Alignment.GetAlignedSize(sizeNeeded, kStagingBufferAlignment), kMinBufferSize);

        sizeNeeded = alignedSize < maxBufferSize ? alignedSize : maxBufferSize;

        if (!stagingBuffer.Empty)
        {
            bool isEnoughSize = sizeNeeded <= stagingBufferSize;
            bool isMaxSize = stagingBufferSize == maxBufferSize;

            if (isEnoughSize || isMaxSize)
            {
                return;
            }
        }

        WaitAndReset();

        // deallocate the previous staging buffer
        stagingBuffer = BufferHolder.Null;

        // if the combined size of the new staging buffer and the existing one is larger than the limit imposed by some architectures on buffers
        // that are device and host visible, we need to wait for the current buffer to be destroyed before we can allocate a new one
        if ((sizeNeeded + stagingBufferSize) > maxBufferSize)
        {
            ctx.WaitDeferredTasks();
        }

        stagingBufferSize = sizeNeeded;

        string debugName = $"Buffer: staging buffer {stagingBufferCounter++}";
        var ret = ctx.CreateBuffer(stagingBufferSize,
                                      VK.VK_BUFFER_USAGE_TRANSFER_SRC_BIT | VK.VK_BUFFER_USAGE_TRANSFER_DST_BIT,
                                      VK.VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT,
                                      out var newBuf,
                                      debugName);
        if (ret.HasError())
        {
            logger.LogError("Failed to create staging buffer of size {SIZE} bytes.", stagingBufferSize);
            return;
        }

        stagingBuffer = new(ctx, newBuf);
        HxDebug.Assert(stagingBuffer.Valid);

        regions.Clear();
        regions.Add(new MemoryRegionDesc() { offset = 0, size = stagingBufferSize });
    }

    public void WaitAndReset()
    {
        foreach (var r in regions)
        {
            ctx.Immediate!.Wait(r.handle);
        }
        ;

        regions.Clear();
        regions.Add(new MemoryRegionDesc(0, stagingBufferSize));
    }

    #region Dispose
    bool disposedValue;
    private void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                stagingBuffer.Dispose();
                stagingBuffer = BufferHolder.Null;
            }

            // TODO: free unmanaged resources (unmanaged objects) and override finalizer
            // TODO: set large fields to null
            disposedValue = true;
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