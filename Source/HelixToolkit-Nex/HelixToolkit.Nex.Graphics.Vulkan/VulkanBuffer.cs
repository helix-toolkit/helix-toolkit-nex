namespace HelixToolkit.Nex.Graphics.Vulkan;

internal sealed class VulkanBuffer : IDisposable
{
    private static readonly ILogger _logger = LogManager.Create<VulkanBuffer>();
    private readonly VulkanContext? _ctx;
    public readonly VkDeviceSize BufferSize = 0;
    public readonly VkBufferUsageFlags VkUsageFlags = 0;
    public readonly VkMemoryPropertyFlags VkMemFlags = 0;

    private VkBuffer _vkBuffer = VkBuffer.Null;
    public VkBuffer VkBuffer => _vkBuffer;

    private VkDeviceMemory _vkMemory = VkDeviceMemory.Null;
    public VkDeviceMemory VkMemory => _vkMemory;

    private VmaAllocation _vmaAllocation = VmaAllocation.Null;
    public VmaAllocation VmaAllocation => _vmaAllocation;

    private VkDeviceAddress _vkDeviceAddress = 0;
    public VkDeviceAddress VkDeviceAddress => _vkDeviceAddress;

    private nint _mappedPtr = nint.Zero;
    public nint MappedPtr => _mappedPtr;

    private bool _isCoherentMemory = false;
    public bool IsCoherentMemory => _isCoherentMemory;

    public bool IsMapped => _mappedPtr != nint.Zero;

    public bool Valid =>
        _ctx is not null
        && (_vkBuffer != VkBuffer.Null || _vkMemory != VkDeviceMemory.Null)
        && BufferSize > 0;

    private VulkanBuffer() { }

    public VulkanBuffer(
        VulkanContext ctx,
        VkDeviceSize bufferSize,
        VkBufferUsageFlags usage,
        VkMemoryPropertyFlags memFlags
    )
    {
        _ctx = ctx;
        VkUsageFlags = usage;
        BufferSize = bufferSize;
        VkMemFlags = memFlags;
    }

    public ResultCode Create(string? debugName = null)
    {
        HxDebug.Assert(_ctx is not null);
        VkBufferCreateInfo ci = new()
        {
            size = BufferSize,
            usage = VkUsageFlags,
            sharingMode = VK.VK_SHARING_MODE_EXCLUSIVE,
            queueFamilyIndexCount = 0,
        };
        if (_ctx!.UseVmaAllocator)
        {
            VmaAllocationCreateInfo vmaAllocInfo = new();

            // Initialize VmaAllocation Info
            if (VkMemFlags.HasFlag(VK.VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT))
            {
                vmaAllocInfo = new()
                {
                    flags =
                        VmaAllocationCreateFlags.Mapped | VmaAllocationCreateFlags.HostAccessRandom,
                    requiredFlags = VK.VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT,
                    preferredFlags =
                        VK.VK_MEMORY_PROPERTY_HOST_COHERENT_BIT
                        | VK.VK_MEMORY_PROPERTY_HOST_CACHED_BIT,
                };
            }

            if (VkMemFlags.HasFlag(VK.VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT))
            {
                // Check if coherent buffer is available.
                VkMemoryRequirements requirements = new();
                unsafe
                {
                    VK.vkCreateBuffer(_ctx!.VkDevice, &ci, null, out _vkBuffer).CheckResult();
                    VK.vkGetBufferMemoryRequirements(_ctx!.VkDevice, _vkBuffer, &requirements);
                    VK.vkDestroyBuffer(_ctx!.VkDevice, _vkBuffer, null);
                }

                _vkBuffer = VkBuffer.Null;

                if (
                    (requirements.memoryTypeBits | (uint)VK.VK_MEMORY_PROPERTY_HOST_COHERENT_BIT)
                    != 0
                )
                {
                    vmaAllocInfo.requiredFlags |= VK.VK_MEMORY_PROPERTY_HOST_COHERENT_BIT;
                    _isCoherentMemory = true;
                }
            }

            vmaAllocInfo.usage = VmaMemoryUsage.Auto;
            unsafe
            {
                Vma.vmaCreateBufferWithAlignment(
                    _ctx!.VmaAllocator,
                    &ci,
                    &vmaAllocInfo,
                    16,
                    out _vkBuffer,
                    out _vmaAllocation,
                    out _
                );

                // handle memory-mapped buffers
                if (VkMemFlags.HasFlag(VK.VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT))
                {
                    void* mappedPtr;
                    Vma.vmaMapMemory(_ctx!.VmaAllocator, _vmaAllocation, &mappedPtr);
                    this._mappedPtr = (nint)mappedPtr;
                }
            }
        }
        else
        {
            unsafe
            {
                VK.vkCreateBuffer(_ctx!.VkDevice, &ci, null, out _vkBuffer);

                // back the buffer with some memory
                {
                    VkBufferMemoryRequirementsInfo2 ri = new() { buffer = _vkBuffer };
                    VkMemoryRequirements2 requirements = new();
                    VK.vkGetBufferMemoryRequirements2(_ctx!.VkDevice, &ri, &requirements);
                    if (
                        (
                            requirements.memoryRequirements.memoryTypeBits
                            & (uint)VK.VK_MEMORY_PROPERTY_HOST_COHERENT_BIT
                        ) != 0
                    )
                    {
                        _isCoherentMemory = true;
                    }

                    var ret = HxVkUtils.AllocateMemory2(
                        _ctx!.VkPhysicalDevice,
                        _ctx!.VkDevice,
                        requirements,
                        VkMemFlags,
                        out _vkMemory
                    );
                    if (ret != VkResult.Success)
                    {
                        _logger.LogError("Failed to allocate memory for buffer: {REASON}", ret);
                        return ResultCode.RuntimeError;
                    }
                    VK.vkBindBufferMemory(_ctx!.VkDevice, _vkBuffer, _vkMemory, 0).CheckResult();
                }

                // handle memory-mapped buffers
                if (VkMemFlags.HasFlag(VK.VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT))
                {
                    void* mappedPtr;
                    VK.vkMapMemory(_ctx!.VkDevice, _vkMemory, 0, BufferSize, 0, &mappedPtr)
                        .CheckResult();
                    this._mappedPtr = (nint)mappedPtr;
                }
            }
        }
        HxDebug.Assert(_vkBuffer != VkBuffer.Null);

        if (GraphicsSettings.EnableDebug && !string.IsNullOrEmpty(debugName))
        {
            // set debug name
            _ctx!.VkDevice.SetDebugObjectName(
                VK.VK_OBJECT_TYPE_BUFFER,
                (nuint)_vkBuffer,
                $"[Vk.Buf]: {debugName}"
            );
        }

        // handle shader access
        if (VkUsageFlags.HasFlag(VK.VK_BUFFER_USAGE_SHADER_DEVICE_ADDRESS_BIT))
        {
            VkBufferDeviceAddressInfo ai = new() { buffer = _vkBuffer };
            unsafe
            {
                _vkDeviceAddress = VK.vkGetBufferDeviceAddress(_ctx!.VkDevice, &ai);
            }

            HxDebug.Assert(_vkDeviceAddress != 0);
        }
        return ResultCode.Ok;
    }

