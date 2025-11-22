namespace HelixToolkit.Nex.Graphics.Vulkan;

internal sealed class VulkanImmediateCommands : IDisposable
{
    // the maximum number of command buffers which can similtaneously exist in the system; when we run out of buffers, we stall and wait until
    // an existing buffer becomes available
    private const uint32_t KMaxCommandBuffers = 64;
    private static readonly ILogger Logger = LogManager.Create<VulkanImmediateCommands>();

    public sealed class CommandBufferWrapper()
    {
        public VkCommandBuffer Instance = VkCommandBuffer.Null;
        public VkCommandBuffer CmdBufAllocated = VkCommandBuffer.Null;
        public SubmitHandle Handle = SubmitHandle.Null;
        public VkFence Fence = VkFence.Null;
        public VkSemaphore Semaphore = VkSemaphore.Null;
        public bool IsEncoding = false;
    };

    private readonly VkDevice _device = VkDevice.Null;
    private readonly VkQueue _queue = VkQueue.Null;
    private readonly VkCommandPool _commandPool = VkCommandPool.Null;
    private readonly uint32_t _queueFamilyIndex = 0;
    private readonly bool _hasExtDeviceFault = false;
    private readonly string _debugName = string.Empty;

    private readonly CommandBufferWrapper[] _buffers = new CommandBufferWrapper[KMaxCommandBuffers];
    private SubmitHandle _lastSubmitHandle = SubmitHandle.Null;
    private SubmitHandle _nextSubmitHandle = SubmitHandle.Null;
    private VkSemaphoreSubmitInfo _lastSubmitSemaphore = new()
    {
        stageMask = VkPipelineStageFlags2.AllCommands,
    };
    private VkSemaphoreSubmitInfo _waitSemaphore = new()
    {
        stageMask = VkPipelineStageFlags2.AllCommands,
    }; // extra "wait" semaphore
    private VkSemaphoreSubmitInfo _signalSemaphore = new()
    {
        stageMask = VkPipelineStageFlags2.AllCommands,
    }; // extra "signal" semaphore
    private uint32_t _numAvailableCommandBuffers = KMaxCommandBuffers;
    private uint32_t _submitCounter = 1;

    public VulkanImmediateCommands(
        in VkDevice device,
        uint32_t queueFamilyIndex,
        bool has_EXT_device_fault,
        string? debugName
    )
    {
        this._device = device;
        this._queueFamilyIndex = queueFamilyIndex;
        this._hasExtDeviceFault = has_EXT_device_fault;
        this._debugName = debugName ?? string.Empty;

        VK.vkGetDeviceQueue(device, queueFamilyIndex, 0, out _queue);

        VkCommandPoolCreateInfo ci = new()
        {
            flags =
                VK.VK_COMMAND_POOL_CREATE_RESET_COMMAND_BUFFER_BIT
                | VK.VK_COMMAND_POOL_CREATE_TRANSIENT_BIT,
            queueFamilyIndex = queueFamilyIndex,
        };
        unsafe
        {
            VK.vkCreateCommandPool(device, ci, null, out _commandPool).CheckResult();
        }
        if (GraphicsSettings.EnableDebug && !string.IsNullOrEmpty(this._debugName))
        {
            device.SetDebugObjectName(
                VK.VK_OBJECT_TYPE_COMMAND_POOL,
                (nuint)_commandPool.Handle,
                $"[Vk.ImmediateCmdPool]: {debugName}"
            );
        }

        VkCommandBufferAllocateInfo ai = new()
        {
            commandPool = _commandPool,
            level = VK.VK_COMMAND_BUFFER_LEVEL_PRIMARY,
            commandBufferCount = 1,
        };

        for (uint32_t i = 0; i != KMaxCommandBuffers; i++)
        {
            unsafe
            {
                string? fenceName = null;
                string? semaphoreName = null;
                if (this._debugName != string.Empty)
                {
                    fenceName = $"Fence: {this._debugName} (cmdbuf {i})";
                    semaphoreName = $"Semaphore: {this._debugName} (cmdbuf {i})";
                }
                _buffers[i] = new CommandBufferWrapper();
                var buf = _buffers[i];
                buf.Semaphore = device.CreateSemaphore(semaphoreName);
                buf.Fence = device.CreateFence(fenceName);
                VkCommandBuffer cmdBuf = VkCommandBuffer.Null;
                VK.vkAllocateCommandBuffers(device, &ai, &cmdBuf);
                buf.CmdBufAllocated = cmdBuf;
                buf.Handle.BufferIndex = i;
            }
        }
    }

