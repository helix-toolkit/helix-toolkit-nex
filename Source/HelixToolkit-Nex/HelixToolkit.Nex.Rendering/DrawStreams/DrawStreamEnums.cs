namespace HelixToolkit.Nex.Rendering.DrawStreams;

/// <summary>
/// Specifies the rendering type of a draw stream, which determines the rendering order and blending mode.
/// </summary>
public enum DrawStreamType : int
{
    None = -1,

    /// <summary>Draw stream for rendering opaque geometry.</summary>
    Opaque = 0,

    /// <summary>Draw stream for rendering alpha masked geometry.</summary>
    AlphaMask,

    /// <summary>Draw stream for rendering transparent geometry.</summary>
    Transparent,

    MeshStreamTypeCount,

    /// <summary>
    /// Draw stream for rendering lines.
    /// </summary>
    Line,

    /// <summary>
    /// Draw stream for rendering point.
    /// </summary>
    Point,

    /// <summary>
    /// Draw stream for billboards.
    /// </summary>
    Billboard,
}

/// <summary>
/// Flags representing various characteristics of a draw stream, such as whether it contains dynamic geometry, instanced geometry, or hitable geometry.
/// </summary>
[Flags]
public enum DrawStreamVariants : uint
{
    /// <summary>Static mesh.</summary>
    None = 0,

    /// <summary>Stream contains dynamic geometry (per-draw index buffer).</summary>
    Dynamic = 1,

    /// <summary>Stream contains instanced geometry.</summary>
    Instancing = 1 << 1,

    /// <summary>Stream contains hitable geometry (writes entity ID for picking).</summary>
    Hitable = 1 << 2,
}

/// <summary>
/// Identifies a named draw stream in the <see cref="IDrawStreamRegistry{DRAW_TYPE}"/>.
/// Each value corresponds to a unique combination of rendering characteristics
/// (opaque/transparent, static/dynamic, instancing, hitability).
/// </summary>
public enum DrawStreamName : int
{
    None = -1,

    /// <summary>Static hitable geometry using the shared global index buffer.</summary>
    StaticHitable,

    /// <summary>Dynamic hitable geometry with per-draw index buffer binding.</summary>
    DynamicHitable,

    /// <summary>Static hitable geometry rendered with GPU instancing.</summary>
    StaticInstancingHitable,

    /// <summary>Dynamic hitable geometry rendered with GPU instancing.</summary>
    DynamicInstancingHitable,

    /// <summary>Opaque static non-hitable geometry using the shared global index buffer.</summary>
    Static,

    /// <summary>Opaque dynamic non-hitable geometry with per-draw index buffer binding.</summary>
    Dynamic,

    /// <summary>Opaque static non-hitable geometry rendered with GPU instancing.</summary>
    StaticInstancing,

    /// <summary>Opaque dynamic non-hitable geometry rendered with GPU instancing.</summary>
    DynamicInstancing,

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
    public static DrawStreamVariants GetVariants(this DrawStreamName name)
    {
        return name switch
        {
            DrawStreamName.Static => DrawStreamVariants.None,

            DrawStreamName.Dynamic => DrawStreamVariants.Dynamic,

            DrawStreamName.StaticInstancing => DrawStreamVariants.Instancing,

            DrawStreamName.DynamicInstancing => DrawStreamVariants.Dynamic
                | DrawStreamVariants.Instancing,

            DrawStreamName.StaticHitable => DrawStreamVariants.Hitable,

            DrawStreamName.DynamicHitable => DrawStreamVariants.Dynamic
                | DrawStreamVariants.Hitable,

            DrawStreamName.StaticInstancingHitable => DrawStreamVariants.Instancing
                | DrawStreamVariants.Hitable,

            DrawStreamName.DynamicInstancingHitable => DrawStreamVariants.Dynamic
                | DrawStreamVariants.Instancing
                | DrawStreamVariants.Hitable,

            _ => DrawStreamVariants.None,
        };
    }

    public static DrawStreamName GetStreamName(this DrawStreamVariants category)
    {
        return category switch
        {
            DrawStreamVariants.None => DrawStreamName.Static,

            DrawStreamVariants.Dynamic => DrawStreamName.Dynamic,

            DrawStreamVariants.Instancing => DrawStreamName.StaticInstancing,

            DrawStreamVariants.Dynamic | DrawStreamVariants.Instancing =>
                DrawStreamName.DynamicInstancing,

            DrawStreamVariants.Hitable => DrawStreamName.StaticHitable,

            DrawStreamVariants.Dynamic | DrawStreamVariants.Hitable =>
                DrawStreamName.DynamicHitable,

            DrawStreamVariants.Instancing | DrawStreamVariants.Hitable =>
                DrawStreamName.StaticInstancingHitable,

            DrawStreamVariants.Dynamic
                | DrawStreamVariants.Instancing
                | DrawStreamVariants.Hitable => DrawStreamName.DynamicInstancingHitable,

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
