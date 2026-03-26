using System.Collections.Concurrent;
using Vortice.Vulkan;

namespace HelixToolkit.Nex.Graphics.Vulkan;

/// <summary>
/// Manages asynchronous GPU upload operations using a dedicated or shared transfer queue.
/// </summary>
/// <remarks>
/// <para>
/// This class provides a thread-safe mechanism for uploading buffer and texture data to the GPU
/// without blocking the main rendering thread. It uses a separate command pool and queue
/// (ideally a dedicated DMA transfer queue) to perform copy operations concurrently.
/// </para>
/// <para>
/// <b>Architecture:</b>
/// <list type="bullet">
/// <item>A background thread processes queued upload requests</item>
/// <item>Each upload copies data to a staging buffer, records a transfer command, and submits it</item>
/// <item>When a dedicated transfer queue family is used, queue family ownership transfers are performed</item>
/// <item>Callers receive an <see cref="AsyncUploadHandle"/> to track completion</item>
/// </list>
/// </para>
/// </remarks>
internal sealed class VulkanTransferQueue : IDisposable
{
    private const int KMaxInflightTransfers = 16;
    private const uint KStagingAlignment = 16;
    private const uint KStagingBufferSize = 64u * 1024u * 1024u; // 64 MB staging buffer

    private static readonly ILogger _logger = LogManager.Create<VulkanTransferQueue>();

    private readonly VulkanContext _ctx;
    private readonly VkDevice _device;
    private readonly VkQueue _transferQueue;
    private readonly VkCommandPool _commandPool;
    private readonly uint32_t _transferQueueFamilyIndex;
    private readonly uint32_t _graphicsQueueFamilyIndex;
    private readonly bool _needsOwnershipTransfer;

    // Staging buffer for CPU→GPU copies
    private BufferResource _stagingBuffer = BufferResource.Null;
    private uint32_t _stagingBufferOffset = 0;

    // Inflight transfer tracking
    private readonly TransferSlot[] _slots = new TransferSlot[KMaxInflightTransfers];
    private readonly object _lock = new();

    // Background upload queue
    private readonly BlockingCollection<UploadRequest> _uploadQueue = new();
    private readonly Thread _workerThread;
    private volatile bool _disposed;

    private struct TransferSlot
    {
        public VkCommandBuffer CommandBuffer;
        public VkFence Fence;
        public bool InUse;
    }

    private abstract record UploadRequest(AsyncUploadHandle Handle);

    private sealed record BufferUploadRequest(
        AsyncUploadHandle Handle,
        BufferHandle DestBuffer,
        uint Offset,
        byte[] Data
    ) : UploadRequest(Handle);

    private sealed record TextureUploadRequest(
        AsyncUploadHandle Handle,
        TextureHandle DestTexture,
        TextureRangeDesc Range,
        byte[] Data
    ) : UploadRequest(Handle);

    public VulkanTransferQueue(VulkanContext ctx)
    {
        _ctx = ctx;
        _device = ctx.VkDevice;
        _graphicsQueueFamilyIndex = ctx.DeviceQueues.GraphicsQueueFamilyIndex;

        // Use dedicated transfer queue if available, otherwise fall back to graphics queue
        if (ctx.DeviceQueues.HasDedicatedTransferQueue)
        {
            _transferQueueFamilyIndex = ctx.DeviceQueues.TransferQueueFamilyIndex;
            _transferQueue = ctx.DeviceQueues.TransferQueue;
            _needsOwnershipTransfer = true;
            _logger.LogInformation(
                "Using dedicated transfer queue (family {INDEX})",
                _transferQueueFamilyIndex
            );
        }
        else
        {
            _transferQueueFamilyIndex = _graphicsQueueFamilyIndex;
            _transferQueue = ctx.DeviceQueues.GraphicsQueue;
            _needsOwnershipTransfer = false;
            _logger.LogInformation(
                "Using graphics queue for async transfers (no dedicated transfer queue)"
            );
        }

        // Create command pool for the transfer queue family
        unsafe
        {
            VkCommandPoolCreateInfo ci = new()
            {
                flags =
                    VK.VK_COMMAND_POOL_CREATE_RESET_COMMAND_BUFFER_BIT
                    | VK.VK_COMMAND_POOL_CREATE_TRANSIENT_BIT,
                queueFamilyIndex = _transferQueueFamilyIndex,
            };
            VK.vkCreateCommandPool(_device, ci, null, out _commandPool).CheckResult();

            if (GraphicsSettings.EnableDebug)
            {
                _device.SetDebugObjectName(
                    VK.VK_OBJECT_TYPE_COMMAND_POOL,
                    (nuint)_commandPool.Handle,
                    "[Vk.TransferCmdPool]: async uploads"
                );
            }

            // Allocate command buffers and fences for inflight transfers
            for (int i = 0; i < KMaxInflightTransfers; i++)
            {
                VkCommandBufferAllocateInfo ai = new()
                {
                    commandPool = _commandPool,
                    level = VK.VK_COMMAND_BUFFER_LEVEL_PRIMARY,
                    commandBufferCount = 1,
                };
                VkCommandBuffer cmdBuf = VkCommandBuffer.Null;
                VK.vkAllocateCommandBuffers(_device, &ai, &cmdBuf).CheckResult();

                _slots[i] = new TransferSlot
                {
                    CommandBuffer = cmdBuf,
                    Fence = _device.CreateFence($"Fence: transfer slot {i}"),
                    InUse = false,
                };
            }
        }

        // Create staging buffer
        CreateStagingBuffer();

        // Start worker thread
        _workerThread = new Thread(WorkerLoop)
        {
            Name = "VulkanTransferQueue",
            IsBackground = true,
            Priority = GraphicsSettings.UploadThreadPriority,
        };
        _workerThread.Start();
    }