    // returns the current command buffer (creates one if it does not exist)
    public CommandBufferWrapper Acquire()
    {
        if (_numAvailableCommandBuffers == 0)
        {
            Purge();
        }

        while (_numAvailableCommandBuffers == 0)
        {
            Logger.LogWarning("Waiting for command buffers...\n");
            Purge();
        }

        int idx = 0;

        // we are ok with any available buffer
        for (; idx < KMaxCommandBuffers; ++idx)
        {
            if (_buffers[idx].Instance == VkCommandBuffer.Null)
            {
                break;
            }
        }

        HxDebug.Assert(_numAvailableCommandBuffers > 0, "No available command buffers");
        HxDebug.Assert(idx < KMaxCommandBuffers, "No available command buffers");
        HxDebug.Assert(_buffers[idx].CmdBufAllocated != VkCommandBuffer.Null);
        var buf = _buffers[idx];

        buf.Handle.SubmitId = _submitCounter;
        _numAvailableCommandBuffers--;

        buf.Instance = buf.CmdBufAllocated;
        buf.IsEncoding = true;
        unsafe
        {
            VkCommandBufferBeginInfo bi = new()
            {
                flags = VK.VK_COMMAND_BUFFER_USAGE_ONE_TIME_SUBMIT_BIT,
            };
            VK.vkBeginCommandBuffer(buf.Instance, &bi).CheckResult();
        }

        _nextSubmitHandle = buf.Handle;

        return buf;
    }

