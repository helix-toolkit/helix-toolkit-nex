namespace HelixToolkit.Nex.Graphics;

/// <summary>
/// Describes the configuration for creating a compute pipeline.
/// </summary>
/// <remarks>
/// A compute pipeline is used for GPU compute operations. It specifies the compute shader,
/// entry point, and optional specialization constants that control shader behavior at pipeline creation time.
/// </remarks>
public struct ComputePipelineDesc()
{
    /// <summary>
    /// The handle to the compute shader module to use in this pipeline.
    /// </summary>
    public ShaderModuleHandle ComputeShader;

    /// <summary>
    /// Specialization constants that allow compile-time configuration of the shader.
    /// </summary>
    /// <remarks>
    /// Specialization constants enable optimizations by providing constant values at pipeline creation time
    /// rather than at shader compilation time. See <see cref="SpecializationConstantDesc"/> for more information.
    /// </remarks>
    public SpecializationConstantDesc SpecInfo;

    /// <summary>
    /// The name of the entry point function in the compute shader. Defaults to "main".
    /// </summary>
    public string EntryPoint = "main";

    /// <summary>
    /// Optional debug name for the pipeline, used in debugging and profiling tools.
    /// </summary>
    public string DebugName = string.Empty;
}
