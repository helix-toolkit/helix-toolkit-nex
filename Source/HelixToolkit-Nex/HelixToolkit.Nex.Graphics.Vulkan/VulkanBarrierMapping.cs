namespace HelixToolkit.Nex.Graphics.Vulkan;

/// <summary>
/// The result of translating a <see cref="BarrierDescriptor"/> onto native Vulkan stage and access
/// flags. Carries the mapped source/destination stage and access flags together with the
/// backend-agnostic stage flags that had no native counterpart and were therefore omitted.
/// </summary>
/// <param name="SrcStage">The mapped native source pipeline stages.</param>
/// <param name="DstStage">The mapped native destination pipeline stages.</param>
/// <param name="SrcAccess">The mapped native source access flags.</param>
/// <param name="DstAccess">The mapped native destination access flags.</param>
/// <param name="UnmappedSrcStages">The source stage flags that had no native counterpart.</param>
/// <param name="UnmappedDstStages">The destination stage flags that had no native counterpart.</param>
internal readonly record struct MappedStageAccess(
    VkPipelineStageFlags2 SrcStage,
    VkPipelineStageFlags2 DstStage,
    VkAccessFlags2 SrcAccess,
    VkAccessFlags2 DstAccess,
    PipelineStageFlags UnmappedSrcStages,
    PipelineStageFlags UnmappedDstStages
);

/// <summary>
/// Pure, deterministic translation of the backend-agnostic barrier configuration types
/// (<see cref="PipelineStageFlags"/>, <see cref="AccessFlags"/>, and <see cref="TextureLayout"/>)
/// onto their native Vulkan counterparts (<see cref="VkPipelineStageFlags2"/>,
/// <see cref="VkAccessFlags2"/>, and <see cref="VkImageLayout"/>).
/// </summary>
/// <remarks>
/// The mappings are fixed at compile time, so each input always produces the same output
/// (deterministic and repeatable). Flag inputs that have no native counterpart are reported
/// through the <c>unmapped</c> out parameters so callers can decide how to handle them.
/// </remarks>
internal static class VulkanBarrierMapping
{
    /// <summary>
    /// The fixed pipeline-stage mapping table. Each entry maps exactly one backend-agnostic
    /// <see cref="PipelineStageFlags"/> bit to its native <see cref="VkPipelineStageFlags2"/> value.
    /// </summary>
    private static readonly (PipelineStageFlags Flag, VkPipelineStageFlags2 Native)[] StageTable =
    [
        (PipelineStageFlags.Host, VkPipelineStageFlags2.Host),
        (PipelineStageFlags.Transfer, VkPipelineStageFlags2.Transfer),
        (PipelineStageFlags.ComputeShader, VkPipelineStageFlags2.ComputeShader),
        (PipelineStageFlags.VertexInput, VkPipelineStageFlags2.VertexInput),
        (PipelineStageFlags.DrawIndirect, VkPipelineStageFlags2.DrawIndirect),
        (PipelineStageFlags.VertexShader, VkPipelineStageFlags2.VertexShader),
        (PipelineStageFlags.FragmentShader, VkPipelineStageFlags2.FragmentShader),
        (PipelineStageFlags.AllCommands, VkPipelineStageFlags2.AllCommands),
    ];

    /// <summary>
    /// The fixed access mapping table. Each entry maps exactly one backend-agnostic
    /// <see cref="AccessFlags"/> bit to its native <see cref="VkAccessFlags2"/> value.
    /// </summary>
    private static readonly (AccessFlags Flag, VkAccessFlags2 Native)[] AccessTable =
    [
        (AccessFlags.ShaderRead, VkAccessFlags2.ShaderRead),
        (AccessFlags.ShaderWrite, VkAccessFlags2.ShaderWrite),
        (AccessFlags.TransferRead, VkAccessFlags2.TransferRead),
        (AccessFlags.TransferWrite, VkAccessFlags2.TransferWrite),
        (AccessFlags.IndexRead, VkAccessFlags2.IndexRead),
        (AccessFlags.IndirectCommandRead, VkAccessFlags2.IndirectCommandRead),
        (AccessFlags.HostRead, VkAccessFlags2.HostRead),
        (AccessFlags.HostWrite, VkAccessFlags2.HostWrite),
        (AccessFlags.MemoryRead, VkAccessFlags2.MemoryRead),
        (AccessFlags.MemoryWrite, VkAccessFlags2.MemoryWrite),
    ];

