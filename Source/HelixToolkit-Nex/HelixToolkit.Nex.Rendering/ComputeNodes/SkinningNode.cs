namespace HelixToolkit.Nex.Rendering.ComputeNodes;

/// <summary>
/// Deforms all skinned and morphed meshes on the GPU ahead of rendering.
/// </summary>
/// <remarks>
/// Runs in <see cref="RenderStage.Prepare"/> in two compute passes:
/// <list type="number">
/// <item>Palette (skinned meshes only): <c>palette[j] = jointMatrix[j] * inverseBind[j]</c>, one thread per joint.</item>
/// <item>Vertices: morph targets (weighted deltas of the active targets), then linear blend
/// skinning of positions, normals and tangents, one thread per vertex.</item>
/// </list>
/// The deformed vertices are written to buffers owned by each <see cref="Skinning"/> (or by the
/// <see cref="Morphing"/> of a mesh that is not skinned) and referenced by the mesh draw commands,
/// so every later pass (depth, opaque, transparent, picking) reads them.
/// </remarks>
public sealed class SkinningNode : ComputeNode
{
    /// <summary>
    /// Previous-frame vertex/compute reads of the outputs must finish before this frame overwrites them.
    /// </summary>
    private static readonly BarrierDescriptor ReadToComputeWrite = new(
        PipelineStageFlags.ComputeShader
            | PipelineStageFlags.VertexShader
            | PipelineStageFlags.FragmentShader,
        PipelineStageFlags.ComputeShader,
        AccessFlags.ShaderRead,
        AccessFlags.ShaderWrite
    );

    private ComputePipelineResource _palettePipeline = ComputePipelineResource.Null;
    private ComputePipelineResource _verticesPipeline = ComputePipelineResource.Null;
    private readonly FastList<BufferHandle> _hostBuffers = [];
    private readonly FastList<BufferHandle> _paletteBuffers = [];
    private readonly FastList<BufferHandle> _outputBuffers = [];

    public override string Name => nameof(SkinningNode);

    public override Color4 DebugColor => Color.DarkOrange;

    protected override bool OnSetup()
    {
        if (Context is null)
        {
            return false;
        }
        _palettePipeline = CreatePipeline(GpuSkinning.Pass.Palette, "SkinPalette");
        _verticesPipeline = CreatePipeline(GpuSkinning.Pass.Vertices, "SkinVertices");
        return _palettePipeline.Valid && _verticesPipeline.Valid;
    }

    private ComputePipelineResource CreatePipeline(GpuSkinning.Pass pass, string debugName)
    {
        var shader = GpuSkinning.GenerateComputeShader(pass);
        using var module = Context!.CreateShaderModuleGlsl(shader, ShaderStage.Compute, debugName);
        var pipeline = Context.CreateComputePipeline(
            new ComputePipelineDesc { ComputeShader = module }
        );
        Debug.Assert(pipeline.Valid);
        return pipeline;
    }

    protected override void OnTeardown()
    {
        _palettePipeline.Dispose();
        _verticesPipeline.Dispose();
        base.OnTeardown();
    }

    protected override bool CanRender(in RenderResources res)
    {
        var skinning = res.RenderContext.Data?.SkinningData;
        return skinning is not null && skinning.Dispatches.Length > 0;
    }

    protected override void OnSetupRender(in RenderResources res) { }

