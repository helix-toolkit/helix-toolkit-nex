using HelixToolkit.Nex.ECS;
using HelixToolkit.Nex.Rendering.Components;
using HelixToolkit.Nex.Rendering.RenderNodes;
using HelixToolkit.Nex.Shaders.Frag;

namespace HelixToolkit.Nex.Rendering.PostEffects;

/// <summary>
/// Border-highlight post-processing effect.
///
/// For every mesh entity that carries a <see cref="BorderHighlightComponent"/> the
/// effect draws a coloured outline around the mesh silhouette using a two-stage
/// pipeline:
/// <list type="number">
///   <item>
///     <term>Silhouette mask pass</term>
///     <description>
///       Re-draws the highlighted meshes into a dedicated single-channel
///       (<see cref="SystemBufferNames.TextureHighlightMask"/>) render target
///       using a flat-white fragment shader so that every rasterised pixel
///       of each highlighted mesh becomes solid white.
///     </description>
///   </item>
///   <item>
///     <term>Composite pass</term>
///     <description>
///       A full-screen triangle pass reads the scene colour and the mask,
///       runs a cross-shaped neighbourhood sample to detect silhouette edges,
///       and blends the configured highlight colour over the scene only where
///       an edge is found (interior pixels are excluded so mesh surfaces are
///       not tinted).
///     </description>
///   </item>
/// </list>
///
/// The intermediate mask texture is registered into the shared
/// <see cref="RenderGraph"/> resource set via <see cref="RegisterResources"/> so it
/// is automatically created and resized with the viewport.
/// </summary>
public sealed class BorderHighlightPostEffect : PostEffect
{
    private static readonly ILogger _logger = LogManager.Create<BorderHighlightPostEffect>();

    // Silhouette-mask pass: standard mesh VS + flat-white FS.
    private RenderPipelineResource _maskPipeline = RenderPipelineResource.Null;

    // Composite pass: full-screen-quad VS + edge-detect/blend FS.
    private RenderPipelineResource _compositePipeline = RenderPipelineResource.Null;

    private SamplerResource _pointSampler = SamplerResource.Null;
    private SamplerResource _linearSampler = SamplerResource.Null;
    private readonly Dependencies _deps = new();
    private readonly Framebuffer _frameBuffer = new();
    private readonly RenderPass _pass = new();

    private readonly List<HighlightEntry> _entries = [];

    public override string Name => nameof(BorderHighlightPostEffect);
    public override Color DebugColor => Color.Orange;

    // -----------------------------------------------------------------------
    // Resource registration (graph-time)
    // -----------------------------------------------------------------------

    /// <inheritdoc/>
    /// <remarks>
    /// Registers <see cref="SystemBufferNames.TextureHighlightMask"/> — a full-resolution
    /// single-channel (R8) render target — into the render graph so it is
    /// allocated and resized by the shared resource set alongside all other render targets.
    /// </remarks>
    public override void RegisterResources(RenderGraph graph)
    {
        graph.AddTexture(
            SystemBufferNames.TextureHighlightMask,
            p =>
                p.Context.Context.CreateTexture2D(
                    Format.R_UN8,
                    (uint)p.Context.WindowSize.Width,
                    (uint)p.Context.WindowSize.Height,
                    TextureUsageBits.Sampled | TextureUsageBits.Attachment,
                    StorageType.Device,
                    debugName: SystemBufferNames.TextureHighlightMask
                ),
            dependsOnScreenSize: true
        );
    }

    // -----------------------------------------------------------------------
    // PostEffect interface
    // -----------------------------------------------------------------------

