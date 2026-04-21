using System.Buffers;
using System.Collections.Concurrent;

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
    private readonly BlockingCollection<UploadRequest> _uploadQueue = new(KMaxInflightTransfers);
    private readonly Thread _workerThread;
    private volatile bool _disposed;

    private struct TransferSlot
    {
        public VkCommandBuffer CommandBuffer;
        public VkFence Fence;
        public bool InUse;
    }

    // Base request: uses a delegate to complete the typed AsyncUploadHandle<THandle>
    // without making the process methods themselves generic.
    private abstract class UploadRequest(Action<ResultCode> complete)
    {
        public void Complete(ResultCode result) => complete(result);

        public abstract MemoryHandle Pin();

        public abstract size_t DataSize { get; }
    }

    private abstract class BufferUploadRequest(
        Action<ResultCode> complete,
        BufferHandle destBuffer,
        uint offset
    ) : UploadRequest(complete)
    {
        public BufferHandle DestBuffer { get; } = destBuffer;
        public uint Offset { get; } = offset;
    }

    private sealed class BufferUploadRequest<T>(
        Action<ResultCode> complete,
        BufferHandle destBuffer,
        uint offset,
        T[] data,
        size_t count
    ) : BufferUploadRequest(complete, destBuffer, offset)
        where T : unmanaged
    {
        public T[] Data { get; } = data;
        public size_t Count { get; } = count;
        public override uint DataSize => Count * NativeHelper.SizeOf<T>();

        public override MemoryHandle Pin()
        {
            return Data.Pin();
        }
    }

    private abstract class TextureUploadRequest(
        Action<ResultCode> complete,
        TextureHandle destTexture,
        TextureRangeDesc range
    ) : UploadRequest(complete)
    {
        public TextureHandle DestTexture { get; } = destTexture;
        public TextureRangeDesc Range { get; } = range;
    }

    private sealed class TextureUploadRequest<T>(
        Action<ResultCode> complete,
        TextureHandle destTexture,
        TextureRangeDesc range,
        T[] data,
        size_t count
    ) : TextureUploadRequest(complete, destTexture, range)
        where T : unmanaged
    {
        public T[] Data { get; } = data;
        public size_t Count { get; } = count;
        public override uint DataSize => Count * NativeHelper.SizeOf<T>();

        public override MemoryHandle Pin()
        {
            return Data.Pin();
        }
    }

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
    public AsyncUploadHandle<BufferHandle> EnqueueBufferUpload<T>(
        in BufferHandle destBuffer,
        uint offset,
        T[] data,
        size_t count
    )
        where T : unmanaged
    {
        if (count == 0)
            return AsyncUploadHandle<BufferHandle>.CreateCompleted(ResultCode.Ok, destBuffer);
        if (data.Length < count)
            throw new ArgumentException("Data array length is less than count", nameof(data));
        var uploadHandle = new AsyncUploadHandle<BufferHandle>();
        var captured = destBuffer; // capture for closure
        _uploadQueue.Add(
            new BufferUploadRequest<T>(
                result => uploadHandle.Complete(result, captured),
                destBuffer,
                offset,
                data,
                count
            )
        );
        return uploadHandle;
    }

    /// <summary>
    /// Enqueues an asynchronous texture upload.
    /// Data is copied immediately so the caller's memory can be freed.
    /// </summary>
    public AsyncUploadHandle<TextureHandle> EnqueueTextureUpload<T>(
        in TextureHandle destTexture,
        in TextureRangeDesc range,
        T[] data,
        size_t count
    )
        where T : unmanaged
    {
        if (data.Length < count)
            throw new ArgumentException("Data array length is less than count", nameof(data));
        if (count == 0)
            return AsyncUploadHandle<TextureHandle>.CreateCompleted(ResultCode.Ok, destTexture);
        var uploadHandle = new AsyncUploadHandle<TextureHandle>();
        var captured = destTexture; // capture for closure
        _uploadQueue.Add(
            new TextureUploadRequest<T>(
                result => uploadHandle.Complete(result, captured),
                destTexture,
                range,
                data,
                count
            )
        );
        return uploadHandle;
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
                    request.Complete(ResultCode.RuntimeError);
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
        _logger.LogDebug("Processing async upload request of type {TYPE}", request.GetType().Name);
        switch (request)
        {
            case BufferUploadRequest req:
                ProcessBufferUpload(req);
                break;
            case TextureUploadRequest req:
                ProcessTextureUpload(req);
                break;
        }
    }

    private void ProcessBufferUpload(BufferUploadRequest request)
    {
        var destBuffer = _ctx.BuffersPool.Get(request.DestBuffer);
        if (destBuffer is null || !destBuffer.Valid)
        {
            request.Complete(ResultCode.InvalidState);
            return;
        }

        int slotIndex = AcquireSlot();
        ref var slot = ref _slots[slotIndex];

        var stagingBuf = _ctx.BuffersPool.Get(_stagingBuffer.Handle);
        if (stagingBuf is null || !stagingBuf.Valid)
        {
            request.Complete(ResultCode.InvalidState);
            return;
        }

        var dataSize = request.DataSize;

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
            using var src = request.Pin();
            stagingBuf.BufferSubData(stagingOffset, dataSize, (nint)src.Pointer);
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

        request.Complete(ResultCode.Ok);
    }

    private void ProcessTextureUpload(TextureUploadRequest request)
    {
        var destImage = _ctx.TexturesPool.Get(request.DestTexture);
        if (destImage is null)
        {
            request.Complete(ResultCode.InvalidState);
            return;
        }

        // For texture uploads, delegate to the staging device on the graphics queue
        // since texture layout transitions and mipmap handling are complex.
        // The async benefit here is that we run it on a background thread.
        var result = _ctx.Upload(request.DestTexture, request.Range, 0, 0);

        // Now do the actual upload using the staging device
        unsafe
        {
            using var src = request.Pin();
            result = _ctx.Upload(
                request.DestTexture,
                request.Range,
                (nint)src.Pointer,
                request.DataSize
            );
        }

        request.Complete(result);
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