    public ResultCode BufferSubData(size_t offset, size_t size, nint data)
    {
        // only host-visible buffers can be uploaded this way
        HxDebug.Assert(IsMapped);
        if (!IsMapped)
        {
            _logger.LogError("Buffer is not mapped, cannot upload data.");
            return ResultCode.InvalidState;
        }

        HxDebug.Assert(offset + size <= BufferSize);

        unsafe
        {
            if (data != nint.Zero)
            {
                NativeHelper.MemoryCopy((nint)(_mappedPtr + offset), data, size);
            }
            else
            {
                Span<byte> dest = new((byte*)_mappedPtr.ToPointer() + (long)offset, (int)size);
                dest.Clear();
            }
        }

        if (!_isCoherentMemory)
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
            _logger.LogError("Buffer is not mapped, cannot download data.");
            return ResultCode.InvalidState;
        }

        HxDebug.Assert(offset + size <= BufferSize);

        if (!_isCoherentMemory)
        {
            InvalidateMappedMemory(offset, size);
        }

        unsafe
        {
            var src = (nint)(_mappedPtr + offset);
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

        if (_ctx!.UseVmaAllocator)
        {
            Vma.vmaFlushAllocation(_ctx!.VmaAllocator, _vmaAllocation, offset, size);
        }
        else
        {
            unsafe
            {
                VkMappedMemoryRange range = new()
                {
                    memory = _vkMemory,
                    offset = offset,
                    size = size,
                };
                VK.vkFlushMappedMemoryRanges(_ctx!.GetVkDevice(), 1, &range);
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

        if (_ctx!.UseVmaAllocator)
        {
            Vma.vmaInvalidateAllocation(_ctx!.VmaAllocator, _vmaAllocation, offset, size);
        }
        else
        {
            unsafe
            {
                VkMappedMemoryRange range = new()
                {
                    memory = _vkMemory,
                    offset = offset,
                    size = size,
                };
                VK.vkInvalidateMappedMemoryRanges(_ctx!.GetVkDevice(), 1, &range);
            }
        }
    }

    #region IDisposable Support
    private bool _disposedValue;

    private void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                if (!Valid)
                {
                    return;
                }
                if (_ctx!.UseVmaAllocator)
                {
                    if (_mappedPtr.Valid())
                    {
                        Vma.vmaUnmapMemory(_ctx!.VmaAllocator, _vmaAllocation);
                    }
                    _ctx!.DeferredTask(
                        () =>
                        {
                            Vma.vmaDestroyBuffer(_ctx!.VmaAllocator, _vkBuffer, _vmaAllocation);
                        },
                        SubmitHandle.Null
                    );
                }
                else
                {
                    if (_mappedPtr.Valid())
                    {
                        VK.vkUnmapMemory(_ctx!.VkDevice, _vkMemory);
                    }

                    _ctx!.DeferredTask(
                        () =>
                        {
                            unsafe
                            {
                                VK.vkDestroyBuffer(_ctx!.VkDevice, _vkBuffer, null);
                                VK.vkFreeMemory(_ctx!.VkDevice, _vkMemory, null);
                            }
                        },
                        SubmitHandle.Null
                    );
                }
            }

            // TODO: free unmanaged resources (unmanaged objects) and override finalizer
            // TODO: set large fields to null
            _disposedValue = true;
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
