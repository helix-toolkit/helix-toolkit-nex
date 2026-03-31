using HelixToolkit.Nex.Engine.Cameras;

namespace HelixToolkit.Nex.Engine.CameraControllers;

/// <summary>
/// A turntable (trackball-style) camera controller that constrains rotation around a
/// fixed world-up axis. Commonly used in CAD and 3D modelling applications.
/// Unlike <see cref="OrbitCameraController"/>, this controller uses screen-space delta
/// directly for azimuth/elevation so it feels natural on a turntable.
/// </summary>
public class TurntableCameraController : ICameraController
{
    private float _lastRotateX;
    private float _lastRotateY;
    private float _lastPanX;
    private float _lastPanY;

    private float _theta; // Azimuth in radians (around Up axis)
    private float _phi; // Elevation in radians (angle from horizontal plane)
    private float _radius; // Distance from target

    private readonly float _initialTheta;
    private readonly float _initialPhi;
    private readonly float _initialRadius;
    private readonly Vector3 _initialTarget;

    // Smooth damping state
    private float _thetaVelocity;
    private float _phiVelocity;

    /// <summary>
    /// Gets the camera being controlled.
    /// </summary>
    public Camera Camera { get; }

    /// <summary>
    /// Gets or sets the rotation sensitivity multiplier.
    /// </summary>
    public float RotationSensitivity { get; set; } = 0.005f;

    /// <summary>
    /// Gets or sets the pan sensitivity multiplier.
    /// </summary>
    public float PanSensitivity { get; set; } = 0.01f;

    /// <summary>
    /// Gets or sets the zoom sensitivity multiplier.
    /// </summary>
    public float ZoomSensitivity { get; set; } = 0.1f;

    /// <summary>
    /// Gets or sets the minimum elevation angle in radians.
    /// Defaults to -85° to prevent flipping.
    /// </summary>
    public float MinElevation { get; set; } = -MathUtil.DegreesToRadians(85f);

    /// <summary>
    /// Gets or sets the maximum elevation angle in radians.
    /// Defaults to +85° to prevent flipping.
    /// </summary>
    public float MaxElevation { get; set; } = MathUtil.DegreesToRadians(85f);

    /// <summary>
    /// Gets or sets the minimum zoom distance.
    /// </summary>
    public float MinRadius { get; set; } = 0.5f;

    /// <summary>
    /// Gets or sets the maximum zoom distance.
    /// </summary>
    public float MaxRadius { get; set; } = 10000f;

    /// <summary>
    /// Gets or sets the inertia damping factor (0 = no inertia, 1 = infinite).
    /// A value of 0.9 gives a smooth deceleration effect.
    /// </summary>
    public float InertiaDamping { get; set; } = 0f;

    /// <summary>
    /// Gets or sets whether to invert the vertical rotation direction.
    /// </summary>
    public bool InvertY { get; set; } = true;

    /// <summary>
    /// Initializes a new <see cref="TurntableCameraController"/> from the current camera state.
    /// The initial orbit parameters are derived from the camera's position and target.
    /// </summary>
    /// <param name="camera">The camera to control.</param>
    public TurntableCameraController(Camera camera)
    {
        Camera = camera ?? throw new ArgumentNullException(nameof(camera));

        var offset = camera.Position - camera.Target;
        _radius = offset.Length();
        if (_radius < MathUtil.ZeroTolerance)
        {
            _radius = 1f;
            offset = -Vector3.UnitZ;
        }

        var normalized = offset / _radius;
        // Elevation: angle from horizontal plane (positive = above)
        _phi = MathF.Asin(MathUtil.Clamp(normalized.Y, -1f, 1f));
        // Azimuth: angle in XZ plane
        _theta = MathF.Atan2(normalized.X, normalized.Z);

        _initialTheta = _theta;
        _initialPhi = _phi;
        _initialRadius = _radius;
        _initialTarget = camera.Target;

        UpdateCameraPosition();
    }

    /// <inheritdoc />
    public void OnRotateBegin(float x, float y)
    {
        _lastRotateX = x;
        _lastRotateY = y;
        _thetaVelocity = 0;
        _phiVelocity = 0;
    }

