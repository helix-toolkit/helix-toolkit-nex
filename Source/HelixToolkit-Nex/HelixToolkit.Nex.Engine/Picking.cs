using HelixToolkit.Nex.Rendering.Components;

namespace HelixToolkit.Nex.Engine;

public enum PickedGeometryType
{
    None,
    Mesh,
    Point,
    Line,
    Billboard,
}

public readonly struct PickingResponse
{
    public RenderContext Context { get; init; }
    public Vector2 Coord { get; init; }
    public ulong Data { get; init; }
    public uint RequestId { get; init; }

    public readonly bool TryGetPickingResult(out PickingResult result)
    {
        result = default;
        if (!Context.TryGetPickFromId(Coord, Data, out result))
        {
            return false;
        }
        return true;
    }
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

    /// <summary>
    /// Attempts to identify and retrieve picking information for a rendered entity at the specified screen coordinates.
    /// </summary>
    /// <param name="context">The rendering context in which the picking operation is performed. Cannot be null.</param>
    /// <param name="coord">The screen coordinates of the point to pick.</param>
    /// <param name="id">The pre-encoded picking ID.</param>
    /// <param name="result">When this method returns, contains the picking result data if the operation succeeds; otherwise, its contents
    /// are undefined. Must not be null.</param>
    /// <returns>true if an entity was successfully picked at the specified coordinates; otherwise, false.</returns>
    public static bool TryGetPickFromId(
        this RenderContext context,
        Vector2 coord,
        ulong id,
        out PickingResult result
    )
    {
        if (coord.X < 0 || coord.Y < 0)
        {
            result = default;
            return false;
        }
        return TryGetPickFromId(context, (int)coord.X, (int)coord.Y, id, out result);
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
    public static bool TryGetPickFromId(
        this RenderContext context,
        int x,
        int y,
        ulong id,
        out PickingResult result
    )
    {
        result = default;
        if (id == 0)
        {
            return false;
        }
        Utils.UnpackMeshInfo(
            id,
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
        if (entity.Has<MeshDrawInfo>())
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
                ref var meshComponent = ref entity.Get<MeshDrawInfo>();
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
        else if (entity.Has<PointCloudDrawInfo>())
        {
            if (entity.TryGetPointFromPointCloud(primitiveId, out var point))
            {
                position = point;
                geometryType = PickedGeometryType.Point;
                return true;
            }
        }
        else if (entity.Has<LineDrawInfo>())
        {
            // For simplicity, we treat line primitives as points for picking purposes.
            // A more robust implementation might compute the nearest point on the line segment to the ray.
            if (entity.TryGetLine(instanceId, out var p0, out var p1))
            {
                ray.GetRayToLineDistance(p0, p1, out position, out _, out _, out _);
                geometryType = PickedGeometryType.Line; // Reuse PointCloud type for lines
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
        if (!entity.Has<MeshDrawInfo>())
        {
            return false;
        }
        var mesh = entity.Get<MeshDrawInfo>().Geometry;
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
        if (!entity.Has<PointCloudDrawInfo>())
        {
            return false;
        }
        var pointCloud = entity.Get<PointCloudDrawInfo>().Geometry;
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

    public static bool TryGetLine(
        this Entity entity,
        uint instanceId,
        out Vector3 p0,
        out Vector3 p1
    )
    {
        p0 = default;
        p1 = default;
        if (!entity.Has<LineDrawInfo>())
        {
            return false;
        }
        var lineGeo = entity.Get<LineDrawInfo>().Geometry;
        if (lineGeo is null)
        {
            return false;
        }
        var vertices = lineGeo.Vertices;
        if (instanceId * 2 >= vertices.Count)
        {
            return false;
        }
        p0 = vertices[(int)instanceId * 2].ToVector3();
        p1 = vertices[(int)instanceId * 2 + 1].ToVector3();
        return true;
    }
}
