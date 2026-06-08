using System.Runtime.CompilerServices;
using HelixToolkit.Nex.Rendering.Components;

namespace HelixToolkit.Nex.Engine;

/// <summary>
/// Holds all GPU-side state required for non-blocking, 1-frame-latency pick readbacks.
/// <para>
/// Usage pattern (per viewport / per RenderContext):
/// <list type="number">
/// <item>Create one instance and keep it alive alongside your <see cref="RenderContext"/>.</item>
/// <item>After recording the render graph into a command buffer (but <b>before</b> submitting),
///       call <see cref="GpuPicking.SchedulePickReadback"/> to copy one pixel from the mesh-id
///       texture into the internal host-visible buffer via <c>CopyTextureToBuffer</c> —
///       no GPU stall.</item>
/// <item>Submit the command buffer and store the returned <see cref="SubmitHandle"/> via
///       <see cref="SetPendingSubmit"/>.</item>
/// <item>On the <b>next frame</b> (or any later frame), call <see cref="GpuPicking.TryPickAsync"/>
///       to poll <c>IsReady</c>. If the GPU has finished, the pixel is read directly from
///       host-visible buffer memory with zero blocking and a full <see cref="PickingResult"/> is assembled.</item>
/// </list>
/// </para>
/// </summary>
public sealed class PickingReadbackContext : IDisposable
{
    private readonly IContext _context;

    /// <summary>
    /// 8-byte host-visible buffer that receives one mesh-id pixel (2 × float32 = RG_F32)
    /// copied from the entity-id texture each frame via <c>CopyTextureToBuffer</c>.
    /// Kept alive for the lifetime of this object.
    /// </summary>
    internal BufferResource StagingBuffer { get; }

    /// <summary>
    /// The submit handle of the command buffer that contains the last <c>CopyTextureToBuffer</c>.
    /// <see cref="SubmitHandle.Empty"/> when no readback is in flight.
    /// </summary>
    internal SubmitHandle PendingHandle { get; private set; }

    /// <summary>Screen-space X coordinate of the pending pick request.</summary>
    internal int PendingX { get; private set; }

    /// <summary>Screen-space Y coordinate of the pending pick request.</summary>
    internal int PendingY { get; private set; }

    /// <summary>
    /// Whether a readback is scheduled (i.e., a pick coordinate has been enqueued
    /// and the copy has been recorded into a command buffer).
    /// </summary>
    public bool HasPending => !PendingHandle.Empty;

    /// <param name="context">The graphics context that owns the staging buffer.</param>
    public PickingReadbackContext(IContext context)
    {
        _context = context;
        // 8 bytes — two float32 channels of one RG_F32 pixel
        context
            .CreateBuffer(
                new BufferDesc(
                    BufferUsageBits.Storage,
                    StorageType.HostVisible,
                    nint.Zero,
                    sizeof(ulong),
                    "PickingReadbackStagingBuffer"
                ),
                out var buf
            )
            .CheckResult();
        StagingBuffer = buf;
    }

    /// <summary>
    /// Records the pending pick position and submit handle after the copy command has
    /// been recorded into the command buffer and the buffer has been submitted.
    /// </summary>
    /// <param name="handle">The <see cref="SubmitHandle"/> returned by <see cref="IContext.Submit"/>.</param>
    /// <param name="x">Screen-space X coordinate that was scheduled.</param>
    /// <param name="y">Screen-space Y coordinate that was scheduled.</param>
    public void SetPendingSubmit(SubmitHandle handle, int x, int y)
    {
        PendingHandle = handle;
        PendingX = x;
        PendingY = y;
    }

    /// <summary>Clears the pending readback state without consuming the result.</summary>
    public void ClearPending()
    {
        PendingHandle = default;
        PendingX = 0;
        PendingY = 0;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        StagingBuffer.Dispose();
    }
}

public enum PickedGeometryType
{
    None,
    Mesh,
    PointCloud,
}

public readonly struct PickingResult
{
    public Entity Entity { get; init; }
    public uint InstanceId { get; init; }
    public uint PrimitiveId { get; init; }
    public PickedGeometryType PickGeometryType { get; init; }
    public Vector3 WorldPosition { get; init; }
    public Ray Ray { get; init; }
}

public static class GpuPicking
{
    private static readonly ILogger _logger = LogManager.Create("GpuPicking");

