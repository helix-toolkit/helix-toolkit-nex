namespace HelixToolkit.Nex.Rendering.ComputeNodes;

public sealed class FrustumCullNode : ComputeNode
{
    private ComputePipelineResource _cullingPipeline = ComputePipelineResource.Null;
    private ComputePipelineResource _instancingCullingPipeline = ComputePipelineResource.Null;
    private ComputePipelineResource _lineCullingPipeline = ComputePipelineResource.Null;
    private ComputePipelineResource _resetInstanceCountPipeline = ComputePipelineResource.Null;
    private RingFixSizeBuffer<CullingConstants>? _cullBuffer;
    private CullingConstants _cullConst = new();

    public override string Name => nameof(FrustumCullNode);

    public override Color4 DebugColor => Color.BurlyWood;

    /// <summary>
    /// Gets or sets the maximum distance, in world units, at which objects are rendered.
    /// </summary>
    public float MaxDrawDistance { get; set; } = 0;

    /// <summary>
    /// Minimum screen size (in percentage of screen height) for an object to be rendered. Objects smaller than this threshold will be culled.
    /// </summary>
    public float MinScreenSize { get; set; } = 0.002f;

    protected override bool OnSetup()
    {
        CreateCullingPipeline();
        CreateInstancingCullingPipeline();
        CreateLineCullingPipeline();
        _cullBuffer = new RingFixSizeBuffer<CullingConstants>(
            Context!,
            (int)GraphicsSettings.MaxFrameInFlight,
            debugName: "CullConst"
        );
        return true;
    }

    protected override void OnTeardown()
    {
        Disposer.DisposeAndRemove(ref _cullBuffer);
        _cullingPipeline.Dispose();
        _instancingCullingPipeline.Dispose();
        _resetInstanceCountPipeline.Dispose();
        _lineCullingPipeline.Dispose();
        base.OnTeardown();
    }

    protected override bool CanRender(in RenderResources res)
    {
        if (res.RenderContext.Data is null)
        {
            return false;
        }
        return true;
    }

    protected override void OnSetupRender(in RenderResources res)
    {
        res.Deps.PushBuffer(res.Buffers[SystemBufferNames.BufferMeshInfo]);
        res.Deps.PushBuffer(res.Buffers[SystemBufferNames.BufferNodeInfo]);
    }

    protected override void OnRender(in RenderResources res)
    {
        var context = res.RenderContext;
        var frustum = context.CameraFrustum;

        // BeginFrame Culling Constants
        _cullConst.CullingEnabled = Enabled ? 1u : 0u;

        _cullConst.ViewMatrix = context.CameraParams.View;
        _cullConst.ViewProjectionMatrix = context.CameraParams.ViewProjection;
        _cullConst.ProjectionMatrix = context.CameraParams.Projection;
        _cullConst.PlaneCount = frustum.Far.Normal == Vector3.Zero ? 5u : 6u;

        // Pack frustum planes for the shader
        _cullConst.FrustumPlanes_0 = frustum.Left.ToVector4();
        _cullConst.FrustumPlanes_1 = frustum.Right.ToVector4();
        _cullConst.FrustumPlanes_2 = frustum.Top.ToVector4();
        _cullConst.FrustumPlanes_3 = frustum.Bottom.ToVector4();
        _cullConst.FrustumPlanes_4 = frustum.Near.ToVector4();
        _cullConst.FrustumPlanes_5 = frustum.Far.ToVector4();

        _cullConst.MaxDrawDistance = MaxDrawDistance;
        _cullConst.MinScreenSize = MinScreenSize;
        _cullConst.MeshInfoBufferAddress = context.Data!.MeshInfos.GpuAddress;
        _cullConst.NodeInfoBufferAddress = context.Data.NodeInfos.GpuAddress;
        _cullBuffer!.AdvanceAndUpdate(ref _cullConst);
        res.Deps.PushBuffer(_cullBuffer!.Buffer);
        foreach (var stream in context.Data.MeshDrawStreams.AllStreams)
        {
            if (stream.Count == 0)
            {
                continue;
            }
            stream.Barrier(res.CmdBuffer);
            using var _ = res.Deps.PushBufferScoped(stream.Buffer);
            Cull(context, res.CmdBuffer, stream, res.Deps);
        }

        foreach (var stream in context.Data.LineDrawStreams.AllStreams)
        {
            if (stream.Count == 0)
            {
                continue;
            }
            stream.Barrier(res.CmdBuffer);
            using var _ = res.Deps.PushBufferScoped(stream.Buffer);
            CullMeshes(context, res.CmdBuffer, stream, res.Deps, _lineCullingPipeline);
        }
    }

    private void Cull(
        RenderContext context,
        ICommandBuffer cmdBuffer,
        IDrawStream<MeshDraw> stream,
        Dependencies deps
    )
    {
        if (stream.IsInstancing)
        {
            ResetMeshDrawInstancingCount(context, cmdBuffer, stream);
            CullInstancingMeshes(context, cmdBuffer, stream, deps);
        }
        else
        {
            CullMeshes(context, cmdBuffer, stream, deps, _cullingPipeline);
        }
    }

    private void CullMeshes<DRAW_TYPE>(
        RenderContext context,
        ICommandBuffer cmdBuffer,
        IDrawStream<DRAW_TYPE> stream,
        Dependencies deps,
        ComputePipelineResource pipeline
    )
        where DRAW_TYPE : unmanaged
    {
        cmdBuffer.BindComputePipeline(pipeline);
        cmdBuffer.PushConstants(
            new FrustumCullPC
            {
                CullingConstAddress = _cullBuffer!.GpuAddress,
                InstanceCount = stream.Count,
                MeshDrawIdxOffset = 0,
                MeshDrawBufferAddress = stream.Buffer.GpuAddress(Context!),
            }
        );
        // Cull all static meshes in one dispatch. Each thread checks one mesh's visibility.
        cmdBuffer.DispatchThreadGroups(
            new Dimensions(GpuFrustumCulling.GetGroupSize(stream.Count), 1, 1),
            deps
        );
    }