    /// <summary>
    /// Enqueues an asynchronous buffer upload.
    /// Data is copied immediately so the caller's memory can be freed.
    /// </summary>
    public AsyncUploadHandle EnqueueBufferUpload(
        in BufferHandle destBuffer,
        uint offset,
        nint data,
        uint size
    )
    {
        if (size == 0)
            return AsyncUploadHandle.CompletedOk;

        // Copy data immediately so caller can free their memory
        var dataCopy = new byte[size];
        unsafe
        {
            fixed (byte* dst = dataCopy)
            {
                NativeHelper.MemoryCopy((nint)dst, data, size);
            }
        }

        var handle = new AsyncUploadHandle();
        _uploadQueue.Add(new BufferUploadRequest(handle, destBuffer, offset, dataCopy));
        return handle;
    }

    /// <summary>
    /// Enqueues an asynchronous texture upload.
    /// Data is copied immediately so the caller's memory can be freed.
    /// </summary>
    public AsyncUploadHandle EnqueueTextureUpload(
        in TextureHandle destTexture,
        in TextureRangeDesc range,
        nint data,
        uint dataSize
    )
    {
        if (dataSize == 0)
            return AsyncUploadHandle.CompletedOk;

        // Copy data immediately so caller can free their memory
        var dataCopy = new byte[dataSize];
        unsafe
        {
            fixed (byte* dst = dataCopy)
            {
                NativeHelper.MemoryCopy((nint)dst, data, dataSize);
            }
        }

        var handle = new AsyncUploadHandle();
        _uploadQueue.Add(new TextureUploadRequest(handle, destTexture, range, dataCopy));
        return handle;
    }

    /// <summary>
    /// Waits for all pending uploads to complete. Call during frame boundaries or shutdown.
    /// </summary>
    public void WaitAll()
    {
        lock (_lock)
        {
            unsafe
            {
                for (int i = 0; i < KMaxInflightTransfers; i++)
                {
                    if (_slots[i].InUse)
                    {
                        var fence = _slots[i].Fence;
                        VK.vkWaitForFences(_device, 1, &fence, VkBool32.True, ulong.MaxValue);
                        VK.vkResetFences(_device, 1, &fence);
                        VK.vkResetCommandBuffer(
                            _slots[i].CommandBuffer,
                            new VkCommandBufferResetFlags()
                        );
                        _slots[i].InUse = false;
                    }
                }
            }
        }
    }

