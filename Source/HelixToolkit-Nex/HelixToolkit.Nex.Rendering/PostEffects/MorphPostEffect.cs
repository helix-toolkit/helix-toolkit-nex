using HelixToolkit.Nex.ECS;
using HelixToolkit.Nex.Rendering.Components;
using HelixToolkit.Nex.Rendering.RenderNodes;
using HelixToolkit.Nex.Scene;

namespace HelixToolkit.Nex.Rendering.PostEffects;

/// <summary>
/// Morph target debug visualization post-processing effect.
///
/// For every mesh entity that carries a <see cref="MorphOverlay"/> and a
/// <see cref="MeshDrawInfo.Morphing"/>, the effect draws one line per vertex from its un-morphed
/// position to its morphed position. For a skinned mesh both ends are skinned, so the lines show only
/// the displacement contributed by the morph targets. Vertices that are not displaced are not drawn.
/// Lines are drawn on top of the scene by default.
/// </summary>
/// <remarks>
/// Everything is read on the GPU from buffers the deformation pass already uses (base vertices,
/// deformed output, skinning palette), so no per-vertex data is uploaded. Instanced meshes are drawn
/// for the mesh transform only.
/// </remarks>
public sealed class MorphPostEffect : PostEffect
{
    /// <summary>
    /// Marks a morphed mesh entity for morph displacement visualization.
    /// </summary>
    public struct MorphOverlay
    {
        /// <summary>
        /// The color of the displacement lines.
        /// </summary>
        public Color4 Color;

        public MorphOverlay(Color4 color)
        {
            Color = color;
        }

        /// <summary>
        /// A default cyan overlay.
        /// </summary>
        public static readonly MorphOverlay Default = new(new Color4(0.1f, 0.9f, 1f, 1f));
    }

    private static readonly ILogger _logger = LogManager.Create<MorphPostEffect>();

    private RenderPipelineResource _pipeline = RenderPipelineResource.Null;

    private readonly Dependencies _deps = new();
    private readonly Framebuffer _frameBuffer = new();
    private readonly RenderPass _pass = new();
    private readonly FastList<MorphLineMesh> _meshes = [];

    /// <summary>
    /// Per-frame mesh buffers. Every viewport rendered in the same engine frame appends its meshes
    /// to the current slot, so the ring advances once per frame and never overwrites in-flight data.
    /// </summary>
    private RingElementBuffer<MorphLineMesh>? _meshBuffer;
    private uint _bufferFrameIndex = uint.MaxValue;
    private int _bufferOffset;

    public override string Name => nameof(MorphPostEffect);
    public override Color4 DebugColor => Color.Cyan;
    public override uint Priority => (uint)PostEffectPriority.Highlight;

    /// <summary>
    /// When <c>true</c>, lines are drawn with depth testing against the scene depth buffer. When
    /// <c>false</c> (default), lines are drawn on top of the scene.
    /// </summary>
    public bool UseDepthTest { get; set; } = false;

    /// <summary>
    /// Gets or sets the factor the drawn displacements are scaled by, to exaggerate small morphs.
    /// Defaults to 1.
    /// </summary>
    public float LengthScale { get; set; } = 1f;

    /// <summary>
    /// Gets or sets the world-space displacement length below which a vertex is not drawn.
    /// Defaults to 1e-4.
    /// </summary>
    public float MinLength { get; set; } = 1e-4f;

    public override bool Apply(in RenderResources res, ref string readSlot, ref string writeSlot)
    {
        Debug.Assert(_pipeline.Valid, "Morph pipeline is not valid.");

        var world = res.RenderContext.Data?.World;
        if (world is null || !world.HasAnyComponent<MorphOverlay>())
        {
            return false;
        }

        GatherMeshes(world);
        if (_meshes.Count == 0 || !UploadMeshes(res.RenderContext.FrameIndex, out var meshAddress))
        {
            return false;
        }

        // Swap so we write into the current texture (same pattern as BoundingBoxPostEffect).
        (readSlot, writeSlot) = (writeSlot, readSlot);

        DrawMeshes(in res, res.CmdBuffer, writeSlot, meshAddress);
        return true;
    }

