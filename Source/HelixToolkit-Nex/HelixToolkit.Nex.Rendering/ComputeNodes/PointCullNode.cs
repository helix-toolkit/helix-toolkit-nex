using HelixToolkit.Nex.Rendering.Components;

namespace HelixToolkit.Nex.Rendering.ComputeNodes;

public sealed class PointCullNode : ComputeNode
{
    private static readonly ILogger _logger = LogManager.Create<PointCullNode>();
    private ComputePipelineResource _expandPipeline = ComputePipelineResource.Null;

    private BufferResource _pointExpandArgsBuffer = BufferResource.Null;

    /// <summary>
    /// Gets or sets the maximum number of points this node can render.
    /// Changing this value causes GPU buffers to be reallocated on the next frame.
    /// </summary>
    public int MaxPoints { get; set; } = 1_000_000;

    /// <summary>
    /// Gets or sets the minimum screen-space size (in pixels) below which a point is culled.
    /// </summary>
    public float MinScreenSize { get; set; } = 1f;

    public override string Name => nameof(PointCullNode);

    public override Color4 DebugColor => Color.Cornsilk;

    protected override bool OnSetup()
    {
        if (CreateExpandPipeline())
        {
            _pointExpandArgsBuffer = Context!.CreateBuffer(
                new BufferDesc
                {
                    DataSize = PointExpandArgs.SizeInBytes,
                    Usage = BufferUsageBits.Storage,
                    Storage = StorageType.Device,
                    DebugName = "PointExpandArgs",
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
            GlslUtils.GetEmbeddedGlslShader("Point.csPointExpand")
        );
        if (!result.Success)
        {
            _logger.LogError(
                "Failed to compile point expand compute shader: {Errors}",
                result.Errors
            );
            return false;
        }

        using var cs = Renderer!.ShaderRepository.GetOrCreateFromGlsl(
            ShaderStage.Compute,
            result.Source!,
            [],
            "PointExpand_CS"
        );

        _expandPipeline = Context!.CreateComputePipeline(
            new ComputePipelineDesc { ComputeShader = cs }
        );
        return _expandPipeline.Valid;
    }

    protected override void OnRender(in RenderResources res)
    {
        if (res.Context is null || res.Context.Data is null)
        {
            _logger.LogWarning("Context.Data is null. Skipping point culling.");
            return;
        }

        var points = res.Context.Data.PointCloudData;
        if (points!.TotalPointCount == 0)
        {
            return;
        }

        res.CmdBuffer.PushDebugGroupLabel("PointCull", new Color4(0.8f, 0.6f, 0.2f, 1.0f));

        // --- Shared camera state ---
        var camera = res.Context.CameraParams;
        var view = camera.View;
        var right = new Vector3(view.M11, view.M21, view.M31);
        var up = new Vector3(view.M12, view.M22, view.M32);
        float fovY = 2.0f * MathF.Atan(1.0f / camera.Projection.M22);

        res.CmdBuffer.BindComputePipeline(_expandPipeline);
        var expandPC = new PointExpandArgs
        {
            ViewProjection = camera.ViewProjection,
            CameraPosition = camera.Position,
            CameraRight = right,
            ScreenHeight = res.Context.WindowSize.Height,
            CameraUp = up,
            FovY = fovY,
            MinScreenSize = MinScreenSize,
        };
        res.CmdBuffer.UpdateBuffer(_pointExpandArgsBuffer, ref expandPC);
        res.Deps.Buffers[0] = _pointExpandArgsBuffer;

        foreach (var entry in points.Data.Values)
        {
            if (!entry.Valid)
            {
                continue;
            }
            entry.EnsureCapacity();
            // --- Reset indirect args ---
            var args = new PointDrawIndirectArgs
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
                ref var comp = ref entity.Get<PointCloudComponent>();
                if (comp.PointCount == 0)
                {
                    continue;
                }
                var expandArgs = new PointExpandPC
                {
                    DrawDataAddress = entry.DrawDataBuffer,
                    IndirectArgsAddress = entry.DrawArgsBuffer.GpuAddress,
                    ArgsAddress = _pointExpandArgsBuffer.GpuAddress,
                    PointPosAddress = comp.Geometry!.VertexBuffer.GpuAddress,
                    PointColorAddress = comp.Geometry!.VertexColorBuffer.GpuAddress,
                    PointCount = (uint)comp.PointCount,
                    WorldId = entity.WorldId,
                    EntityId = (uint)entity.Id,
                    TextureIndex = comp.TextureIndex,
                    SamplerIndex = comp.SamplerIndex,
                    FixedSize = comp.FixedSize ? 1u : 0,
                    Size = comp.Size,
                    Color = comp.Color,
                };
                res.CmdBuffer.PushConstants(expandArgs);
                uint groupCount = ((uint)comp.PointCount + 63) / 64;
                res.CmdBuffer.DispatchThreadGroups(new Dimensions(groupCount, 1, 1), res.Deps);
            }
        }

        res.CmdBuffer.PopDebugGroupLabel();
    }

    public override void AddToGraph(RenderGraph graph)
    {
        graph.AddPass(
            nameof(PointCullNode),
            inputs: [],
            outputs:
            [
                new(SystemBufferNames.BufferPointDrawData, ResourceType.Buffer),
                new(SystemBufferNames.BufferPointIndirectArgs, ResourceType.Buffer),
            ],
            onSetup: (res) => { }
        );
    }
}
