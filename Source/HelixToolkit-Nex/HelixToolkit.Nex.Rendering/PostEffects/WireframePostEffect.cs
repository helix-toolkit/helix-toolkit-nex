using HelixToolkit.Nex.ECS;
using HelixToolkit.Nex.Rendering.Components;
using HelixToolkit.Nex.Rendering.RenderNodes;

namespace HelixToolkit.Nex.Rendering.PostEffects;

/// <summary>
/// Wireframe post-processing effect.
///
/// For every mesh entity that carries a <see cref="WireframeComponent"/> the
/// effect re-draws the mesh geometry with <c>PolygonMode.Line</c> and a flat
/// colour fragment shader so all triangle edges are visible as coloured lines
/// overlaid directly onto the scene colour texture.
///
/// Because the effect renders directly into the current <paramref name="writeSlot"/>
/// with <see cref="LoadOp.Load"/> it composes naturally with all other post effects
/// that run before or after it.
/// </summary>
public sealed class WireframePostEffect : PostEffect
{
    private static readonly ILogger _logger = LogManager.Create<WireframePostEffect>();

    // Wireframe draw pipeline: standard mesh VS + flat-colour FS with PolygonMode.Line.
    private RenderPipelineResource _wireframePipeline = RenderPipelineResource.Null;

    private readonly Dependencies _deps = new();
    private readonly Framebuffer _frameBuffer = new();
    private readonly RenderPass _pass = new();
    private readonly DepthState _depthState = DepthState.DefaultReversedZ.Clone();

    private readonly List<WireframeEntry> _entries = [];

    public override string Name => nameof(WireframePostEffect);
    public override Color DebugColor => Color.Chartreuse;

    /// <summary>
    /// Gets or sets the constant depth bias factor applied to fragment depth values.
    /// </summary>
    /// <remarks>This property is typically used to reduce z-fighting artifacts in rendering by adjusting the
    /// depth values of fragments.  The effect of this property depends on the depth bias settings of the rendering
    /// pipeline.</remarks>
    public float DepthBiasConstant
    {
        get => _depthState.DepthBiasConstantFactor;
        set => _depthState.DepthBiasConstantFactor = value;
    }

    /// <summary>
    /// Gets or sets the slope scale factor used to apply a depth bias to fragments.
    /// </summary>
    /// <remarks>Depth bias is typically used to reduce artifacts such as z-fighting in rendering. Adjust this
    /// value carefully to achieve the desired visual effect.</remarks>
    public float DepthBiasSlope
    {
        get => _depthState.DepthBiasSlopeFactor;
        set => _depthState.DepthBiasSlopeFactor = value;
    }

    public WireframePostEffect()
    {
        _depthState.IsDepthBiasEnabled = true;
        DepthBiasConstant = 1f;
        DepthBiasSlope = 1f;
    }

    // -----------------------------------------------------------------------
    // PostEffect interface
    // -----------------------------------------------------------------------

    public override bool Apply(in RenderResources res, ref string readSlot, ref string writeSlot)
    {
        Debug.Assert(_wireframePipeline.Valid, "Wireframe pipeline is not valid.");

        var data = res.Context.Data;
        if (data is null)
        {
            return false;
        }

        var world = data.World;
        if (world is null || !world.HasAnyComponent<WireframeComponent>())
        {
            // Nothing to draw wireframe for — skip.
            return false;
        }

        GatherWireframeDraws(world, data);
        if (_entries.Count == 0)
        {
            return false;
        }
        (readSlot, writeSlot) = (writeSlot, readSlot); // Swap slots so we write into the current texture.
        DrawWireframe(in res, res.CmdBuffer, data, ref readSlot, ref writeSlot, _entries);
        return true;
    }

    protected override ResultCode OnInitializing()
    {
        if (Context is null)
        {
            _logger.LogError("Render context is null during wireframe initialisation.");
            return ResultCode.InvalidState;
        }
        return CreatePipelines();
    }

    protected override ResultCode OnTearingDown()
    {
        _wireframePipeline.Dispose();
        return ResultCode.Ok;
    }

    // -----------------------------------------------------------------------
    // Gather helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Gathers draw commands for every enabled entity that has both a
    /// <see cref="MeshComponent"/> and a <see cref="WireframeComponent"/>.
    /// </summary>
    private void GatherWireframeDraws(World world, IRenderDataProvider data)
    {
        _entries.Clear();
        var meshDraws = data.MeshDrawsOpaque;

        foreach (var entity in world.GetComponentEntities<WireframeComponent>())
        {
            if (!entity.Has<MeshComponent>())
            {
                continue;
            }

            ref var meshComp = ref entity.Get<MeshComponent>();
            if (!meshComp.Valid || meshComp.Index < 0)
            {
                continue;
            }

            // Validate the draw command index is within the buffer range.
            if ((uint)meshComp.Index >= meshDraws.Count)
            {
                continue;
            }

            ref var wireframe = ref entity.Get<WireframeComponent>();
            _entries.Add(
                new WireframeEntry(DrawIndex: (uint)meshComp.Index, Color: wireframe.Color)
            );
        }
    }

    // -----------------------------------------------------------------------
    // Draw pass
    // -----------------------------------------------------------------------

