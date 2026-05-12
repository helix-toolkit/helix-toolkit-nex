namespace HelixToolkit.Nex.Rendering.DrawStreams;

/// <summary>
/// Flags enum for categorizing draw streams. Used by the registry to support
/// batch queries (e.g., retrieve all opaque streams, all instancing streams).
/// Multiple flags can be combined to narrow or broaden a category query.
/// </summary>
[Flags]
public enum DrawStreamCategory : uint
{
    /// <summary>No category.</summary>
    None = 0,

    /// <summary>Stream contains opaque geometry.</summary>
    Opaque = 1,

    /// <summary>Stream contains transparent geometry.</summary>
    Transparent = 1 << 1,

    /// <summary>Stream contains dynamic geometry (per-draw index buffer).</summary>
    Dynamic = 1 << 3,

    /// <summary>Stream contains instanced geometry.</summary>
    Instancing = 1 << 4,

    /// <summary>Stream contains hitable geometry (writes entity ID for picking).</summary>
    Hitable = 1 << 6,
}

/// <summary>
/// Identifies a named draw stream in the <see cref="IDrawStreamRegistry"/>.
/// Each value corresponds to a unique combination of rendering characteristics
/// (opaque/transparent, static/dynamic, instancing, hitability).
/// </summary>
public enum DrawStreamName : int
{
    None = -1,

    /// <summary>Opaque static geometry using the shared global index buffer.</summary>
    OpaqueStatic,

    /// <summary>Opaque dynamic geometry with per-draw index buffer binding.</summary>
    OpaqueDynamic,

    /// <summary>Opaque static geometry rendered with GPU instancing.</summary>
    OpaqueStaticInstancing,

    /// <summary>Opaque dynamic geometry rendered with GPU instancing.</summary>
    OpaqueDynamicInstancing,

    /// <summary>Transparent static hitable geometry using the shared global index buffer.</summary>
    TransparentStaticHitable,

    /// <summary>Transparent dynamic hitable geometry with per-draw index buffer binding.</summary>
    TransparentDynamicHitable,

    /// <summary>Transparent static hitable geometry rendered with GPU instancing.</summary>
    TransparentStaticInstancingHitable,

    /// <summary>Transparent dynamic hitable geometry rendered with GPU instancing.</summary>
    TransparentDynamicInstancingHitable,

    /// <summary>Transparent static non-hitable geometry using the shared global index buffer.</summary>
    TransparentStaticNonHitable,

    /// <summary>Transparent dynamic non-hitable geometry with per-draw index buffer binding.</summary>
    TransparentDynamicNonHitable,

    /// <summary>Transparent static non-hitable geometry rendered with GPU instancing.</summary>
    TransparentStaticInstancingNonHitable,

    /// <summary>Transparent dynamic non-hitable geometry rendered with GPU instancing.</summary>
    TransparentDynamicInstancingNonHitable,

    /// <summary>
    /// Total number of predefined stream names. Used for validation and array sizing in the registry.
    /// </summary>
    Count,
}

public static class DrawStreamNameExtensions
{
    /// <summary>
    /// Gets the category flags associated with a given <see cref="DrawStreamName"/>.
    /// This mapping is based on the predefined combinations of characteristics for each stream name.
    /// </summary>
    public static DrawStreamCategory GetCategory(this DrawStreamName name)
    {
        return name switch
        {
            DrawStreamName.OpaqueStatic => DrawStreamCategory.Opaque,

            DrawStreamName.OpaqueDynamic => DrawStreamCategory.Opaque | DrawStreamCategory.Dynamic,

            DrawStreamName.OpaqueStaticInstancing => DrawStreamCategory.Opaque
                | DrawStreamCategory.Instancing,

            DrawStreamName.OpaqueDynamicInstancing => DrawStreamCategory.Opaque
                | DrawStreamCategory.Dynamic
                | DrawStreamCategory.Instancing,

            DrawStreamName.TransparentStaticHitable => DrawStreamCategory.Transparent
                | DrawStreamCategory.Hitable,

            DrawStreamName.TransparentDynamicHitable => DrawStreamCategory.Transparent
                | DrawStreamCategory.Dynamic
                | DrawStreamCategory.Hitable,

            DrawStreamName.TransparentStaticInstancingHitable => DrawStreamCategory.Transparent
                | DrawStreamCategory.Instancing
                | DrawStreamCategory.Hitable,

            DrawStreamName.TransparentDynamicInstancingHitable => DrawStreamCategory.Transparent
                | DrawStreamCategory.Dynamic
                | DrawStreamCategory.Instancing
                | DrawStreamCategory.Hitable,

            DrawStreamName.TransparentStaticNonHitable => DrawStreamCategory.Transparent,

            DrawStreamName.TransparentDynamicNonHitable => DrawStreamCategory.Transparent
                | DrawStreamCategory.Dynamic,

            DrawStreamName.TransparentStaticInstancingNonHitable => DrawStreamCategory.Transparent
                | DrawStreamCategory.Instancing,

            DrawStreamName.TransparentDynamicInstancingNonHitable => DrawStreamCategory.Transparent
                | DrawStreamCategory.Dynamic
                | DrawStreamCategory.Instancing,

            _ => DrawStreamCategory.None,
        };
    }

