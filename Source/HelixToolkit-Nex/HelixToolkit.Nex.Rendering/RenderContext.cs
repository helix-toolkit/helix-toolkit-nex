namespace HelixToolkit.Nex.Rendering;

public readonly record struct Range(uint Start, uint Count)
{
    public readonly bool Empty => Count == 0;

    public readonly uint End => Start + Count;

    public static readonly Range Zero = new(0, 0);
}

public readonly struct CameraParams(
    Matrix4x4 view,
    Matrix4x4 projection,
    Matrix4x4 invView,
    Matrix4x4 invProjection,
    Vector3 position,
    Vector3 target,
    Vector3 up,
    float nearPlane,
    float farPlane
)
{
    public readonly Matrix4x4 View = view;
    public readonly Matrix4x4 Projection = projection;
    public readonly Matrix4x4 InvView = invView;
    public readonly Matrix4x4 InvProjection = invProjection;
    public readonly Vector3 Position = position;
    public readonly Vector3 Target = target;
    public readonly Vector3 Up = up;
    public readonly float NearPlane = nearPlane;
    public readonly float FarPlane = farPlane;

    public static readonly CameraParams Identity = new(
        Matrix4x4.Identity,
        Matrix4x4.Identity,
        Matrix4x4.Identity,
        Matrix4x4.Identity,
        Vector3.Zero,
        Vector3.Zero,
        Vector3.UnitY,
        0,
        0
    );
};

public sealed class RenderContext(IServiceProvider services) : Initializable
{
    private static readonly ILogger _logger = LogManager.Create<RenderContext>();

    private BufferResource _lightGridBuf = BufferResource.Null;
    private BufferResource _lightIndexBuf = BufferResource.Null;
    private bool _windowSizeChanged = true;

    public readonly IContext Context = services.GetRequiredService<IContext>();

    public readonly ForwardPlusLightCulling.Config FPLightConfig = ForwardPlusLightCulling
        .Config
        .Default;

    public struct UseExternalPipelineScope : IDisposable
    {
        private readonly RenderContext _context;

        public UseExternalPipelineScope(RenderContext context)
        {
            _context = context;
            _context.UseExternalPipeline = true;
        }

        public void Dispose()
        {
            _context.UseExternalPipeline = false;
        }
    }

    public IRenderDataProvider? Data { set; get; }

    private Size _windowSize;

    public Size WindowSize
    {
        set
        {
            if (_windowSize != value)
            {
                _windowSize = value;
                _logger.LogInformation(
                    "Window size changed: {Width}x{Height}",
                    value.Width,
                    value.Height
                );
                _windowSizeChanged = true;
                TileCountX =
                    (WindowSize.Width + (int)FPLightConfig.TileSize - 1)
                    / (int)FPLightConfig.TileSize;
                TileCountY =
                    (WindowSize.Height + (int)FPLightConfig.TileSize - 1)
                    / (int)FPLightConfig.TileSize;
            }
        }
        get => _windowSize;
    }

    public int TileCountX { private set; get; }

    public int TileCountY { private set; get; }

    public float DpiScale { set; get; } = 1;

    public CameraParams CameraParams { set; get; } = CameraParams.Identity;

    public BufferResource FPConstantsBuffer { private set; get; } = BufferResource.Null;

    public bool UseExternalPipeline { get; private set; } = false;

    public TextureHandle FinalOutputTexture { get; set; } = TextureHandle.Null;

    public UseExternalPipelineScope EnableExternalPipelineScoped() => new(this);

    public RenderStatistics Statistics { get; } = new();

    public override string Name => throw new NotImplementedException();

    public void Update(ICommandBuffer cmd)
    {
        if (Data?.Update() == false)
        {
            _logger.LogWarning("Failed to update render data.");
            return;
        }
        if (WindowSize == Size.Empty)
            return;
        HandleWindowSizeChanged();
        if (FPConstantsBuffer.Valid)
        {
            var fpData = new FPConstants
            {
                Time = (float)DateTime.Now.TimeOfDay.TotalSeconds,
                CameraPosition = CameraParams.InvView.Translation,
                InverseViewProjection = CameraParams.InvProjection * CameraParams.InvView,
                ViewProjection = CameraParams.View * CameraParams.Projection,
                LightCount = Data?.Lights.Count ?? 0,
                MaxLightsPerTile = FPLightConfig.MaxLightsPerTile,
                TileSize = FPLightConfig.TileSize,
                ScreenDimensions = new Vector2(WindowSize.Width, WindowSize.Height),
                TileCountX = (uint)TileCountX,
                TileCountY = (uint)TileCountY,
                MeshInfoBufferAddress = Data?.MeshInfos.GpuAddress ?? 0,
                LightBufferAddress = Data?.Lights.GpuAddress ?? 0,
                LightGridBufferAddress = _lightGridBuf.GpuAddress,
                LightIndexBufferAddress = _lightIndexBuf.GpuAddress,
                MaterialBufferAddress = Data?.PBRPropertiesBuffer.GpuAddress ?? 0,
                MeshDrawBufferAddress = Data?.MeshDrawsOpaque.GpuAddress ?? 0,
                DirectionalLightsBufferAddress = Data?.DirectionalLights.GpuAddress ?? 0,
            };
            cmd.UpdateBuffer(FPConstantsBuffer, fpData);
        }
    }

    protected override ResultCode OnInitializing()
    {
        FPConstantsBuffer = Context.CreateBuffer(
            new FPConstants(),
            BufferUsageBits.Storage,
            StorageType.Device,
            SystemBufferNames.ForwardPlusConstants
        );
        return ResultCode.Ok;
    }

    protected override ResultCode OnTearingDown()
    {
        FPConstantsBuffer.Dispose();
        FPConstantsBuffer = BufferResource.Null;
        _lightGridBuf.Dispose();
        _lightGridBuf = BufferResource.Null;
        _lightIndexBuf.Dispose();
        _lightIndexBuf = BufferResource.Null;
        return ResultCode.Ok;
    }

    private void HandleWindowSizeChanged()
    {
        if (!_windowSizeChanged)
        {
            return;
        }
        _lightGridBuf.Dispose();
        _lightIndexBuf.Dispose();
        var totalTiles = TileCountX * TileCountY;
        // Light grid buffer: stores light count and index offset per tile
        _lightGridBuf = Context.CreateBuffer(
            new BufferDesc
            {
                DataSize = (uint)(totalTiles * LightGridTile.SizeInBytes),
                Usage = BufferUsageBits.Storage,
                Storage = StorageType.Device,
            },
            "ForwardPlus_LightGrid"
        );

        // Light index list buffer: stores light indices for all tiles
        _lightIndexBuf = Context.CreateBuffer(
            new BufferDesc
            {
                DataSize = (uint)(totalTiles * FPLightConfig.MaxLightsPerTile * sizeof(uint)),
                Usage = BufferUsageBits.Storage,
                Storage = StorageType.Device,
            },
            "ForwardPlus_LightIndices"
        );
        _windowSizeChanged = false;
    }
}