    /// <summary>
    /// Re-draws every wireframe mesh directly into the current write slot using
    /// <see cref="PolygonMode.Line"/> so that only triangle edges are rasterised.
    /// The pass loads the existing scene colour so previous rendering is preserved.
    /// </summary>
    private void DrawWireframe(
        in RenderResources res,
        ICommandBuffer cmdBuffer,
        IRenderDataProvider data,
        ref string readSlot,
        ref string writeSlot,
        List<WireframeEntry> entries
    )
    {
        var context = res.Context;
        var meshDraws = data.MeshDrawsOpaque;
        var fpConstBuf = res.Buffers[SystemBufferNames.BufferForwardPlusConstants];

        // Render wireframes directly onto the write-slot texture.
        // Load existing content so we composite on top of the scene.
        _pass.Colors[0].LoadOp = LoadOp.Load;
        _pass.Colors[0].StoreOp = StoreOp.Store;
        _pass.Depth.LoadOp = LoadOp.Load;
        _pass.Depth.StoreOp = StoreOp.DontCare; // No need to preserve depth after this pass.
        _frameBuffer.Colors[0].Texture = res.Textures[writeSlot];
        _frameBuffer.DepthStencil.Texture = res.Textures[SystemBufferNames.TextureDepthF32];

        // No sampled-texture dependencies — mesh draws only need the scene target as output.
        _deps.Textures[0] = TextureHandle.Null;
        _deps.Textures[1] = TextureHandle.Null;

        cmdBuffer.BeginRendering(_pass, _frameBuffer, _deps);
        cmdBuffer.BindRenderPipeline(_wireframePipeline);
        cmdBuffer.BindDepthState(_depthState);

        // Use external-pipeline scope so RenderHelper skips per-material pipeline binding.
        using var _ = context.EnableExternalPipelineScoped();

        var fpConstAddress = fpConstBuf.GpuAddress(context.Context);

        foreach (var entry in entries)
        {
            var drawCmd = meshDraws.DrawCommands[(int)entry.DrawIndex];
            var isDynamic = drawCmd.IsDynamic();
            if (!isDynamic)
            {
                cmdBuffer.BindIndexBuffer(data.StaticMeshIndexData.Buffer, IndexFormat.UI32);
            }
            else
            {
                // Dynamic mesh — bind its own index buffer.
                var geom = data.GetGeometry(drawCmd.MeshId);
                if (geom is null)
                {
                    continue;
                }
                cmdBuffer.BindIndexBuffer(geom.IndexBuffer, IndexFormat.UI32);
            }

            cmdBuffer.PushConstants(
                new MeshDrawPushConstant()
                {
                    FpConstAddress = fpConstAddress,
                    DrawCommandIdxOffset = entry.DrawIndex,
                }
            );
            cmdBuffer.PushConstants(entry.Color, MeshDrawPushConstant.SizeInBytes);

            cmdBuffer.DrawIndexedIndirect(
                meshDraws.Buffer,
                entry.DrawIndex * meshDraws.Stride,
                1,
                meshDraws.Stride
            );
        }

        cmdBuffer.EndRendering();
    }

    // -----------------------------------------------------------------------
    // Pipeline creation
    // -----------------------------------------------------------------------

    private ResultCode CreatePipelines()
    {
        if (Context is null)
        {
            _logger.LogError("Render context is null during wireframe pipeline creation.");
            return ResultCode.InvalidState;
        }

        var shaderCompiler = new ShaderCompiler();

        // Fragment shader: flat-colour output.
        var fsResult = shaderCompiler.CompileFragmentShader(
            GlslUtils.GetEmbeddedGlslShader("Frag/psWireframe.glsl")
        );
        if (!fsResult.Success || fsResult.Source is null)
        {
            _logger.LogError(
                "Failed to compile wireframe fragment shader: {ERRORS}",
                string.Join("\n", fsResult.Errors)
            );
            return ResultCode.CompileError;
        }

        // Vertex shader: standard mesh vertex shader (same as depth pass / highlight mask).
        var vsResult = shaderCompiler.CompileVertexShader(
            GlslUtils.GetEmbeddedGlslShader("Vert.vsMainTemplate")
        );
        if (!vsResult.Success || vsResult.Source is null)
        {
            _logger.LogError(
                "Failed to compile wireframe vertex shader: {ERRORS}",
                string.Join("\n", vsResult.Errors)
            );
            return ResultCode.CompileError;
        }

        using var vs = Renderer!.ShaderRepository.GetOrCreateFromGlsl(
            ShaderStage.Vertex,
            vsResult.Source,
            [new ShaderDefine(BuildFlags.EXCLUDE_MESH_PROPS)],
            "Wireframe_Vertex"
        );
        using var fs = Renderer!.ShaderRepository.GetOrCreateFromGlsl(
            ShaderStage.Fragment,
            fsResult.Source,
            [],
            "Wireframe_Frag"
        );

        var desc = new RenderPipelineDesc
        {
            VertexShader = vs,
            FragmentShader = fs,
            DebugName = "Wireframe",
            CullMode = CullMode.None,
            FrontFaceWinding = WindingMode.CCW,
            PolygonMode = PolygonMode.Line,
        };
        desc.Colors[0] = ColorAttachment.CreateOpaque(RenderSettings.IntermediateTargetFormat);
        desc.DepthFormat = RenderSettings.DepthBufferFormat;

        _wireframePipeline = Context.CreateRenderPipeline(desc);

        if (!_wireframePipeline.Valid)
        {
            _logger.LogError("Wireframe pipeline failed to create.");
            return ResultCode.RuntimeError;
        }

        return ResultCode.Ok;
    }

    // -----------------------------------------------------------------------
    // Internal data
    // -----------------------------------------------------------------------

    private readonly record struct WireframeEntry(uint DrawIndex, Vector4 Color);
}
