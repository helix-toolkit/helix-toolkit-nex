namespace HelixToolkit.Nex.Graphics.Vulkan;

internal sealed class VulkanBuffer : IDisposable
{
    static readonly ILogger logger = LogManager.Create<VulkanBuffer>();
    readonly VulkanContext? ctx;
    public readonly VkDeviceSize BufferSize = 0;
    public readonly VkBufferUsageFlags vkUsageFlags_ = 0;
    public readonly VkMemoryPropertyFlags vkMemFlags_ = 0;

    VkBuffer vkBuffer = VkBuffer.Null;
    public VkBuffer VkBuffer => vkBuffer;

    VkDeviceMemory vkMemory = VkDeviceMemory.Null;
    public VkDeviceMemory VkMemory => vkMemory;

    VmaAllocation vmaAllocation = VmaAllocation.Null;
    public VmaAllocation VmaAllocation => vmaAllocation;

    VkDeviceAddress vkDeviceAddress = 0;
    public VkDeviceAddress VkDeviceAddress => vkDeviceAddress;

    nint mappedPtr = nint.Zero;
    public nint MappedPtr => mappedPtr;

    bool isCoherentMemory_ = false;
    public bool IsCoherentMemory => isCoherentMemory_;

    public bool IsMapped => mappedPtr != nint.Zero;

    public bool Valid => ctx is not null && (vkBuffer != VkBuffer.Null || vkMemory != VkDeviceMemory.Null) && BufferSize > 0;

    private VulkanBuffer() { }

    public VulkanBuffer(VulkanContext ctx, VkDeviceSize bufferSize, VkBufferUsageFlags usage, VkMemoryPropertyFlags memFlags)
    {
        this.ctx = ctx;
        vkUsageFlags_ = usage;
        BufferSize = bufferSize;
        vkMemFlags_ = memFlags;
    }

    public ResultCode Create(string? debugName = null)
    {
        HxDebug.Assert(ctx is not null);
        VkBufferCreateInfo ci = new()
        {
            size = BufferSize,
            usage = vkUsageFlags_,
            sharingMode = VK.VK_SHARING_MODE_EXCLUSIVE,
            queueFamilyIndexCount = 0,
        };
        if (ctx!.UseVmaAllocator)
        {
            VmaAllocationCreateInfo vmaAllocInfo = new();

            // Initialize VmaAllocation Info
            if (vkMemFlags_.HasFlag(VK.VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT))
            {
                vmaAllocInfo = new()
                {
                    flags = VmaAllocationCreateFlags.Mapped | VmaAllocationCreateFlags.HostAccessRandom,
                    requiredFlags = VK.VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT,
                    preferredFlags = VK.VK_MEMORY_PROPERTY_HOST_COHERENT_BIT | VK.VK_MEMORY_PROPERTY_HOST_CACHED_BIT,
                };
            }

            if (vkMemFlags_.HasFlag(VK.VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT))
            {
                // Check if coherent buffer is available.
                VkMemoryRequirements requirements = new();
                unsafe
                {
                    VK.vkCreateBuffer(ctx!.VkDevice, &ci, null, out vkBuffer).CheckResult();
                    VK.vkGetBufferMemoryRequirements(ctx!.VkDevice, vkBuffer, &requirements);
                    VK.vkDestroyBuffer(ctx!.VkDevice, vkBuffer, null);
                }

                vkBuffer = VkBuffer.Null;

                if ((requirements.memoryTypeBits | (uint)VK.VK_MEMORY_PROPERTY_HOST_COHERENT_BIT) != 0)
                {
                    vmaAllocInfo.requiredFlags |= VK.VK_MEMORY_PROPERTY_HOST_COHERENT_BIT;
                    isCoherentMemory_ = true;
                }
            }

            vmaAllocInfo.usage = VmaMemoryUsage.Auto;
            unsafe
            {
                Vma.vmaCreateBufferWithAlignment(ctx!.VmaAllocator, &ci, &vmaAllocInfo, 16, out vkBuffer, out vmaAllocation, out _);

                // handle memory-mapped buffers
                if (vkMemFlags_.HasFlag(VK.VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT))
                {
                    void* mappedPtr;
                    Vma.vmaMapMemory(ctx!.VmaAllocator, vmaAllocation, &mappedPtr);
                    this.mappedPtr = (nint)mappedPtr;
                }
            }
        }
        else
        {
            unsafe
            {
                VK.vkCreateBuffer(ctx!.VkDevice, &ci, null, out vkBuffer);

                // back the buffer with some memory
                {
                    VkBufferMemoryRequirementsInfo2 ri = new()
                    {
                        buffer = vkBuffer,
                    };
                    VkMemoryRequirements2 requirements = new();
                    VK.vkGetBufferMemoryRequirements2(ctx!.VkDevice, &ri, &requirements);
                    if ((requirements.memoryRequirements.memoryTypeBits & (uint)VK.VK_MEMORY_PROPERTY_HOST_COHERENT_BIT) != 0)
                    {
                        isCoherentMemory_ = true;
                    }

                    var ret = HxVkUtils.AllocateMemory2(ctx!.VkPhysicalDevice, ctx!.VkDevice, requirements, vkMemFlags_, out vkMemory);
                    if (ret != VkResult.Success)
                    {
                        logger.LogError("Failed to allocate memory for buffer: {REASON}", ret);
                        return ResultCode.RuntimeError;
                    }
                    VK.vkBindBufferMemory(ctx!.VkDevice, vkBuffer, vkMemory, 0).CheckResult();
                }

                // handle memory-mapped buffers
                if (vkMemFlags_.HasFlag(VK.VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT))
                {
                    void* mappedPtr;
                    VK.vkMapMemory(ctx!.VkDevice, vkMemory, 0, BufferSize, 0, &mappedPtr).CheckResult();
                    this.mappedPtr = (nint)mappedPtr;
                }
            }
        }
        HxDebug.Assert(vkBuffer != VkBuffer.Null);

        if (GraphicsSettings.EnableDebug && !string.IsNullOrEmpty(debugName))
        {
            // set debug name
            ctx!.VkDevice.SetDebugObjectName(VK.VK_OBJECT_TYPE_BUFFER, (nuint)vkBuffer, debugName);
        }

        // handle shader access
        if (vkUsageFlags_.HasFlag(VK.VK_BUFFER_USAGE_SHADER_DEVICE_ADDRESS_BIT))
        {
            VkBufferDeviceAddressInfo ai = new()
            {
                buffer = vkBuffer,
            };
            unsafe
            {
                vkDeviceAddress = VK.vkGetBufferDeviceAddress(ctx!.VkDevice, &ai);
            }

            HxDebug.Assert(vkDeviceAddress != 0);
        }
        return ResultCode.Ok;
    }

