namespace HelixToolkit.Nex.Graphics.Vulkan;

/// <summary>
/// Manages a pool of Vulkan command buffers for immediate submission.
/// Uses a free-list pool design: any available buffer can be acquired,
/// and buffers are returned to the pool after their fence signals.
///
/// Buffer states:
///   Available  — IsAvailable=true (CmdBuffer==Null). Ready to be acquired.
///   Encoding   — IsEncoding=true. Being recorded into. Not yet submitted.
///   InFlight   — !IsAvailable && !IsEncoding. Submitted to GPU, fence pending.
///
/// Invariants:
///   - A buffer is only reset (vkResetCommandBuffer + vkResetFences) after its fence signals.
///   - Wait(handle) only resets the buffer if handle.SubmitId matches the buffer's current SubmitId.
///   - No external counter tracks availability — IsAvailable is the single source of truth.
/// </summary>
internal sealed class VulkanImmediateCommands : IDisposable
{
    private const uint32_t KMaxCommandBuffers = 32;
    private const uint32_t KMaxSecondaryCommandBuffers = 64;
    private static readonly ILogger _logger = LogManager.Create<VulkanImmediateCommands>();

    private readonly VulkanContext _context;
    private readonly VkDevice _device = VkDevice.Null;
    private readonly VkQueue _queue = VkQueue.Null;
    private readonly VkCommandPool _commandPool = VkCommandPool.Null;
    private readonly VkCommandPool _secondaryCommandPool = VkCommandPool.Null;
    private readonly uint32_t _queueFamilyIndex = 0;
    private readonly bool _hasExtDeviceFault = false;

    private readonly CommandBuffer[] _buffers = new CommandBuffer[KMaxCommandBuffers];

    // Secondary command buffer pool
    private readonly List<CommandBuffer> _secondaryBuffers = new();
    private readonly object _secondaryBuffersLock = new();

    // Semaphore state for the next submit
    private SubmitHandle _lastSubmitHandle = SubmitHandle.Null;
    private SubmitHandle _nextSubmitHandle = SubmitHandle.Null;
    private VkSemaphoreSubmitInfo _lastSubmitSemaphore = new()
    {
        stageMask = VkPipelineStageFlags2.AllCommands,
    };
    private VkSemaphoreSubmitInfo _waitSemaphore = new()
    {
        stageMask = VkPipelineStageFlags2.AllCommands,
    };
    private VkSemaphoreSubmitInfo _signalSemaphore = new()
    {
        stageMask = VkPipelineStageFlags2.AllCommands,
    };
    private VkSemaphoreSubmitInfo _presentSignalSemaphore = new()
    {
        stageMask = VkPipelineStageFlags2.AllCommands,
    };
    private uint32_t _submitCounter = 1;

    public VulkanImmediateCommands(
        VulkanContext context,
        uint32_t queueFamilyIndex,
        bool hasEXTDeviceFault
    )
    {
        _context = context;
        _device = context.GetVkDevice();
        _queueFamilyIndex = queueFamilyIndex;
        _hasExtDeviceFault = hasEXTDeviceFault;

        VK.vkGetDeviceQueue(_device, queueFamilyIndex, 0, out _queue);

        VkCommandPoolCreateInfo ci = new()
        {
            flags =
                VK.VK_COMMAND_POOL_CREATE_RESET_COMMAND_BUFFER_BIT
                | VK.VK_COMMAND_POOL_CREATE_TRANSIENT_BIT,
            queueFamilyIndex = queueFamilyIndex,
        };
        unsafe
        {
            VK.vkCreateCommandPool(_device, ci, null, out _commandPool).CheckResult();
        }

        ci.flags = VK.VK_COMMAND_POOL_CREATE_RESET_COMMAND_BUFFER_BIT;
        unsafe
        {
            VK.vkCreateCommandPool(_device, ci, null, out _secondaryCommandPool).CheckResult();
        }

        if (GraphicsSettings.EnableDebug)
        {
            _device.SetDebugObjectName(
                VK.VK_OBJECT_TYPE_COMMAND_POOL,
                (nuint)_commandPool.Handle,
                "ImmediateCmdPool"
            );
            _device.SetDebugObjectName(
                VK.VK_OBJECT_TYPE_COMMAND_POOL,
                (nuint)_secondaryCommandPool.Handle,
                "SecondaryCmdPool"
            );
        }

        for (uint32_t i = 0; i != KMaxCommandBuffers; i++)
        {
            _buffers[i] = new CommandBuffer(context, false, i, in _commandPool);
            _freeStack.Push(i);
        }
    }

