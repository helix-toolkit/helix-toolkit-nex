using HelixToolkit.Nex.ECS;
using HelixToolkit.Nex.Rendering.Components;
using HelixToolkit.Nex.Rendering.RenderNodes;
using HelixToolkit.Nex.Scene;

namespace HelixToolkit.Nex.Rendering.PostEffects;

/// <summary>
/// Skeleton (bone) debug visualization post-processing effect.
///
/// For every mesh entity that carries a <see cref="SkeletonOverlay"/> and a
/// <see cref="MeshDrawInfo.Skinning"/>, the effect draws one line segment from each joint's
/// parent to the joint, using the current world transforms of the joints. Bones are drawn on top
/// of the scene by default so they stay visible inside the skinned mesh.
/// </summary>
public sealed class SkeletonPostEffect : PostEffect
{
    /// <summary>
    /// Marks a skinned mesh entity for bone visualization.
    /// </summary>
    public struct SkeletonOverlay
    {
        /// <summary>
        /// The color of the bone lines.
        /// </summary>
        public Color4 Color;

        public SkeletonOverlay(Color4 color)
        {
            Color = color;
        }

        /// <summary>
        /// A default yellow skeleton.
        /// </summary>
        public static readonly SkeletonOverlay Default = new(new Color4(1f, 0.85f, 0.1f, 1f));
    }

    private static readonly ILogger _logger = LogManager.Create<SkeletonPostEffect>();

    private RenderPipelineResource _pipeline = RenderPipelineResource.Null;

    private readonly Dependencies _deps = new();
    private readonly Framebuffer _frameBuffer = new();
    private readonly RenderPass _pass = new();
    private readonly FastList<BoneLine> _bones = [];
    private readonly HashSet<Entity> _jointSet = [];

    /// <summary>
    /// Per-frame bone buffers. Every viewport rendered in the same engine frame appends its bones
    /// to the current slot, so the ring advances once per frame and never overwrites in-flight data.
    /// </summary>
    private RingElementBuffer<BoneLine>? _boneBuffer;
    private uint _bufferFrameIndex = uint.MaxValue;
    private int _bufferOffset;

    public override string Name => nameof(SkeletonPostEffect);
    public override Color4 DebugColor => Color.Orange;
    public override uint Priority => (uint)PostEffectPriority.Highlight;

    /// <summary>
    /// When <c>true</c>, bones are drawn with depth testing against the scene depth buffer so
    /// occluded bones are hidden. When <c>false</c> (default), bones are drawn on top of the scene.
    /// </summary>
    public bool UseDepthTest { get; set; } = false;

    /// <summary>
    /// Gets or sets the shape each bone is drawn with. Defaults to <see cref="BoneStyle.Arrow"/>.
    /// </summary>
    public BoneStyle Style { get; set; } = BoneStyle.Arrow;

    /// <summary>
    /// Gets or sets the arrow head length (<see cref="BoneStyle.Arrow"/>) or the octahedron waist
    /// position and radius (<see cref="BoneStyle.Octahedral"/>), as a fraction of the bone length
    /// in [0, 1]. Defaults to 0.2.
    /// </summary>
    public float HeadSize { get; set; } = 0.2f;

    public override bool Apply(in RenderResources res, ref string readSlot, ref string writeSlot)
    {
        Debug.Assert(_pipeline.Valid, "Skeleton pipeline is not valid.");

        var data = res.RenderContext.Data;
        var world = data?.World;
        if (world is null || !world.HasAnyComponent<SkeletonOverlay>())
        {
            return false;
        }

        GatherBones(world);
        if (_bones.Count == 0 || !UploadBones(res.RenderContext.FrameIndex, out var bonesAddress))
        {
            return false;
        }

        // Swap so we write into the current texture (same pattern as BoundingBoxPostEffect).
        (readSlot, writeSlot) = (writeSlot, readSlot);

        DrawBones(in res, res.CmdBuffer, writeSlot, bonesAddress);
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
        _boneBuffer = new RingElementBuffer<BoneLine>(
            Context!,
            (int)GraphicsSettings.MaxFrameInFlight,
            initialCapacity: 256,
            BufferUsageBits.Storage,
            hostVisible: true,
            debugName: "SkeletonBones"
        );
        _bufferFrameIndex = uint.MaxValue;
        _bufferOffset = 0;
        return ResultCode.Ok;
    }

    protected override ResultCode OnTearingDown()
    {
        _pipeline.Dispose();
        Disposer.DisposeAndRemove(ref _boneBuffer);
        _bones.Clear();
        _jointSet.Clear();
        return ResultCode.Ok;
    }

    private void GatherBones(World world)
    {
        _bones.Clear();
        foreach (var entity in world.GetComponentEntities<SkeletonOverlay>())
        {
            if (!entity.Has<MeshDrawInfo>())
            {
                continue;
            }
            var skinning = entity.Get<MeshDrawInfo>().Skinning;
            if (skinning is null)
            {
                continue;
            }
            var color = entity.Get<SkeletonOverlay>().Color;
            var joints = skinning.Joints;
            _jointSet.Clear();
            for (var i = 0; i < joints.Count; ++i)
            {
                _jointSet.Add(joints[i]);
            }
            for (var i = 0; i < joints.Count; ++i)
            {
                var joint = joints[i];
                if (!joint.Valid || !joint.Has<WorldTransform>() || !joint.Has<Parent>())
                {
                    continue;
                }
                var parent = joint.Get<Parent>().ParentEntity;
                if (!parent.Valid || !_jointSet.Contains(parent) || !parent.Has<WorldTransform>())
                {
                    continue;
                }
                _bones.Add(
                    new BoneLine
                    {
                        Start = parent.Get<WorldTransform>().Value.Translation,
                        End = joint.Get<WorldTransform>().Value.Translation,
                        Color = color,
                    }
                );
            }
        }
    }

