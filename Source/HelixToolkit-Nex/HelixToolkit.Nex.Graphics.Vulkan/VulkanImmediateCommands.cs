namespace HelixToolkit.Nex.Graphics.Vulkan;

internal sealed class VulkanImmediateCommands : IDisposable
{
    // the maximum number of command buffers which can similtaneously exist in the system; when we run out of buffers, we stall and wait until
    // an existing buffer becomes available
    const uint32_t kMaxCommandBuffers = 64;
    static readonly ILogger logger = LogManager.Create<VulkanImmediateCommands>();
    public sealed class CommandBufferWrapper()
    {
        public VkCommandBuffer Instance = VkCommandBuffer.Null;
        public VkCommandBuffer CmdBufAllocated = VkCommandBuffer.Null;
        public SubmitHandle Handle = SubmitHandle.Null;
        public VkFence Fence = VkFence.Null;
        public VkSemaphore Semaphore = VkSemaphore.Null;
        public bool IsEncoding = false;
    };

    readonly VkDevice device = VkDevice.Null;
    readonly VkQueue queue = VkQueue.Null;
    readonly VkCommandPool commandPool = VkCommandPool.Null;
    readonly uint32_t queueFamilyIndex = 0;
    readonly bool hasExtDeviceFault = false;
    readonly string debugName = string.Empty;

    readonly CommandBufferWrapper[] buffers = new CommandBufferWrapper[kMaxCommandBuffers];
    SubmitHandle lastSubmitHandle = SubmitHandle.Null;
    SubmitHandle nextSubmitHandle = SubmitHandle.Null;
    VkSemaphoreSubmitInfo lastSubmitSemaphore = new()
    {
        stageMask = VkPipelineStageFlags2.AllCommands
    };
    VkSemaphoreSubmitInfo waitSemaphore = new()
    {
        stageMask = VkPipelineStageFlags2.AllCommands
    }; // extra "wait" semaphore
    VkSemaphoreSubmitInfo signalSemaphore = new()
    {
        stageMask = VkPipelineStageFlags2.AllCommands
    }; // extra "signal" semaphore
    uint32_t numAvailableCommandBuffers = kMaxCommandBuffers;
    uint32_t submitCounter = 1;