    /// <summary>
    /// Attempts to retrieve mesh picking information at the specified pixel coordinates from the given texture.
    /// </summary>
    /// <remarks>If the specified coordinates are outside the bounds of the texture, the method returns <see
    /// langword="false"/> and all output parameters are set to zero. Output identifiers are only valid if the method
    /// returns <see langword="true"/>.</remarks>
    /// <param name="context">The context used to perform the texture download operation.</param>
    /// <param name="meshIdTexture">The texture containing mesh identification data to sample from.</param>
    /// <param name="textureWidth">The width of the mesh ID texture, in pixels.</param>
    /// <param name="textureHeight">The height of the mesh ID texture, in pixels.</param>
    /// <param name="x">The x-coordinate of the pixel to sample, in texture space. Must be greater than or equal to 0 and less than
    /// <paramref name="textureWidth"/>.</param>
    /// <param name="y">The y-coordinate of the pixel to sample, in texture space. Must be greater than or equal to 0 and less than
    /// <paramref name="textureHeight"/>.</param>
    /// <param name="worldId">When this method returns, contains the world identifier at the specified pixel, if the operation succeeds;
    /// otherwise, zero.</param>
    /// <param name="entityId">When this method returns, contains the entity identifier at the specified pixel, if the operation succeeds;
    /// otherwise, zero.</param>
    /// <param name="instanceId">When this method returns, contains the instance identifier at the specified pixel, if the operation succeeds;
    /// otherwise, zero.</param>
    /// <param name="primitiveId">When this method returns, contains the primitive identifier at the specified pixel, if the operation succeeds;
    /// otherwise, zero.</param>
    /// <returns><see langword="true"/> if picking information was successfully retrieved and valid identifiers were found;
    /// otherwise, <see langword="false"/>.</returns>
    public static bool TryPickRaw(
        this IContext context,
        TextureHandle meshIdTexture,
        uint textureWidth,
        uint textureHeight,
        int x,
        int y,
        out uint worldId,
        out uint entityId,
        out uint instanceId,
        out uint primitiveId
    )
    {
        worldId = 0;
        entityId = 0;
        instanceId = 0;
        primitiveId = 0;
        if (x < 0 || y < 0 || x >= textureWidth || y >= textureHeight)
        {
            return false;
        }
        unsafe
        {
            var data = stackalloc uint[2];
            var ret = context
                .Download(
                    meshIdTexture,
                    new TextureRangeDesc()
                    {
                        Dimensions = new Dimensions(1, 1, 1),
                        Offset = new Offset3D(x, y),
                    },
                    (nint)data,
                    sizeof(ulong)
                )
                .CheckResult();
            if (ret == ResultCode.Ok)
            {
                Utils.UnpackMeshInfo(
                    data[0],
                    data[1],
                    out worldId,
                    out entityId,
                    out instanceId,
                    out primitiveId
                );
                return worldId > 0 && entityId > 0;
            }
        }

        return false;
    }

    /// <summary>
    /// Attempts to retrieve picking information for a specified screen coordinate from the current render context.
    /// </summary>
    /// <remarks>This method returns false if the picking texture is unavailable or empty. Output parameters
    /// are set to zero if picking fails.</remarks>
    /// <param name="context">The render context containing the resources and state used for picking.</param>
    /// <param name="x">The x-coordinate, in pixels, of the screen position to pick.</param>
    /// <param name="y">The y-coordinate, in pixels, of the screen position to pick.</param>
    /// <param name="worldId">When this method returns, contains the identifier of the world at the specified coordinate, if picking succeeds;
    /// otherwise, zero.</param>
    /// <param name="entityId">When this method returns, contains the identifier of the entity at the specified coordinate, if picking
    /// succeeds; otherwise, zero.</param>
    /// <param name="instanceId">When this method returns, contains the identifier of the instance at the specified coordinate, if picking
    /// succeeds; otherwise, zero.</param>
    /// <param name="primitiveId">When this method returns, contains the identifier of the primitive at the specified coordinate, if picking
    /// succeeds; otherwise, zero.</param>
    /// <returns>true if picking information was successfully retrieved for the specified coordinate; otherwise, false.</returns>
    public static bool TryPickRaw(
        this RenderContext context,
        int x,
        int y,
        out uint worldId,
        out uint entityId,
        out uint instanceId,
        out uint primitiveId
    )
    {
        worldId = 0;
        entityId = 0;
        instanceId = 0;
        primitiveId = 0;
        if (
            !context.ResourceSet.Textures.TryGetValue(
                SystemBufferNames.TextureEntityId,
                out var texture
            ) || texture.Empty
        )
        {
            return false;
        }
        var dimem = context.Context.GetDimensions(texture);
        return TryPickRaw(
            context.Context,
            texture,
            dimem.Width,
            dimem.Height,
            x,
            y,
            out worldId,
            out entityId,
            out instanceId,
            out primitiveId
        );
    }

