using HelixToolkit.Nex.Engine.Cameras;

namespace HelixToolkit.Nex.Engine.CameraControllers;

/// <summary>
/// A camera controller suited for orthographic (and perspective) cameras
/// that provides panning, zooming, and optional rotation.
/// Zooming adjusts <see cref="OrthographicCamera.Width"/> for orthographic cameras
/// or moves the camera forward/backward for perspective cameras.
/// </summary>
public class PanZoomCameraController : ICameraController
{
    private float _lastPanX;
    private float _lastPanY;
    private float _lastRotateX;
    private float _lastRotateY;
    private float _panWorldPerPixel; // World units per pixel at the pan depth

    private float _yaw;
    private float _pitch;
    private float _distance;

    private readonly float _initialYaw;
    private readonly float _initialPitch;
    private readonly float _initialDistance;
    private readonly Vector3 _initialTarget;
    private readonly float _initialOrthoWidth;

    /// <summary>
    /// Gets the camera being controlled.
    /// </summary>
    public Camera Camera { get; }

    /// <inheritdoc />
    public float ViewportWidth { get; set; } = 1;

    /// <inheritdoc />
    public float ViewportHeight { get; set; } = 1;

    /// <summary>
    /// Gets or sets the pan sensitivity multiplier.
    /// </summary>
    public float PanSensitivity { get; set; } = 0.005f;

    /// <summary>
    /// Gets or sets the zoom sensitivity multiplier.
    /// </summary>
    public float ZoomSensitivity { get; set; } = 0.1f;

    /// <summary>
    /// Gets or sets the rotation sensitivity multiplier.
    /// Only effective when <see cref="AllowRotation"/> is true.
    /// </summary>
    public float RotationSensitivity { get; set; } = 0.005f;

    /// <summary>
    /// Gets or sets whether rotation is allowed. When false, rotate inputs are ignored.
    /// Default is false for a pure pan/zoom controller.
    /// </summary>
    public bool AllowRotation { get; set; } = false;

    /// <summary>
    /// Gets or sets the minimum orthographic width for orthographic cameras.
    /// </summary>
    public float MinOrthoWidth { get; set; } = 0.1f;

    /// <summary>
    /// Gets or sets the maximum orthographic width for orthographic cameras.
    /// </summary>
    public float MaxOrthoWidth { get; set; } = 100000f;

    /// <summary>
    /// Gets or sets the minimum zoom distance for perspective cameras.
    /// </summary>
    public float MinDistance { get; set; } = 0.1f;

    /// <summary>
    /// Gets or sets the maximum zoom distance for perspective cameras.
    /// </summary>
    public float MaxDistance { get; set; } = 10000f;

    /// <summary>
    /// Initializes a new <see cref="PanZoomCameraController"/> from the current state of the given camera.
    /// </summary>
    /// <param name="camera">The camera to control.</param>
    public PanZoomCameraController(Camera camera)
    {
        Camera = camera ?? throw new ArgumentNullException(nameof(camera));

        var offset = camera.Position - camera.Target;
        _distance = offset.Length();
        if (_distance < MathUtil.ZeroTolerance)
        {
            _distance = 1f;
            offset = -Vector3.UnitZ;
        }

        var normalized = offset / _distance;
        _pitch = MathF.Asin(MathUtil.Clamp(normalized.Y, -1f, 1f));
        _yaw = MathF.Atan2(normalized.X, normalized.Z);

        _initialYaw = _yaw;
        _initialPitch = _pitch;
        _initialDistance = _distance;
        _initialTarget = camera.Target;
        _initialOrthoWidth = camera is OrthographicCamera ortho ? ortho.Width : 10f;
    }

    /// <inheritdoc />
    public void OnRotateBegin(float x, float y, Vector3? pickPosition = null)
    {
        _lastRotateX = x;
        _lastRotateY = y;
    }

    /// <inheritdoc />
    public void OnRotateDelta(float x, float y)
    {
        if (!AllowRotation)
            return;

        float dx = x - _lastRotateX;
        float dy = y - _lastRotateY;

        _yaw -= dx * RotationSensitivity;
        _pitch += dy * RotationSensitivity;
        _pitch = MathUtil.Clamp(_pitch, -MathUtil.PiOverTwo + 0.01f, MathUtil.PiOverTwo - 0.01f);

        _lastRotateX = x;
        _lastRotateY = y;

        UpdateCameraPosition();
    }

