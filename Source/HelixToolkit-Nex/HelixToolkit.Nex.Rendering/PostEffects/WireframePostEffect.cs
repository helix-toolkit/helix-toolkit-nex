using HelixToolkit.Nex.ECS;
using HelixToolkit.Nex.Rendering.Components;
using HelixToolkit.Nex.Rendering.RenderNodes;
using HelixToolkit.Nex.Scene;

namespace HelixToolkit.Nex.Rendering.PostEffects;

/// <summary>
/// Wireframe post-processing effect.
///
/// For every mesh entity that carries a <see cref="WireframeOverlay"/> the
/// effect re-draws the mesh geometry with <c>PolygonMode.Fill</c> and a flat
/// color fragment shader so all triangle edges are visible as colored lines
/// overlaid directly onto the scene color texture.
///
/// Because the effect renders directly into the current <paramref name="writeSlot"/>
/// with <see cref="LoadOp.Load"/> it composes naturally with all other post effects
/// that run before or after it.
/// </summary>
public sealed class WireframePostEffect : PostEffect
{
    /// <summary>
    /// Marks a mesh entity for wireframe rendering.
    /// When present on an entity that also has a <see cref="MeshDrawInfo"/>,
    /// the <c>WireframePostEffect</c> will draw the mesh's edges as colored lines
    /// overlaid on the scene color during the post-processing stage.
    /// </summary>
    public struct WireframeOverlay
    {
        /// <summary>
        /// The color of the wireframe lines.
        /// </summary>
        public Color4 Color = new(0, 0, 1, 1);

        /// <summary>
        /// Set to a non-negative value to use a specific instance from the geometry's instancing buffer.
        /// If negative (default), the entire instance buffer is drawn. This allows selectively drawing individual instances.
        /// </summary>
        public int InstancingIndex = -1;

        /// <summary>
        /// Enable depth testing for the wireframe pass. When true, wireframe lines will be occluded by scene geometry as expected.
        /// Default is false to ensure wireframes are always visible, but enabling depth test can improve visual clarity in complex scenes.
        /// </summary>
        public bool EnableDepthTest = false;

        public WireframeOverlay() { }

        /// <summary>
        /// Initializes a new instance of the <see cref="WireframeOverlay"/> struct with the specified color and optional instancing
        /// index.
        /// </summary>
        /// <param name="color">The color to use for rendering the wireframe.</param>
        /// <param name="instancingIndex">The index used for instancing. Specify -1 to indicate that instancing is not used or to draw all instances.</param>
        public WireframeOverlay(Color4 color, int instancingIndex = -1)
        {
            Color = color;
            InstancingIndex = instancingIndex;
        }

        /// <summary>
        /// A default blue wireframe.
        /// </summary>
        public static readonly WireframeOverlay Default = new();
    }

    private static readonly ILogger _logger = LogManager.Create<WireframePostEffect>();

    // Wireframe draw pipeline: standard mesh VS + flat-color FS with PolygonMode.Line.
    private RenderPipelineResource _wireframePipeline = RenderPipelineResource.Null;

    private readonly Dependencies _deps = new();
    private readonly Framebuffer _frameBuffer = new();
    private readonly RenderPass _pass = new();

    private readonly List<WireframeEntry> _entries = [];

    public override string Name => nameof(WireframePostEffect);
    public override Color4 DebugColor => Color.Chartreuse;

