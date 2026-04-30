namespace HelixToolkit.Nex.Rendering;

public readonly record struct DrawRange(uint Start, uint Count)
{
    public readonly bool Empty => Count == 0;

    public readonly uint End => Start + Count;

    public static readonly DrawRange Zero = new(0, 0);
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

public sealed class RenderParams
{
    public Color4 BackgroundColor = Color.Black;
    public bool EnableLightCulling = true;
    public bool EnableGammaCorrection = false;
}

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

    /// <summary>
    /// Gets or sets the resource set that holds GPU resources (textures and buffers)
    /// for the current render graph execution. The resource set is owned by the
    /// <see cref="RenderContext"/> and disposed when the context is torn down.
    /// </summary>
    public RenderGraphResourceSet ResourceSet { get; } = new();
    /// <summary>
    /// Render parameters including background color and other settings.
    /// </summary>
    public RenderParams RenderParams { get; } = new();

    private static readonly Size DefaultWindowSize = new(1, 1);
    private Size _windowSize = DefaultWindowSize;

    public Size WindowSize
    {
        set
        {
            if (_windowSize != value)
            {
                _windowSize = value;
                if (value.Width <= 0 || value.Height <= 0)
                {
                    _logger.LogWarning(
                        "Invalid window size set: {Width}x{Height}. Window size must be positive. Defaulting to {DefaultWidth}x{DefaultHeight}.",
                        value.Width,
                        value.Height,
                        DefaultWindowSize.Width,
                        DefaultWindowSize.Height
                    );
                    _windowSize = DefaultWindowSize;
                }
                _logger.LogInformation(
                    "Window size changed: {Width}x{Height}",
                    value.Width,
                    value.Height
                );
                TileCountX = Math.Max(
                    (WindowSize.Width + (int)FPLightConfig.TileSize - 1)
                        / (int)FPLightConfig.TileSize,
                    1
                );
                TileCountY = Math.Max(
                    (WindowSize.Height + (int)FPLightConfig.TileSize - 1)
                        / (int)FPLightConfig.TileSize,
                    1
                );
            }
        }
        get => _windowSize;
    }

    public int TileCountX { private set; get; } = 1;

    public int TileCountY { private set; get; } = 1;

    public float DpiScale { set; get; } = 1;
    /// <summary>
    /// Gets the current camera parameters applied to the view.
    /// </summary>
    public CameraParams CameraParams = CameraParams.Identity;
    /// <summary>
    /// Gets the pointer ring used to provide recent pointer as ray in world space in shader.
    /// </summary>
    public PointerRing PointerRing = new()
    { Enabled = 0, Color = Color.Yellow.ToColor3(), ColorMix = 0.8f, OuterDistThreshold = 1f, InnerDistThreshold = 0.8f };
    /// <summary>
    /// Current mouse pointer position in screen space (pixels), with (0,0) at the top-left corner of the window.
    /// </summary>
    public Vector2 Pointer { private set; get; }

    public bool UseExternalPipeline { get; private set; } = false;

    public TextureHandle FinalOutputTexture { get; set; } = TextureHandle.Null;

    public UseExternalPipelineScope EnableExternalPipelineScoped() => new(this);

    public RenderStatistics Statistics { get; } = new();

    public override string Name => nameof(RenderContext);

    /// <summary>
    /// Gets the elapsed time, in milliseconds, since the application started.
    /// </summary>
    /// <remarks>The value is calculated based on the system's high-resolution performance counter.</remarks>
    public ulong TimeMs => Time.GetMonoTimeMs();

    /// <summary>
    /// Gets the current Intermidiate Color Buffer texture handle, which is expected to be in R16G16B16A16_Float format.
    /// This texture is used for rendering the scene with high dynamic range (HDR) color precision.
    /// </summary>
    public TextureHandle TextureColorF16Current => ResourceSet?.Textures[SystemBufferNames.TextureColorF16Current] ?? TextureHandle.Null;

    /// <summary>
    /// Initiates the rendering process for a new frame.
    /// </summary>
    /// <remarks>This method updates the rendering data and logs a warning if the update fails. Ensure that
    /// the <c>Data</c> object is properly initialized before calling this method.</remarks>
    public void BeginFrame()
    {
        Statistics.BeginFrame();
        if (Data?.Update() == false)
        {
            _logger.LogWarning("Failed to update render data.");
            return;
        }
    }

    /// <summary>
    /// Marks the end of the current frame and updates the frame statistics.
    /// </summary>
    /// <remarks>This method should be called at the end of each frame to ensure that frame statistics are
    /// correctly updated. It is typically used in rendering loops or similar iterative processes.</remarks>
    public void EndFrame()
    {
        Statistics.EndFrame();
    }

    /// <summary>
    /// Updates the window size and recalculates camera parameters based on the specified provider.
    /// </summary>
    /// <remarks>Call this method when the window size changes to ensure camera parameters remain consistent
    /// with the display.</remarks>
    /// <param name="windowSize">The new size of the window, used to determine the aspect ratio for camera parameter calculation.</param>
    /// <param name="cameraParamsProvider">An object that provides camera parameters based on the current aspect ratio. Cannot be null.</param>
    public void Update(Size windowSize, ICameraParamsProvider cameraParamsProvider)
    {
        WindowSize = windowSize;
        CameraParams = cameraParamsProvider.ToCameraParams(
            (float)WindowSize.Width / WindowSize.Height
        );
    }

    /// <summary>
    /// Recalculates camera parameters based on the specified provider.
    /// </summary>
    /// <param name="cameraParamsProvider">An object that provides camera parameters based on the current aspect ratio. Cannot be null.</param>
    public void Update(ICameraParamsProvider cameraParamsProvider)
    {
        Update(WindowSize, cameraParamsProvider);
    }

    /// <summary>
    /// Set current mouse pointer position and calculate the corresponding picking ray in world space.
    /// Must be called after <see cref="Update"/> to ensure the camera parameters are up to date for correct ray calculation.
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    public void SetPointer(float x, float y)
    {
        Pointer = new Vector2(x, y);
        if (Pointer.X < 0 || Pointer.Y < 0 || Pointer.X > WindowSize.Width || Pointer.Y > WindowSize.Height)
        {
            PointerRing.RayDirection = Vector3.Zero;
            PointerRing.RayOrigin = Vector3.Zero;
            return;
        }
        if (this.TryUnProject(x, y, out var ray))
        {
            PointerRing.RayDirection = ray.Direction;
            PointerRing.RayOrigin = ray.Position;
        }
    }

    /// <summary>
    /// Set current mouse pointer position and calculate the corresponding picking ray in world space.
    /// Must be called after <see cref="Update"/> to ensure the camera parameters are up to date for correct ray calculation.
    /// </summary>
    /// <param name="pos"></param>
    public void SetPointer(Vector2 pos)
    {
        SetPointer(pos.X, pos.Y);
    }

    protected override ResultCode OnInitializing()
    {
        return ResultCode.Ok;
    }

    protected override ResultCode OnTearingDown()
    {
        ResourceSet.Dispose();
        return ResultCode.Ok;
    }

    public void SwapIntermediateBuffers()
    {
        var temp = ResourceSet.Textures[SystemBufferNames.TextureColorF16Current];
        if (temp == TextureHandle.Null)
        {
            _logger.LogWarning("Current intermediate buffer is null. Cannot swap buffers.");
            return;
        }
        if (temp == ResourceSet.Textures[SystemBufferNames.TextureColorF16A])
        {
            ResourceSet.Textures[SystemBufferNames.TextureColorF16Current] = ResourceSet.Textures[SystemBufferNames.TextureColorF16B];
        }
        else
        {
            ResourceSet.Textures[SystemBufferNames.TextureColorF16Current] = ResourceSet.Textures[SystemBufferNames.TextureColorF16A];
        }
    }
}