    public SubmitHandle Submit(CommandBufferWrapper wrapper)
    {
        HxDebug.Assert(wrapper.IsEncoding);
        VK.vkEndCommandBuffer(wrapper.Instance).CheckResult();
        unsafe
        {
            var waitSemaphores = stackalloc VkSemaphoreSubmitInfo[2];
            uint32_t numWaitSemaphores = 0;
            if (_waitSemaphore.semaphore != VkSemaphore.Null)
            {
                waitSemaphores[numWaitSemaphores++] = _waitSemaphore;
            }
            if (_lastSubmitSemaphore.semaphore != VkSemaphore.Null)
            {
                waitSemaphores[numWaitSemaphores++] = _lastSubmitSemaphore;
            }

            var signalSemaphores = stackalloc VkSemaphoreSubmitInfo[2];

            signalSemaphores[0] = new VkSemaphoreSubmitInfo()
            {
                semaphore = wrapper.Semaphore,
                stageMask = VkPipelineStageFlags2.AllCommands,
            };

            uint32_t numSignalSemaphores = 1;
            if (_signalSemaphore.semaphore != VkSemaphore.Null)
            {
                signalSemaphores[numSignalSemaphores++] = _signalSemaphore;
            }

            VkCommandBufferSubmitInfo bufferSI = new() { commandBuffer = wrapper.Instance };
            VkSubmitInfo2 si = new()
            {
                waitSemaphoreInfoCount = numWaitSemaphores,
                pWaitSemaphoreInfos = waitSemaphores,
                commandBufferInfoCount = 1u,
                pCommandBufferInfos = &bufferSI,
                signalSemaphoreInfoCount = numSignalSemaphores,
                pSignalSemaphoreInfos = signalSemaphores,
            };
            var result = VK.vkQueueSubmit2(_queue, 1u, &si, wrapper.Fence);

            if (_hasExtDeviceFault && result == VkResult.ErrorDeviceLost)
            {
                VkDeviceFaultCountsEXT count = new();
                VK.vkGetDeviceFaultInfoEXT(_device, &count, null);
                FastList<VkDeviceFaultAddressInfoEXT> addressInfo = new(
                    (int)count.addressInfoCount
                );
                FastList<VkDeviceFaultVendorInfoEXT> vendorInfo = new((int)count.vendorInfoCount);
                FastList<uint8_t> binary = new((int)count.vendorBinarySize);
                VkDeviceFaultInfoEXT info = new();

                using var pAddressInfos = addressInfo.GetInternalArray().Pin();
                using var pVenderInfo = vendorInfo.GetInternalArray().Pin();
                using var pBinary = binary.GetInternalArray().Pin();

                info.pAddressInfos = (VkDeviceFaultAddressInfoEXT*)pAddressInfos.Pointer;
                info.pVendorInfos = (VkDeviceFaultVendorInfoEXT*)pVenderInfo.Pointer;
                info.pVendorBinaryData = (uint8_t*)pBinary.Pointer;
                VK.vkGetDeviceFaultInfoEXT(_device, &count, &info);

                Logger.LogWarning(
                    "VK_ERROR_DEVICE_LOST: {DESCRIPTION}",
                    Marshal.PtrToStringAnsi((IntPtr)info.description)
                );
                foreach (var aInfo in addressInfo)
                {
                    VkDeviceSize lowerAddress =
                        aInfo.reportedAddress & ~(aInfo.addressPrecision - 1);
                    VkDeviceSize upperAddress =
                        aInfo.reportedAddress | (aInfo.addressPrecision - 1);
                    Logger.LogWarning(
                        "...address range [ {LOWER_ADDR}, {UPPER_ADDR} ]: {}",
                        lowerAddress,
                        upperAddress,
                        aInfo.addressType
                    );
                }
                foreach (var vInfo in vendorInfo)
                {
                    Logger.LogWarning(
                        "...caused by `{DESCRIPTION}` with error code {FAULT_CODE} and data {FAULT_DATA}",
                        Marshal.PtrToStringAnsi((IntPtr)vInfo.description),
                        vInfo.vendorFaultCode,
                        vInfo.vendorFaultData
                    );
                }
                var binarySize = count.vendorBinarySize;

                if (
                    info.pVendorBinaryData != null
                    && binarySize >= (ulong)sizeof(VkDeviceFaultVendorBinaryHeaderVersionOneEXT)
                )
                {
                    var header = (VkDeviceFaultVendorBinaryHeaderVersionOneEXT*)
                        info.pVendorBinaryData;

                    var hexDigits =
                        stackalloc char[] {
                            '0',
                            '1',
                            '2',
                            '3',
                            '4',
                            '5',
                            '6',
                            '7',
                            '8',
                            '9',
                            'a',
                            'b',
                            'c',
                            'd',
                            'e',
                            'f',
                        };
                    var uuid = new char[VK.VK_UUID_SIZE * 2 + 1];
                    for (uint32_t i = 0; i < VK.VK_UUID_SIZE; ++i)
                    {
                        uuid[i * 2 + 0] = hexDigits[(header->pipelineCacheUUID[i] >> 4) & 0xF];
                        uuid[i * 2 + 1] = hexDigits[header->pipelineCacheUUID[i] & 0xF];
                    }
                    Logger.LogWarning("VkDeviceFaultVendorBinaryHeaderVersionOne:");
                    Logger.LogWarning("   headerSize        : {}", header->headerSize);
                    Logger.LogWarning("   headerVersion     : {}", (uint32_t)header->headerVersion);
                    Logger.LogWarning("   vendorID          : {}", header->vendorID);
                    Logger.LogWarning("   deviceID          : {}", header->deviceID);
                    Logger.LogWarning("   driverVersion     : {}", header->driverVersion);
                    Logger.LogWarning("   pipelineCacheUUID : {}", uuid);
                    if (
                        header->applicationNameOffset > 0
                        && header->applicationNameOffset < binarySize
                    )
                    {
                        Logger.LogWarning(
                            "   applicationName   : {NAME}",
                            Marshal.PtrToStringAnsi(
                                (IntPtr)(
                                    (char*)info.pVendorBinaryData + header->applicationNameOffset
                                )
                            )
                        );
                    }
                    Logger.LogWarning(
                        "   applicationVersion: {MAJOR}.{MINOR}.{PATCH}",
                        header->applicationVersion.Major,
                        header->applicationVersion.Minor,
                        header->applicationVersion.Patch
                    );
                    if (header->engineNameOffset > 0 && header->engineNameOffset < binarySize)
                    {
                        Logger.LogWarning(
                            "   engineName        : {NAME}",
                            Marshal.PtrToStringAnsi(
                                (IntPtr)((char*)info.pVendorBinaryData + header->engineNameOffset)
                            )
                        );
                    }
                    Logger.LogWarning(
                        "   engineVersion     : {MAJOR}.{MINOR}.{PATCH}",
                        header->engineVersion.Major,
                        header->engineVersion.Minor,
                        header->engineVersion.Patch
                    );
                    Logger.LogWarning(
                        "   apiVersion        : {MAJOR}.{MINOR}.{PATCH}.{VARIANT}",
                        header->apiVersion.Major,
                        header->apiVersion.Minor,
                        header->apiVersion.Patch,
                        header->apiVersion.Variant
                    );
                }
            }

            result.CheckResult();

            _lastSubmitSemaphore.semaphore = wrapper.Semaphore;
            _lastSubmitHandle = wrapper.Handle;
            _waitSemaphore.semaphore = VkSemaphore.Null;
            _signalSemaphore.semaphore = VkSemaphore.Null;

            // reset
            wrapper.IsEncoding = false;
            _submitCounter++;

            if (_submitCounter == 0)
            {
                // skip the 0 value - when uint32_t wraps around (null SubmitHandle)
                _submitCounter++;
            }

            return _lastSubmitHandle;
        }
    }

    public void WaitSemaphore(in VkSemaphore semaphore)
    {
        HxDebug.Assert(_waitSemaphore.semaphore == VkSemaphore.Null);

        _waitSemaphore.semaphore = semaphore;
    }

    public void SignalSemaphore(in VkSemaphore semaphore, uint64_t signalValue)
    {
        HxDebug.Assert(_signalSemaphore.semaphore == VkSemaphore.Null);

        _signalSemaphore.semaphore = semaphore;
        _signalSemaphore.value = signalValue;
    }