    /// <summary>
    /// Attempts to perform a picking operation at the specified screen coordinates and returns the result if
    /// successful.
    /// </summary>
    /// <param name="context">The render context in which the picking operation is performed. Cannot be null.</param>
    /// <param name="x">The x-coordinate, in screen space, where the picking operation is attempted.</param>
    /// <param name="y">The y-coordinate, in screen space, where the picking operation is attempted.</param>
    /// <returns>A <see cref="PickingResult"/> containing the details of the picked object if the operation succeeds; otherwise,
    /// <see langword="null"/>.</returns>
    public static PickingResult? Pick(this RenderContext context, int x, int y)
    {
        if (context.TryPick(x, y, out var result))
        {
            return result;
        }
        return null;
    }

    /// <summary>
    /// Attempts to identify and retrieve picking information for a rendered entity at the specified screen coordinates.
    /// </summary>
    /// <remarks>This method does not throw exceptions for missing entities or invalid coordinates; it returns
    /// false if picking fails for any reason. The contents of the result parameter are only valid if the method returns
    /// true.</remarks>
    /// <param name="context">The rendering context in which the picking operation is performed. Cannot be null.</param>
    /// <param name="x">The x-coordinate, in screen space, of the point to pick.</param>
    /// <param name="y">The y-coordinate, in screen space, of the point to pick.</param>
    /// <param name="result">When this method returns, contains the picking result data if the operation succeeds; otherwise, its contents
    /// are undefined. Must not be null.</param>
    /// <returns>true if an entity was successfully picked at the specified coordinates; otherwise, false.</returns>
    public static bool TryPick(this RenderContext context, int x, int y, out PickingResult result)
    {
        result = default;
        if (
            !TryPickRaw(
                context,
                x,
                y,
                out var worldId,
                out var entityId,
                out var instanceId,
                out var primitiveId
            )
        )
        {
            return false;
        }
        var world = World.GetWorldById((int)worldId);
        if (world is null)
        {
            _logger.LogWarning("Picking failed: world with ID {WorldId} not found", worldId);
            return false;
        }
        var entity = world.GetEntity((int)entityId);
        if (!context.TryUnProject(x, y, out var ray))
        {
            _logger.LogWarning(
                "Picking failed: unable to unproject screen coordinates ({X}, {Y}) to a ray",
                x,
                y
            );
            return false;
        }

        if (
            !entity.TryGetPickPosition(
                instanceId,
                primitiveId,
                ray,
                out var worldPosition,
                out var pickedType
            )
        )
        {
            _logger.LogWarning(
                "Picking failed: unable to retrieve pick position for entity ID {EntityId} at primitive ID {PrimitiveId} and instance ID {InstanceId}",
                entityId,
                primitiveId,
                instanceId
            );
            return false;
        }
        result = new()
        {
            Entity = entity,
            PrimitiveId = primitiveId,
            InstanceId = instanceId,
            WorldPosition = worldPosition,
            PickGeometryType = pickedType,
            Ray = ray,
        };
        return true;
    }

    public static bool TryGetPickPosition(
        this Entity entity,
        uint instanceId,
        uint primitiveId,
        Ray ray,
        out Vector3 position,
        out PickedGeometryType geometryType
    )
    {
        position = default;
        geometryType = PickedGeometryType.None;
        if (entity.Has<MeshComponent>())
        {
            if (entity.TryGetTriangleFromMesh(primitiveId, out var p0, out var p1, out var p2))
            {
                if (entity.Has<WorldTransform>())
                {
                    ref var transform = ref entity.Get<WorldTransform>();
                    p0 = Vector3.Transform(p0, transform.Value);
                    p1 = Vector3.Transform(p1, transform.Value);
                    p2 = Vector3.Transform(p2, transform.Value);
                }
                ref var meshComponent = ref entity.Get<MeshComponent>();
                if (meshComponent.Instancing is not null)
                {
                    if (instanceId >= meshComponent.Instancing.Transforms.Count)
                    {
                        return false;
                    }
                    var instanceTransform = meshComponent
                        .Instancing!.Transforms[(int)instanceId]
                        .ToMatrix();
                    p0 = Vector3.Transform(p0, instanceTransform);
                    p1 = Vector3.Transform(p1, instanceTransform);
                    p2 = Vector3.Transform(p2, instanceTransform);
                }
                ray.Intersects(ref p0, ref p1, ref p2, out position);
                geometryType = PickedGeometryType.Mesh;
                return true;
            }
        }
        else if (entity.Has<PointCloudComponent>())
        {
            if (entity.TryGetPointFromPointCloud(primitiveId, out var point))
            {
                position = point;
                geometryType = PickedGeometryType.PointCloud;
                return true;
            }
        }
        return false;
    }

