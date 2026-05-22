using HelixToolkit.Nex.ECS;
using HelixToolkit.Nex.Rendering.Components;
using HelixToolkit.Nex.Rendering.RenderNodes;
using HelixToolkit.Nex.Scene;

namespace HelixToolkit.Nex.Rendering.PostEffects;

/// <summary>
/// Bounding-box debug visualization post-processing effect.
///
/// For every mesh entity that carries a <see cref="BoundingBoxComponent"/> the
/// effect draws a wireframe axis-aligned bounding box (AABB) directly from the
/// existing GPU buffers (<c>MeshInfo</c>, <c>NodeInfo</c>, <c>MeshDraw</c>)
/// without uploading any additional geometry.
///
/// The vertex shader procedurally generates the 12 edges (24 vertices, line-list)
/// of each box using <c>gl_VertexIndex</c> to select edge endpoints and
/// <c>gl_InstanceIndex</c> to select which mesh's bounding box to draw.
/// Culled meshes (those with <c>instanceCount == 0</c> after frustum culling)
/// are automatically skipped in the shader.
///
/// The effect renders directly into the current write-slot colour texture with
/// <see cref="LoadOp.Load"/> so it composites on top of the existing scene.
/// </summary>
public sealed class BoundingBoxPostEffect : PostEffect
{
    /// <summary>
    /// Marks a mesh entity for bounding box visualization.
    /// When present on an entity that also has a <see cref="MeshComponent"/>,
    /// the <c>BoundingBoxPostEffect</c> will draw a wireframe AABB around the
    /// mesh's local-space bounding box during the post-processing stage.
    /// </summary>
    public struct BoundingBoxComponent
    {
        /// <summary>
        /// The colour of the bounding box wireframe lines.
        /// </summary>
        public Color4 Color;

        public uint InstanceIndex; // For internal use by the post-effect; ignored if set manually.

        public BoundingBoxComponent(Color4 color, uint instanceIndex = 0)
        {
            Color = color;
            InstanceIndex = instanceIndex;
        }

        /// <summary>
        /// A default green bounding box.
        /// </summary>
        public static readonly BoundingBoxComponent Default = new(new Color4(0f, 1f, 0f, 0.8f));
    }

    private static readonly ILogger _logger = LogManager.Create<BoundingBoxPostEffect>();

    private RenderPipelineResource _bboxPipeline = RenderPipelineResource.Null;

    private readonly Dependencies _deps = new();
    private readonly Framebuffer _frameBuffer = new();
    private readonly RenderPass _pass = new();
    private readonly List<BBoxEntry> _entries = [];

    public override string Name => nameof(BoundingBoxPostEffect);
    public override Color4 DebugColor => Color.Cyan;
    public override uint Priority => (uint)PostEffectPriority.Highlight;

    /// <summary>
    /// When <c>true</c>, bounding boxes are drawn with depth testing against the
    /// scene depth buffer so occluded edges are hidden. When <c>false</c>, all
    /// edges are drawn on top of the scene (X-ray mode).
    /// Defaults to <c>true</c>.
    /// </summary>
    public bool UseDepthTest { get; set; } = true;

    // -----------------------------------------------------------------------
    // PostEffect interface
    // -----------------------------------------------------------------------

    public override bool Apply(in RenderResources res, ref string readSlot, ref string writeSlot)
    {
        Debug.Assert(_bboxPipeline.Valid, "BoundingBox pipeline is not valid.");

        var data = res.RenderContext.Data;
        if (data is null)
        {
            return false;
        }

        var world = data.World;
        if (world is null || !world.HasAnyComponent<BoundingBoxComponent>())
        {
            return false;
        }

        GatherBBoxEntities(world, res.RenderContext);
        if (_entries.Count == 0)
        {
            return false;
        }

        // Swap so we write into the current texture (same pattern as WireframePostEffect).
        (readSlot, writeSlot) = (writeSlot, readSlot);

        DrawBoundingBoxes(in res, res.CmdBuffer, data, ref readSlot, ref writeSlot);
        return true;
    }

    protected override ResultCode OnInitializing()
    {
        base.OnInitializing();
        return CreatePipeline();
    }

    protected override ResultCode OnTearingDown()
    {
        _bboxPipeline.Dispose();
        return ResultCode.Ok;
    }

    // -----------------------------------------------------------------------
    // Gather helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Gathers draw commands for every enabled entity that has both a
    /// <see cref="MeshComponent"/> and a <see cref="BoundingBoxComponent"/>.
    /// </summary>
    private void GatherBBoxEntities(World world, RenderContext context)
    {
        _entries.Clear();
        var frustum = context.CameraFrustum;
        foreach (var entity in world.GetComponentEntities<BoundingBoxComponent>())
        {
            if (!entity.Has<MeshComponent>() || !entity.Has<Renderable>())
            {
                continue;
            }

            if (!entity.Has<WorldTransform>())
            {
                continue;
            }

            ref var mesh = ref entity.Get<MeshComponent>();
            if (!mesh.Valid)
            {
                continue;
            }

            var box = mesh.Geometry!.BoundingBoxLocal;
            if (box.Minimum == box.Maximum)
            {
                continue; // Degenerate box — skip.
            }

            ref var bbox = ref entity.Get<BoundingBoxComponent>();
            ref var transform = ref entity.Get<WorldTransform>();
            var transformMatrix = transform.Value;
            if (mesh.Instancing is not null && bbox.InstanceIndex < mesh.Instancing.Transforms.Count)
            {
                transformMatrix *= mesh.Instancing.Transforms[(int)bbox.InstanceIndex].ToMatrix();
            }
            if (!frustum.Intersects(box.Transform(ref transformMatrix)))
            {
                continue;
            }
            _entries.Add(new BBoxEntry(Box: box, Transform: transformMatrix, Color: bbox.Color));
        }
    }