    public VkSemaphore AcquireLastSubmitSemaphore()
    {
        var semaphore = _lastSubmitSemaphore.semaphore;
        _lastSubmitSemaphore.semaphore = VkSemaphore.Null;
        return semaphore;
    }

    public VkFence GetVkFence(SubmitHandle handle)
    {
        return handle.Empty ? VkFence.Null : _buffers[handle.BufferIndex].Fence;
    }

    public SubmitHandle GetLastSubmitHandle()
    {
        return _lastSubmitHandle;
    }

    public SubmitHandle GetNextSubmitHandle()
    {
        return _nextSubmitHandle;
    }

    public bool IsReady(in SubmitHandle handle, bool fastCheckNoVulkan = false)
    {
        if (handle.Empty)
        {
            // a null handle
            return true;
        }

        ref var buf = ref _buffers[handle.BufferIndex];

        if (buf.Instance == VkCommandBuffer.Null)
        {
            // already recycled and not yet reused
            return true;
        }

        if (buf.Handle.SubmitId != handle.SubmitId)
        {
            // already recycled and reused by another command buffer
            return true;
        }

        if (fastCheckNoVulkan)
        {
            // do not ask the Vulkan API about it, just let it retire naturally (when submitId for this bufferIndex gets incremented)
            return false;
        }
        unsafe
        {
            var fence = buf.Fence;
            return VK.vkWaitForFences(_device, 1, &fence, VkBool32.True, 0) == VkResult.Success;
        }
    }

    public void Wait(in SubmitHandle handle)
    {
        if (handle.Empty)
        {
            VK.vkDeviceWaitIdle(_device);
            return;
        }

        if (IsReady(handle))
        {
            return;
        }
        HxDebug.Assert(!_buffers[handle.BufferIndex].IsEncoding);
        unsafe
        {
            var fence = _buffers[handle.BufferIndex].Fence;
            VK.vkWaitForFences(_device, 1, &fence, VkBool32.True, ulong.MaxValue).CheckResult();
        }
        Purge();
    }

    public void WaitAll()
    {
        unsafe
        {
            var fences = stackalloc VkFence[(int)KMaxCommandBuffers];

            uint32_t numFences = 0;

            for (uint32_t i = 0; i < KMaxCommandBuffers; ++i)
            {
                ref CommandBufferWrapper buf = ref _buffers[i];
                if (buf.Instance != VkCommandBuffer.Null && !buf.IsEncoding)
                {
                    fences[numFences++] = buf.Fence;
                }
            }

            if (numFences > 0)
            {
                VK.vkWaitForFences(_device, numFences, fences, VkBool32.True, ulong.MaxValue)
                    .CheckResult();
            }
        }
        Purge();
    }

    private void Purge()
    {
        unsafe
        {
            // if we have a fence, we can wait for it to become signaled and reset the command buffer
            for (uint32_t i = 0; i != KMaxCommandBuffers; i++)
            {
                ref CommandBufferWrapper buf = ref _buffers[
                    (i + _lastSubmitHandle.BufferIndex + 1) % KMaxCommandBuffers
                ];
                if (buf.Instance == VkCommandBuffer.Null || buf.IsEncoding)
                {
                    continue;
                }
                var result = VK.vkGetFenceStatus(_device, buf.Fence);
                if (result == VkResult.Success)
                {
                    VK.vkResetCommandBuffer(buf.Instance, new VkCommandBufferResetFlags())
                        .CheckResult();
                    var fence = buf.Fence;
                    VK.vkResetFences(_device, 1, &fence).CheckResult();
                    buf.Instance = VkCommandBuffer.Null;
                    _numAvailableCommandBuffers++;
                }
                else if (result != VkResult.Timeout)
                {
                    result.CheckResult();
                }
            }
        }
    }

    #region Dispose
    private bool _disposedValue;

    private void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                WaitAll();
                unsafe
                {
                    for (int i = 0; i < KMaxCommandBuffers; ++i)
                    {
                        ref var buf = ref _buffers[i];

                        // lifetimes of all VkFence objects are managed explicitly we do not use deferredTask() for them
                        VK.vkDestroyFence(_device, buf.Fence, null);
                        VK.vkDestroySemaphore(_device, buf.Semaphore, null);
                        buf.Semaphore = VkSemaphore.Null;
                        buf.Fence = VkFence.Null;
                        var cmdBuf = buf.CmdBufAllocated;
                        VK.vkFreeCommandBuffers(_device, _commandPool, 1, &cmdBuf);
                        buf.CmdBufAllocated = VkCommandBuffer.Null;
                    }

                    VK.vkDestroyCommandPool(_device, _commandPool, null);
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
}