    public static bool TryGetTriangleFromMesh(
        this Entity entity,
        uint primitiveId,
        out Vector3 p0,
        out Vector3 p1,
        out Vector3 p2
    )
    {
        p0 = default;
        p1 = default;
        p2 = default;
        if (!entity.Has<MeshComponent>())
        {
            return false;
        }
        var mesh = entity.Get<MeshComponent>().Geometry;
        if (mesh is null)
        {
            return false;
        }
        var indices = mesh.Indices;
        var vertices = mesh.Vertices;
        var idx = (int)primitiveId * 3;
        p0 = vertices[(int)indices[idx]].ToVector3();
        p1 = vertices[(int)indices[idx + 1]].ToVector3();
        p2 = vertices[(int)indices[idx + 2]].ToVector3();
        return true;
    }

    public static bool TryGetPointFromPointCloud(
        this Entity entity,
        uint primitiveId,
        out Vector3 position
    )
    {
        position = default;
        if (!entity.Has<PointCloudComponent>())
        {
            return false;
        }
        var pointCloud = entity.Get<PointCloudComponent>().Geometry;
        if (pointCloud is null)
        {
            return false;
        }
        var points = pointCloud.Vertices;
        if (primitiveId >= points.Count)
        {
            return false;
        }
        position = points[(int)primitiveId].ToVector3();
        return true;
    }

    /// <summary>
    /// Records a GPU copy of the single pixel at <paramref name="x"/>,<paramref name="y"/> from
    /// the mesh-id texture into the host-visible staging buffer inside
    /// <paramref name="readbackCtx"/> via <c>CopyTextureToBuffer</c>. No GPU stall occurs —
    /// the copy is simply enqueued in <paramref name="commandBuffer"/> alongside the rest of
    /// the frame's commands.
    /// </summary>
    /// <remarks>
    /// Call this method <b>after</b> the render graph has been recorded into
    /// <paramref name="commandBuffer"/> but <b>before</b> the buffer is submitted.
    /// After submitting, pass the returned <see cref="SubmitHandle"/> to
    /// <see cref="PickingReadbackContext.SetPendingSubmit"/> so that
    /// <see cref="TryPickAsync"/> can poll for completion.
    /// </remarks>
    /// <param name="commandBuffer">The command buffer that is currently being recorded.</param>
    /// <param name="renderContext">The render context that owns the entity-id texture.</param>
    /// <param name="readbackCtx">The readback context that holds the staging buffer.</param>
    /// <param name="x">Screen-space X coordinate to pick.</param>
    /// <param name="y">Screen-space Y coordinate to pick.</param>
    /// <returns>
    /// <see langword="true"/> if the copy command was successfully recorded;
    /// <see langword="false"/> if the coordinates are out of range or the entity-id texture
    /// is not available.
    /// </returns>
    public static bool SchedulePickReadback(
        this ICommandBuffer commandBuffer,
        RenderContext renderContext,
        PickingReadbackContext readbackCtx,
        int x,
        int y
    )
    {
        if (
            !renderContext.ResourceSet.Textures.TryGetValue(
                SystemBufferNames.TextureEntityId,
                out var srcTexture
            ) || srcTexture.Empty
        )
        {
            return false;
        }
        var dims = renderContext.Context.GetDimensions(srcTexture);
        if (x < 0 || y < 0 || x >= (int)dims.Width || y >= (int)dims.Height)
        {
            return false;
        }


        commandBuffer.CopyTextureToBuffer(
            srcTexture,
            readbackCtx.StagingBuffer,
            bufferOffset: 0,
            srcOffset: new Offset3D(x, y),
            extent: new Dimensions(1, 1, 1),
            layers: new TextureLayers()
        );
        return true;
    }