    // -----------------------------------------------------------------------
    // Draw pass
    // -----------------------------------------------------------------------

    /// <summary>
    /// Draws bounding boxes for each entity that has a <see cref="BoundingBoxComponent"/>.
    /// Issues one <c>Draw(24, 1, 0, slot)</c> per entity to render its bounding box
    /// at the correct draw stream slot.
    /// </summary>
    private void DrawBoundingBoxes(
        in RenderResources res,
        ICommandBuffer cmdBuffer,
        IRenderDataProvider data,
        ref string readSlot,
        ref string writeSlot
    )
    {
        var fpBuffer = res.Buffers[SystemBufferNames.BufferForwardPlusConstants];

        _pass.Colors[0].LoadOp = LoadOp.Load;
        _pass.Colors[0].StoreOp = StoreOp.Store;
        _pass.Depth.LoadOp = LoadOp.Load;
        _pass.Depth.StoreOp = StoreOp.Store;

        _frameBuffer.Colors[0].Texture = res.Textures[writeSlot];
        _frameBuffer.DepthStencil.Texture = res.Textures[SystemBufferNames.TextureDepthF32];

        using var depScope = _deps.PushBufferScoped(fpBuffer);

        cmdBuffer.BeginRendering(_pass, _frameBuffer, _deps);
        cmdBuffer.BindRenderPipeline(_bboxPipeline);
        cmdBuffer.BindDepthState(UseDepthTest ? DepthState.ReadOnlyInvZ : DepthState.Disabled);

        var fpConstAddress = fpBuffer.GpuAddress(res.RenderContext.Context);
        var dataStreams = data.DrawStreams;

        foreach (var entry in _entries)
        {
            cmdBuffer.PushConstants(
                new BBoxPushConstant
                {
                    FpConstAddress = fpConstAddress,
                    Color = entry.Color,
                    BoxMax = entry.Box.Maximum,
                    BoxMin = entry.Box.Minimum,
                    ModelTransform = entry.Transform,
                }
            );

            // 24 vertices per box (12 edges × 2 endpoints), single instance at the slot offset.
            cmdBuffer.Draw(24, 1);
        }

        cmdBuffer.EndRendering();
    }

    // -----------------------------------------------------------------------
    // Pipeline creation
    // -----------------------------------------------------------------------

    private ResultCode CreatePipeline()
    {
        if (Context is null)
        {
            _logger.LogError("Render context is null during bounding box pipeline creation.");
            return ResultCode.InvalidState;
        }

        var shaderCompiler = new ShaderCompiler();

        // Vertex shader: procedural box edge generation.
        var vsResult = shaderCompiler.CompileVertexShader(
            GlslUtils.GetEmbeddedGlslShader("Vert/vsBoundingBox.glsl")
        );
        if (!vsResult.Success || vsResult.Source is null)
        {
            _logger.LogError(
                "Failed to compile bounding box vertex shader: {ERRORS}",
                string.Join("\n", vsResult.Errors)
            );
            return ResultCode.CompileError;
        }

        // Fragment shader: flat-colour output.
        var fsResult = shaderCompiler.CompileFragmentShader(
            GlslUtils.GetEmbeddedGlslShader("Frag/psBoundingBox.glsl")
        );
        if (!fsResult.Success || fsResult.Source is null)
        {
            _logger.LogError(
                "Failed to compile bounding box fragment shader: {ERRORS}",
                string.Join("\n", fsResult.Errors)
            );
            return ResultCode.CompileError;
        }

        using var vs = Renderer!.ShaderRepository.GetOrCreateFromGlsl(
            ShaderStage.Vertex,
            vsResult.Source,
            [],
            "BoundingBox_Vertex"
        );
        using var fs = Renderer!.ShaderRepository.GetOrCreateFromGlsl(
            ShaderStage.Fragment,
            fsResult.Source,
            [],
            "BoundingBox_Frag"
        );

        var desc = new RenderPipelineDesc
        {
            VertexShader = vs,
            FragmentShader = fs,
            DebugName = "BoundingBox_Wireframe",
            CullMode = CullMode.None,
            FrontFaceWinding = WindingMode.CCW,
            Topology = Topology.Line,
        };
        desc.Colors[0] = ColorAttachment.CreateOpaque(GraphicsSettings.IntermediateTargetFormat);
        desc.DepthFormat = GraphicsSettings.DepthBufferFormat;

        _bboxPipeline = Context.CreateRenderPipeline(desc);

        if (!_bboxPipeline.Valid)
        {
            _logger.LogError("Bounding box pipeline failed to create.");
            return ResultCode.RuntimeError;
        }

        return ResultCode.Ok;
    }

    // -----------------------------------------------------------------------
    // Internal data
    // -----------------------------------------------------------------------

    private readonly record struct BBoxEntry(Matrix4x4 Transform, BoundingBox Box, Color4 Color);
}
