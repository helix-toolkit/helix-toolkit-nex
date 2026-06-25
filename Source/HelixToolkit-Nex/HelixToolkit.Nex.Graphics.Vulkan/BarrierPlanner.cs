namespace HelixToolkit.Nex.Graphics.Vulkan;

/// <summary>
/// The outcome of the lazy/dirty/force emit decision for a single buffer barrier.
/// </summary>
internal enum BarrierEmitDecision
{
    /// <summary>The barrier is redundant under lazy mode and should not be emitted.</summary>
    Skip,

    /// <summary>The barrier should be emitted.</summary>
    Emit,
}

/// <summary>
/// Pure, GPU-free helpers that decide <em>what</em> barrier to emit for the Vulkan backend.
/// </summary>
/// <remarks>
/// The methods here contain the input-varying barrier logic that does not require a live GPU
/// (default-descriptor derivation from buffer usage, and the lazy/dirty/force emit decision),
/// so they can be unit- and property-tested in isolation. The Vulkan <c>CommandBuffer</c>
/// emission path is a thin adapter over these helpers.
/// </remarks>
internal static class BarrierPlanner
{
    /// <summary>
    /// Stages that are <em>not</em> shader stages in the access-derivation logic. Matches the
    /// pre-feature baseline in <c>HxVkExtensions.BufferBarrier2</c> and the Vulkan
    /// <c>CommandBuffer.Barrier</c> default overloads.
    /// </summary>
    private const PipelineStageFlags NonShaderStages =
        PipelineStageFlags.Transfer
        | PipelineStageFlags.DrawIndirect
        | PipelineStageFlags.VertexInput;

    /// <summary>
    /// Derives the default <see cref="BarrierDescriptor"/> for the parameterless
    /// <c>Barrier(in BufferHandle, bool)</c> / <c>Barrier(ReadOnlySpan&lt;BufferHandle&gt;, bool)</c>
    /// overloads from a buffer's usage flags.
    /// </summary>
    /// <param name="usage">The buffer usage flags that drive destination-stage derivation.</param>
    /// <returns>
    /// A descriptor whose source stages are <see cref="PipelineStageFlags.Host"/> combined with
    /// <see cref="PipelineStageFlags.ComputeShader"/>; whose destination stages always include
    /// <see cref="PipelineStageFlags.VertexShader"/>, <see cref="PipelineStageFlags.FragmentShader"/>,
    /// and <see cref="PipelineStageFlags.ComputeShader"/>, with <see cref="PipelineStageFlags.VertexInput"/>
    /// added for vertex/index usage and <see cref="PipelineStageFlags.DrawIndirect"/> added for indirect
    /// usage; and whose access flags are derived from those stages (plus
    /// <see cref="AccessFlags.IndexRead"/> for index usage).
    /// </returns>
    /// <remarks>
    /// This reproduces, field-by-field, the synchronization configuration that was in effect immediately
    /// before this feature for the same usage flags (Requirements 4.3, 4.4, 4.5). Access masks are derived
    /// from the stage scopes exactly as the baseline did: Host source → host read/write, any shader stage →
    /// shader read/write, <c>DrawIndirect</c> destination → indirect-command read, and index usage →
    /// index read. The native-only <c>IndexInput</c> destination stage that the baseline adds for index
    /// buffers is a Vulkan emission detail and is not part of the backend-agnostic descriptor.
    /// </remarks>
    internal static BarrierDescriptor DeriveDefaultDescriptor(BufferUsageBits usage)
    {
        const PipelineStageFlags srcStages =
            PipelineStageFlags.Host | PipelineStageFlags.ComputeShader;

        var dstStages =
            PipelineStageFlags.VertexShader
            | PipelineStageFlags.FragmentShader
            | PipelineStageFlags.ComputeShader;

        var hasVertexOrIndex =
            (usage & BufferUsageBits.Vertex) != 0 || (usage & BufferUsageBits.Index) != 0;
        if (hasVertexOrIndex)
        {
            dstStages |= PipelineStageFlags.VertexInput;
        }

        var hasIndirect = (usage & BufferUsageBits.Indirect) != 0;
        if (hasIndirect)
        {
            dstStages |= PipelineStageFlags.DrawIndirect;
        }

        var hasIndex = (usage & BufferUsageBits.Index) != 0;

        var srcAccess = DeriveSourceAccess(srcStages);
        var dstAccess = DeriveDestinationAccess(dstStages, hasIndex);

        return new BarrierDescriptor(srcStages, dstStages, srcAccess, dstAccess);
    }

    /// <summary>
    /// Decides whether a buffer barrier should be emitted under the lazy/dirty/force rules, encoding the
    /// pre-feature condition <c>if (!buf.IsDirty &amp;&amp; !force &amp;&amp; EnableLazyBufferBarrier) skip;</c>.
    /// </summary>
    /// <param name="lazyModeEnabled">Whether <c>EnableLazyBufferBarrier</c> (Lazy_Barrier_Mode) is enabled.</param>
    /// <param name="isDirty">Whether the target buffer is currently in Dirty_State.</param>
    /// <param name="force">Whether the caller forced emission via the Force_Flag.</param>
    /// <returns>
    /// <see cref="BarrierEmitDecision.Skip"/> only when lazy mode is enabled, the buffer is not dirty, and
    /// force is false; otherwise <see cref="BarrierEmitDecision.Emit"/>.
    /// </returns>
    /// <remarks>
    /// The decision depends solely on these three inputs and is therefore identical for default, preset,
    /// and custom-descriptor barriers (Requirements 5.1, 5.2, 5.3, 5.5).
    /// </remarks>
    internal static BarrierEmitDecision Decide(bool lazyModeEnabled, bool isDirty, bool force)
    {
        return lazyModeEnabled && !isDirty && !force
            ? BarrierEmitDecision.Skip
            : BarrierEmitDecision.Emit;
    }

    /// <summary>
    /// Derives source access flags from source stages, matching the baseline derivation in
    /// <c>HxVkExtensions.BufferBarrier2</c>.
    /// </summary>
    private static AccessFlags DeriveSourceAccess(PipelineStageFlags srcStages)
    {
        var access = AccessFlags.None;
        if ((srcStages & PipelineStageFlags.Host) != 0)
        {
            access |= AccessFlags.HostRead | AccessFlags.HostWrite;
        }
        if ((srcStages & PipelineStageFlags.Transfer) != 0)
        {
            access |= AccessFlags.TransferRead | AccessFlags.TransferWrite;
        }
        if ((srcStages & ~NonShaderStages) != 0)
        {
            access |= AccessFlags.ShaderRead | AccessFlags.ShaderWrite;
        }
        return access;
    }

    /// <summary>
    /// Derives destination access flags from destination stages (and index usage), matching the
    /// baseline derivation in <c>HxVkExtensions.BufferBarrier2</c>.
    /// </summary>
    private static AccessFlags DeriveDestinationAccess(
        PipelineStageFlags dstStages,
        bool hasIndexUsage
    )
    {
        var access = AccessFlags.None;
        if ((dstStages & PipelineStageFlags.Transfer) != 0)
        {
            access |= AccessFlags.TransferRead | AccessFlags.TransferWrite;
        }
        if ((dstStages & ~NonShaderStages) != 0)
        {
            access |= AccessFlags.ShaderRead | AccessFlags.ShaderWrite;
        }
        if ((dstStages & PipelineStageFlags.DrawIndirect) != 0)
        {
            access |= AccessFlags.IndirectCommandRead;
        }
        if (hasIndexUsage)
        {
            access |= AccessFlags.IndexRead;
        }
        return access;
    }
}
