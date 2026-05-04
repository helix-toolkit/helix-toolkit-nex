using HelixToolkit.Nex.Rendering.Components;
using HelixToolkit.Nex.Scene;

namespace HelixToolkit.Nex.Rendering.ComputeNodes;

public sealed class BillboardCullNode : ComputeNode
{
    private static readonly ILogger _logger = LogManager.Create<BillboardCullNode>();
    private ComputePipelineResource _expandPipeline = ComputePipelineResource.Null;

    private BufferResource _billboardExpandArgsBuffer = BufferResource.Null;

    /// <summary>
    /// Gets or sets the maximum number of billboards this node can render.
    /// Changing this value causes GPU buffers to be reallocated on the next frame.
    /// </summary>
    public int MaxBillboards { get; set; } = 1_000_000;

    /// <summary>
    /// Gets or sets the minimum screen-space size (in pixels) below which a billboard is culled.
    /// </summary>
    public float MinScreenSize { get; set; } = 1f;

    public override string Name => nameof(BillboardCullNode);

    public override Color4 DebugColor => Color.CadetBlue;

    protected override bool OnSetup()
    {
        if (CreateExpandPipeline())
        {
            _billboardExpandArgsBuffer = Context!.CreateBuffer(
                new BufferDesc
                {
                    DataSize = BillboardExpandArgs.SizeInBytes,
                    Usage = BufferUsageBits.Storage,
                    Storage = StorageType.Device,
                    DebugName = "BillboardExpandArgs",
                }
            );
            return true;
        }
        return false;
    }

    protected override void OnTeardown()
    {
        _expandPipeline.Dispose();
        base.OnTeardown();
    }

    private bool CreateExpandPipeline()
    {
        var compiler = new ShaderCompiler();
        var result = compiler.CompileComputeShader(
            GlslUtils.GetEmbeddedGlslShader("Billboard.csBillboardExpand")
        );
        if (!result.Success)
        {
            _logger.LogError(
                "Failed to compile billboard expand compute shader: {Errors}",
                result.Errors
            );
            return false;
        }

        using var cs = Renderer!.ShaderRepository.GetOrCreateFromGlsl(
            ShaderStage.Compute,
            result.Source!,
            [],
            "BillboardExpand_CS"
        );

        _expandPipeline = Context!.CreateComputePipeline(
            new ComputePipelineDesc { ComputeShader = cs }
        );
        return _expandPipeline.Valid;
    }

    protected override void OnRender(in RenderResources res)
    {
        if (res.RenderContext is null || res.RenderContext.Data is null)
        {
            _logger.LogWarning("Context.Data is null. Skipping billboard culling.");
            return;
        }

        var billboards = res.RenderContext.Data.BillboardData;
        if (billboards!.TotalBillboardCount == 0)
        {
            return;
        }

        // --- Shared camera state ---
        var camera = res.RenderContext.CameraParams;
        var view = camera.View;
        var right = new Vector3(view.M11, view.M21, view.M31);
        var up = new Vector3(view.M12, view.M22, view.M32);
        float fovY = 2.0f * MathF.Atan(1.0f / camera.Projection.M22);

        res.CmdBuffer.BindComputePipeline(_expandPipeline);
        var expandArgs = new BillboardExpandArgs
        {
            ViewProjection = camera.ViewProjection,
            CameraPosition = camera.Position,
            CameraRight = right,
            ScreenHeight = res.RenderContext.WindowSize.Height,
            CameraUp = up,
            FovY = fovY,
            MinScreenSize = MinScreenSize,
        };
        res.CmdBuffer.UpdateBuffer(_billboardExpandArgsBuffer, ref expandArgs);
        res.Deps.Buffers[0] = _billboardExpandArgsBuffer;

        foreach (var entry in billboards.Data.Values)
        {
            if (!entry.Valid)
            {
                continue;
            }
            entry.EnsureCapacity();
            // --- Reset indirect args ---
            var args = new BillboardDrawIndirectArgs
            {
                VertexCount = 4, // triangle strip quad
                InstanceCount = 0, // compute shader will atomically increment
                FirstVertex = 0,
                FirstInstance = 0,
            };
            res.CmdBuffer.UpdateBuffer(entry.DrawArgsBuffer, ref args);
            res.Deps.Buffers[1] = entry.DrawArgsBuffer;
            foreach (var entity in entry.Entities)
            {
                ref var comp = ref entity.Get<BillboardComponent>();
                if (comp.BillboardCount == 0)
                {
                    continue;
                }
                ref var worldTransform = ref entity.Get<WorldTransform>();
                var geo = comp.BillboardGeometry!;
                var expandPC = new BillboardExpandPC
                {
                    DrawDataAddress = entry.DrawDataBuffer,
                    IndirectArgsAddress = entry.DrawArgsBuffer.GpuAddress,
                    ArgsAddress = _billboardExpandArgsBuffer.GpuAddress,
                    BillboardVertexAddress = geo.VertexBuffer.GpuAddress,
                    BillboardCount = (uint)comp.BillboardCount,
                    WorldId = entity.WorldId,
                    EntityId = (uint)entity.Id,
                    TextureIndex = comp.TextureIndex,
                    SamplerIndex = comp.SamplerIndex,
                    FixedSize = comp.FixedSize ? 1u : 0,
                    AxisConstrained = comp.AxisConstrained ? 1u : 0,
                    ConstraintAxis = comp.ConstraintAxis,
                    Color = comp.Color,
                    SdfDistanceRange = comp.SdfDistanceRange,
                    SdfDistanceRangeMiddle = comp.SdfDistanceRangeMiddle,
                    SdfGlyphCellSize = comp.SdfGlyphCellSize,
                    SdfAtlasWidth = comp.SdfAtlasWidth,
                    SdfAtlasHeight = comp.SdfAtlasHeight,
                    WorldTransform = worldTransform.Value,
                };
                res.CmdBuffer.PushConstants(expandPC);
                uint groupCount = ((uint)comp.BillboardCount + 63) / 64;
                res.CmdBuffer.DispatchThreadGroups(new Dimensions(groupCount, 1, 1), res.Deps);
            }
        }
    }

    public override void AddToGraph(RenderGraph graph)
    {
        graph.AddPass(
            RenderStage.Prepare,
            nameof(BillboardCullNode),
            inputs: [],
            outputs:
            [
                new(SystemBufferNames.BufferBillboardDrawData, ResourceType.Buffer),
                new(SystemBufferNames.BufferBillboardIndirectArgs, ResourceType.Buffer),
            ]
        );
    }

    protected override void OnSetupRender(in RenderResources res) { }
}