    public VulkanImmediateCommands(in VkDevice device, uint32_t queueFamilyIndex, bool has_EXT_device_fault, string? debugName)
    {
        this.device = device;
        this.queueFamilyIndex = queueFamilyIndex;
        this.hasExtDeviceFault = has_EXT_device_fault;
        this.debugName = debugName ?? string.Empty;

        VK.vkGetDeviceQueue(device, queueFamilyIndex, 0, out queue);

        VkCommandPoolCreateInfo ci = new()
        {
            flags = VK.VK_COMMAND_POOL_CREATE_RESET_COMMAND_BUFFER_BIT | VK.VK_COMMAND_POOL_CREATE_TRANSIENT_BIT,
            queueFamilyIndex = queueFamilyIndex,
        };
        unsafe
        {
            VK.vkCreateCommandPool(device, ci, null, out commandPool).CheckResult();
        }
        if (GraphicsSettings.EnableDebug && !string.IsNullOrEmpty(this.debugName))
        {
            device.SetDebugObjectName(VK.VK_OBJECT_TYPE_COMMAND_POOL, (nuint)commandPool.Handle, debugName);
        }


        VkCommandBufferAllocateInfo ai = new()
        {
            commandPool = commandPool,
            level = VK.VK_COMMAND_BUFFER_LEVEL_PRIMARY,
            commandBufferCount = 1,
        };

        for (uint32_t i = 0; i != kMaxCommandBuffers; i++)
        {
            unsafe
            {
                string? fenceName = null;
                string? semaphoreName = null;
                if (this.debugName != string.Empty)
                {
                    fenceName = $"Fence: {this.debugName} (cmdbuf {i})";
                    semaphoreName = $"Semaphore: {this.debugName} (cmdbuf {i})";
                }
                buffers[i] = new CommandBufferWrapper();
                var buf = buffers[i];
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
        if (numAvailableCommandBuffers == 0)
        {
            Purge();
        }

        while (numAvailableCommandBuffers == 0)
        {
            logger.LogWarning("Waiting for command buffers...\n");
            Purge();
        }

        int idx = 0;

        // we are ok with any available buffer
        for (; idx < kMaxCommandBuffers; ++idx)
        {
            if (buffers[idx].Instance == VkCommandBuffer.Null)
            {
                break;
            }
        }

        HxDebug.Assert(numAvailableCommandBuffers > 0, "No available command buffers");
        HxDebug.Assert(idx < kMaxCommandBuffers, "No available command buffers");
        HxDebug.Assert(buffers[idx].CmdBufAllocated != VkCommandBuffer.Null);
        var buf = buffers[idx];

        buf.Handle.SubmitId = submitCounter;
        numAvailableCommandBuffers--;

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

        nextSubmitHandle = buf.Handle;

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
            if (waitSemaphore.semaphore != VkSemaphore.Null)
            {
                waitSemaphores[numWaitSemaphores++] = waitSemaphore;
            }
            if (lastSubmitSemaphore.semaphore != VkSemaphore.Null)
            {
                waitSemaphores[numWaitSemaphores++] = lastSubmitSemaphore;
            }

            var signalSemaphores = stackalloc VkSemaphoreSubmitInfo[2];

            signalSemaphores[0] = new VkSemaphoreSubmitInfo()
            {
                semaphore = wrapper.Semaphore,
                stageMask = VkPipelineStageFlags2.AllCommands
            };

            uint32_t numSignalSemaphores = 1;
            if (signalSemaphore.semaphore != VkSemaphore.Null)
            {
                signalSemaphores[numSignalSemaphores++] = signalSemaphore;
            }

            VkCommandBufferSubmitInfo bufferSI = new()
            {
                commandBuffer = wrapper.Instance,
            };
            VkSubmitInfo2 si = new()
            {
                waitSemaphoreInfoCount = numWaitSemaphores,
                pWaitSemaphoreInfos = waitSemaphores,
                commandBufferInfoCount = 1u,
                pCommandBufferInfos = &bufferSI,
                signalSemaphoreInfoCount = numSignalSemaphores,
                pSignalSemaphoreInfos = signalSemaphores,
            };
            var result = VK.vkQueueSubmit2(queue, 1u, &si, wrapper.Fence);

            if (hasExtDeviceFault && result == VkResult.ErrorDeviceLost)
            {
                VkDeviceFaultCountsEXT count = new();
                VK.vkGetDeviceFaultInfoEXT(device, &count, null);
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
                VK.vkGetDeviceFaultInfoEXT(device, &count, &info);


                logger.LogWarning("VK_ERROR_DEVICE_LOST: {DESCRIPTION}", Marshal.PtrToStringAnsi((IntPtr)info.description));
                foreach (var aInfo in addressInfo)
                {
                    VkDeviceSize lowerAddress = aInfo.reportedAddress & ~(aInfo.addressPrecision - 1);
                    VkDeviceSize upperAddress = aInfo.reportedAddress | (aInfo.addressPrecision - 1);
                    logger.LogWarning("...address range [ {LOWER_ADDR}, {UPPER_ADDR} ]: {}",
                          lowerAddress,
                          upperAddress,
                          aInfo.addressType);
                }
                foreach (var vInfo in vendorInfo)
                {
                    logger.LogWarning("...caused by `{DESCRIPTION}` with error code {FAULT_CODE} and data {FAULT_DATA}",
                          Marshal.PtrToStringAnsi((IntPtr)vInfo.description),
                          vInfo.vendorFaultCode,
                          vInfo.vendorFaultData);
                }
                var binarySize = count.vendorBinarySize;

                if (info.pVendorBinaryData != null && binarySize >= (ulong)sizeof(VkDeviceFaultVendorBinaryHeaderVersionOneEXT))
                {

                    var header = (VkDeviceFaultVendorBinaryHeaderVersionOneEXT*)info.pVendorBinaryData;

                    var hexDigits = stackalloc char[] { '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', 'a', 'b', 'c', 'd', 'e', 'f' };
                    var uuid = new char[VK.VK_UUID_SIZE * 2 + 1];
                    for (uint32_t i = 0; i < VK.VK_UUID_SIZE; ++i)
                    {
                        uuid[i * 2 + 0] = hexDigits[(header->pipelineCacheUUID[i] >> 4) & 0xF];
                        uuid[i * 2 + 1] = hexDigits[header->pipelineCacheUUID[i] & 0xF];
                    }
                    logger.LogWarning("VkDeviceFaultVendorBinaryHeaderVersionOne:");
                    logger.LogWarning("   headerSize        : {}", header->headerSize);
                    logger.LogWarning("   headerVersion     : {}", (uint32_t)header->headerVersion);
                    logger.LogWarning("   vendorID          : {}", header->vendorID);
                    logger.LogWarning("   deviceID          : {}", header->deviceID);
                    logger.LogWarning("   driverVersion     : {}", header->driverVersion);
                    logger.LogWarning("   pipelineCacheUUID : {}", uuid);
                    if (header->applicationNameOffset > 0 && header->applicationNameOffset < binarySize)
                    {
                        logger.LogWarning("   applicationName   : {NAME}", Marshal.PtrToStringAnsi((IntPtr)((char*)info.pVendorBinaryData + header->applicationNameOffset)));
                    }
                    logger.LogWarning("   applicationVersion: {MAJOR}.{MINOR}.{PATCH}",
                          header->applicationVersion.Major,
                          header->applicationVersion.Minor,
                          header->applicationVersion.Patch);
                    if (header->engineNameOffset > 0 && header->engineNameOffset < binarySize)
                    {
                        logger.LogWarning("   engineName        : {NAME}", Marshal.PtrToStringAnsi((IntPtr)((char*)info.pVendorBinaryData + header->engineNameOffset)));
                    }
                    logger.LogWarning("   engineVersion     : {MAJOR}.{MINOR}.{PATCH}",
                          header->engineVersion.Major,
                          header->engineVersion.Minor,
                          header->engineVersion.Patch);
                    logger.LogWarning("   apiVersion        : {MAJOR}.{MINOR}.{PATCH}.{VARIANT}",
                          header->apiVersion.Major,
                          header->apiVersion.Minor,
                          header->apiVersion.Patch,
                          header->apiVersion.Variant);
                }
            }

            result.CheckResult();

            lastSubmitSemaphore.semaphore = wrapper.Semaphore;
            lastSubmitHandle = wrapper.Handle;
            waitSemaphore.semaphore = VkSemaphore.Null;
            signalSemaphore.semaphore = VkSemaphore.Null;

            // reset
            wrapper.IsEncoding = false;
            submitCounter++;

            if (submitCounter == 0)
            {
                // skip the 0 value - when uint32_t wraps around (null SubmitHandle)
                submitCounter++;
            }

            return lastSubmitHandle;
        }
    }

    public void WaitSemaphore(in VkSemaphore semaphore)
    {
        HxDebug.Assert(waitSemaphore.semaphore == VkSemaphore.Null);

        waitSemaphore.semaphore = semaphore;
    }

    public void SignalSemaphore(in VkSemaphore semaphore, uint64_t signalValue)
    {
        HxDebug.Assert(signalSemaphore.semaphore == VkSemaphore.Null);

        signalSemaphore.semaphore = semaphore;
        signalSemaphore.value = signalValue;
    }

    public VkSemaphore AcquireLastSubmitSemaphore()
    {
        var semaphore = lastSubmitSemaphore.semaphore;
        lastSubmitSemaphore.semaphore = VkSemaphore.Null;
        return semaphore;
    }

    public VkFence GetVkFence(SubmitHandle handle)
    {
        return handle.Empty ? VkFence.Null : buffers[handle.BufferIndex].Fence;
    }

    public SubmitHandle GetLastSubmitHandle()
    {
        return lastSubmitHandle;
    }

    public SubmitHandle GetNextSubmitHandle()
    {
        return nextSubmitHandle;
    }

    public bool IsReady(in SubmitHandle handle, bool fastCheckNoVulkan = false)
    {
        if (handle.Empty)
        {
            // a null handle
            return true;
        }

        ref var buf = ref buffers[handle.BufferIndex];

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
            return VK.vkWaitForFences(device, 1, &fence, VkBool32.True, 0) == VkResult.Success;
        }
    }
    public void Wait(in SubmitHandle handle)
    {
        if (handle.Empty)
        {
            VK.vkDeviceWaitIdle(device);
            return;
        }

        if (IsReady(handle))
        {
            return;
        }
        HxDebug.Assert(!buffers[handle.BufferIndex].IsEncoding);
        unsafe
        {
            var fence = buffers[handle.BufferIndex].Fence;
            VK.vkWaitForFences(device, 1, &fence, VkBool32.True, ulong.MaxValue).CheckResult();
        }
        Purge();
    }

