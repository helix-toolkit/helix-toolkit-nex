using HelixToolkit.Nex.Rendering.Components;

namespace HelixToolkit.Nex.Rendering.ComputeNodes;

public sealed class FrustumCullNode : ComputeNode
{
    private ComputePipelineResource _cullingPipeline = ComputePipelineResource.Null;
    private ComputePipelineResource _instancingCullingPipeline = ComputePipelineResource.Null;
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
        _cullBuffer = new RingFixSizeBuffer<CullingConstants>(
            Context!,
            (int)RenderSettings.MaxFrameInFlight,
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
        base.OnTeardown();
    }

    protected override void OnSetupRender(in RenderResources res)
    {
        res.Deps.PushBuffer(res.Buffers[SystemBufferNames.BufferMeshInfo]);
        res.Deps.PushBuffer(res.Buffers[SystemBufferNames.BufferNodeInfo]);
    }

    protected override void OnRender(in RenderResources res)
    {
        if (res.RenderContext.Data is null)
        {
            return;
        }
        if (
            res.RenderContext.Data.MeshDrawsOpaque.Count == 0
            && res.RenderContext.Data.MeshDrawsTransparent.Count == 0
        )
        {
            return;
        }
        var context = res.RenderContext;
        var frustum = BoundingFrustum.FromViewProjectInversedZ(context.CameraParams.ViewProjection);

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
        _cullConst.MeshInfoBufferAddress = context.Data.MeshInfos.GpuAddress;
        _cullConst.NodeInfoBufferAddress = context.Data.NodeInfos.GpuAddress;
        _cullBuffer!.AdvanceAndUpdate(ref _cullConst);
        res.Deps.PushBuffer(_cullBuffer!.Buffer);

        Cull(context, res.CmdBuffer, context.Data.MeshDrawsOpaque, res.Deps);
        Cull(context, res.CmdBuffer, context.Data.MeshDrawsTransparent, res.Deps);
    }

    private void Cull(
        RenderContext context,
        ICommandBuffer cmdBuffer,
        IMeshDrawData data,
        Dependencies deps
    )
    {
        var variant = MeshVariant.Hitable;
        CullMeshes(context, cmdBuffer, data, deps, variant);

        variant |= MeshVariant.Dynamic;
        CullMeshes(context, cmdBuffer, data, deps, variant);

        variant = MeshVariant.Hitable;
        ResetMeshDrawInstancingCount(context, cmdBuffer, data, variant);
        CullInstancingMeshes(context, cmdBuffer, data, deps, variant);

        variant |= MeshVariant.Dynamic;
        ResetMeshDrawInstancingCount(context, cmdBuffer, data, variant);
        CullInstancingMeshes(context, cmdBuffer, data, deps, variant);
    }

    private void CullMeshes(
        RenderContext context,
        ICommandBuffer cmdBuffer,
        IMeshDrawData data,
        Dependencies deps,
        MeshVariant variant
    )
    {
        if (!data.HasAny(variant))
        {
            return;
        }
        cmdBuffer.BindComputePipeline(_cullingPipeline);
        var pc = new FrustumCullPC()
        {
            CullingConstAddress = _cullBuffer!.GpuAddress,
        };
        cmdBuffer.PushConstants(ref pc);

        {
            // For opaque static meshes
            var (buffer, range) = data.GetBuffer(variant);
            if (range.Count > 0)
            {
                pc.InstanceCount = range.Count;
                pc.MeshDrawIdxOffset = range.Start;
                pc.MeshDrawBufferAddress = buffer.GpuAddress(Context!);
                cmdBuffer.PushConstants(ref pc);
                deps.PushBuffer(buffer);
                // Cull all static meshes in one dispatch. Each thread checks one mesh's visibility.
                cmdBuffer.DispatchThreadGroups(
                    new Dimensions(GpuFrustumCulling.GetGroupSize(pc.InstanceCount), 1, 1),
                    deps
                );
                deps.PopBuffer();
            }
        }
    }

