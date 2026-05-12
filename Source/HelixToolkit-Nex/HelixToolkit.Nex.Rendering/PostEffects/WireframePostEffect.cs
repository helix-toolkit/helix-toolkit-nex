using HelixToolkit.Nex.ECS;
using HelixToolkit.Nex.Rendering.Components;
using HelixToolkit.Nex.Rendering.DrawStreams;
using HelixToolkit.Nex.Rendering.RenderNodes;
using HelixToolkit.Nex.Scene;

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
    /// <summary>
    /// Marks a mesh entity for wireframe rendering.
    /// When present on an entity that also has a <see cref="MeshComponent"/>,
    /// the <c>WireframePostEffect</c> will draw the mesh's edges as coloured lines
    /// overlaid on the scene colour during the post-processing stage.
    /// </summary>
    public struct WireframeComponent
    {
        /// <summary>
        /// The colour of the wireframe lines.
        /// </summary>
        public Color4 Color;

        public WireframeComponent(Color4 color)
        {
            Color = color;
        }

        /// <summary>
        /// A default green wireframe.
        /// </summary>
        public static readonly WireframeComponent Default = new(new Color4(0, 1, 0, 1));
    }

    private static readonly ILogger _logger = LogManager.Create<WireframePostEffect>();

    // Wireframe draw pipeline: standard mesh VS + flat-colour FS with PolygonMode.Line.
    private RenderPipelineResource _wireframePipeline = RenderPipelineResource.Null;

    private readonly Dependencies _deps = new();
    private readonly Framebuffer _frameBuffer = new();
    private readonly RenderPass _pass = new();
    private readonly DepthState _depthState = DepthState.DefaultReversedZ.Clone();

    private readonly List<WireframeEntry> _entries = [];

    public override string Name => nameof(WireframePostEffect);
    public override Color4 DebugColor => Color.Chartreuse;

    public override uint Priority => (uint)PostEffectPriority.Highlight;

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

        var data = res.RenderContext.Data;
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

        if (_entries.Count > 0)
        {
            DrawWireframe(in res, res.CmdBuffer, data, ref readSlot, ref writeSlot);
        }

        return true;
    }

    protected override ResultCode OnInitializing()
    {
        base.OnInitializing();
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

        foreach (var entity in world.GetComponentEntities<WireframeComponent>())
        {
            if (!entity.Has<MeshComponent>() || !entity.Has<Renderable>())
            {
                continue;
            }

            ref var wireframe = ref entity.Get<WireframeComponent>();
            ref var renderable = ref entity.Get<Renderable>();
            if (renderable.GPUIndex < 0 || renderable.DrawCategory == 0)
            {
                continue;
            }
            _entries.Add(
                new WireframeEntry(
                    entity,
                    (DrawStreamCategory)renderable.DrawCategory,
                    wireframe.Color
                )
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
        ref string writeSlot
    )
    {
        var context = res.RenderContext;

        var fpBuffer = res.Buffers[SystemBufferNames.BufferForwardPlusConstants];
        // Render wireframes directly onto the write-slot texture.
        // Load existing content so we composite on top of the scene.
        _pass.Colors[0].LoadOp = LoadOp.Load;
        _pass.Colors[0].StoreOp = StoreOp.Store;
        _pass.Depth.LoadOp = LoadOp.Load;
        _pass.Depth.StoreOp = StoreOp.Store;
        _frameBuffer.Colors[0].Texture = res.Textures[writeSlot];
        _frameBuffer.DepthStencil.Texture = res.Textures[SystemBufferNames.TextureDepthF32];

        // No sampled-texture dependencies — mesh draws only need the scene target as output.
        using var depScope = _deps.PushBufferScoped(fpBuffer);

        cmdBuffer.BeginRendering(_pass, _frameBuffer, _deps);
        cmdBuffer.BindRenderPipeline(_wireframePipeline);
        cmdBuffer.BindDepthState(_depthState);

        // Use external-pipeline scope so RenderHelper skips per-material pipeline binding.
        using var _ = context.EnableExternalPipelineScoped();

        var fpConstAddress = fpBuffer.GpuAddress(res.RenderContext.Context);

        foreach (var entry in _entries)
        {
            var streams = data.DrawStreams.GetStreams(entry.Category);
            foreach (var stream in streams)
            {
                if (stream.Categories != entry.Category)
                {
                    continue;
                }
                var (drawCmd, slot) = stream.GetMeshDraw(entry.Entity);
                if (stream.IndexBufferStrategy == IndexBufferStrategy.Shared)
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
                        DrawCommandIdxOffset = (uint)slot,
                        MeshDrawBufferAddress = stream.Buffer.GpuAddress(Context!),
                    }
                );
                cmdBuffer.PushConstants(entry.Color, MeshDrawPushConstant.SizeInBytes);

                cmdBuffer.DrawIndexedIndirect(
                    stream.Buffer,
                    (uint)slot * stream.Stride,
                    1,
                    stream.Stride
                );
                break;
            }
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
        desc.Colors[0] = ColorAttachment.CreateOpaque(GraphicsSettings.IntermediateTargetFormat);
        desc.DepthFormat = GraphicsSettings.DepthBufferFormat;

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

    private readonly record struct WireframeEntry(
        Entity Entity,
        DrawStreamCategory Category,
        Vector4 Color
    );
}
