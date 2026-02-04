namespace HelixToolkit.Nex;

/// <summary>
/// Defines the shader pipeline stage.
/// </summary>
public enum ShaderStage : byte
{
    /// <summary>
    /// Vertex shader stage.
    /// </summary>
    Vertex,

    /// <summary>
    /// Tessellation control shader stage (hull shader).
    /// </summary>
    TessellationControl,

    /// <summary>
    /// Tessellation evaluation shader stage (domain shader).
    /// </summary>
    TessellationEvaluation,

    /// <summary>
    /// Geometry shader stage.
    /// </summary>
    Geometry,

    /// <summary>
    /// Fragment shader stage (pixel shader).
    /// </summary>
    Fragment,

    /// <summary>
    /// Compute shader stage.
    /// </summary>
    Compute,

    /// <summary>
    /// Task shader stage (for mesh shading pipeline).
    /// </summary>
    Task,

    /// <summary>
    /// Mesh shader stage.
    /// </summary>
    Mesh,
}