    public override uint Priority => (uint)PostEffectPriority.Highlight;

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
        if (world is null || !world.HasAnyComponent<WireframeOverlay>())
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
    /// <see cref="MeshDrawInfo"/> and a <see cref="WireframeOverlay"/>.
    /// </summary>
    private void GatherWireframeDraws(World world, IRenderDataProvider data)
    {
        _entries.Clear();

        foreach (var entity in world.GetComponentEntities<WireframeOverlay>())
        {
            if (!entity.Has<MeshDrawInfo>() || !entity.Has<Renderable>())
            {
                continue;
            }

            ref var wireframe = ref entity.Get<WireframeOverlay>();
            ref var renderable = ref entity.Get<Renderable>();
            ref var mesh = ref entity.Get<MeshDrawInfo>();
            if (
                renderable.GPUIndex < 0
                || renderable.DrawType < 0
                || renderable.DrawVariants == 0
                || !mesh.Valid
            )
            {
                continue;
            }
            _entries.Add(
                new WireframeEntry(
                    entity,
                    wireframe.Color,
                    wireframe.InstancingIndex,
                    renderable.GPUIndex,
                    wireframe.EnableDepthTest
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
    /// The pass loads the existing scene color so previous rendering is preserved.
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

        // Use external-pipeline scope so RenderHelper skips per-material pipeline binding.
        using var _ = context.EnableExternalPipelineScoped();

        var fpConstAddress = fpBuffer.GpuAddress(context.Context);
        var sharedIndexBufferAddress =
            res.RenderContext.Data!.StaticMeshIndexData.Buffer.GpuAddress(context.Context);

        WireframePushConstants pc = new() { FpConstantBufferAddress = fpConstAddress };
        foreach (var entry in _entries)
        {
            ref var mesh = ref entry.Entity.Get<MeshDrawInfo>();

            if (mesh.Geometry is null)
            {
                continue;
            }

            if (mesh.Geometry.IsDynamic)
            {
                pc.IndexBufferAddress = mesh.Geometry.IndexBuffer.GpuAddress;
            }
            else
            {
                pc.IndexBufferAddress = sharedIndexBufferAddress;
            }

            uint instanceCount = 1;
            if (mesh.Instancing is not null)
            {
                pc.InstancingBufferAddress = mesh.Instancing.Buffer!;
                instanceCount = (uint)mesh.Instancing.Transforms.Count;
            }
            else
            {
                pc.InstancingBufferAddress = 0;
            }

            pc.VertexBufferAddress = mesh.Geometry.VertexBuffer.GpuAddress;
            pc.VertexPropsBufferAddress = mesh.Geometry.VertexPropsBuffer.GpuAddress;

            pc.Color = entry.Color;
            pc.NodeIndex = (uint)entry.NodeIndex;
            pc.MaterialId = mesh.MaterialProperties!.Index;

            cmdBuffer.PushConstants(pc);

            if (entry.EnableDepthTest)
            {
                cmdBuffer.BindDepthState(DepthState.ReadOnlyInvZBias);
            }
            else
            {
                cmdBuffer.BindDepthState(DepthState.Disabled);
            }

            if (entry.InstancingIndex >= 0 && pc.InstancingBufferAddress != 0)
            {
                if (entry.InstancingIndex >= instanceCount)
                {
                    _logger.LogError(
                        "Instancing index {INDEX} is out of bounds for mesh with {INSTANCE_COUNT} instances. Skipping.",
                        entry.InstancingIndex,
                        instanceCount
                    );
                    continue;
                }
                cmdBuffer.Draw(
                    mesh.Geometry.IndexCount,
                    1u,
                    mesh.Geometry.IndexOffset,
                    (uint)entry.InstancingIndex
                );
            }
            else
            {
                cmdBuffer.Draw(
                    mesh.Geometry.IndexCount,
                    instanceCount,
                    mesh.Geometry.IndexOffset,
                    0
                );
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

        // Fragment shader: flat-color output.
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
            GlslUtils.GetEmbeddedGlslShader("Vert/vsWireframe.glsl")
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
            [],
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
            PolygonMode = PolygonMode.Fill,
        };
        desc.Colors[0] = ColorAttachment.CreateAlphaBlend(
            GraphicsSettings.IntermediateTargetFormat
        );
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
        Vector4 Color,
        int InstancingIndex,
        int NodeIndex,
        bool EnableDepthTest
    );
}
