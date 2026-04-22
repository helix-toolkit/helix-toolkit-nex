using HelixToolkit.Nex.Rendering.Components;

namespace HelixToolkit.Nex.Engine;

public enum PickedGeometryType
{
    None,
    Mesh,
    PointCloud,
}

public class PickingResult
{
    public Entity Entity { set; get; }
    public uint InstanceId { get; set; }
    public uint PrimitiveId { get; set; }
    public PickedGeometryType PickGeometryType { get; set; }
    public Vector3 WorldPosition { get; set; }
    public Ray Ray { set; get; }

    public void Reset()
    {
        Entity = default!;
        InstanceId = 0;
        PrimitiveId = 0;
        PickGeometryType = PickedGeometryType.None;
        WorldPosition = default;
        Ray = default;
    }
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
        return TryPickRaw(
            context.Context,
            texture,
            (uint)context.WindowSize.Width,
            (uint)context.WindowSize.Height,
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
        var result = new PickingResult();
        if (context.TryPick(x, y, result))
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
    public static bool TryPick(this RenderContext context, int x, int y, PickingResult result)
    {
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

        result.Entity = entity;
        result.PrimitiveId = primitiveId;
        result.InstanceId = instanceId;
        result.WorldPosition = worldPosition;
        result.PickGeometryType = pickedType;
        result.Ray = ray;
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
}
