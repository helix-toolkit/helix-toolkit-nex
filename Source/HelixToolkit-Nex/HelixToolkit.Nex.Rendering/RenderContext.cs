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
    public readonly Matrix4x4 ViewProjection = view * projection;
    public readonly Matrix4x4 InvViewProjection = invProjection * invView;

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

    public readonly IContext Context = services.GetRequiredService<IContext>();

    public readonly ForwardPlusLightCulling.Config FPLightConfig = ForwardPlusLightCulling
        .Config
        .Default;

    public readonly struct UseExternalPipelineScope : IDisposable
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

    public bool UseExternalPipeline { get; private set; } = false;

    public TextureHandle FinalOutputTexture { get; set; } = TextureHandle.Null;

    public UseExternalPipelineScope EnableExternalPipelineScoped() => new(this);

    public RenderStatistics Statistics { get; } = new();

    public override string Name => nameof(RenderContext);

    public void Update(ICommandBuffer cmd)
    {
        Statistics.ResetPerFrame();
        if (Data?.Update() == false)
        {
            _logger.LogWarning("Failed to update render data.");
            return;
        }
    }

    protected override ResultCode OnInitializing()
    {
        return ResultCode.Ok;
    }

    protected override ResultCode OnTearingDown()
    {
        return ResultCode.Ok;
    }
}