    #region Primary Command Buffers - Acquire / Submit / Wait

    /// <summary>
    /// Stack of available buffer indices. Pop to acquire, push to return.
    /// </summary>
    private readonly Stack<uint32_t> _freeStack = new((int)KMaxCommandBuffers);

    /// <summary>
    /// Queue of in-flight buffer indices, ordered by submission time (oldest at front).
    /// Used for efficient fence polling — only need to check the front.
    /// </summary>
    private readonly Queue<uint32_t> _inFlightQueue = new((int)KMaxCommandBuffers);

    /// <summary>
    /// Acquires an available command buffer. O(1) when buffers are available.
    /// If none free, recycles completed buffers from the in-flight queue (front-to-back).
    /// If all still in-flight, stalls on the oldest buffer's fence.
    /// </summary>
    public CommandBuffer Acquire()
    {
        // Fast path: pop from free stack
        if (_freeStack.Count > 0)
        {
            return BeginBuffer(_buffers[_freeStack.Pop()]);
        }

        // No free buffers — try to recycle completed ones from the front of the in-flight queue
        TryRecycleCompleted();

        if (_freeStack.Count > 0)
        {
            return BeginBuffer(_buffers[_freeStack.Pop()]);
        }

        // Still nothing — must stall on the oldest in-flight buffer
        _logger.LogWarning("All command buffers in-flight, stalling...");
        WaitAndRecycleFront();

        return BeginBuffer(_buffers[_freeStack.Pop()]);
    }

    private CommandBuffer BeginBuffer(CommandBuffer buf)
    {
        buf.Handle.SubmitId = _submitCounter;
        buf.BeginEncoding();
        _nextSubmitHandle = buf.Handle;
        return buf;
    }

