namespace HelixToolkit.Nex.Rendering;

internal class DepthPassRenderer(Format depthFormat = Format.Z_F32) : Renderer
{
    private static readonly ILogger _logger = LogManager.Create<DepthPassRenderer>();
    private readonly RenderPass _renderPass = new();
    private readonly Framebuffer _framebuffer = new();

    public TextureResource DepthBuffer { private set; get; } = TextureResource.Null;
    public TextureResource MeshIdTexture { private set; get; } = TextureResource.Null;

    public RenderPipelineResource Pipeline { private set; get; } = RenderPipelineResource.Null;

    public Format DepthFormat => depthFormat;

    public override RenderStages Stage => RenderStages.Begin;

    public override string Name => nameof(DepthPassRenderer);

    public override IEnumerable<string> GetOutputs()
    {
        yield return CommonNames.TextureDepth;
        yield return CommonNames.TextureMeshId;
    }

    protected override bool OnSetup()
    {
        Debug.Assert(Context is not null && RenderManager is not null);

        CreateTargets(Width, Height);
        return CreatePipeline();
    }

    protected override void OnTearDown()
    {
        DepthBuffer.Dispose();
        MeshIdTexture.Dispose();
        base.OnTearDown();
    }

    protected override void OnResize(int width, int height)
    {
        base.Resize(width, height);
        CreateTargets(width, height);
    }

    protected override void OnRender(RenderContext renderContext) { }

    private bool CreatePipeline()
    {
        if (Context is null || RenderManager is null)
        {
            return false;
        }
        using var vs = RenderManager.ShaderRepository.GetOrCreateFromGlsl(
            ShaderStage.Vertex,
            GlslUtils.GetEmbeddedGlslShader("Vert.vsMainTemplate"),
            [new ShaderDefine(BuildFlags.OUTPUT_DRAW_ID, BuildFlags.EXCLUDE_MESH_PROPS)],
            "DepthPass_VS"
        );
        using var fs = RenderManager.ShaderRepository.GetOrCreateFromGlsl(
            ShaderStage.Fragment,
            GlslUtils.GetEmbeddedGlslShader("Frag.psEntityId"),
            [],
            "EntityId_PS"
        );

        var pipelineDesc = new RenderPipelineDesc
        {
            VertexShader = vs,
            FragementShader = fs,
            DebugName = "DepthPass",
            CullMode = CullMode.Back,
            FrontFaceWinding = WindingMode.CCW,
            DepthFormat = Format.Z_F32,
        };
        Pipeline = Context.CreateRenderPipeline(pipelineDesc);
        return Pipeline.Valid;
    }

    private void CreateTargets(int width, int height)
    {
        DepthBuffer.Dispose();
        DepthBuffer = TextureResource.Null;
        MeshIdTexture.Dispose();
        MeshIdTexture = TextureResource.Null;
        if (RenderManager is null || Context is null)
        {
            return;
        }
        if (width > 0 && height > 0)
        {
            DepthBuffer = Context!.CreateTexture(
                new TextureDesc()
                {
                    Type = TextureType.Texture2D,
                    Format = Format.Z_F32,
                    Dimensions = new Dimensions(
                        (uint)RenderManager!.Width,
                        (uint)RenderManager!.Height,
                        1
                    ),
                    NumLayers = 1,
                    NumSamples = 1,
                    Usage = TextureUsageBits.Attachment | TextureUsageBits.Sampled,
                    NumMipLevels = 1,
                    Storage = StorageType.Device,
                }
            );
            MeshIdTexture = Context!.CreateTexture(
                new TextureDesc()
                {
                    Type = TextureType.Texture2D,
                    Format = Format.R_UI32,
                    Dimensions = new Dimensions(
                        (uint)RenderManager!.Width,
                        (uint)RenderManager!.Height,
                        1
                    ),
                    NumLayers = 1,
                    NumSamples = 1,
                    Usage = TextureUsageBits.Attachment | TextureUsageBits.Sampled,
                    NumMipLevels = 1,
                    Storage = StorageType.Device,
                }
            );
        }

        _framebuffer.DepthStencil.Texture = DepthBuffer;
        _framebuffer.Colors[0].Texture = MeshIdTexture;

        _renderPass.Depth.ClearDepth = 0.0f;
        _renderPass.Depth.LoadOp = LoadOp.Clear;
        _renderPass.Depth.StoreOp = StoreOp.Store;

        _renderPass.Colors[0].ClearColor = new(0, 0, 0, 0);
        _renderPass.Colors[0].LoadOp = LoadOp.Clear;
        _renderPass.Colors[0].StoreOp = StoreOp.Store;
    }
}
