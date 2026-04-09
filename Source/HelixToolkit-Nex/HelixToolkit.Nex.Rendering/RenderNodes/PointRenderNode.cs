namespace HelixToolkit.Nex.Rendering.RenderNodes;

/// <summary>
/// Renders point clouds using a compute-expand + triangle-strip pipeline.
/// <para>
/// <b>Architecture:</b>
/// <list type="number">
/// <item><b>Compute pass</b> — Frustum-culls and screen-size-culls input <c>PointData</c>,
/// writes one <c>PointDrawData</c> per visible point, and atomically increments
/// <c>PointDrawIndirectArgs.instanceCount</c>.</item>
/// <item><b>Render pass</b> — Uses <c>DrawIndirect</c> with <c>vertexCount=4</c> (triangle strip)
/// and <c>instanceCount</c> from the compute pass.  The vertex shader expands each instance
/// into a screen-aligned quad via <c>gl_VertexIndex</c> (0..3) and
/// <c>gl_InstanceIndex</c> (which visible point).  The fragment shader outputs color
/// to <see cref="SystemBufferNames.TextureColorF16A"/> and entity ID to
/// <see cref="SystemBufferNames.TextureEntityId"/> for GPU picking.</item>
/// </list>
/// </para>
/// <para>
/// <b>Fragment shader customization:</b> Users can supply custom GLSL for the
/// <c>getPointColor()</c> function by setting <see cref="CustomFragmentShaderSource"/>
/// before the node is set up. The custom code replaces the region between
/// <c>/*TEMPLATE_POINT_COLOR_START*/</c> and <c>/*TEMPLATE_POINT_COLOR_END*/</c>
/// in <c>psPointTemplate.glsl</c>. The custom shader has access to bindless textures
/// via <c>v_textureIndex</c> / <c>v_samplerIndex</c>, the PBR lighting helpers through
/// <c>FPConstants</c>, and the standard point varyings (<c>v_uv</c>, <c>v_color</c>,
/// <c>v_screenSize</c>, <c>v_entityId</c>).
/// </para>
/// </summary>
public sealed class PointRenderNode : RenderNode
{
    private static readonly ILogger _logger = LogManager.Create<PointRenderNode>();

    private ComputePipelineResource _expandPipeline = ComputePipelineResource.Null;
    private RenderPipelineResource _renderPipeline = RenderPipelineResource.Null;

    // Transient GPU resources — recreated when point capacity changes.
    private BufferResource _drawDataBuffer = BufferResource.Null;
    private BufferResource _indirectArgsBuffer = BufferResource.Null;
    private BufferResource _pointExpandArgsBuffer = BufferResource.Null;
    private int _currentCapacity;
    private readonly Dependencies _expandDeps = new Dependencies();

    /// <summary>
    /// Gets or sets the maximum number of points this node can render.
    /// Changing this value causes GPU buffers to be reallocated on the next frame.
    /// </summary>
    public int MaxPoints { get; set; } = 1_000_000;

    /// <summary>
    /// Gets or sets the minimum screen-space size (in pixels) below which a point is culled.
    /// </summary>
    public float MinScreenSize { get; set; } = 1f;

    /// <summary>
    /// Gets or sets a custom fragment shader source string that replaces the default
    /// <c>getPointColor()</c> implementation. Set to <see langword="null"/> (default)
    /// to use the built-in circle-SDF shader.
    /// <para>
    /// The source must define a <c>vec4 getPointColor()</c> function.
    /// Available varyings: <c>v_uv</c>, <c>v_color</c>, <c>v_screenSize</c>,
    /// <c>v_entityId</c>, <c>v_textureIndex</c>, <c>v_samplerIndex</c>.
    /// Bindless texture helpers from <c>HeaderFrag.glsl</c> are available.
    /// </para>
    /// </summary>
    public string? CustomFragmentShaderSource { get; set; }

    public override string Name => nameof(PointRenderNode);
    public override Color4 DebugColor => new(0.8f, 0.6f, 0.2f, 1.0f);

    #region Setup / Teardown

    protected override bool OnSetup()
    {
        if (Context is null || Renderer is null)
        {
            _logger.LogError("Context or Renderer is null during PointRenderNode setup.");
            return false;
        }
        _pointExpandArgsBuffer = Context.CreateBuffer(
            new BufferDesc
            {
                DataSize = PointExpandArgs.SizeInBytes,
                Usage = BufferUsageBits.Storage,
                Storage = StorageType.Device,
                DebugName = "PointExpandArgs",
            }
        );
        return CreateExpandPipeline() && CreateRenderPipeline();
    }