    /// <summary>
    /// Polls for the completion of a previously scheduled pick readback and, if ready,
    /// assembles a full <see cref="PickingResult"/> without blocking the CPU.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This method uses Strategy 1+3 to avoid GPU pipeline stalls:
    /// <list type="bullet">
    /// <item><b>Strategy 1</b> — The pixel was already copied into a host-visible staging
    /// texture via <see cref="SchedulePickReadback"/> as part of the previous frame's command
    /// buffer. No blocking <c>Download</c> on the full device texture is needed.</item>
    /// <item><b>Strategy 3</b> — <see cref="IContext.IsReady"/> is polled non-blockingly.
    /// The result is only consumed once the GPU signals completion.</item>
    /// </list>
    /// </para>
    /// <para>
    /// Typical call site per frame:
    /// <code>
    /// // --- record phase (before Submit) ---
    /// cmdBuf.SchedulePickReadback(renderContext, readbackCtx, mouseX, mouseY);
    ///
    /// // --- submit ---
    /// var handle = context.Submit(cmdBuf, presentTexture);
    /// readbackCtx.SetPendingSubmit(handle, mouseX, mouseY);
    ///
    /// // --- next frame poll ---
    /// if (readbackCtx.TryPickAsync(renderContext, out var result))
    /// {
    ///     // use result
    /// }
    /// </code>
    /// </para>
    /// </remarks>
    /// <param name="readbackCtx">The readback context that holds the staging texture and pending state.</param>
    /// <param name="renderContext">The render context used to resolve entities.</param>
    /// <param name="result">
    /// When this method returns <see langword="true"/>, contains the picking result;
    /// otherwise the value is undefined.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if the GPU readback is complete and a valid entity was found
    /// at the scheduled coordinates; <see langword="false"/> if the readback is still in
    /// flight, no readback was scheduled, or no entity was hit.
    /// </returns>
    public static bool TryPickAsync(
        this PickingReadbackContext readbackCtx,
        RenderContext renderContext,
        out PickingResult result
    )
    {
        result = default;
        if (!readbackCtx.HasPending)
        {
            return false;
        }

        if (!renderContext.Context.IsReady(readbackCtx.PendingHandle))
        {
            return false;
        }

        // GPU is done — read the single pixel from the host-visible staging buffer.
        // Download on a HostVisible buffer is a direct memory read: no staging, no stall.
        var x = readbackCtx.PendingX;
        var y = readbackCtx.PendingY;
        readbackCtx.ClearPending();

        var mappedPtr = renderContext.Context.GetMappedPtr(readbackCtx.StagingBuffer);

        if (mappedPtr == IntPtr.Zero)
        {
            _logger.LogError("TryPickAsync: failed to map staging buffer for reading");
            return false;
        }

        ulong raw = 0;
        unsafe
        {
            raw = Unsafe.Read<ulong>(mappedPtr.ToPointer());
        }

        var data0 = (uint)(raw & 0xFFFFFFFF);
        var data1 = (uint)(raw >> 32);
        Utils.UnpackMeshInfo(
            data0,
            data1,
            out var worldId,
            out var entityId,
            out var instanceId,
            out var primitiveId
        );

        if (worldId == 0 || entityId == 0)
        {
            return false;
        }

        var world = World.GetWorldById((int)worldId);
        if (world is null)
        {
            _logger.LogWarning("TryPickAsync: world with ID {WorldId} not found", worldId);
            return false;
        }

        var entity = world.GetEntity((int)entityId);
        if (!renderContext.TryUnProject(x, y, out var ray))
        {
            _logger.LogWarning("TryPickAsync: unable to unproject ({X}, {Y})", x, y);
            return false;
        }

        if (
            !entity.TryGetPickPosition(
                instanceId,
                primitiveId,
                ray,
                out var worldPosition,
                out var pickedType
            )
        )
        {
            _logger.LogWarning(
                "TryPickAsync: unable to retrieve pick position for entity {EntityId}, primitive {PrimitiveId}, instance {InstanceId}",
                entityId,
                primitiveId,
                instanceId
            );
            return false;
        }

        result = new PickingResult
        {
            Entity = entity,
            PrimitiveId = primitiveId,
            InstanceId = instanceId,
            WorldPosition = worldPosition,
            PickGeometryType = pickedType,
            Ray = ray,
        };
        return true;
    }
}