    private void ResetMeshDrawInstancingCount(
        RenderContext context,
        ICommandBuffer cmdBuffer,
        IDrawStream<MeshDraw> stream
    )
    {
        cmdBuffer.BindComputePipeline(_resetInstanceCountPipeline);
        // For instancing meshes, we need to reset instance count before culling, as the count may change every frame.
        cmdBuffer.PushConstants(
            new ResetMeshDrawInstanceCountPC
            {
                MeshDrawBufferAddress = stream.Buffer.GpuAddress(Context!),
                MeshDrawCount = stream.Count,
                MeshDrawOffset = 0,
            }
        );
        // Reset all instance count value in mesh draw buffer to 0.
        cmdBuffer.DispatchThreadGroups(
            new Dimensions(GpuFrustumCulling.GetGroupSize(stream.Count), 1, 1),
            Dependencies.Empty
        );
    }

    private void CullInstancingMeshes(
        RenderContext context,
        ICommandBuffer cmdBuffer,
        IDrawStream<MeshDraw> stream,
        Dependencies deps
    )
    {
        var pc = new FrustumCullInstancingPC()
        {
            CullingConstAddress = _cullBuffer!.GpuAddress,
            MeshDrawBufferAddress = stream.Buffer.GpuAddress(Context!),
        };
        // For instancing meshes
        cmdBuffer.BindComputePipeline(_instancingCullingPipeline);
        foreach (var matId in stream.GetMaterialTypesCore())
        {
            var subRange = stream.GetRangeByMaterial(matId);
            for (var i = subRange.Start; i < subRange.End; ++i)
            {
                pc.DrawCommandIdx = i;
                pc.InstanceCount = stream.TryGetDraw((int)i, out var draw) ? draw.InstanceCount : 0;
                if (pc.InstanceCount == 0)
                {
                    continue;
                }
                cmdBuffer.PushConstants(pc);
                // Run one thread per instance to check visibility
                cmdBuffer.DispatchThreadGroups(
                    new Dimensions(GpuFrustumCulling.GetGroupSize((uint)pc.InstanceCount), 1, 1),
                    deps
                );
            }
        }
    }

    private void CreateCullingPipeline()
    {
        if (Context is null)
        {
            throw new InvalidOperationException("Context is null, cannot create culling pipeline.");
        }
        // Generates the compute shader code for frustum checking.
        // Mode 'MultiMeshSingleInstance' means each thread processes one unique object/DrawCommand.
        var cullingShader = GpuFrustumCulling.GenerateComputeShader(
            GpuFrustumCulling.CullMode.MultiMeshSingleInstance
        );
        using var cullingModule = Context.CreateShaderModuleGlsl(
            cullingShader,
            ShaderStage.Compute,
            "FrustumCulling"
        );
        _cullingPipeline = Context.CreateComputePipeline(
            new ComputePipelineDesc { ComputeShader = cullingModule }
        );
        Debug.Assert(_cullingPipeline.Valid);
    }

    private void CreateInstancingCullingPipeline()
    {
        if (Context is null)
        {
            throw new InvalidOperationException("Context is null, cannot create culling pipeline.");
        }
        var cullingShader = GpuFrustumCulling.GenerateComputeShader(
            GpuFrustumCulling.CullMode.SingleMeshInstancing
        );
        using var cullingModule = Context.CreateShaderModuleGlsl(
            cullingShader,
            ShaderStage.Compute,
            "FrustumCullingCompute"
        );
        _instancingCullingPipeline = Context.CreateComputePipeline(
            new ComputePipelineDesc { ComputeShader = cullingModule }
        );
        Debug.Assert(_cullingPipeline.Valid);

        var resetShader = GpuFrustumCulling.GenerateComputeShader(
            GpuFrustumCulling.CullMode.ResetInstanceCount
        );
        using var resetModule = Context.CreateShaderModuleGlsl(resetShader, ShaderStage.Compute);
        _resetInstanceCountPipeline = Context.CreateComputePipeline(
            resetModule,
            "ResetDrawInstanceCount"
        );
    }

    private void CreateLineCullingPipeline()
    {
        if (Context is null)
        {
            throw new InvalidOperationException("Context is null, cannot create culling pipeline.");
        }
        // Generates the compute shader code for frustum checking.
        // Mode 'MultiMeshSingleInstance' means each thread processes one unique object/DrawCommand.
        var cullingShader = GpuFrustumCulling.GenerateComputeShader(
            GpuFrustumCulling.CullMode.MultiLineSingleInstance
        );
        using var cullingModule = Context.CreateShaderModuleGlsl(
            cullingShader,
            ShaderStage.Compute,
            "LineCulling"
        );
        _lineCullingPipeline = Context.CreateComputePipeline(
            new ComputePipelineDesc { ComputeShader = cullingModule }
        );
        Debug.Assert(_lineCullingPipeline.Valid);
    }

    public override void AddToGraph(RenderGraph graph)
    {
        graph.AddPass(
            RenderStage.Prepare,
            nameof(FrustumCullNode),
            inputs: [new(SystemBufferNames.BufferMeshInfo, ResourceType.Buffer)],
            outputs: [new(SystemBufferNames.BufferMeshDrawPlaceholder, ResourceType.Buffer)]
        );
    }
}