    private void ResetMeshDrawInstancingCount(
        RenderContext context,
        ICommandBuffer cmdBuffer,
        IMeshDrawData data,
        MeshVariant variant
    )
    {
        variant |= MeshVariant.Instancing;
        if (!data.HasAny(variant))
        {
            return;
        }
        cmdBuffer.BindComputePipeline(_resetInstanceCountPipeline);
        { // For static instancing meshes, we need to reset instance count before culling, as the count may change every frame.
            var (buffer, range) = data.GetBuffer(variant);
            if (range.Count > 0)
            {
                cmdBuffer.PushConstants(
                    new ResetMeshDrawInstanceCountPC
                    {
                        MeshDrawBufferAddress = buffer.GpuAddress(Context!),
                        MeshDrawCount = (uint)range.Count,
                        MeshDrawOffset = range.Start,
                    }
                );
                // Reset all instance count value in mesh draw buffer to 0.
                cmdBuffer.DispatchThreadGroups(
                    new Dimensions(GpuFrustumCulling.GetGroupSize(range.Count), 1, 1),
                    Dependencies.Empty
                );
            }
        }
    }

    private void CullInstancingMeshes(
        RenderContext context,
        ICommandBuffer cmdBuffer,
        IMeshDrawData data,
        Dependencies deps,
        MeshVariant variant
    )
    {
        variant |= MeshVariant.Instancing;
        if (!data.HasAny(variant))
        {
            return;
        }
        var pc = new FrustumCullInstancingPC() { CullingConstAddress = _cullBuffer!.GpuAddress };
        // For instancing meshes
        cmdBuffer.BindComputePipeline(_instancingCullingPipeline);

        {
            var (buffer, range) = data.GetBuffer(variant);

            if (range.Count > 0)
            {
                pc.MeshDrawBufferAddress = buffer.GpuAddress(Context!);
                deps.PushBuffer(buffer);
                var matIds = data.GetMaterialTypes(variant);
                foreach (var matId in matIds)
                {
                    var subRange = data.GetRangeByMaterial(variant, matId);
                    for (var i = subRange.Start; i < subRange.End; i++)
                    {
                        pc.DrawCommandIdx = i;
                        pc.InstanceCount = data.GetMeshDraw(variant, matId, (int)i).InstanceCount;
                        cmdBuffer.PushConstants(pc);
                        // Run one thread per instance to check visibility
                        cmdBuffer.DispatchThreadGroups(
                            new Dimensions(
                                GpuFrustumCulling.GetGroupSize((uint)pc.InstanceCount),
                                1,
                                1
                            ),
                            deps
                        );
                    }
                }
                deps.PopBuffer();
            }
        }

        //{
        //    varaint |= MeshVariant.Dynamic;
        //    var (buffer, range) = data.GetBuffer(varaint);

        //    if (range.Count > 0)
        //    {
        //        pc.MeshDrawBufferAddress = buffer.GpuAddress(Context!);
        //        deps.PushBuffer(buffer);
        //        var matIds = data.GetMaterialTypes(varaint);
        //        foreach (var matId in matIds)
        //        {
        //            var subRange = data.GetRangeByMaterial(varaint, matId);
        //            for (var i = subRange.Start; i < subRange.End; i++)
        //            {
        //                pc.DrawCommandIdx = i;
        //                pc.InstanceCount = data.GetMeshDraw(varaint, matId, (int)i).InstanceCount;
        //                cmdBuffer.PushConstants(pc);
        //                // Run one thread per instance to check visibility
        //                cmdBuffer.DispatchThreadGroups(
        //                    new Dimensions(
        //                        GpuFrustumCulling.GetGroupSize((uint)pc.InstanceCount),
        //                        1,
        //                        1
        //                    ),
        //                    deps
        //                );
        //            }
        //        }
        //        deps.PopBuffer();
        //    }
        //}
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