    public override void Apply(in RenderResources res, ref string readSlot, ref string writeSlot)
    {
        Debug.Assert(_maskPipeline.Valid, "Highlight mask pipeline is not valid.");
        Debug.Assert(_compositePipeline.Valid, "Highlight composite pipeline is not valid.");

        var data = res.Context.Data;
        if (data is null)
        {
            return;
        }

        var world = data.World;
        if (world is null || !world.HasAnyComponent<BorderHighlightComponent>())
        {
            // Nothing to highlight — skip both passes to avoid unnecessary work.
            return;
        }
        var cmdBuffer = res.CmdBuffer;
        var maskTex = res.Textures[SystemBufferNames.TextureHighlightMask];

        // ------------------------------------------------------------------
        // Gather highlighted draw commands and their colours.
        // We iterate over all entities that carry both MeshComponent and
        // BorderHighlightComponent, collecting each entity's draw-command index
        // so we can re-dispatch it in the silhouette pass.
        // ------------------------------------------------------------------

        GatherHighlightedDraws(world, data);
        if (_entries.Count == 0)
        {
            return;
        }

        // Compute texel dimensions for the mask texture (used in the composite pass).
        var maskDims = res.Context.Context.GetDimensions(maskTex);
        float texelW = maskDims.Width > 0 ? 1.0f / maskDims.Width : 0f;
        float texelH = maskDims.Height > 0 ? 1.0f / maskDims.Height : 0f;

        // ------------------------------------------------------------------
        // Stage 0: Silhouette mask
        //   Draw all highlighted meshes into TextureHighlightMask as flat white.
        //   The interior is solid white; the exterior stays black (clear colour).
        // ------------------------------------------------------------------
        DrawSilhouetteMask(in res, cmdBuffer, maskTex, data, _entries);

        // ------------------------------------------------------------------
        // Stage 1: Composite
        //   Blend the outline colour over the scene colour using edge detection
        //   on the mask.  Because all highlighted entities may share different
        //   colours we do one composite pass per unique colour group, blending
        //   them onto the scene in the ping-pong write slot.
        // ------------------------------------------------------------------
        DrawComposite(
            in res,
            cmdBuffer,
            maskTex,
            texelW,
            texelH,
            ref readSlot,
            ref writeSlot,
            _entries
        );
    }

    protected override ResultCode OnInitializing()
    {
        if (Context is null)
        {
            _logger.LogError("Render context is null during highlight initialisation.");
            return ResultCode.InvalidState;
        }

        _pointSampler = Context.CreateSampler(SamplerStateDesc.PointClamp);
        _linearSampler = Context.CreateSampler(SamplerStateDesc.LinearClamp);

        if (!_pointSampler.Valid || !_linearSampler.Valid)
        {
            return ResultCode.RuntimeError;
        }

        return CreatePipelines();
    }

    protected override ResultCode OnTearingDown()
    {
        _maskPipeline.Dispose();
        _compositePipeline.Dispose();
        _pointSampler.Dispose();
        _linearSampler.Dispose();
        // TextureHighlightMask is owned by the shared RenderGraphResourceSet — not disposed here.
        return ResultCode.Ok;
    }