    /// <inheritdoc />
    public void OnRotateDelta(float x, float y)
    {
        float dx = x - _lastRotateX;
        float dy = y - _lastRotateY;

        float dTheta = -dx * RotationSensitivity;
        float dPhi = (InvertY ? -1f : 1f) * dy * RotationSensitivity;

        _theta += dTheta;
        _phi += dPhi;
        _phi = MathUtil.Clamp(_phi, MinElevation, MaxElevation);

        _thetaVelocity = dTheta;
        _phiVelocity = dPhi;

        _lastRotateX = x;
        _lastRotateY = y;

        UpdateCameraPosition();
    }

    /// <inheritdoc />
    public void OnPanBegin(float x, float y)
    {
        _lastPanX = x;
        _lastPanY = y;
    }

    /// <inheritdoc />
    public void OnPanDelta(float x, float y)
    {
        float dx = x - _lastPanX;
        float dy = y - _lastPanY;

        // Compute the camera's local right and up vectors directly from spherical coordinates.
        GetCameraRightUp(out var right, out var up);

        float panScale = PanSensitivity * _radius;
        var panOffset = -right * dx * panScale + up * dy * panScale;
        Camera.Target += panOffset;

        _lastPanX = x;
        _lastPanY = y;

        UpdateCameraPosition();
    }

    /// <inheritdoc />
    public void OnZoomDelta(float delta)
    {
        // Additive zoom scaled by current radius for consistent feel at all distances.
        _radius -= delta * ZoomSensitivity * _radius;
        _radius = MathUtil.Clamp(_radius, MinRadius, MaxRadius);

        UpdateCameraPosition();
    }

    /// <inheritdoc />
    public void Update(float deltaTime)
    {
        if (
            InertiaDamping > 0
            && (
                MathF.Abs(_thetaVelocity) > MathUtil.ZeroTolerance
                || MathF.Abs(_phiVelocity) > MathUtil.ZeroTolerance
            )
        )
        {
            _theta += _thetaVelocity;
            _phi += _phiVelocity;
            _phi = MathUtil.Clamp(_phi, MinElevation, MaxElevation);

            _thetaVelocity *= InertiaDamping;
            _phiVelocity *= InertiaDamping;

            UpdateCameraPosition();
        }
    }

    /// <inheritdoc />
    public void Reset()
    {
        _theta = _initialTheta;
        _phi = _initialPhi;
        _radius = _initialRadius;
        Camera.Target = _initialTarget;
        _thetaVelocity = 0;
        _phiVelocity = 0;

        UpdateCameraPosition();
    }

    /// <summary>
    /// Focuses the camera on a specific point at a given distance.
    /// </summary>
    /// <param name="target">The world-space point to focus on.</param>
    /// <param name="distance">The desired distance from the target. If zero or negative, the current radius is kept.</param>
    public void FocusOn(Vector3 target, float distance = 0)
    {
        Camera.Target = target;
        if (distance > 0)
        {
            _radius = MathUtil.Clamp(distance, MinRadius, MaxRadius);
        }

        UpdateCameraPosition();
    }

    private void UpdateCameraPosition()
    {
        float cosPhi = MathF.Cos(_phi);
        float sinPhi = MathF.Sin(_phi);
        float sinTheta = MathF.Sin(_theta);
        float cosTheta = MathF.Cos(_theta);

        var offset = new Vector3(
            _radius * cosPhi * sinTheta,
            _radius * sinPhi,
            _radius * cosPhi * cosTheta
        );

        Camera.Position = Camera.Target + offset;
        Camera.Up = Vector3.UnitY;
    }

    /// <summary>
    /// Computes the camera's local right and up vectors from the current spherical coordinates.
    /// </summary>
    private void GetCameraRightUp(out Vector3 right, out Vector3 up)
    {
        // For elevation-based coordinates:
        //   offset direction = (cosPhi*sinTheta, sinPhi, cosPhi*cosTheta)
        //   right = d(offset)/d(theta) normalized = (cosTheta, 0, -sinTheta)
        //   up = cross(offset_dir, right)
        float cosPhi = MathF.Cos(_phi);
        float sinPhi = MathF.Sin(_phi);
        float sinTheta = MathF.Sin(_theta);
        float cosTheta = MathF.Cos(_theta);

        right = new Vector3(cosTheta, 0, -sinTheta);
        var offsetDir = new Vector3(cosPhi * sinTheta, sinPhi, cosPhi * cosTheta);
        up = Vector3.Cross(offsetDir, right);
        float upLen = up.Length();
        if (upLen > MathUtil.ZeroTolerance)
            up /= upLen;
        else
            up = Vector3.UnitY;
    }
}
