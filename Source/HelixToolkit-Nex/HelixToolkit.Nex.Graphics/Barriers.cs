namespace HelixToolkit.Nex.Graphics;

/// <summary>
/// Backend-agnostic pipeline stage flags used to describe the source and destination
/// scopes of a GPU memory barrier.
/// </summary>
/// <remarks>
/// These values are independent of any backend (for example Vulkan) and are combined
/// using a bitwise OR to identify multiple pipeline stages. A concrete <c>Backend</c>
/// maps each flag onto its native synchronization primitive.
/// </remarks>
[Flags]
public enum PipelineStageFlags : uint
{
    /// <summary>No pipeline stage.</summary>
    None = 0,

    /// <summary>The host (CPU) stage.</summary>
    Host = 1 << 0,

    /// <summary>The transfer (copy/blit) stage.</summary>
    Transfer = 1 << 1,

    /// <summary>The compute shader stage.</summary>
    ComputeShader = 1 << 2,

    /// <summary>The vertex-input (index/vertex fetch) stage.</summary>
    VertexInput = 1 << 3,

    /// <summary>The indirect draw/dispatch argument consumption stage.</summary>
    DrawIndirect = 1 << 4,

    /// <summary>The vertex shader stage.</summary>
    VertexShader = 1 << 5,

    /// <summary>The fragment shader stage.</summary>
    FragmentShader = 1 << 6,

    /// <summary>All commands (the broadest pipeline scope).</summary>
    AllCommands = 1 << 7,
}

/// <summary>
/// Backend-agnostic memory access flags used to describe the source and destination
/// access types of a GPU memory barrier.
/// </summary>
/// <remarks>
/// These values are independent of any backend and are combined using a bitwise OR
/// to identify multiple access types. A concrete <c>Backend</c> maps each flag onto
/// its native access mask.
/// </remarks>
[Flags]
public enum AccessFlags : uint
{
    /// <summary>No memory access.</summary>
    None = 0,

    /// <summary>Read access from a shader.</summary>
    ShaderRead = 1 << 0,

    /// <summary>Write access from a shader.</summary>
    ShaderWrite = 1 << 1,

    /// <summary>Read access by a transfer operation.</summary>
    TransferRead = 1 << 2,

    /// <summary>Write access by a transfer operation.</summary>
    TransferWrite = 1 << 3,

    /// <summary>Read access of an index buffer during vertex input.</summary>
    IndexRead = 1 << 4,

    /// <summary>Read access of indirect draw/dispatch command arguments.</summary>
    IndirectCommandRead = 1 << 5,

    /// <summary>Read access by the host (CPU).</summary>
    HostRead = 1 << 6,

    /// <summary>Write access by the host (CPU).</summary>
    HostWrite = 1 << 7,

    /// <summary>Generic memory read access.</summary>
    MemoryRead = 1 << 8,

    /// <summary>Generic memory write access.</summary>
    MemoryWrite = 1 << 9,
}

/// <summary>
/// Backend-agnostic enumeration of image layouts used by image/texture barrier transitions.
/// </summary>
/// <remarks>
/// A concrete <c>Backend</c> maps each value onto its native image layout
/// (for example <c>VkImageLayout</c> on the Vulkan backend).
/// </remarks>
public enum TextureLayout : uint
{
    /// <summary>Undefined/unknown layout (contents may be discarded on transition).</summary>
    Undefined,

    /// <summary>Read-only layout for sampling/reading in shaders.</summary>
    ShaderReadOnly,

    /// <summary>General-purpose layout supporting all access types.</summary>
    General,

    /// <summary>Optimal layout for use as the source of a transfer operation.</summary>
    TransferSource,

    /// <summary>Optimal layout for use as the destination of a transfer operation.</summary>
    TransferDestination,

    /// <summary>Optimal layout for use as a color attachment.</summary>
    ColorAttachment,

    /// <summary>Optimal layout for use as a depth/stencil attachment.</summary>
    DepthStencilAttachment,
}

/// <summary>
/// A backend-agnostic description of a fully custom GPU memory barrier, specifying
/// explicit source/destination pipeline stages and source/destination access flags.
/// </summary>
/// <param name="SrcStages">The source pipeline stages that must complete before the barrier.</param>
/// <param name="DstStages">The destination pipeline stages that wait on the barrier.</param>
/// <param name="SrcAccess">The source memory access types made available by the barrier.</param>
/// <param name="DstAccess">The destination memory access types made visible by the barrier.</param>
/// <remarks>
/// This type carries exactly four fields. Value equality (provided by the record struct)
/// allows a barrier produced from a <see cref="BarrierPreset"/> to be compared field-by-field
/// against the descriptor that the preset maps to.
/// </remarks>
public readonly record struct BarrierDescriptor(
    PipelineStageFlags SrcStages,
    PipelineStageFlags DstStages,
    AccessFlags SrcAccess,
    AccessFlags DstAccess
);

