using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Graphics.Vulkan;
using Microsoft.Extensions.Logging;
using Vortice.Vulkan;
using VK = Vortice.Vulkan.Vulkan;

namespace HelixToolkit.Nex.Avalonia;

/// <summary>
/// Linux implementation of <see cref="IEngineOutputBridge"/>. Shares the engine's offscreen output
/// through a pure Vulkan-to-Vulkan external-memory path (no DirectX): the engine renders into a
/// <c>VkImage</c> whose <c>VkDeviceMemory</c> is allocated as exportable external memory
/// (<see cref="VkExportMemoryAllocateInfo"/> with the opaque-fd / dma-buf handle types), and the
/// backing allocation is exported as a POSIX file descriptor via <c>vkGetMemoryFdKHR</c>. That fd is
/// later handed to the Avalonia compositor for import through <c>ICompositionGpuInterop</c>.
/// </summary>
/// <remarks>
/// <para>
/// The bridge is compiled unconditionally (it is not guarded by a Windows/Linux preprocessor symbol)
/// so it can be compile-verified on both the <c>net8.0-windows</c> and <c>net8.0</c> target
/// frameworks of the single-conditional-TFM Avalonia project. It has no DirectX dependency and only
/// uses Vulkan symbols (available on both TFMs through <c>Vortice.Vulkan</c>). The external-memory-fd
/// device functions it calls (for example <c>vkGetMemoryFdKHR</c>) exist only at runtime on Linux
/// with <see cref="VulkanContextConfig.EnableExternalMemoryFd"/> enabled (task 8.1); callers must
/// guard construction behind <see cref="OperatingSystem.IsLinux"/>.
/// </para>
/// <para>
/// Task 8.2 owns the exportable <c>VkImage</c>/<c>VkDeviceMemory</c> allocation, the fd export, the
/// engine render target (<see cref="EngineTarget"/>), and <see cref="Resize"/>. The semaphore
/// synchronization together with <see cref="CreateImportDescription"/> / <see cref="CreateSurfaceSync"/>
/// are completed by task 8.3; the fields those members depend on
/// (<see cref="_memoryFd"/>, <see cref="_memorySize"/>, <see cref="_dmaBufModifier"/>,
/// <see cref="_width"/>, <see cref="_height"/>) are established here so that wiring can plug in
/// cleanly. Engine writes on Linux are serialized with a Vulkan semaphore rather than a keyed mutex,
/// so <see cref="EngineSyncInfo"/> stays <c>default</c> (<see cref="KeyedMutexSyncType.None"/>).
/// </para>
/// </remarks>
internal sealed class LinuxExternalMemoryBridge : IEngineOutputBridge
{
    private static readonly ILogger _logger = LogManager.Create<LinuxExternalMemoryBridge>();

    /// <summary>
    /// External-memory handle types the backing allocation is made exportable for. The allocation is
    /// created with both opaque-fd and dma-buf so the compositor can import via either path; the
    /// exported file descriptor itself is obtained for <see cref="ExportHandleType"/>.
    /// </summary>
    private const VkExternalMemoryHandleTypeFlags ExportHandleTypes =
        VkExternalMemoryHandleTypeFlags.OpaqueFD | VkExternalMemoryHandleTypeFlags.DmaBufEXT;

    /// <summary>
    /// The single handle type used for the actual fd export and the format-capability query.
    /// Opaque-fd is used because it is the most broadly supported and does not require querying a
    /// dma-buf format modifier.
    /// </summary>
    private const VkExternalMemoryHandleTypeFlags ExportHandleType =
        VkExternalMemoryHandleTypeFlags.OpaqueFD;

    /// <summary>
    /// The external-semaphore handle type used for the exportable write/read serialization
    /// semaphores. Opaque-fd matches the Avalonia
    /// <c>KnownPlatformGraphicsExternalSemaphoreHandleTypes.VulkanOpaquePosixFileDescriptor</c>
    /// import path.
    /// </summary>
    private const VkExternalSemaphoreHandleTypeFlags SemaphoreExportHandleType =
        VkExternalSemaphoreHandleTypeFlags.OpaqueFD;