    protected override void OnTeardown()
    {
        _expandPipeline.Dispose();
        _renderPipeline.Dispose();
        _drawDataBuffer.Dispose();
        _indirectArgsBuffer.Dispose();
        _pointExpandArgsBuffer.Dispose();
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

    private bool CreateRenderPipeline()
    {
        var compiler = new ShaderCompiler();

        // Vertex shader
        var vsResult = compiler.CompileVertexShader(
            GlslUtils.GetEmbeddedGlslShader("Point.vsPoint")
        );
        if (!vsResult.Success)
        {
            _logger.LogError("Failed to compile point vertex shader: {Errors}", vsResult.Errors);
            return false;
        }
        using var vs = Renderer!.ShaderRepository.GetOrCreateFromGlsl(
            ShaderStage.Vertex,
            vsResult.Source!,
            [],
            "Point_VS"
        );

        // Fragment shader — apply custom override if provided
        var fragmentSource = GlslUtils.GetEmbeddedGlslShader("Point.psPointTemplate");
        if (!string.IsNullOrEmpty(CustomFragmentShaderSource))
        {
            fragmentSource = ReplaceTemplateRegion(
                fragmentSource,
                "TEMPLATE_POINT_COLOR_START",
                "TEMPLATE_POINT_COLOR_END",
                CustomFragmentShaderSource!
            );
        }

        var fsResult = compiler.CompileFragmentShader(fragmentSource);
        if (!fsResult.Success)
        {
            _logger.LogError("Failed to compile point fragment shader: {Errors}", fsResult.Errors);
            return false;
        }
        using var fs = Renderer.ShaderRepository.GetOrCreateFromGlsl(
            ShaderStage.Fragment,
            fsResult.Source!,
            [],
            "Point_PS"
        );

        var pipelineDesc = new RenderPipelineDesc
        {
            VertexShader = vs,
            FragmentShader = fs,
            DebugName = "PointRender",
            CullMode = CullMode.None,
            FrontFaceWinding = WindingMode.CCW,
            Topology = Topology.TriangleStrip,
            DepthFormat = RenderSettings.DepthBufferFormat,
        };

        // Color 0: scene color (pre-multiplied alpha blend)
        pipelineDesc.Colors[0] = ColorAttachment.CreateAlphaBlend(
            RenderSettings.IntermediateTargetFormat
        );

        // Color 1: entity ID (no blend — closest fragment wins via depth test)
        pipelineDesc.Colors[1] = new ColorAttachment
        {
            Format = Format.RG_F32,
            BlendEnabled = false,
        };

        _renderPipeline = Context!.CreateRenderPipeline(pipelineDesc);
        return _renderPipeline.Valid;
    }

    #endregion

    #region Render

    protected override bool BeginRender(in RenderResources res)
    {
        var pointCloudData = res.Context.Data?.PointCloudData;
        if (pointCloudData is null || pointCloudData.TotalPointCount == 0)
            return false;

        EnsureBuffers(res.Context.Context, (int)pointCloudData.TotalPointCount);

        // --- Reset indirect args ---
        var args = new PointDrawIndirectArgs
        {
            VertexCount = 4, // triangle strip quad
            InstanceCount = 0, // compute shader will atomically increment
            FirstVertex = 0,
            FirstInstance = 0,
        };
        res.CmdBuffer.UpdateBuffer(_indirectArgsBuffer, args);

        // --- Shared camera state ---
        var camera = res.Context.CameraParams;
        var view = camera.View;
        var right = new Vector3(view.M11, view.M21, view.M31);
        var up = new Vector3(view.M12, view.M22, view.M32);
        float fovY = 2.0f * MathF.Atan(1.0f / camera.Projection.M22);

        res.CmdBuffer.PushDebugGroupLabel("PointExpand", new Color4(0.8f, 0.6f, 0.2f, 1.0f));
        res.CmdBuffer.BindComputePipeline(_expandPipeline);

        // --- Dispatch per point cloud entity ---
        var dispatches = pointCloudData.Dispatches;
        var pointBufferAddress = pointCloudData.GpuAddress;

        _expandDeps.Buffers[0] = pointCloudData.Buffer;
        _expandDeps.Buffers[1] = _indirectArgsBuffer;
        _expandDeps.Buffers[2] = _pointExpandArgsBuffer;
        var expandPC = new PointExpandArgs
        {
            DrawDataAddress = _drawDataBuffer.GpuAddress,
            IndirectArgsAddress = _indirectArgsBuffer.GpuAddress,
            ViewProjection = camera.ViewProjection,
            CameraPosition = camera.Position,
            CameraRight = right,
            ScreenHeight = res.Context.WindowSize.Height,
            CameraUp = up,
            FovY = fovY,
            MinScreenSize = MinScreenSize,
        };
        res.CmdBuffer.UpdateBuffer(_pointExpandArgsBuffer, expandPC);

        for (var i = 0; i < dispatches.Count; i++)
        {
            ref var d = ref dispatches.At(i);
            var expandArgs = new PointExpandPC
            {
                ArgsAddress = _pointExpandArgsBuffer.GpuAddress,
                PointDataAddress = pointBufferAddress + d.BufferOffset * PointData.SizeInBytes,
                PointCount = d.PointCount,
                EntityId = d.EntityId,
                EntityVer = d.EntityVer,
                TextureIndex = d.TextureIndex,
                SamplerIndex = d.SamplerIndex,
                FixedSize = d.FixedSize,
            };
            res.CmdBuffer.PushConstants(expandArgs);
            uint groupCount = (d.PointCount + 63) / 64;
            res.CmdBuffer.DispatchThreadGroups(new Dimensions(groupCount, 1, 1), _expandDeps);
        }

        res.CmdBuffer.PopDebugGroupLabel();

        // Begin the render pass
        res.Deps.Buffers[0] = _drawDataBuffer;
        res.Deps.Buffers[1] = _indirectArgsBuffer;
        res.CmdBuffer.BeginRendering(res.Pass, res.Framebuf, res.Deps);
        return true;
    }

    protected override void OnRender(in RenderResources res)
    {
        Debug.Assert(_renderPipeline.Valid, "Point render pipeline is not valid.");

        var fpConstAddress = res.Buffers[SystemBufferNames.BufferForwardPlusConstants]
            .GpuAddress(res.Context.Context);

        res.CmdBuffer.BindRenderPipeline(_renderPipeline);
        res.CmdBuffer.BindDepthState(DepthState.DefaultReversedZ);

        // For the render pass, fixedSize is read from each PointDrawData.screenSize
        // which was already correctly computed by the compute shader.
        // We still pass fixedSize=0 for now; the vertex shader uses screenSize
        // from PointDrawData which is correct for both modes.
        res.CmdBuffer.PushConstants(
            new PointRenderPC
            {
                DrawDataAddress = _drawDataBuffer.GpuAddress,
                FpConstAddress = fpConstAddress,
            }
        );

        res.CmdBuffer.DrawIndirect(_indirectArgsBuffer, 0, 1, PointDrawIndirectArgs.SizeInBytes);
    }

    protected override void EndRender(in RenderResources res)
    {
        res.CmdBuffer.EndRendering();
    }

    #endregion

    #region Render Graph

    public override void AddToGraph(RenderGraph graph)
    {
        // Register point-specific GPU buffers
        graph.AddPass(
            nameof(PointRenderNode),
            inputs:
            [
                new(SystemBufferNames.TextureDepthF32, ResourceType.Texture),
                new(SystemBufferNames.TextureEntityId, ResourceType.Texture),
                new(SystemBufferNames.BufferForwardPlusConstants, ResourceType.Buffer),
            ],
            outputs: [new(SystemBufferNames.TextureColorF16Current, ResourceType.Texture)],
            after: [nameof(ForwardPlusOpaqueNode)],
            onSetup: (res) =>
            {
                // Color 0: scene color (load existing opaque content)
                res.Framebuf.Colors[0].Texture = res.Textures[
                    SystemBufferNames.TextureColorF16Current
                ];
                res.Pass.Colors[0].LoadOp = LoadOp.Load;
                res.Pass.Colors[0].StoreOp = StoreOp.Store;

                // Color 1: entity ID (load existing mesh IDs, overwrite where points are closer)
                res.Framebuf.Colors[1].Texture = res.Textures[SystemBufferNames.TextureEntityId];
                res.Pass.Colors[1].LoadOp = LoadOp.Load;
                res.Pass.Colors[1].StoreOp = StoreOp.Store;

                // Depth: read + write (points depth-test against meshes AND each other)
                res.Framebuf.DepthStencil.Texture = res.Textures[SystemBufferNames.TextureDepthF32];
                res.Pass.Depth.LoadOp = LoadOp.Load;
                res.Pass.Depth.StoreOp = StoreOp.Store;

                // Dependencies
                res.Deps.Textures[0] = res.Textures[SystemBufferNames.TextureColorF16Current];
                res.Deps.Textures[1] = res.Textures[SystemBufferNames.TextureDepthF32];
            }
        );
    }

    #endregion

    #region Helpers

    private void EnsureBuffers(IContext context, int totalPointCount)
    {
        var requiredCapacity = Math.Max(totalPointCount, MaxPoints);
        if (_currentCapacity >= requiredCapacity && _drawDataBuffer.Valid)
            return;

        // Dispose old buffers
        _drawDataBuffer.Dispose();
        _indirectArgsBuffer.Dispose();

        _currentCapacity = requiredCapacity;

        _drawDataBuffer = context.CreateBuffer(
            new BufferDesc
            {
                DataSize = (uint)(_currentCapacity * (int)PointDrawData.SizeInBytes),
                Usage = BufferUsageBits.Storage,
                Storage = StorageType.Device,
                DebugName = "PointDrawDataBuffer",
            }
        );

        _indirectArgsBuffer = context.CreateBuffer(
            new PointDrawIndirectArgs(),
            BufferUsageBits.Storage | BufferUsageBits.Indirect,
            StorageType.Device,
            "PointIndirectArgsBuffer"
        );
    }

    private static string ReplaceTemplateRegion(
        string source,
        string startMarker,
        string endMarker,
        string replacement
    )
    {
        var fullStart = $"/*{startMarker}*/";
        var fullEnd = $"/*{endMarker}*/";
        int startIdx = source.IndexOf(fullStart, StringComparison.Ordinal);
        int endIdx = source.IndexOf(fullEnd, StringComparison.Ordinal);
        if (startIdx < 0 || endIdx < 0 || endIdx <= startIdx)
        {
            return source;
        }
        int contentStart = startIdx + fullStart.Length;
        return source[..contentStart] + "\n" + replacement + "\n" + source[endIdx..];
    }

    #endregion
}