    /// <inheritdoc />
    public void OnPanBegin(float x, float y, Vector3? pickPosition = null)
    {
        _lastPanX = x;
        _lastPanY = y;

        float panDepth;
        if (pickPosition.HasValue)
        {
            panDepth = (Camera.Position - pickPosition.Value).Length();
            if (panDepth < MathUtil.ZeroTolerance)
                panDepth = _distance;
        }
        else
        {
            panDepth = _distance;
        }

        _panWorldPerPixel = ComputeWorldPerPixel(panDepth);
    }

    /// <inheritdoc />
    public void OnPanDelta(float x, float y)
    {
        float dx = x - _lastPanX;
        float dy = y - _lastPanY;

        var view = Camera.CreateView();
        var right = new Vector3(view.M11, view.M21, view.M31);
        var up = new Vector3(view.M12, view.M22, view.M32);

        var panOffset = -right * dx * _panWorldPerPixel + up * dy * _panWorldPerPixel;
        Camera.Target += panOffset;

        _lastPanX = x;
        _lastPanY = y;

        UpdateCameraPosition();
    }

    /// <inheritdoc />
    public void OnZoomDelta(float delta, Vector3? pickPosition = null)
    {
        if (Camera is OrthographicCamera ortho)
        {
            float oldWidth = ortho.Width;
            ortho.Width *= 1f - delta * ZoomSensitivity;
            ortho.Width = MathUtil.Clamp(ortho.Width, MinOrthoWidth, MaxOrthoWidth);

            // Shift the target toward the pick point proportionally to the zoom change
            if (pickPosition.HasValue && oldWidth > MathUtil.ZeroTolerance)
            {
                float zoomRatio = 1f - ortho.Width / oldWidth;
                Camera.Target += (pickPosition.Value - Camera.Target) * zoomRatio;
                UpdateCameraPosition();
            }
        }
        else
        {
            float oldDistance = _distance;
            _distance *= 1f - delta * ZoomSensitivity;
            _distance = MathUtil.Clamp(_distance, MinDistance, MaxDistance);

            if (pickPosition.HasValue && oldDistance > MathUtil.ZeroTolerance)
            {
                float zoomRatio = 1f - _distance / oldDistance;
                Camera.Target += (pickPosition.Value - Camera.Target) * zoomRatio;
            }

            UpdateCameraPosition();
        }
    }

    /// <inheritdoc />
    public void Update(float deltaTime)
    {
        // No-op; state is driven by input events.
    }

    /// <inheritdoc />
    public void Reset()
    {
        _yaw = _initialYaw;
        _pitch = _initialPitch;
        _distance = _initialDistance;
        Camera.Target = _initialTarget;

        if (Camera is OrthographicCamera ortho)
        {
            ortho.Width = _initialOrthoWidth;
        }

        UpdateCameraPosition();
    }

    private void UpdateCameraPosition()
    {
        float cosPitch = MathF.Cos(_pitch);
        float sinPitch = MathF.Sin(_pitch);
        float sinYaw = MathF.Sin(_yaw);
        float cosYaw = MathF.Cos(_yaw);

        var offset = new Vector3(
            _distance * cosPitch * sinYaw,
            _distance * sinPitch,
            _distance * cosPitch * cosYaw
        );

        Camera.Position = Camera.Target + offset;
        Camera.Up = Vector3.UnitY;
    }

    /// <summary>
    /// Computes the world-space units per screen pixel at the given depth from the camera.
    /// </summary>
    private float ComputeWorldPerPixel(float depth)
    {
        if (Camera is PerspectiveCamera persp)
        {
            return 2f * depth * MathF.Tan(persp.Fov * 0.5f) / MathF.Max(ViewportHeight, 1f);
        }
        else if (Camera is OrthographicCamera ortho)
        {
            return ortho.Width / MathF.Max(ViewportWidth, 1f);
        }

        return PanSensitivity * depth;
    }
}