    protected override void OnRender(in RenderResources res)
    {
        var dispatches = res.RenderContext.Data!.SkinningData!.Dispatches;
        var cmdBuf = res.CmdBuffer;

        _hostBuffers.Clear();
        _paletteBuffers.Clear();
        _outputBuffers.Clear();
        foreach (var dispatch in dispatches)
        {
            if (dispatch.Skinning is { } skinning)
            {
                _hostBuffers.Add(skinning.JointMatrixBuffer);
                _paletteBuffers.Add(skinning.PaletteBuffer);
            }
            if (dispatch.Morphing is { ActiveWeightCount: > 0 } morphing)
            {
                _hostBuffers.Add(morphing.WeightBuffer);
            }
            _outputBuffers.Add(dispatch.OutputVertexBuffer);
            if (dispatch.OutputVertexPropsBuffer.Valid)
            {
                _outputBuffers.Add(dispatch.OutputVertexPropsBuffer);
            }
        }

        if (_hostBuffers.Count > 0)
        {
            cmdBuf.Barrier(AsSpan(_hostBuffers), BarrierPreset.HostWriteToShaderRW, force: true);
        }

        // Pass 1: skinning palette.
        if (_paletteBuffers.Count > 0)
        {
            cmdBuf.Barrier(AsSpan(_paletteBuffers), ReadToComputeWrite, force: true);
            cmdBuf.BindComputePipeline(_palettePipeline);
            foreach (var dispatch in dispatches)
            {
                if (dispatch.Skinning is not { } skinning)
                {
                    continue;
                }
                var jointCount = (uint)skinning.Skeleton.JointCount;
                cmdBuf.PushConstants(
                    new SkinPalettePC
                    {
                        JointMatrixAddress = skinning.JointMatrixBuffer.GpuAddress,
                        InverseBindAddress = skinning.Skeleton.InverseBindBuffer.GpuAddress,
                        PaletteAddress = skinning.PaletteBuffer.GpuAddress,
                        JointCount = jointCount,
                    }
                );
                cmdBuf.DispatchThreadGroups(
                    new Dimensions(GpuSkinning.GetGroupSize(jointCount), 1, 1),
                    Dependencies.Empty
                );
            }
            cmdBuf.Barrier(AsSpan(_paletteBuffers), BarrierPreset.ComputeWriteToShaderRW, force: true);
        }

        cmdBuf.Barrier(AsSpan(_outputBuffers), ReadToComputeWrite, force: true);

        // Pass 2: vertex deformation (morph targets, then skinning).
        cmdBuf.BindComputePipeline(_verticesPipeline);
        foreach (var dispatch in dispatches)
        {
            var skinning = dispatch.Skinning;
            var morphing = dispatch.Morphing;
            var geometry = dispatch.Geometry;
            var vertexCount = (uint)dispatch.VertexCount;
            var morphed = morphing is { ActiveWeightCount: > 0 };
            cmdBuf.PushConstants(
                new SkinVertexPC
                {
                    PaletteAddress = skinning?.PaletteBuffer.GpuAddress ?? 0,
                    SrcVertexAddress = geometry.VertexBuffer.GpuAddress,
                    SrcPropsAddress = geometry.VertexPropsBuffer.GpuAddress,
                    InfluenceAddress = skinning is not null ? geometry.JointInfluenceBuffer.GpuAddress : 0,
                    DstVertexAddress = dispatch.OutputVertexBuffer.GpuAddress,
                    DstPropsAddress = dispatch.OutputVertexPropsBuffer.GpuAddress,
                    MorphDeltaAddress = morphed ? geometry.MorphDeltaBuffer.GpuAddress : 0,
                    MorphWeightAddress = morphed ? morphing!.WeightBuffer.GpuAddress : 0,
                    VertexCount = vertexCount,
                    JointCount = (uint)(skinning?.Skeleton.JointCount ?? 0),
                    MorphWeightCount = morphed ? (uint)morphing!.ActiveWeightCount : 0,
                    Flags = morphed ? GetDeformFlags(geometry) : 0,
                }
            );
            cmdBuf.DispatchThreadGroups(
                new Dimensions(GpuSkinning.GetGroupSize(vertexCount), 1, 1),
                Dependencies.Empty
            );
        }

        // Make the deformed vertices visible to every later shader stage.
        cmdBuf.Barrier(AsSpan(_outputBuffers), BarrierPreset.ComputeWriteToShaderRW, force: true);
    }

    /// <summary>
    /// Gets the <see cref="SkinVertexPC.Flags"/> of a morphed geometry: which generated attributes
    /// must follow the morphed surface.
    /// </summary>
    private static uint GetDeformFlags(Geometry geometry)
    {
        var flags = 0u;
        if (geometry.HasFlatNormals)
        {
            flags |= GpuSkinning.FlagFlatNormals;
        }
        if (geometry.HasGeneratedTangents)
        {
            flags |= GpuSkinning.FlagGeneratedTangents;
        }
        return flags;
    }

    private static ReadOnlySpan<BufferHandle> AsSpan(FastList<BufferHandle> list)
    {
        return list.GetInternalArray().AsSpan(0, list.Count);
    }

    public override void AddToGraph(RenderGraph graph)
    {
        graph.AddPass(
            RenderStage.Prepare,
            nameof(SkinningNode),
            inputs: [],
            outputs: [new(SystemBufferNames.BufferSkinnedVertices, ResourceType.Buffer)]
        );
    }
}