    /// <summary>
    /// Appends the gathered bones to the current frame's bone buffer.
    /// </summary>
    /// <param name="frameIndex">The engine frame index of the viewport being rendered.</param>
    /// <param name="bonesAddress">The GPU address of the first uploaded bone.</param>
    /// <returns><c>true</c> if the bones were uploaded.</returns>
    private unsafe bool UploadBones(uint frameIndex, out ulong bonesAddress)
    {
        bonesAddress = 0;
        if (_boneBuffer is null)
        {
            return false;
        }
        if (frameIndex != _bufferFrameIndex)
        {
            _bufferFrameIndex = frameIndex;
            _bufferOffset = 0;
            _boneBuffer.Advance();
        }
        // Growing replaces the buffer; earlier viewports of this frame keep drawing from the old
        // one, whose destruction is deferred until the GPU is done with it.
        var result = _boneBuffer.EnsureCapacity(_bufferOffset + _bones.Count);
        if (result == ResultCode.Ok)
        {
            result = _boneBuffer.Current.Upload(
                _bones,
                0,
                _bones.Count,
                _bufferOffset * sizeof(BoneLine)
            );
        }
        if (result != ResultCode.Ok)
        {
            _logger.LogError("Failed to upload {COUNT} bones: {RESULT}", _bones.Count, result);
            return false;
        }
        bonesAddress = _boneBuffer.GpuAddress + (ulong)(_bufferOffset * sizeof(BoneLine));
        _bufferOffset += _bones.Count;
        return true;
    }

    private void DrawBones(
        in RenderResources res,
        ICommandBuffer cmdBuffer,
        string writeSlot,
        ulong bonesAddress
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
        // Vertices per bone must match the segment layout of each style in vsBoneLine.glsl.
        var vertexCount = Style switch
        {
            BoneStyle.Arrow => 18u,
            BoneStyle.Octahedral => 24u,
            _ => 2u,
        };
        cmdBuffer.PushConstants(
            new BoneLinePushConstant
            {
                BonesAddress = bonesAddress,
                FpConstAddress = fpConstAddress,
                Style = (uint)Style,
                HeadSize = Math.Clamp(HeadSize, 0f, 1f),
            }
        );
        // One instance per bone; the vertex shader reads the bone at gl_InstanceIndex.
        cmdBuffer.Draw(vertexCount, (uint)_bones.Count);

        cmdBuffer.EndRendering();
    }

    private ResultCode CreatePipeline()
    {
        if (Context is null)
        {
            _logger.LogError("Render context is null during skeleton pipeline creation.");
            return ResultCode.InvalidState;
        }

        var shaderCompiler = new ShaderCompiler();
        var vsResult = shaderCompiler.CompileVertexShader(
            GlslUtils.GetEmbeddedGlslShader("Vert/vsBoneLine.glsl")
        );
        if (!vsResult.Success || vsResult.Source is null)
        {
            _logger.LogError(
                "Failed to compile skeleton vertex shader: {ERRORS}",
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
                "Failed to compile skeleton fragment shader: {ERRORS}",
                string.Join("\n", fsResult.Errors)
            );
            return ResultCode.CompileError;
        }

        using var vs = Renderer!.ShaderRepository.GetOrCreateFromGlsl(
            ShaderStage.Vertex,
            vsResult.Source,
            [],
            "Skeleton_Vertex"
        );
        using var fs = Renderer!.ShaderRepository.GetOrCreateFromGlsl(
            ShaderStage.Fragment,
            fsResult.Source,
            [],
            "Skeleton_Frag"
        );

        var desc = new RenderPipelineDesc
        {
            VertexShader = vs,
            FragmentShader = fs,
            DebugName = "Skeleton_Bones",
            CullMode = CullMode.None,
            FrontFaceWinding = WindingMode.CCW,
            Topology = Topology.Line,
        };
        desc.Colors[0] = ColorAttachment.CreateOpaque(GraphicsSettings.IntermediateTargetFormat);
        desc.DepthFormat = GraphicsSettings.DepthBufferFormat;

        _pipeline = Context.CreateRenderPipeline(desc);
        if (!_pipeline.Valid)
        {
            _logger.LogError("Skeleton pipeline failed to create.");
            return ResultCode.RuntimeError;
        }
        return ResultCode.Ok;
    }
}

/// <summary>
/// The shape <see cref="SkeletonPostEffect"/> draws each bone with. Values match the
/// <c>style</c> push constant of <c>vsBoneLine.glsl</c>.
/// </summary>
public enum BoneStyle : uint
{
    /// <summary>A single line from the parent joint to the joint.</summary>
    Line = 0,

    /// <summary>A line with a wireframe pyramid head at the joint, showing the bone direction.</summary>
    Arrow = 1,

    /// <summary>A wireframe octahedron, wide near the parent joint and pointed at the joint.</summary>
    Octahedral = 2,
}