    public static DrawStreamName GetStreamName(this DrawStreamCategory category)
    {
        return category switch
        {
            DrawStreamCategory.Opaque => DrawStreamName.OpaqueStatic,

            DrawStreamCategory.Opaque | DrawStreamCategory.Dynamic => DrawStreamName.OpaqueDynamic,

            DrawStreamCategory.Opaque | DrawStreamCategory.Instancing =>
                DrawStreamName.OpaqueStaticInstancing,

            DrawStreamCategory.Opaque
                | DrawStreamCategory.Dynamic
                | DrawStreamCategory.Instancing => DrawStreamName.OpaqueDynamicInstancing,

            DrawStreamCategory.Opaque | DrawStreamCategory.Hitable => DrawStreamName.OpaqueStatic,

            DrawStreamCategory.Opaque | DrawStreamCategory.Dynamic | DrawStreamCategory.Hitable =>
                DrawStreamName.OpaqueDynamic,

            DrawStreamCategory.Opaque
                | DrawStreamCategory.Instancing
                | DrawStreamCategory.Hitable => DrawStreamName.OpaqueStaticInstancing,

            DrawStreamCategory.Opaque
                | DrawStreamCategory.Dynamic
                | DrawStreamCategory.Instancing
                | DrawStreamCategory.Hitable => DrawStreamName.OpaqueDynamicInstancing,

            DrawStreamCategory.Transparent | DrawStreamCategory.Hitable =>
                DrawStreamName.TransparentStaticHitable,

            DrawStreamCategory.Transparent
                | DrawStreamCategory.Dynamic
                | DrawStreamCategory.Hitable => DrawStreamName.TransparentDynamicHitable,

            DrawStreamCategory.Transparent
                | DrawStreamCategory.Instancing
                | DrawStreamCategory.Hitable => DrawStreamName.TransparentStaticInstancingHitable,

            DrawStreamCategory.Transparent
                | DrawStreamCategory.Dynamic
                | DrawStreamCategory.Instancing
                | DrawStreamCategory.Hitable => DrawStreamName.TransparentDynamicInstancingHitable,

            DrawStreamCategory.Transparent => DrawStreamName.TransparentStaticNonHitable,

            DrawStreamCategory.Transparent | DrawStreamCategory.Dynamic =>
                DrawStreamName.TransparentDynamicNonHitable,

            DrawStreamCategory.Transparent | DrawStreamCategory.Instancing =>
                DrawStreamName.TransparentStaticInstancingNonHitable,

            DrawStreamCategory.Transparent
                | DrawStreamCategory.Dynamic
                | DrawStreamCategory.Instancing =>
                DrawStreamName.TransparentDynamicInstancingNonHitable,

            _ => DrawStreamName.None,
        };
    }
}

/// <summary>
/// Specifies how index buffers are bound when issuing draw calls for a stream.
/// </summary>
public enum IndexBufferStrategy
{
    /// <summary>
    /// Static meshes share the global index buffer; consumers bind it once before
    /// issuing all draw calls in the stream.
    /// </summary>
    Shared,

    /// <summary>
    /// Dynamic meshes have per-draw index buffers; consumers must look up and bind
    /// each mesh's individual index buffer per draw call.
    /// </summary>
    PerDraw,
}