/// <summary>
/// Named, predefined buffer barrier configurations covering commonly used synchronization
/// scenarios. Each member maps deterministically to exactly one <see cref="BarrierDescriptor"/>.
/// </summary>
public enum BarrierPreset : uint
{
    /// <summary>Compute-shader write to shader read-write (RW) in compute, vertex, and fragment stages.</summary>
    ComputeWriteToShaderRW,

    /// <summary>Transfer write to vertex-input read (including index reads).</summary>
    TransferWriteToVertexInputRead,

    /// <summary>Transfer write to shader read-write (RW) in compute, vertex, and fragment stages.</summary>
    TransferWriteToShaderRW,

    /// <summary>Host write to shader read-write (RW) in compute, vertex, and fragment stages.</summary>
    HostWriteToShaderRW,

    /// <summary>Shader write to indirect draw/dispatch argument read.</summary>
    WriteToIndirectDrawRead,
}

/// <summary>
/// Named image/texture layout transitions covering commonly used scenarios. Each member
/// resolves (in the backend) to a target <see cref="TextureLayout"/> plus a
/// <see cref="BarrierDescriptor"/> describing the stages and access for the transition.
/// </summary>
public enum ImageTransition : uint
{
    /// <summary>Transition the texture to a shader-read-only layout.</summary>
    ToShaderReadOnly,

    /// <summary>Transition the texture to a transfer-source layout.</summary>
    ToTransferSource,

    /// <summary>Transition the texture to a transfer-destination layout.</summary>
    ToTransferDestination,
}

/// <summary>
/// Provides the total, deterministic mapping from each defined <see cref="BarrierPreset"/>
/// member to its corresponding <see cref="BarrierDescriptor"/>.
/// </summary>
public static class BarrierPresets
{
    /// <summary>The shader stages used by shader-read presets (compute, vertex, and fragment).</summary>
    private const PipelineStageFlags Shaders =
        PipelineStageFlags.ComputeShader
        | PipelineStageFlags.VertexShader
        | PipelineStageFlags.FragmentShader;

    /// <summary>
    /// Resolves a <see cref="BarrierPreset"/> to its <see cref="BarrierDescriptor"/>.
    /// </summary>
    /// <param name="preset">The preset to resolve.</param>
    /// <param name="descriptor">
    /// When this method returns <see langword="true"/>, contains the descriptor the preset maps to;
    /// otherwise contains <see langword="default"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> for every defined <see cref="BarrierPreset"/> member;
    /// <see langword="false"/> for any value that is not a defined member.
    /// </returns>
    /// <remarks>
    /// The mapping is total over the defined enum members and deterministic: the same member
    /// always yields a field-by-field equal descriptor.
    /// </remarks>
    public static bool TryGetDescriptor(BarrierPreset preset, out BarrierDescriptor descriptor)
    {
        switch (preset)
        {
            case BarrierPreset.ComputeWriteToShaderRW:
                descriptor = new BarrierDescriptor(
                    PipelineStageFlags.ComputeShader,
                    Shaders,
                    AccessFlags.ShaderWrite,
                    AccessFlags.ShaderRead | AccessFlags.ShaderWrite
                );
                return true;

            case BarrierPreset.TransferWriteToVertexInputRead:
                descriptor = new BarrierDescriptor(
                    PipelineStageFlags.Transfer,
                    PipelineStageFlags.VertexInput,
                    AccessFlags.TransferWrite,
                    AccessFlags.IndexRead
                );
                return true;

            case BarrierPreset.TransferWriteToShaderRW:
                descriptor = new BarrierDescriptor(
                    PipelineStageFlags.Transfer,
                    Shaders,
                    AccessFlags.TransferWrite,
                    AccessFlags.ShaderRead | AccessFlags.ShaderWrite
                );
                return true;

            case BarrierPreset.HostWriteToShaderRW:
                descriptor = new BarrierDescriptor(
                    PipelineStageFlags.Host,
                    Shaders,
                    AccessFlags.HostWrite,
                    AccessFlags.ShaderRead | AccessFlags.ShaderWrite
                );
                return true;

            case BarrierPreset.WriteToIndirectDrawRead:
                descriptor = new BarrierDescriptor(
                    PipelineStageFlags.ComputeShader,
                    PipelineStageFlags.DrawIndirect,
                    AccessFlags.ShaderWrite,
                    AccessFlags.IndirectCommandRead
                );
                return true;

            default:
                descriptor = default;
                return false;
        }
    }
}