    public ResultCode BufferSubData(size_t offset, size_t size, nint data)
    {
        // only host-visible buffers can be uploaded this way
        HxDebug.Assert(IsMapped);
        if (!IsMapped)
        {
            logger.LogError("Buffer is not mapped, cannot upload data.");
            return ResultCode.InvalidState;
        }

        HxDebug.Assert(offset + size <= BufferSize);

        unsafe
        {
            if (data != nint.Zero)
            {
                NativeHelper.MemoryCopy((nint)(mappedPtr + offset), data, size);

            }
            else
            {
                Span<byte> dest = new((byte*)mappedPtr.ToPointer() + (long)offset, (int)size);
                dest.Clear();

            }
        }

        if (!isCoherentMemory_)
        {
            FlushMappedMemory(offset, size);
        }
        return ResultCode.Ok;
    }

    public ResultCode GetBufferSubData(size_t offset, size_t size, nint data)
    {
        // only host-visible buffers can be downloaded this way
        HxDebug.Assert(IsMapped);

        if (!IsMapped)
        {
            logger.LogError("Buffer is not mapped, cannot download data.");
            return ResultCode.InvalidState;
        }

        HxDebug.Assert(offset + size <= BufferSize);

        if (!isCoherentMemory_)
        {
            InvalidateMappedMemory(offset, size);
        }

        unsafe
        {
            var src = (nint)(mappedPtr + offset);
            NativeHelper.MemoryCopy(data, src, size);
        }
        return ResultCode.Ok;
    }

    public void FlushMappedMemory(VkDeviceSize offset, VkDeviceSize size)
    {
        HxDebug.Assert(IsMapped);
        if (!IsMapped)
        {
            return;
        }

        if (ctx!.UseVmaAllocator)
        {
            Vma.vmaFlushAllocation(ctx!.VmaAllocator, vmaAllocation, offset, size);
        }
        else
        {
            unsafe
            {
                VkMappedMemoryRange range = new()
                {
                    memory = vkMemory,
                    offset = offset,
                    size = size,
                };
                VK.vkFlushMappedMemoryRanges(ctx!.GetVkDevice(), 1, &range);
            }
        }
    }

    public void InvalidateMappedMemory(VkDeviceSize offset, VkDeviceSize size)
    {
        HxDebug.Assert(IsMapped);

        if (!IsMapped)
        {
            return;
        }

        if (ctx!.UseVmaAllocator)
        {
            Vma.vmaInvalidateAllocation(ctx!.VmaAllocator, vmaAllocation, offset, size);
        }
        else
        {
            unsafe
            {
                VkMappedMemoryRange range = new()
                {
                    memory = vkMemory,
                    offset = offset,
                    size = size,
                };
                VK.vkInvalidateMappedMemoryRanges(ctx!.GetVkDevice(), 1, &range);
            }
        }
    }
    #region IDisposable Support
    private bool disposedValue;
    private void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                if (!Valid)
                {
                    return;
                }
                if (ctx!.UseVmaAllocator)
                {
                    if (mappedPtr.Valid())
                    {
                        Vma.vmaUnmapMemory(ctx!.VmaAllocator, vmaAllocation);
                    }
                    ctx!.DeferredTask(() =>
                    {
                        Vma.vmaDestroyBuffer(ctx!.VmaAllocator, vkBuffer, vmaAllocation);
                    }, SubmitHandle.Null);
                }
                else
                {
                    if (mappedPtr.Valid())
                    {
                        VK.vkUnmapMemory(ctx!.VkDevice, vkMemory);
                    }

                    ctx!.DeferredTask(() =>
                    {
                        unsafe
                        {
                            VK.vkDestroyBuffer(ctx!.VkDevice, vkBuffer, null);
                            VK.vkFreeMemory(ctx!.VkDevice, vkMemory, null);
                        }
                    }, SubmitHandle.Null);
                }
            }

            // TODO: free unmanaged resources (unmanaged objects) and override finalizer
            // TODO: set large fields to null
            disposedValue = true;
        }
    }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
    }
    #endregion

    public static readonly VulkanBuffer Null = new();

    public static implicit operator bool(VulkanBuffer? buffer)
    {
        return buffer is not null && buffer.Valid;
    }
}