    protected override ResultCode OnInitializing()
    {
        base.OnInitializing();
        var result = CreatePipeline();
        if (result != ResultCode.Ok)
        {
            return result;
        }
        _meshBuffer = new RingElementBuffer<MorphLineMesh>(
            Context!,
            (int)GraphicsSettings.MaxFrameInFlight,
            initialCapacity: 16,
            BufferUsageBits.Storage,
            hostVisible: true,
            debugName: "MorphLineMeshes"
        );
        _bufferFrameIndex = uint.MaxValue;
        _bufferOffset = 0;
        return ResultCode.Ok;
    }

    protected override ResultCode OnTearingDown()
    {
        _pipeline.Dispose();
        Disposer.DisposeAndRemove(ref _meshBuffer);
        _meshes.Clear();
        return ResultCode.Ok;
    }

    private void GatherMeshes(World world)
    {
        _meshes.Clear();
        var lengthScale = LengthScale;
        var minLength = MathF.Max(0f, MinLength);
        foreach (var entity in world.GetComponentEntities<MorphOverlay>())
        {
            // Hidden meshes are not deformed this frame, so their buffers may hold a stale pose.
            if (
                !entity.Has<MeshDrawInfo>()
                || !entity.Has<WorldTransform>()
                || !entity.Has<Renderable>()
                || !entity.Has<NodeInfo>()
                || !entity.Get<NodeInfo>().Enabled
            )
            {
                continue;
            }
            ref var mesh = ref entity.Get<MeshDrawInfo>();
            var geometry = mesh.Geometry;
            var deformed = mesh.DeformedVertexBuffer;
            // While an upload is pending, the deformation pass skips the geometry, so its output
            // may still be sized for the previous vertices.
            if (
                mesh.Morphing is not { ActiveWeightCount: > 0 } morphing
                || geometry is null
                || geometry.HasPendingBufferUpdate
                || !geometry.VertexBuffer.Valid
                || !deformed.Valid
            )
            {
                continue;
            }
            var skinning = mesh.Skinning?.SkinnedVertexBuffer.Valid == true ? mesh.Skinning : null;
            // Never draw more vertices than the deformed output holds.
            var vertexCount = Math.Min(
                geometry.Vertices.Count,
                skinning?.VertexCount ?? morphing.VertexCount
            );
            if (vertexCount == 0)
            {
                continue;
            }
            _meshes.Add(
                new MorphLineMesh
                {
                    ModelTransform = entity.Get<WorldTransform>().Value,
                    Color = entity.Get<MorphOverlay>().Color,
                    SrcVertexAddress = geometry.VertexBuffer.GpuAddress,
                    DstVertexAddress = deformed.GpuAddress,
                    PaletteAddress = skinning?.PaletteBuffer.GpuAddress ?? 0,
                    InfluenceAddress = skinning is not null ? geometry.JointInfluenceBuffer.GpuAddress : 0,
                    VertexCount = (uint)vertexCount,
                    JointCount = (uint)(skinning?.Skeleton.JointCount ?? 0),
                    MinLength = minLength,
                    LengthScale = lengthScale,
                }
            );
        }
    }

    /// <summary>
    /// Appends the gathered meshes to the current frame's mesh buffer.
    /// </summary>
    /// <param name="frameIndex">The engine frame index of the viewport being rendered.</param>
    /// <param name="meshAddress">The GPU address of the first uploaded mesh.</param>
    /// <returns><c>true</c> if the meshes were uploaded.</returns>
    private unsafe bool UploadMeshes(uint frameIndex, out ulong meshAddress)
    {
        meshAddress = 0;
        if (_meshBuffer is null)
        {
            return false;
        }
        if (frameIndex != _bufferFrameIndex)
        {
            _bufferFrameIndex = frameIndex;
            _bufferOffset = 0;
            _meshBuffer.Advance();
        }
        // Growing replaces the buffer; earlier viewports of this frame keep drawing from the old
        // one, whose destruction is deferred until the GPU is done with it.
        var result = _meshBuffer.EnsureCapacity(_bufferOffset + _meshes.Count);
        if (result == ResultCode.Ok)
        {
            result = _meshBuffer.Current.Upload(
                _meshes,
                0,
                _meshes.Count,
                _bufferOffset * sizeof(MorphLineMesh)
            );
        }
        if (result != ResultCode.Ok)
        {
            _logger.LogError("Failed to upload {COUNT} morph meshes: {RESULT}", _meshes.Count, result);
            return false;
        }
        meshAddress = _meshBuffer.GpuAddress + (ulong)(_bufferOffset * sizeof(MorphLineMesh));
        _bufferOffset += _meshes.Count;
        return true;
    }

