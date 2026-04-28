using HelixToolkit.Nex.Graphics;

namespace HelixToolkit.Nex.Rendering.ComputeNodes;

public sealed class FrustumCullNode : ComputeNode
{
    private ComputePipelineResource _cullingPipeline = ComputePipelineResource.Null;
    private ComputePipelineResource _instancingCullingPipeline = ComputePipelineResource.Null;
    private ComputePipelineResource _resetInstanceCountPipeline = ComputePipelineResource.Null;
    private BufferResource _cullBuffer = BufferResource.Null;
    private CullingConstants _cullConst;
    private Dependencies _cullDeps = new();
    private Dependencies _instancingCullDeps = new();

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
        _cullBuffer = Context!.CreateBuffer(
            _cullConst,
            BufferUsageBits.Storage,
            StorageType.Device,
            "FrustumCulling"
        );
        _cullDeps.Buffers[0] = _cullBuffer;
        _instancingCullDeps.Buffers[0] = _cullBuffer;
        return true;
    }

    protected override void OnTeardown()
    {
        _cullBuffer.Dispose();
        _cullingPipeline.Dispose();
        _instancingCullingPipeline.Dispose();
        _resetInstanceCountPipeline.Dispose();
        base.OnTeardown();
    }

    protected override void OnRender(in RenderResources res)
    {
        if (res.Context.Data is null)
        {
            return;
        }
        if (
            res.Context.Data.MeshDrawsOpaque.Count == 0
            && res.Context.Data.MeshDrawsTransparent.Count == 0
        )
        {
            return;
        }
        var context = res.Context;
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
        Cull(context, res.CmdBuffer, context.Data.MeshDrawsOpaque);
        Cull(context, res.CmdBuffer, context.Data.MeshDrawsTransparent);
    }

    private void Cull(RenderContext context, ICommandBuffer cmdBuffer, IMeshDrawData data)
    {
        if (data.HasStaticMesh || data.HasDynamicMesh)
        { // For opaque meshes
            CullMeshes(context, cmdBuffer, data);
        }
        if (data.HasStaticInstancingMesh || data.HasDynamicInstancingMesh)
        {
            ResetMeshDrawInstancingCount(context, cmdBuffer, data);
            CullInstancingMeshes(context, cmdBuffer, data);
        }
    }

    private void CullMeshes(RenderContext context, ICommandBuffer cmdBuffer, IMeshDrawData data)
    { // For opaque meshes
        cmdBuffer.BindComputePipeline(_cullingPipeline);
        cmdBuffer.PushConstants(_cullBuffer.GpuAddress);
        _cullConst.MeshDrawBufferAddress = data.GpuAddress;
        // For opaque static meshes
        var range = data.RangeStaticMesh;
        if (range.Count > 0)
        {
            _cullConst.InstanceCount = range.Count;
            _cullConst.MeshDrawIdxOffset = range.Start;
            cmdBuffer.UpdateBuffer(_cullBuffer, ref _cullConst);

            // Cull all static meshes in one dispatch. Each thread checks one mesh's visibility.
            cmdBuffer.DispatchThreadGroups(
                new Dimensions(GpuFrustumCulling.GetGroupSize(_cullConst.InstanceCount), 1, 1),
                _cullDeps
            );
        }

        // For opaque dynamic meshes
        range = data.RangeDynamicMesh;
        if (range.Count > 0)
        {
            _cullConst.InstanceCount = range.Count;
            _cullConst.MeshDrawIdxOffset = range.Start;
            cmdBuffer.UpdateBuffer(_cullBuffer, ref _cullConst);

            // Cull all static meshes in one dispatch. Each thread checks one mesh's visibility.
            cmdBuffer.DispatchThreadGroups(
                new Dimensions(GpuFrustumCulling.GetGroupSize(_cullConst.InstanceCount), 1, 1),
                _cullDeps
            );
        }
    }

    private void ResetMeshDrawInstancingCount(
        RenderContext context,
        ICommandBuffer cmdBuffer,
        IMeshDrawData data
    )
    {
        cmdBuffer.BindComputePipeline(_resetInstanceCountPipeline);
        var range = data.RangeStaticMeshInstancing;
        if (range.Count > 0)
        {
            cmdBuffer.PushConstants(
                new ResetMeshDrawInstanceCountPC
                {
                    MeshDrawBufferAddress = data.GpuAddress,
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

        range = data.RangeDynamicMeshInstancing;
        if (range.Count > 0)
        {
            cmdBuffer.PushConstants(
                new ResetMeshDrawInstanceCountPC
                {
                    MeshDrawBufferAddress = data.GpuAddress,
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

    private void CullInstancingMeshes(
        RenderContext context,
        ICommandBuffer cmdBuffer,
        IMeshDrawData data
    )
    { // For instancing meshes
        _cullConst.MeshDrawBufferAddress = data.GpuAddress;
        _cullConst.InstanceCount = 1; // Each draw command has one instance, so instance count is 1.
        cmdBuffer.UpdateBuffer(_cullBuffer, ref _cullConst);
        cmdBuffer.BindComputePipeline(_instancingCullingPipeline);
        FrustumCullInstancingPC pc = new() { CullingConstAddress = _cullBuffer.GpuAddress };
        var range = data.RangeStaticMeshInstancing;
        _instancingCullDeps.Buffers[1] = data.Buffer;
        if (range.Count > 0)
        {
            for (var i = range.Start; i < range.End; i++)
            {
                pc.DrawCommandIdx = i;
                pc.InstanceCount = data.DrawCommands[(int)i].InstanceCount;
                cmdBuffer.PushConstants(pc);
                // Run one thread per instance to check visibility
                cmdBuffer.DispatchThreadGroups(
                    new Dimensions(GpuFrustumCulling.GetGroupSize((uint)pc.InstanceCount), 1, 1),
                    _instancingCullDeps
                );
            }
        }

        range = data.RangeDynamicMeshInstancing;
        if (range.Count > 0)
        {
            for (var i = range.Start; i < range.End; i++)
            {
                pc.DrawCommandIdx = i;
                pc.InstanceCount = data.DrawCommands[(int)i].InstanceCount;
                cmdBuffer.PushConstants(pc);
                // Run one thread per instance to check visibility
                cmdBuffer.DispatchThreadGroups(
                    new Dimensions(GpuFrustumCulling.GetGroupSize((uint)pc.InstanceCount), 1, 1),
                    _instancingCullDeps
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

    public override void AddToGraph(RenderGraph graph)
    {
        graph
            .AddPass(
                nameof(FrustumCullNode),
                inputs:
                [
                    new(SystemBufferNames.BufferMeshInfo, ResourceType.Buffer)
                ],
                outputs:
                [
                    new(SystemBufferNames.BufferMeshDrawOpaque, ResourceType.Buffer),
                    new(SystemBufferNames.BufferMeshDrawTransparent, ResourceType.Buffer),
                ],
                onSetup: (res) =>
                {
                    res.Deps.Buffers[0] = res.Buffers[SystemBufferNames.BufferMeshDrawOpaque];
                    res.Deps.Buffers[1] = res.Buffers[SystemBufferNames.BufferMeshDrawTransparent];
                    res.Deps.Buffers[2] = res.Buffers[SystemBufferNames.BufferMeshInfo];
                },
                stage: RenderStage.Prepare
            );
    }
}