    private void WorkerLoop()
    {
        try
        {
            foreach (var request in _uploadQueue.GetConsumingEnumerable())
            {
                if (_disposed)
                    break;

                try
                {
                    ProcessRequest(request);
                    Thread.Yield(); // Yield to allow other work on this thread if uploads are frequent
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing async upload request");
                    request.Handle.Complete(ResultCode.RuntimeError);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
    }

    private void ProcessRequest(UploadRequest request)
    {
        switch (request)
        {
            case BufferUploadRequest bufReq:
                ProcessBufferUpload(bufReq);
                break;
            case TextureUploadRequest texReq:
                ProcessTextureUpload(texReq);
                break;
        }
    }

    private void ProcessBufferUpload(BufferUploadRequest request)
    {
        var destBuffer = _ctx.BuffersPool.Get(request.DestBuffer);
        if (destBuffer is null || !destBuffer.Valid)
        {
            request.Handle.Complete(ResultCode.InvalidState);
            return;
        }

        int slotIndex = AcquireSlot();
        ref var slot = ref _slots[slotIndex];

        var stagingBuf = _ctx.BuffersPool.Get(_stagingBuffer.Handle);
        if (stagingBuf is null || !stagingBuf.Valid)
        {
            request.Handle.Complete(ResultCode.InvalidState);
            return;
        }

        uint dataSize = (uint)request.Data.Length;

        // Ensure staging buffer has enough space
        uint stagingOffset;
        lock (_lock)
        {
            if (_stagingBufferOffset + dataSize > KStagingBufferSize)
            {
                // Reset staging offset - wait for all inflight transfers first
                WaitAllSlots();
                _stagingBufferOffset = 0;
            }
            stagingOffset = _stagingBufferOffset;
            _stagingBufferOffset += Alignment.GetAlignedSize(dataSize, KStagingAlignment);
        }

        // Copy data to staging buffer
        unsafe
        {
            fixed (byte* src = request.Data)
            {
                stagingBuf.BufferSubData(stagingOffset, dataSize, (nint)src);
            }
        }

        // Record transfer command
        unsafe
        {
            VkCommandBufferBeginInfo bi = new()
            {
                flags = VK.VK_COMMAND_BUFFER_USAGE_ONE_TIME_SUBMIT_BIT,
            };
            VK.vkBeginCommandBuffer(slot.CommandBuffer, &bi).CheckResult();

            VkBufferCopy copy = new()
            {
                srcOffset = stagingOffset,
                dstOffset = request.Offset,
                size = dataSize,
            };
            VK.vkCmdCopyBuffer(
                slot.CommandBuffer,
                stagingBuf.VkBuffer,
                destBuffer.VkBuffer,
                1,
                &copy
            );

            // If using a dedicated transfer queue, release ownership to graphics queue
            if (_needsOwnershipTransfer)
            {
                VkBufferMemoryBarrier2 releaseBarrier = new()
                {
                    srcStageMask = VkPipelineStageFlags2.Transfer,
                    srcAccessMask = VkAccessFlags2.TransferWrite,
                    dstStageMask = VkPipelineStageFlags2.None,
                    dstAccessMask = VkAccessFlags2.None,
                    srcQueueFamilyIndex = _transferQueueFamilyIndex,
                    dstQueueFamilyIndex = _graphicsQueueFamilyIndex,
                    buffer = destBuffer.VkBuffer,
                    offset = request.Offset,
                    size = dataSize,
                };
                VkDependencyInfo depInfo = new()
                {
                    bufferMemoryBarrierCount = 1,
                    pBufferMemoryBarriers = &releaseBarrier,
                };
                VK.vkCmdPipelineBarrier2(slot.CommandBuffer, &depInfo);
            }
            else
            {
                // Same queue family - just a regular barrier
                VkBufferMemoryBarrier2 barrier = new()
                {
                    srcStageMask = VkPipelineStageFlags2.Transfer,
                    srcAccessMask = VkAccessFlags2.TransferWrite,
                    dstStageMask = VkPipelineStageFlags2.AllCommands,
                    dstAccessMask = VkAccessFlags2.MemoryRead | VkAccessFlags2.MemoryWrite,
                    srcQueueFamilyIndex = VK.VK_QUEUE_FAMILY_IGNORED,
                    dstQueueFamilyIndex = VK.VK_QUEUE_FAMILY_IGNORED,
                    buffer = destBuffer.VkBuffer,
                    offset = request.Offset,
                    size = dataSize,
                };
                VkDependencyInfo depInfo = new()
                {
                    bufferMemoryBarrierCount = 1,
                    pBufferMemoryBarriers = &barrier,
                };
                VK.vkCmdPipelineBarrier2(slot.CommandBuffer, &depInfo);
            }

            VK.vkEndCommandBuffer(slot.CommandBuffer).CheckResult();

            // Submit
            VkCommandBufferSubmitInfo bufferSI = new() { commandBuffer = slot.CommandBuffer };
            VkSubmitInfo2 si = new()
            {
                commandBufferInfoCount = 1,
                pCommandBufferInfos = &bufferSI,
            };

            lock (_lock)
            {
                VK.vkQueueSubmit2(_transferQueue, 1, &si, slot.Fence).CheckResult();
            }
        }

        // Wait for this specific transfer and signal completion
        unsafe
        {
            var fence = slot.Fence;
            VK.vkWaitForFences(_device, 1, &fence, VkBool32.True, ulong.MaxValue).CheckResult();
            VK.vkResetFences(_device, 1, &fence);
            VK.vkResetCommandBuffer(slot.CommandBuffer, new VkCommandBufferResetFlags());
        }

        lock (_lock)
        {
            slot.InUse = false;
        }

        request.Handle.Complete(ResultCode.Ok);
    }

    private void ProcessTextureUpload(TextureUploadRequest request)
    {
        var destImage = _ctx.TexturesPool.Get(request.DestTexture);
        if (destImage is null)
        {
            request.Handle.Complete(ResultCode.InvalidState);
            return;
        }

        // For texture uploads, delegate to the staging device on the graphics queue
        // since texture layout transitions and mipmap handling are complex.
        // The async benefit here is that we run it on a background thread.
        var result = _ctx.Upload(request.DestTexture, request.Range, 0, 0);

        // Now do the actual upload using the staging device
        unsafe
        {
            fixed (byte* src = request.Data)
            {
                result = _ctx.Upload(
                    request.DestTexture,
                    request.Range,
                    (nint)src,
                    (uint)request.Data.Length
                );
            }
        }

        request.Handle.Complete(result);
    }

    private int AcquireSlot()
    {
        while (true)
        {
            lock (_lock)
            {
                for (int i = 0; i < KMaxInflightTransfers; i++)
                {
                    if (!_slots[i].InUse)
                    {
                        _slots[i].InUse = true;
                        return i;
                    }

                    // Check if a slot's fence is signaled
                    unsafe
                    {
                        var fence = _slots[i].Fence;
                        if (VK.vkGetFenceStatus(_device, fence) == VkResult.Success)
                        {
                            VK.vkResetFences(_device, 1, &fence);
                            VK.vkResetCommandBuffer(
                                _slots[i].CommandBuffer,
                                new VkCommandBufferResetFlags()
                            );
                            _slots[i].InUse = true;
                            return i;
                        }
                    }
                }
            }

            // All slots in use, wait briefly
            Thread.Sleep(1);
        }
    }

    private void WaitAllSlots()
    {
        unsafe
        {
            for (int i = 0; i < KMaxInflightTransfers; i++)
            {
                if (_slots[i].InUse)
                {
                    var fence = _slots[i].Fence;
                    VK.vkWaitForFences(_device, 1, &fence, VkBool32.True, ulong.MaxValue);
                    VK.vkResetFences(_device, 1, &fence);
                    VK.vkResetCommandBuffer(
                        _slots[i].CommandBuffer,
                        new VkCommandBufferResetFlags()
                    );
                    _slots[i].InUse = false;
                }
            }
        }
    }

    private void CreateStagingBuffer()
    {
        ref readonly var limits = ref _ctx.GetVkPhysicalDeviceProperties().limits;
        uint bufferSize = Math.Min(KStagingBufferSize, limits.maxStorageBufferRange);

        var ret = _ctx.CreateBuffer(
            bufferSize,
            VK.VK_BUFFER_USAGE_TRANSFER_SRC_BIT,
            VK.VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT | VK.VK_MEMORY_PROPERTY_HOST_COHERENT_BIT,
            out var bufHandle,
            "Buffer: async transfer staging"
        );

        if (ret.HasError())
        {
            _logger.LogError("Failed to create async staging buffer: {ERROR}", ret);
            return;
        }

        _stagingBuffer = new BufferResource(_ctx, bufHandle);
    }

    #region Dispose
    private bool _disposedValue;

    private void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            _disposed = true;
            _uploadQueue.CompleteAdding();

            if (disposing)
            {
                // Wait for worker thread to finish
                if (_workerThread.IsAlive)
                {
                    _workerThread.Join(TimeSpan.FromSeconds(5));
                }

                WaitAll();

                unsafe
                {
                    for (int i = 0; i < KMaxInflightTransfers; i++)
                    {
                        var cmdBuf = _slots[i].CommandBuffer;
                        VK.vkFreeCommandBuffers(_device, _commandPool, 1, &cmdBuf);
                        VK.vkDestroyFence(_device, _slots[i].Fence, null);
                    }

                    VK.vkDestroyCommandPool(_device, _commandPool, null);
                }

                _stagingBuffer.Dispose();
                _stagingBuffer = BufferResource.Null;
                _uploadQueue.Dispose();
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