    /// <summary>
    /// Non-blocking: checks fences at the front of the in-flight queue and recycles
    /// completed buffers back to the free stack. Stops at the first non-signaled fence
    /// (single-queue FIFO guarantee: if buffer N isn't done, N+1 can't be either).
    /// </summary>
    private void TryRecycleCompleted()
    {
        unsafe
        {
            while (_inFlightQueue.Count > 0)
            {
                var idx = _inFlightQueue.Peek();
                var buf = _buffers[idx];

                // Skip if already recycled (e.g., by Wait())
                if (buf.IsAvailable)
                {
                    _inFlightQueue.Dequeue();
                    continue;
                }

                var fence = buf.Fence;
                if (VK.vkWaitForFences(_device, 1, &fence, VkBool32.True, 0) == VkResult.Success)
                {
                    buf.Reset();
                    _inFlightQueue.Dequeue();
                    _freeStack.Push(idx);
                }
                else
                {
                    // Oldest isn't done — nothing behind it can be either
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Blocking: waits on the oldest in-flight buffer's fence, then recycles it.
    /// </summary>
    private void WaitAndRecycleFront()
    {
        HxDebug.Assert(_inFlightQueue.Count > 0, "No in-flight buffers to wait on");

        var idx = _inFlightQueue.Dequeue();
        var buf = _buffers[idx];

        // May have been recycled by Wait() already
        if (buf.IsAvailable)
        {
            _freeStack.Push(idx);
            return;
        }

        unsafe
        {
            var fence = buf.Fence;
            VK.vkWaitForFences(_device, 1, &fence, VkBool32.True, ulong.MaxValue).CheckResult();
        }
        buf.Reset();
        _freeStack.Push(idx);
    }

    public SubmitHandle Submit(CommandBuffer cmdBuf)
    {
        unsafe
        {
            return Submit(cmdBuf, null);
        }
    }

    public unsafe SubmitHandle Submit(CommandBuffer cmdBuf, void* submitInfoPNext = null)
    {
        HxDebug.Assert(cmdBuf.IsEncoding);
        cmdBuf.EndEncoding();
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

            var signalSemaphores = stackalloc VkSemaphoreSubmitInfo[3];
            signalSemaphores[0] = new VkSemaphoreSubmitInfo()
            {
                semaphore = cmdBuf.Semaphore,
                stageMask = VkPipelineStageFlags2.AllCommands,
            };

            uint32_t numSignalSemaphores = 1;
            if (_signalSemaphore.semaphore != VkSemaphore.Null)
            {
                signalSemaphores[numSignalSemaphores++] = _signalSemaphore;
            }
            if (_presentSignalSemaphore.semaphore != VkSemaphore.Null)
            {
                signalSemaphores[numSignalSemaphores++] = _presentSignalSemaphore;
            }

            VkCommandBufferSubmitInfo bufferSI = new() { commandBuffer = cmdBuf.CmdBuffer };
            VkSubmitInfo2 si = new()
            {
                waitSemaphoreInfoCount = numWaitSemaphores,
                pWaitSemaphoreInfos = waitSemaphores,
                commandBufferInfoCount = 1u,
                pCommandBufferInfos = &bufferSI,
                signalSemaphoreInfoCount = numSignalSemaphores,
                pSignalSemaphoreInfos = signalSemaphores,
                pNext = submitInfoPNext,
            };
            var result = VK.vkQueueSubmit2(_queue, 1u, &si, cmdBuf.Fence);

            if (_hasExtDeviceFault && result == VkResult.ErrorDeviceLost)
            {
                ReportDeviceFault();
            }

            result.CheckResult();

            _lastSubmitSemaphore.semaphore = cmdBuf.Semaphore;
            _lastSubmitHandle = cmdBuf.Handle;
            _waitSemaphore.semaphore = VkSemaphore.Null;
            _signalSemaphore.semaphore = VkSemaphore.Null;
            _presentSignalSemaphore.semaphore = VkSemaphore.Null;

            _inFlightQueue.Enqueue(cmdBuf.Handle.BufferIndex);

            _submitCounter++;
            if (_submitCounter == 0)
            {
                _submitCounter++;
            }

            return _lastSubmitHandle;
        }
    }

    #endregion

    #region Semaphore Configuration

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

    public void SignalPresentSemaphore(in VkSemaphore semaphore)
    {
        HxDebug.Assert(_presentSignalSemaphore.semaphore == VkSemaphore.Null);
        _presentSignalSemaphore.semaphore = semaphore;
        _presentSignalSemaphore.stageMask = VkPipelineStageFlags2.AllCommands;
    }

    public VkSemaphore AcquireLastSubmitSemaphore()
    {
        var semaphore = _lastSubmitSemaphore.semaphore;
        _lastSubmitSemaphore.semaphore = VkSemaphore.Null;
        return semaphore;
    }

    #endregion

    #region Wait / Query

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

    /// <summary>
    /// Checks whether the submission identified by handle has completed.
    /// </summary>
    public bool IsReady(in SubmitHandle handle, bool fastCheckNoVulkan = false)
    {
        if (handle.Empty)
        {
            return true;
        }

        var buf = _buffers[handle.BufferIndex];

        if (buf.IsAvailable)
        {
            return true;
        }

        if (buf.Handle.SubmitId != handle.SubmitId)
        {
            // Buffer was recycled and reused — the original submission is long done.
            return true;
        }

        if (fastCheckNoVulkan)
        {
            return false;
        }

        unsafe
        {
            var fence = buf.Fence;
            return VK.vkWaitForFences(_device, 1, &fence, VkBool32.True, 0) == VkResult.Success;
        }
    }

    /// <summary>
    /// Waits for a specific submission to complete. Only resets the buffer if it still
    /// belongs to the same submission (SubmitId matches). Safe to call with stale handles.
    /// </summary>
    public void Wait(in SubmitHandle handle, bool reset = true)
    {
        if (handle.Empty)
        {
            VK.vkDeviceWaitIdle(_device);
            return;
        }

        var buf = _buffers[handle.BufferIndex];

        // Already available — nothing to wait for
        if (buf.IsAvailable)
        {
            return;
        }

        // Buffer was recycled and reused by a newer submission — original work is done
        if (buf.Handle.SubmitId != handle.SubmitId)
        {
            return;
        }

        // Buffer is still being recorded — shouldn't happen in correct usage
        if (buf.IsEncoding)
        {
            _logger.LogWarning("Wait called on a command buffer that is still encoding");
            return;
        }

        // Wait for this buffer's fence
        unsafe
        {
            var fence = buf.Fence;
            VK.vkWaitForFences(_device, 1, &fence, VkBool32.True, ulong.MaxValue).CheckResult();
        }

        if (reset)
        {
            buf.Reset();
            _freeStack.Push(handle.BufferIndex);
        }
    }

    /// <summary>
    /// Waits for all in-flight command buffers to complete.
    /// </summary>
    public void WaitAll(bool reset = true)
    {
        unsafe
        {
            var fences = stackalloc VkFence[(int)KMaxCommandBuffers];
            uint32_t numFences = 0;

            for (int i = 0; i < KMaxCommandBuffers; i++)
            {
                var buf = _buffers[i];
                if (!buf.IsAvailable && !buf.IsEncoding)
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

        if (reset)
        {
            _inFlightQueue.Clear();
            for (int i = 0; i < KMaxCommandBuffers; i++)
            {
                var buf = _buffers[i];
                if (!buf.IsAvailable && !buf.IsEncoding)
                {
                    buf.Reset();
                    _freeStack.Push((uint32_t)i);
                }
            }
        }
    }

    #endregion

    #region Secondary Command Buffers

    /// <summary>
    /// Acquires a secondary command buffer from the pool, returned in encoding state.
    /// Secondary buffers share the primary buffer's fence for lifetime tracking.
    /// </summary>
    public CommandBuffer CreateSecondaryBuffer()
    {
        lock (_secondaryBuffersLock)
        {
            foreach (var buf in _secondaryBuffers)
            {
                if (buf.IsAvailable)
                {
                    buf.BeginEncoding();
                    return buf;
                }
            }

            if (_secondaryBuffers.Count >= KMaxSecondaryCommandBuffers)
            {
                _logger.LogWarning(
                    "Maximum secondary command buffers ({MAX}) reached.",
                    KMaxSecondaryCommandBuffers
                );
                foreach (var buf in _secondaryBuffers)
                {
                    if (buf.IsAvailable)
                    {
                        buf.BeginEncoding();
                        return buf;
                    }
                }
                throw new InvalidOperationException(
                    "No secondary command buffers available. Ensure buffers are recycled after use."
                );
            }

            var newBuffer = new CommandBuffer(
                _context,
                true,
                (uint)_secondaryBuffers.Count,
                in _secondaryCommandPool
            );
            _secondaryBuffers.Add(newBuffer);
            newBuffer.BeginEncoding();
            return newBuffer;
        }
    }

    /// <summary>
    /// Ends recording on a secondary command buffer.
    /// </summary>
    public void FinalizeSecondaryBuffer(CommandBuffer cmdBuffer)
    {
        if (!cmdBuffer.IsSecondary)
        {
            throw new InvalidOperationException("Buffer is not a secondary command buffer");
        }
        cmdBuffer.EndEncoding();
    }

    /// <summary>
    /// Returns a secondary command buffer to the pool for reuse.
    /// </summary>
    public void RecycleSecondaryBuffer(CommandBuffer cmdBuffer)
    {
        if (!cmdBuffer.IsSecondary)
        {
            return;
        }

        lock (_secondaryBuffersLock)
        {
            if (!cmdBuffer.IsAvailable)
            {
                if (cmdBuffer.IsEncoding)
                {
                    cmdBuffer.EndEncoding();
                }
                cmdBuffer.Reset();
            }
        }
    }

    #endregion

    #region Device Fault Reporting

    private unsafe void ReportDeviceFault()
    {
        VkDeviceFaultCountsEXT count = new();
        VK.vkGetDeviceFaultInfoEXT(_device, &count, null);
        FastList<VkDeviceFaultAddressInfoEXT> addressInfo = new((int)count.addressInfoCount);
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

        _logger.LogWarning(
            "VK_ERROR_DEVICE_LOST: {DESCRIPTION}",
            Marshal.PtrToStringAnsi((IntPtr)info.description)
        );
        foreach (var aInfo in addressInfo)
        {
            VkDeviceSize lowerAddress = aInfo.reportedAddress & ~(aInfo.addressPrecision - 1);
            VkDeviceSize upperAddress = aInfo.reportedAddress | (aInfo.addressPrecision - 1);
            _logger.LogWarning(
                "...address range [ {LOWER_ADDR}, {UPPER_ADDR} ]: {}",
                lowerAddress,
                upperAddress,
                aInfo.addressType
            );
        }
        foreach (var vInfo in vendorInfo)
        {
            _logger.LogWarning(
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
            var header = (VkDeviceFaultVendorBinaryHeaderVersionOneEXT*)info.pVendorBinaryData;

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
            _logger.LogWarning("VkDeviceFaultVendorBinaryHeaderVersionOne:");
            _logger.LogWarning("   headerSize        : {}", header->headerSize);
            _logger.LogWarning("   headerVersion     : {}", (uint32_t)header->headerVersion);
            _logger.LogWarning("   vendorID          : {}", header->vendorID);
            _logger.LogWarning("   deviceID          : {}", header->deviceID);
            _logger.LogWarning("   driverVersion     : {}", header->driverVersion);
            _logger.LogWarning("   pipelineCacheUUID : {}", uuid);
            if (header->applicationNameOffset > 0 && header->applicationNameOffset < binarySize)
            {
                _logger.LogWarning(
                    "   applicationName   : {NAME}",
                    Marshal.PtrToStringAnsi(
                        (IntPtr)((char*)info.pVendorBinaryData + header->applicationNameOffset)
                    )
                );
            }
            _logger.LogWarning(
                "   applicationVersion: {MAJOR}.{MINOR}.{PATCH}",
                header->applicationVersion.Major,
                header->applicationVersion.Minor,
                header->applicationVersion.Patch
            );
            if (header->engineNameOffset > 0 && header->engineNameOffset < binarySize)
            {
                _logger.LogWarning(
                    "   engineName        : {NAME}",
                    Marshal.PtrToStringAnsi(
                        (IntPtr)((char*)info.pVendorBinaryData + header->engineNameOffset)
                    )
                );
            }
            _logger.LogWarning(
                "   engineVersion     : {MAJOR}.{MINOR}.{PATCH}",
                header->engineVersion.Major,
                header->engineVersion.Minor,
                header->engineVersion.Patch
            );
            _logger.LogWarning(
                "   apiVersion        : {MAJOR}.{MINOR}.{PATCH}.{VARIANT}",
                header->apiVersion.Major,
                header->apiVersion.Minor,
                header->apiVersion.Patch,
                header->apiVersion.Variant
            );
        }
    }

    #endregion

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
                        _buffers[i].Dispose();
                    }

                    foreach (var buf in _secondaryBuffers)
                    {
                        buf.Dispose();
                    }
                    _secondaryBuffers.Clear();

                    VK.vkDestroyCommandPool(_device, _commandPool, null);
                    VK.vkDestroyCommandPool(_device, _secondaryCommandPool, null);
                }
            }
            _disposedValue = true;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
    }
    #endregion
}