    /// <summary>
    /// Avalonia known external-image handle-type name for a Vulkan image whose backing memory was
    /// exported as an opaque POSIX file descriptor. Matches
    /// <c>KnownPlatformGraphicsExternalImageHandleTypes.VulkanOpaquePosixFileDescriptor</c>; used as
    /// a literal to avoid depending on the (internal) Avalonia constant type, mirroring the Windows
    /// bridge.
    /// </summary>
    private const string VulkanOpaqueFdImageHandleType = "VulkanOpaquePosixFileDescriptor";

    /// <summary>Format of the exported render target (matches <see cref="Format.RGBA_UN8"/>).</summary>
    private const VkFormat Format = VkFormat.R8G8B8A8Unorm;

    /// <summary>The engine Vulkan context the exported image is created in.</summary>
    private readonly VulkanContext _ctx;

    /// <summary>The exported VkImage the engine renders into.</summary>
    private VkImage _image = VkImage.Null;

    /// <summary>The exportable device memory backing <see cref="_image"/>.</summary>
    private VkDeviceMemory _memory = VkDeviceMemory.Null;

    /// <summary>The image view created for <see cref="_image"/> (owned by this bridge).</summary>
    private VkImageView _imageView = VkImageView.Null;

    /// <summary>The texture handle registered in the engine's textures pool (the render target).</summary>
    private TextureHandle _handle = TextureHandle.Null;

    /// <summary>
    /// The exported POSIX file descriptor for <see cref="_memory"/>, or <c>-1</c> when not exported.
    /// Consumed by <see cref="CreateImportDescription"/> (task 8.3).
    /// </summary>
    private int _memoryFd = -1;

    /// <summary>
    /// Size, in bytes, of the exported device-memory allocation. Consumed by
    /// <see cref="CreateImportDescription"/> (task 8.3).
    /// </summary>
    private ulong _memorySize;

    /// <summary>
    /// Optional dma-buf format modifier describing the memory layout, when the exported handle is a
    /// dma-buf. <c>null</c> for opaque-fd exports. Consumed by <see cref="CreateImportDescription"/>
    /// (task 8.3).
    /// </summary>
    private ulong? _dmaBufModifier;

    /// <summary>
    /// Exportable binary semaphore the engine signals once it has finished writing the frame; the
    /// Avalonia compositor waits on this before reading (the <c>waitForSemaphore</c> passed to
    /// <c>UpdateWithSemaphoresAsync</c>).
    /// </summary>
    private VkSemaphore _renderFinishedSemaphore = VkSemaphore.Null;

    /// <summary>
    /// Exportable binary semaphore the Avalonia compositor signals once it has finished reading the
    /// frame; the engine waits on this before overwriting the shared image (the
    /// <c>signalSemaphore</c> passed to <c>UpdateWithSemaphoresAsync</c>).
    /// </summary>
    private VkSemaphore _readFinishedSemaphore = VkSemaphore.Null;

    /// <summary>
    /// Exported POSIX file descriptor for <see cref="_renderFinishedSemaphore"/>, or <c>-1</c> when
    /// not exported. Consumed by <see cref="CreateSurfaceSync"/>.
    /// </summary>
    private int _renderFinishedSemaphoreFd = -1;

    /// <summary>
    /// Exported POSIX file descriptor for <see cref="_readFinishedSemaphore"/>, or <c>-1</c> when
    /// not exported. Consumed by <see cref="CreateSurfaceSync"/>.
    /// </summary>
    private int _readFinishedSemaphoreFd = -1;

    /// <summary>
    /// Cached surface-update sync for the current resources. Importing the opaque-fd semaphores
    /// consumes their file descriptors, so the sync object (which imports each fd once and caches the
    /// resulting compositor semaphore) must be reused across frames rather than recreated per frame;
    /// recreating it would re-import an already-consumed fd and fail. Recreated on <see cref="Resize"/>.
    /// </summary>
    private SemaphoreSurfaceUpdateSync? _surfaceSync;

    private uint _width;
    private uint _height;
    private bool _disposed;