    public void WaitAll()
    {
        unsafe
        {
            var fences = stackalloc VkFence[(int)kMaxCommandBuffers];

            uint32_t numFences = 0;

            for (uint32_t i = 0; i < kMaxCommandBuffers; ++i)
            {
                ref CommandBufferWrapper buf = ref buffers[i];
                if (buf.Instance != VkCommandBuffer.Null && !buf.IsEncoding)
                {
                    fences[numFences++] = buf.Fence;
                }
            }

            if (numFences > 0)
            {
                VK.vkWaitForFences(device, numFences, fences, VkBool32.True, ulong.MaxValue).CheckResult();
            }
        }
        Purge();
    }


    private void Purge()
    {
        unsafe
        {

            // if we have a fence, we can wait for it to become signaled and reset the command buffer
            for (uint32_t i = 0; i != kMaxCommandBuffers; i++)
            {
                ref CommandBufferWrapper buf = ref buffers[(i + lastSubmitHandle.BufferIndex + 1) % kMaxCommandBuffers];
                if (buf.Instance == VkCommandBuffer.Null || buf.IsEncoding)
                {
                    continue;
                }
                var result = VK.vkGetFenceStatus(device, buf.Fence);
                if (result == VkResult.Success)
                {
                    VK.vkResetCommandBuffer(buf.Instance, new VkCommandBufferResetFlags()).CheckResult();
                    var fence = buf.Fence;
                    VK.vkResetFences(device, 1, &fence).CheckResult();
                    buf.Instance = VkCommandBuffer.Null;
                    numAvailableCommandBuffers++;
                }
                else if (result != VkResult.Timeout)
                {
                    result.CheckResult();
                }
            }
        }
    }

    #region Dispose
    private bool disposedValue;
    void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                WaitAll();
                unsafe
                {
                    for (int i = 0; i < kMaxCommandBuffers; ++i)
                    {
                        ref var buf = ref buffers[i];

                        // lifetimes of all VkFence objects are managed explicitly we do not use deferredTask() for them
                        VK.vkDestroyFence(device, buf.Fence, null);
                        VK.vkDestroySemaphore(device, buf.Semaphore, null);
                        buf.Semaphore = VkSemaphore.Null;
                        buf.Fence = VkFence.Null;
                        var cmdBuf = buf.CmdBufAllocated;
                        VK.vkFreeCommandBuffers(device, commandPool, 1, &cmdBuf);
                        buf.CmdBufAllocated = VkCommandBuffer.Null;
                    }

                    VK.vkDestroyCommandPool(device, commandPool, null);
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
}