    // -----------------------------------------------------------------------
    // Silhouette mask helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Gathers draw commands (index in the opaque draw buffer, colour, thickness) for every
    /// enabled entity that has both a <see cref="MeshComponent"/> and a
    /// <see cref="BorderHighlightComponent"/>.
    /// </summary>
    private void GatherHighlightedDraws(World world, IRenderDataProvider data)
    {
        _entries.Clear();
        var meshDraws = data.MeshDrawsOpaque;

        foreach (var entity in world.GetComponentEntities<BorderHighlightComponent>())
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

            ref var highlight = ref entity.Get<BorderHighlightComponent>();
            _entries.Add(
                new HighlightEntry(
                    DrawIndex: (uint)meshComp.Index,
                    Color: highlight.Color,
                    Thickness: highlight.Thickness > 0 ? highlight.Thickness : 2f
                )
            );
        }
    }

    /// <summary>
    /// Renders highlighted meshes into the silhouette mask texture.
    /// Uses the standard mesh vertex shader (via <see cref="RenderContext.EnableExternalPipelineScoped"/>)
    /// with a flat-white fragment shader so every rasterised pixel of a highlighted
    /// mesh becomes white in the mask.
    /// </summary>
    private void DrawSilhouetteMask(
        in RenderResources res,
        ICommandBuffer cmdBuffer,
        TextureHandle maskTex,
        IRenderDataProvider data,
        List<HighlightEntry> entries
    )
    {
        var context = res.Context;
        var meshDraws = data.MeshDrawsOpaque;
        var fpConstBuf = res.Buffers[SystemBufferNames.BufferForwardPlusConstants];

        _pass.Colors[0].ClearColor = new Color4(0f, 0f, 0f, 0f);
        _pass.Colors[0].LoadOp = LoadOp.Clear;
        _pass.Colors[0].StoreOp = StoreOp.Store;

        _frameBuffer.Colors[0].Texture = maskTex;
        _deps.Textures[0] = TextureHandle.Null;
        _deps.Textures[1] = TextureHandle.Null;
        cmdBuffer.BeginRendering(_pass, _frameBuffer, _deps);
        cmdBuffer.BindRenderPipeline(_maskPipeline);
        cmdBuffer.BindDepthState(DepthState.Disabled);

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
                new MeshDrawPushConstant
                {
                    FpConstAddress = fpConstAddress,
                    DrawCommandIdxOffset = entry.DrawIndex,
                    MeshDrawId = entry.DrawIndex,
                }
            );

            cmdBuffer.DrawIndexedIndirect(
                meshDraws.Buffer,
                entry.DrawIndex * meshDraws.Stride,
                1,
                meshDraws.Stride
            );
        }

        cmdBuffer.EndRendering();
    }

    /// <summary>
    /// Full-screen composite pass: edge-detects the mask and blends the highlight
    /// colour onto the scene colour written to <paramref name="writeSlot"/>.
    /// </summary>
    private void DrawComposite(
        in RenderResources res,
        ICommandBuffer cmdBuffer,
        TextureHandle maskTex,
        float texelW,
        float texelH,
        ref string readSlot,
        ref string writeSlot,
        List<HighlightEntry> entries
    )
    {
        var sceneTex = res.Textures[readSlot];

        // Determine the dominant colour / thickness (use the first entry for the
        // combined pass; if colours differ we run one composite pass per unique
        // colour).  For the common case (single colour) this is one pass.
        // Group by colour to minimise the number of passes.
        var groups = new Dictionary<(float r, float g, float b, float a, float t), bool>();
        foreach (var e in entries)
        {
            var key = (e.Color.Red, e.Color.Green, e.Color.Blue, e.Color.Alpha, e.Thickness);
            groups[key] = true;
        }

        foreach (var group in groups)
        {
            var (r, g, b, a, thickness) = group.Key;
            RunFullScreenPass(
                cmdBuffer,
                _compositePipeline,
                inputHandle: sceneTex,
                outputHandle: res.Textures[writeSlot],
                new HighlightPushConstants
                {
                    SceneTextureId = sceneTex.Index,
                    SceneSamplerId = _pointSampler.Index,
                    MaskTextureId = maskTex.Index,
                    MaskSamplerId = _linearSampler.Index,
                    TexelWidth = texelW,
                    TexelHeight = texelH,
                    R = r,
                    G = g,
                    B = b,
                    A = a,
                    Thickness = thickness,
                },
                input2Handle: maskTex
            );

            // After the first composite pass the output is in writeSlot.
            // Subsequent passes (different colours) need to read what was just
            // written, so swap for the next iteration.
            (readSlot, writeSlot) = (writeSlot, readSlot);
            sceneTex = res.Textures[readSlot];
        }

        // After the loop the last write was to the current readSlot (because we
        // swap after each pass).  Undo the final swap so the caller receives the
        // correct values — the effect contract is: "after Apply returns, readSlot
        // holds the freshly written texture".
        // (The PostEffectsNode swaps for us after Apply returns.)
        (readSlot, writeSlot) = (writeSlot, readSlot);
    }

    // -----------------------------------------------------------------------
    // Full-screen pass helper (same pattern as Bloom)
    // -----------------------------------------------------------------------

    private void RunFullScreenPass(
        ICommandBuffer cmdBuffer,
        RenderPipelineResource pipeline,
        TextureHandle inputHandle,
        TextureHandle outputHandle,
        HighlightPushConstants pc,
        TextureHandle input2Handle = default
    )
    {
        _deps.Textures[0] = inputHandle;
        if (input2Handle.Valid)
        {
            _deps.Textures[1] = input2Handle;
        }

        _pass.Colors[0].LoadOp = LoadOp.Load;
        _pass.Colors[0].StoreOp = StoreOp.Store;

        _frameBuffer.Colors[0].Texture = outputHandle;

        cmdBuffer.BeginRendering(_pass, _frameBuffer, _deps);
        cmdBuffer.BindRenderPipeline(pipeline);
        cmdBuffer.BindDepthState(DepthState.Disabled);
        cmdBuffer.PushConstants(pc);
        cmdBuffer.Draw(3);
        cmdBuffer.EndRendering();
    }

    // -----------------------------------------------------------------------
    // Pipeline creation
    // -----------------------------------------------------------------------

    private ResultCode CreatePipelines()
    {
        if (Context is null)
        {
            _logger.LogError("Render context is null during highlight pipeline creation.");
            return ResultCode.InvalidState;
        }

        // ----- Silhouette mask pipeline -----
        // Vertex shader: standard mesh vertex shader (same as DepthPassNode).
        // Fragment shader: flat-white stage (HIGHLIGHT_STAGE == MASK == 0).
        var shaderCompiler = new ShaderCompiler();
        var fsResult = shaderCompiler.CompileFragmentShader(
            GlslUtils.GetEmbeddedGlslShader("Frag/psHighlight.glsl")
        );
        if (!fsResult.Success || fsResult.Source is null)
        {
            _logger.LogError(
                "Failed to compile highlight fragment shader: {ERRORS}",
                string.Join("\n", fsResult.Errors)
            );
            return ResultCode.CompileError;
        }
        using var fs = Renderer!.ShaderRepository.GetOrCreateFromGlsl(
            ShaderStage.Fragment,
            fsResult.Source,
            [],
            "Highlight_Frag"
        );

        {
            var vsResult = shaderCompiler.CompileVertexShader(
                GlslUtils.GetEmbeddedGlslShader("Vert.vsMainTemplate")
            );
            if (!vsResult.Success || vsResult.Source is null)
            {
                _logger.LogError(
                    "Failed to compile highlight mask vertex shader: {ERRORS}",
                    string.Join("\n", vsResult.Errors)
                );
                return ResultCode.CompileError;
            }

            using var vs = Renderer!.ShaderRepository.GetOrCreateFromGlsl(
                ShaderStage.Vertex,
                vsResult.Source,
                [new ShaderDefine(BuildFlags.EXCLUDE_MESH_PROPS)],
                "Highlight_Mask"
            );

            var desc = new RenderPipelineDesc
            {
                VertexShader = vs,
                FragmentShader = fs,
                DebugName = "Highlight_Mask",
                CullMode = CullMode.None,
                FrontFaceWinding = WindingMode.CCW,
            };
            desc.Colors[0] = ColorAttachment.CreateOpaque(Format.R_UN8);
            desc.WriteSpecInfo(0, (uint)HighlightMode.Mask);
            _maskPipeline = Context.CreateRenderPipeline(desc);
        }

        // ----- Composite pipeline -----
        // Vertex shader: standard full-screen-quad VS.
        // Fragment shader: edge-detect/blend stage (HIGHLIGHT_STAGE == COMPOSITE == 1).
        {
            var vsResult = shaderCompiler.CompileVertexShader(
                GlslUtils.GetEmbeddedGlslShader("Vert/vsFullScreenQuad.glsl")
            );
            if (!vsResult.Success || vsResult.Source is null)
            {
                _logger.LogError(
                    "Failed to compile highlight composite vertex shader: {ERRORS}",
                    string.Join("\n", vsResult.Errors)
                );
                return ResultCode.CompileError;
            }

            using var vs = Renderer!.ShaderRepository.GetOrCreateFromGlsl(
                ShaderStage.Vertex,
                vsResult.Source,
                [new ShaderDefine(BuildFlags.EXCLUDE_MESH_PROPS)],
                "FullScreenQuad_Vertex"
            );

            var desc = new RenderPipelineDesc
            {
                VertexShader = vs,
                FragmentShader = fs,
                DebugName = "Highlight_Composite",
                CullMode = CullMode.None,
                FrontFaceWinding = WindingMode.CCW,
            };
            desc.Colors[0] = ColorAttachment.CreateOpaque(RenderSettings.IntermediateTargetFormat);
            desc.WriteSpecInfo(0, (uint)HighlightMode.Composite);
            _compositePipeline = Context.CreateRenderPipeline(desc);
        }

        if (!_maskPipeline.Valid || !_compositePipeline.Valid)
        {
            _logger.LogError("One or more highlight pipelines failed to create.");
            return ResultCode.RuntimeError;
        }

        return ResultCode.Ok;
    }

    // -----------------------------------------------------------------------
    // Internal data
    // -----------------------------------------------------------------------

    private readonly record struct HighlightEntry(uint DrawIndex, Color4 Color, float Thickness);
}