    /// <summary>
    /// Creates the bridge and its initial exportable output resources at the given size.
    /// </summary>
    /// <param name="context">
    /// The engine's Vulkan context. Must be a <see cref="VulkanContext"/> created with
    /// <see cref="VulkanContextConfig.EnableExternalMemoryFd"/> enabled.
    /// </param>
    /// <param name="width">Initial output width in pixels; must be greater than zero.</param>
    /// <param name="height">Initial output height in pixels; must be greater than zero.</param>
    public LinuxExternalMemoryBridge(IContext context, uint width, uint height)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context is not VulkanContext ctx)
        {
            throw new InvalidOperationException(
                "LinuxExternalMemoryBridge requires a VulkanContext instance."
            );
        }

        _ctx = ctx;
        CreateResources(width, height);
    }

    /// <inheritdoc />
    public TextureHandle EngineTarget => _handle;

    /// <summary>The exported memory file descriptor, or <c>-1</c> when no resources are allocated.</summary>
    internal int MemoryFd => _memoryFd;

    /// <summary>The size, in bytes, of the exported device-memory allocation.</summary>
    internal ulong MemorySize => _memorySize;

    /// <summary>The dma-buf format modifier when applicable; otherwise <c>null</c>.</summary>
    internal ulong? DmaBufModifier => _dmaBufModifier;

    /// <inheritdoc />
    /// <remarks>
    /// On Linux, engine writes are serialized with exported Vulkan binary semaphores rather than a
    /// keyed mutex. The engine signals <see cref="_renderFinishedSemaphore"/> after writing the frame
    /// (the compositor waits on it before reading) and waits on <see cref="_readFinishedSemaphore"/>
    /// before overwriting the shared image (the compositor signals it after reading). Without this the
    /// compositor's <c>UpdateWithSemaphoresAsync</c> would wait forever on a render-finished semaphore
    /// the engine never signals, deadlocking the present and freezing the window.
    /// </remarks>
    public KeyedMutexSyncInfo EngineSyncInfo =>
        new()
        {
            SyncType = KeyedMutexSyncType.ExternalSemaphore,
            WaitSemaphoreHandle = _readFinishedSemaphore.Handle,
            SignalSemaphoreHandle = _renderFinishedSemaphore.Handle,
        };

    /// <inheritdoc />
    /// <remarks>
    /// Returns the exported external-memory POSIX file descriptor (opaque-fd) together with the
    /// allocation size and, when applicable, the dma-buf format modifier, typed as a Vulkan
    /// opaque-fd image for import by Avalonia's <c>ICompositionGpuInterop.ImportImage</c>.
    /// </remarks>
    public SharedImageDescription CreateImportDescription()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_memoryFd < 0)
        {
            throw new InvalidOperationException(
                "External-memory resources have not been created; call the constructor or Resize first."
            );
        }

        return new SharedImageDescription
        {
            Width = _width,
            Height = _height,
            Format = Graphics.Format.RGBA_UN8,
            MemoryFd = _memoryFd,
            MemorySize = _memorySize,
            DmaBufModifier = _dmaBufModifier,
            ExternalHandleType = VulkanOpaqueFdImageHandleType,
        };
    }

    /// <inheritdoc />
    /// <remarks>
    /// Returns a semaphore-based surface update that imports the exported render-finished
    /// (compositor wait) and read-finished (compositor signal) semaphore fds through
    /// <c>ICompositionGpuInterop.ImportSemaphore</c> and drives
    /// <c>CompositionDrawingSurface.UpdateWithSemaphoresAsync</c>, serializing engine writes against
    /// compositor reads with no OpenGL/EGL/software path.
    /// </remarks>
    public ISurfaceUpdateSync CreateSurfaceSync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_surfaceSync is null)
        {
            throw new InvalidOperationException(
                "Semaphore resources have not been created; call the constructor or Resize first."
            );
        }

        // Reuse the cached sync object so the opaque-fd semaphores are imported once and reused;
        // re-importing a consumed fd every frame fails and crashes the compositor GPU backend.
        return _surfaceSync;
    }

    /// <inheritdoc />
    public void Resize(uint width, uint height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (width == 0 || height == 0)
        {
            // Zero-size ticks are skipped by the control; ignore defensively.
            return;
        }

        if (width == _width && height == _height)
        {
            return;
        }

        ReleaseResources();
        CreateResources(width, height);
    }

    /// <inheritdoc />
    /// <remarks>Single-buffered; multi-buffering is provided by <see cref="BufferedEngineOutputBridge"/>.</remarks>
    public void AdvanceFrame()
    {
        // No-op: this bridge owns a single exportable image.
    }

    /// <summary>
    /// Creates the exportable <c>VkImage</c>/<c>VkDeviceMemory</c> at the given size, exports the
    /// memory fd, and registers the image in the engine textures pool to establish
    /// <see cref="EngineTarget"/>.
    /// </summary>
    private unsafe void CreateResources(uint width, uint height)
    {
        if (width == 0 || height == 0)
        {
            return;
        }

        _logger.LogInformation(
            "Creating Linux external-memory bridge resources at {Width}x{Height}.",
            width,
            height
        );

        var device = _ctx.VkDevice;
        var physicalDevice = _ctx.VkPhysicalDevice;

        // 1. Query external-memory features for the format/handle type to decide on dedicated
        //    allocation, and to fail fast (NotSupportedException) on unsupported formats.
        var externalMemoryFeatures = QueryExternalMemoryFeatures(
            physicalDevice,
            Format,
            ExportHandleType
        );

        // 2. Create the VkImage flagged as external-memory-capable.
        VkExternalMemoryImageCreateInfo externalMemoryImageInfo = new()
        {
            handleTypes = ExportHandleTypes,
        };

        VkImageCreateInfo imageCreateInfo = new()
        {
            pNext = &externalMemoryImageInfo,
            imageType = VkImageType.Image2D,
            format = Format,
            extent = new VkExtent3D(width, height, 1),
            mipLevels = 1,
            arrayLayers = 1,
            samples = VkSampleCountFlags.Count1,
            tiling = VkImageTiling.Optimal,
            usage =
                VkImageUsageFlags.ColorAttachment
                | VkImageUsageFlags.Sampled
                | VkImageUsageFlags.TransferSrc
                | VkImageUsageFlags.TransferDst,
            sharingMode = VkSharingMode.Exclusive,
            initialLayout = VkImageLayout.Undefined,
        };

        VkImage image;
        VK.vkCreateImage(device, &imageCreateInfo, null, &image)
            .CheckResult("Failed to create exportable external-memory image");

        // 3. Query memory requirements.
        VkMemoryRequirements memRequirements;
        VK.vkGetImageMemoryRequirements(device, image, &memRequirements);

        // 4. Allocate exportable device-local memory. The allocation is made exportable through
        //    VkExportMemoryAllocateInfo, optionally chained with a dedicated allocation.
        VkExportMemoryAllocateInfo exportAllocateInfo = new()
        {
            handleTypes = ExportHandleTypes,
        };

        VkMemoryDedicatedAllocateInfo dedicatedAllocateInfo = new() { image = image };
        if (ShouldUseDedicatedAllocation(externalMemoryFeatures))
        {
            // Chain: exportAllocateInfo -> dedicatedAllocateInfo
            exportAllocateInfo.pNext = &dedicatedAllocateInfo;
        }

        VkMemoryAllocateInfo memoryAllocateInfo = new()
        {
            pNext = &exportAllocateInfo,
            allocationSize = memRequirements.size,
            memoryTypeIndex = HxVkUtils.FindMemoryType(
                physicalDevice,
                memRequirements.memoryTypeBits,
                VkMemoryPropertyFlags.DeviceLocal
            ),
        };

        VkDeviceMemory memory;
        VK.vkAllocateMemory(device, &memoryAllocateInfo, null, &memory)
            .CheckResult("Failed to allocate exportable external memory");

        // 5. Bind memory to image.
        VK.vkBindImageMemory(device, image, memory, 0)
            .CheckResult("Failed to bind exportable memory to image");

        // 6. Export the memory as a POSIX file descriptor for the compositor import.
        VkMemoryGetFdInfoKHR getFdInfo = new()
        {
            memory = memory,
            handleType = ExportHandleType,
        };

        int fd = -1;
        VK.vkGetMemoryFdKHR(device, &getFdInfo, &fd)
            .CheckResult("Failed to export device memory as a POSIX file descriptor");

        // 7. Create the image view (owned by this bridge).
        VkImageViewCreateInfo imageViewCreateInfo = new()
        {
            image = image,
            viewType = VkImageViewType.Image2D,
            format = Format,
            components = new VkComponentMapping(
                VkComponentSwizzle.Identity,
                VkComponentSwizzle.Identity,
                VkComponentSwizzle.Identity,
                VkComponentSwizzle.Identity
            ),
            subresourceRange = new VkImageSubresourceRange
            {
                aspectMask = VkImageAspectFlags.Color,
                baseMipLevel = 0,
                levelCount = 1,
                baseArrayLayer = 0,
                layerCount = 1,
            },
        };

        VkImageView imageView;
        VK.vkCreateImageView(device, &imageViewCreateInfo, null, &imageView)
            .CheckResult("Failed to create image view for exported texture");

        // 8. Wrap in a VulkanImage (isOwningVkImage = false — this bridge owns the VkImage/memory and
        //    the image view and frees them in ReleaseResources, mirroring the imported-texture
        //    wrapping approach). The view is intentionally NOT assigned to VulkanImage.ImageView:
        //    VulkanImage.Dispose destroys its own ImageView even when isOwningVkImage is false, so
        //    attaching it here would double-free the view we release manually below.
        var vulkanImage = new VulkanImage(
            _ctx,
            image,
            usage: imageCreateInfo.usage,
            extent: imageCreateInfo.extent,
            type: VkImageType.Image2D,
            format: Format,
            isDepthFormat: false,
            isStencilFormat: false,
            isSwapchainImage: false,
            isOwningVkImage: false,
            debugName: "Exported External-Memory Texture"
        );

        // 9. Register in TexturesPool to obtain the TextureHandle used as the engine render target.
        TextureHandle handle = _ctx.TexturesPool.Create(vulkanImage);
        _ctx.AwaitingCreation = true;

        // 10. Create the exportable write/read serialization semaphores and export their fds. The
        //     render-finished semaphore is signaled by the engine write and awaited by the
        //     compositor read; the read-finished semaphore is signaled by the compositor read and
        //     awaited by the next engine write.
        (VkSemaphore renderFinishedSemaphore, int renderFinishedFd) = CreateExportableSemaphore(
            device,
            "render-finished"
        );

        VkSemaphore readFinishedSemaphore = VkSemaphore.Null;
        int readFinishedFd = -1;
        try
        {
            (readFinishedSemaphore, readFinishedFd) = CreateExportableSemaphore(
                device,
                "read-finished"
            );
        }
        catch
        {
            // Roll back the first semaphore so a partial failure does not leak it.
            if (renderFinishedFd >= 0)
            {
                CloseFileDescriptor(renderFinishedFd);
            }
            if (renderFinishedSemaphore.IsNotNull)
            {
                VK.vkDestroySemaphore(device, renderFinishedSemaphore, null);
            }
            throw;
        }

        // 11. Commit state.
        _image = image;
        _memory = memory;
        _imageView = imageView;
        _handle = handle;
        _memoryFd = fd;
        _memorySize = memRequirements.size;
        _dmaBufModifier = null; // opaque-fd export; dma-buf modifier query is a dma-buf-path concern.
        _renderFinishedSemaphore = renderFinishedSemaphore;
        _renderFinishedSemaphoreFd = renderFinishedFd;
        _readFinishedSemaphore = readFinishedSemaphore;
        _readFinishedSemaphoreFd = readFinishedFd;
        _width = width;
        _height = height;

        // Create the surface-update sync once for these resources. It imports the opaque-fd semaphores
        // lazily on first present and caches them; it is returned unchanged every frame and only
        // disposed when these resources are released (Resize/Dispose).
        _surfaceSync = new SemaphoreSurfaceUpdateSync(renderFinishedFd, readFinishedFd);

        // The engine waits on the read-finished semaphore before its first write to this image, but
        // the compositor has not read (and therefore not signaled) it yet. Pre-signal it once so the
        // very first engine write proceeds instead of deadlocking on an unsignaled binary semaphore.
        SignalSemaphoreInitially(readFinishedSemaphore);
    }

    /// <summary>
    /// Submits an empty signal of the given binary semaphore on the engine's graphics queue so it
    /// starts in the signaled state. Used to bootstrap the read-finished semaphore the engine waits
    /// on before its first write, which the compositor has not yet signaled.
    /// </summary>
    private unsafe void SignalSemaphoreInitially(VkSemaphore semaphore)
    {
        VkSemaphoreSubmitInfo signalInfo = new()
        {
            semaphore = semaphore,
            stageMask = VkPipelineStageFlags2.AllCommands,
        };

        VkSubmitInfo2 submitInfo = new()
        {
            signalSemaphoreInfoCount = 1,
            pSignalSemaphoreInfos = &signalInfo,
        };

        VK.vkQueueSubmit2(_ctx.GraphicsQueue.GraphicsQueue, 1, &submitInfo, VkFence.Null)
            .CheckResult("Failed to pre-signal the read-finished semaphore");
    }

    /// <summary>
    /// Creates a binary <c>VkSemaphore</c> flagged as exportable
    /// (<see cref="VkExportSemaphoreCreateInfo"/> with the opaque-fd handle type) and exports its
    /// backing POSIX file descriptor via <c>vkGetSemaphoreFdKHR</c>.
    /// </summary>
    /// <param name="device">The Vulkan device the semaphore is created on.</param>
    /// <param name="role">A short role label used in error messages (for diagnostics only).</param>
    /// <returns>The created semaphore together with its exported file descriptor.</returns>
    private static unsafe (VkSemaphore Semaphore, int Fd) CreateExportableSemaphore(
        VkDevice device,
        string role
    )
    {
        VkExportSemaphoreCreateInfo exportSemaphoreInfo = new()
        {
            handleTypes = SemaphoreExportHandleType,
        };

        VkSemaphoreCreateInfo semaphoreCreateInfo = new() { pNext = &exportSemaphoreInfo };

        VkSemaphore semaphore;
        VK.vkCreateSemaphore(device, &semaphoreCreateInfo, null, &semaphore)
            .CheckResult($"Failed to create exportable {role} semaphore");

        VkSemaphoreGetFdInfoKHR getFdInfo = new()
        {
            semaphore = semaphore,
            handleType = SemaphoreExportHandleType,
        };

        int fd = -1;
        VkResult result = VK.vkGetSemaphoreFdKHR(device, &getFdInfo, &fd);
        if (result != VkResult.Success)
        {
            VK.vkDestroySemaphore(device, semaphore, null);
            result.CheckResult($"Failed to export {role} semaphore as a POSIX file descriptor");
        }

        return (semaphore, fd);
    }

    /// <summary>
    /// Releases the exported image, its memory, view, texture handle, and any exported fd. Waits for
    /// the GPU to finish before tearing down shared resources.
    /// </summary>
    private unsafe void ReleaseResources()
    {
        // Wait for the GPU to finish reading/writing before tearing down shared resources.
        _ctx.Wait(default);

        // Release the cached surface sync (and its imported compositor semaphores) before destroying
        // the exported semaphores and closing their fds. Disposal is async (composition GPU resources
        // are IAsyncDisposable); fire-and-forget, observing faults, since the compositor's imported
        // copies are backed by their own dup'd fds and are independent of the ones destroyed below.
        var surfaceSync = _surfaceSync;
        _surfaceSync = null;
        if (surfaceSync is not null)
        {
            // DisposeAsync swallows its own faults, so the discarded task never surfaces one.
            _ = surfaceSync.DisposeAsync().AsTask();
        }

        var device = _ctx.VkDevice;

        // Destroy the texture handle in the pool first (disposes the VulkanImage wrapper, which only
        // destroys its own image views since isOwningVkImage = false).
        if (_handle.Valid)
        {
            _ctx.TexturesPool.Destroy(_handle);
            _handle = TextureHandle.Null;
        }

        if (_imageView.IsNotNull)
        {
            VK.vkDestroyImageView(device, _imageView, null);
            _imageView = VkImageView.Null;
        }

        if (_image.IsNotNull)
        {
            VK.vkDestroyImage(device, _image, null);
            _image = VkImage.Null;
        }

        if (_memory.IsNotNull)
        {
            VK.vkFreeMemory(device, _memory, null);
            _memory = VkDeviceMemory.Null;
        }

        // The exported fd is owned by the importer once handed off; until then close it here to
        // avoid leaking descriptors when resources are released before an import occurs.
        if (_memoryFd >= 0)
        {
            CloseFileDescriptor(_memoryFd);
            _memoryFd = -1;
        }

        // Close the exported semaphore fds (owned by the compositor import once handed off) and
        // destroy the semaphores themselves.
        if (_renderFinishedSemaphoreFd >= 0)
        {
            CloseFileDescriptor(_renderFinishedSemaphoreFd);
            _renderFinishedSemaphoreFd = -1;
        }

        if (_readFinishedSemaphoreFd >= 0)
        {
            CloseFileDescriptor(_readFinishedSemaphoreFd);
            _readFinishedSemaphoreFd = -1;
        }

        if (_renderFinishedSemaphore.IsNotNull)
        {
            VK.vkDestroySemaphore(device, _renderFinishedSemaphore, null);
            _renderFinishedSemaphore = VkSemaphore.Null;
        }

        if (_readFinishedSemaphore.IsNotNull)
        {
            VK.vkDestroySemaphore(device, _readFinishedSemaphore, null);
            _readFinishedSemaphore = VkSemaphore.Null;
        }

        _memorySize = 0;
        _dmaBufModifier = null;
        _width = 0;
        _height = 0;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        ReleaseResources();

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Determines whether dedicated allocation is required based on external-memory feature flags.
    /// Returns <c>true</c> when <see cref="VkExternalMemoryFeatureFlags.DedicatedOnly"/> is set.
    /// </summary>
    private static bool ShouldUseDedicatedAllocation(VkExternalMemoryFeatureFlags featureFlags) =>
        featureFlags.HasAllFlags(VkExternalMemoryFeatureFlags.DedicatedOnly);

    /// <summary>
    /// Queries external-memory feature flags for the given format and export handle type, throwing
    /// <see cref="NotSupportedException"/> when the format cannot be exported with that handle type.
    /// </summary>
    private static unsafe VkExternalMemoryFeatureFlags QueryExternalMemoryFeatures(
        VkPhysicalDevice physicalDevice,
        VkFormat format,
        VkExternalMemoryHandleTypeFlags handleType
    )
    {
        VkPhysicalDeviceExternalImageFormatInfo externalFormatInfo = new()
        {
            handleType = handleType,
        };

        VkPhysicalDeviceImageFormatInfo2 formatInfo = new()
        {
            pNext = &externalFormatInfo,
            format = format,
            type = VkImageType.Image2D,
            tiling = VkImageTiling.Optimal,
            usage =
                VkImageUsageFlags.ColorAttachment
                | VkImageUsageFlags.Sampled
                | VkImageUsageFlags.TransferSrc
                | VkImageUsageFlags.TransferDst,
        };

        VkExternalImageFormatProperties externalFormatProperties = new();
        VkImageFormatProperties2 formatProperties = new() { pNext = &externalFormatProperties };

        VkResult result = VK.vkGetPhysicalDeviceImageFormatProperties2(
            physicalDevice,
            &formatInfo,
            &formatProperties
        );

        if (result == VkResult.ErrorFormatNotSupported)
        {
            throw new NotSupportedException(
                $"External-memory handle type '{handleType}' is not supported for format '{format}'."
            );
        }

        result.CheckResult("Failed to query external image format properties");

        return externalFormatProperties.externalMemoryProperties.externalMemoryFeatures;
    }

    /// <summary>
    /// Closes a POSIX file descriptor. The exported memory fd is a POSIX handle even though the
    /// bridge is compiled on both TFMs; on non-Linux hosts this is a defensive no-op because such an
    /// fd is never produced (construction is Linux-guarded by the caller).
    /// </summary>
    private static void CloseFileDescriptor(int fd)
    {
        if (fd < 0)
        {
            return;
        }

        if (OperatingSystem.IsLinux())
        {
            _ = NativeClose(fd);
        }
    }

    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "close", SetLastError = true)]
    private static extern int NativeClose(int fd);
}