    private unsafe void DrawMeshes(
        in RenderResources res,
        ICommandBuffer cmdBuffer,
        string writeSlot,
        ulong meshAddress
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
        cmdBuffer.BindRenderPipeline(_pipeline);
        cmdBuffer.BindDepthState(UseDepthTest ? DepthState.ReadOnlyInvZ : DepthState.Disabled);

        var fpConstAddress = fpBuffer.GpuAddress(res.RenderContext.Context);
        for (var i = 0; i < _meshes.Count; ++i)
        {
            cmdBuffer.PushConstants(
                new MorphLinePushConstant
                {
                    MeshAddress = meshAddress + (ulong)(i * sizeof(MorphLineMesh)),
                    FpConstAddress = fpConstAddress,
                }
            );
            // One instance per vertex; the vertex shader reads the vertex at gl_InstanceIndex.
            cmdBuffer.Draw(2, _meshes[i].VertexCount);
        }

        cmdBuffer.EndRendering();
    }

    private ResultCode CreatePipeline()
    {
        if (Context is null)
        {
            _logger.LogError("Render context is null during morph pipeline creation.");
            return ResultCode.InvalidState;
        }

        var shaderCompiler = new ShaderCompiler();
        var vsResult = shaderCompiler.CompileVertexShader(
            GlslUtils.GetEmbeddedGlslShader("Vert/vsMorphLine.glsl")
        );
        if (!vsResult.Success || vsResult.Source is null)
        {
            _logger.LogError(
                "Failed to compile morph vertex shader: {ERRORS}",
                string.Join("\n", vsResult.Errors)
            );
            return ResultCode.CompileError;
        }

        // Flat-colour fragment shader shared with the bounding box effect.
        var fsResult = shaderCompiler.CompileFragmentShader(
            GlslUtils.GetEmbeddedGlslShader("Frag/psBoundingBox.glsl")
        );
        if (!fsResult.Success || fsResult.Source is null)
        {
            _logger.LogError(
                "Failed to compile morph fragment shader: {ERRORS}",
                string.Join("\n", fsResult.Errors)
            );
            return ResultCode.CompileError;
        }

        using var vs = Renderer!.ShaderRepository.GetOrCreateFromGlsl(
            ShaderStage.Vertex,
            vsResult.Source,
            [],
            "MorphLine_Vertex"
        );
        using var fs = Renderer!.ShaderRepository.GetOrCreateFromGlsl(
            ShaderStage.Fragment,
            fsResult.Source,
            [],
            "MorphLine_Frag"
        );

        var desc = new RenderPipelineDesc
        {
            VertexShader = vs,
            FragmentShader = fs,
            DebugName = "Morph_Displacement",
            CullMode = CullMode.None,
            FrontFaceWinding = WindingMode.CCW,
            Topology = Topology.Line,
        };
        desc.Colors[0] = ColorAttachment.CreateOpaque(GraphicsSettings.IntermediateTargetFormat);
        desc.DepthFormat = GraphicsSettings.DepthBufferFormat;

        _pipeline = Context.CreateRenderPipeline(desc);
        if (!_pipeline.Valid)
        {
            _logger.LogError("Morph pipeline failed to create.");
            return ResultCode.RuntimeError;
        }
        return ResultCode.Ok;
    }
}