    /// <summary>
    /// Maps a set of backend-agnostic <see cref="PipelineStageFlags"/> onto native
    /// <see cref="VkPipelineStageFlags2"/> using the fixed stage table.
    /// </summary>
    /// <param name="stages">The backend-agnostic pipeline stages to map.</param>
    /// <param name="unmapped">
    /// When this method returns, contains the subset of <paramref name="stages"/> that has no
    /// native counterpart (<see cref="PipelineStageFlags.None"/> when every bit was mapped).
    /// </param>
    /// <returns>The native pipeline stages corresponding to the mapped bits of <paramref name="stages"/>.</returns>
    internal static VkPipelineStageFlags2 MapStages(
        PipelineStageFlags stages,
        out PipelineStageFlags unmapped
    )
    {
        VkPipelineStageFlags2 result = VkPipelineStageFlags2.None;
        PipelineStageFlags remaining = stages;

        foreach (var (flag, native) in StageTable)
        {
            if ((remaining & flag) == flag)
            {
                result |= native;
                remaining &= ~flag;
            }
        }

        unmapped = remaining;
        return result;
    }

    /// <summary>
    /// Maps a set of backend-agnostic <see cref="AccessFlags"/> onto native
    /// <see cref="VkAccessFlags2"/> using the fixed access table.
    /// </summary>
    /// <param name="access">The backend-agnostic access flags to map.</param>
    /// <param name="unmapped">
    /// When this method returns, contains the subset of <paramref name="access"/> that has no
    /// native counterpart (<see cref="AccessFlags.None"/> when every bit was mapped).
    /// </param>
    /// <returns>The native access flags corresponding to the mapped bits of <paramref name="access"/>.</returns>
    internal static VkAccessFlags2 MapAccess(AccessFlags access, out AccessFlags unmapped)
    {
        VkAccessFlags2 result = VkAccessFlags2.None;
        AccessFlags remaining = access;

        foreach (var (flag, native) in AccessTable)
        {
            if ((remaining & flag) == flag)
            {
                result |= native;
                remaining &= ~flag;
            }
        }

        unmapped = remaining;
        return result;
    }

    /// <summary>
    /// Maps a backend-agnostic <see cref="TextureLayout"/> onto its native
    /// <see cref="VkImageLayout"/> using the fixed layout table.
    /// </summary>
    /// <param name="layout">The backend-agnostic texture layout to map.</param>
    /// <returns>The native image layout corresponding to <paramref name="layout"/>.</returns>
    internal static VkImageLayout MapLayout(TextureLayout layout)
    {
        return layout switch
        {
            TextureLayout.ShaderReadOnly => VK.VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,
            TextureLayout.General => VK.VK_IMAGE_LAYOUT_GENERAL,
            TextureLayout.TransferSource => VK.VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,
            TextureLayout.TransferDestination => VK.VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,
            TextureLayout.ColorAttachment => VK.VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL,
            TextureLayout.DepthStencilAttachment =>
                VK.VK_IMAGE_LAYOUT_DEPTH_STENCIL_ATTACHMENT_OPTIMAL,
            TextureLayout.Undefined => VK.VK_IMAGE_LAYOUT_UNDEFINED,
            _ => VK.VK_IMAGE_LAYOUT_UNDEFINED,
        };
    }

    /// <summary>
    /// Translates a <see cref="BarrierDescriptor"/> onto native Vulkan stage and access flags,
    /// omitting any stage or access flags that have no native counterpart (Requirement 1.7).
    /// </summary>
    /// <param name="desc">The backend-agnostic barrier descriptor to map.</param>
    /// <param name="mapped">
    /// When this method returns, contains the mapped native stage and access flags together with the
    /// source and destination stage flags that were omitted because they had no native counterpart.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if both the mapped source stage scope and the mapped destination stage
    /// scope are non-empty; otherwise <see langword="false"/>, indicating that omitting unmapped flags
    /// left an empty source or destination stage scope (Requirement 1.8).
    /// </returns>
    internal static bool TryMap(in BarrierDescriptor desc, out MappedStageAccess mapped)
    {
        VkPipelineStageFlags2 srcStage = MapStages(
            desc.SrcStages,
            out PipelineStageFlags unmappedSrcStages
        );
        VkPipelineStageFlags2 dstStage = MapStages(
            desc.DstStages,
            out PipelineStageFlags unmappedDstStages
        );
        VkAccessFlags2 srcAccess = MapAccess(desc.SrcAccess, out _);
        VkAccessFlags2 dstAccess = MapAccess(desc.DstAccess, out _);

        mapped = new MappedStageAccess(
            srcStage,
            dstStage,
            srcAccess,
            dstAccess,
            unmappedSrcStages,
            unmappedDstStages
        );

        return srcStage != VkPipelineStageFlags2.None && dstStage != VkPipelineStageFlags2.None;
    }
}
