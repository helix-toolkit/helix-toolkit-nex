using HelixToolkit.Nex.Engine.Cameras;

namespace HelixToolkit.Nex.Engine.CameraControllers;

/// <summary>
/// An orbit camera controller that rotates around a target point.
/// Supports orbiting (rotate), panning, and zooming via mouse-style input.
/// The camera always looks at <see cref="Camera.Target"/>.
/// </summary>
public class OrbitCameraController : ICameraController
{
    private float _lastRotateX;
    private float _lastRotateY;
    private float _lastPanX;
    private float _lastPanY;
    private float _panWorldPerPixel; // World units per pixel at the pan depth

    private float _theta;
    private float _phi; // Polar angle in radians (from Y axis, clamped to avoid flipping)
    private float _radius; // Distance from camera to target

    private readonly float _initialTheta;
    private readonly float _initialPhi;
    private readonly float _initialRadius;
    private readonly Vector3 _initialTarget;

    /// <summary>
    /// Gets the camera being controlled.
    /// </summary>
    public Camera Camera { get; }

    /// <inheritdoc />
    public float ViewportWidth { get; set; } = 1;

    /// <inheritdoc />
    public float ViewportHeight { get; set; } = 1;

    /// <summary>
    /// Gets or sets the rotation sensitivity multiplier.
    /// Higher values mean faster rotation per pixel of mouse movement.
    /// </summary>
    public float RotationSensitivity { get; set; } = 0.005f;

    /// <summary>
    /// Gets or sets the pan sensitivity multiplier.
    /// Higher values mean faster panning per pixel of mouse movement.
    /// </summary>
    public float PanSensitivity { get; set; } = 0.01f;

    /// <summary>
    /// Gets or sets the zoom sensitivity multiplier.
    /// Higher values mean faster zooming per scroll wheel tick.
    /// </summary>
    public float ZoomSensitivity { get; set; } = 0.1f;

    /// <summary>
    /// Gets or sets the minimum polar angle (radians) to prevent the camera from going
    /// directly above the target. Defaults to 0.1 radians (~5.7°).
    /// </summary>
    public float MinPhi { get; set; } = 0.1f;

    /// <summary>
    /// Gets or sets the maximum polar angle (radians) to prevent the camera from going
    /// directly below the target. Defaults to π - 0.1 radians (~174.3°).
    /// </summary>
    public float MaxPhi { get; set; } = MathF.PI - 0.1f;

    /// <summary>
    /// Gets or sets the minimum orbit radius. Must be greater than 0.
    /// </summary>
    public float MinRadius { get; set; } = 0.5f;

    /// <summary>
    /// Gets or sets the maximum orbit radius.
    /// </summary>
    public float MaxRadius { get; set; } = 10000f;

    /// <summary>
    /// Gets or sets whether to invert the vertical rotation direction.
    /// </summary>
    public bool InvertY { get; set; } = true;

    /// <summary>
    /// Initializes a new <see cref="OrbitCameraController"/> from the current state of the given camera.
    /// The initial orbit parameters (theta, phi, radius) are derived from the camera's
    /// current <see cref="Camera.Position"/> and <see cref="Camera.Target"/>.
    /// </summary>
    /// <param name="camera">The camera to control. Must not be null.</param>
    public OrbitCameraController(Camera camera)
    {
        Camera = camera ?? throw new ArgumentNullException(nameof(camera));

        // Derive spherical coordinates from the current camera position relative to the target
        var offset = camera.Position - camera.Target;
        _radius = offset.Length();
        if (_radius < MathUtil.ZeroTolerance)
        {
            _radius = 1f;
            offset = -Vector3.UnitZ;
        }

        var normalized = offset / _radius;
        // phi: angle from positive Y axis
        _phi = MathF.Acos(MathUtil.Clamp(normalized.Y, -1f, 1f));
        // theta: angle in XZ plane from positive Z axis
        _theta = MathF.Atan2(normalized.X, normalized.Z);

        _initialTheta = _theta;
        _initialPhi = _phi;
        _initialRadius = _radius;
        _initialTarget = camera.Target;

        UpdateCameraPosition();
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
        float dx = x - _lastRotateX;
        float dy = y - _lastRotateY;

        _theta -= dx * RotationSensitivity;
        _phi += (InvertY ? -1f : 1f) * dy * RotationSensitivity;

        // Clamp phi to prevent flipping
        _phi = MathUtil.Clamp(_phi, MinPhi, MaxPhi);

        _lastRotateX = x;
        _lastRotateY = y;

        UpdateCameraPosition();
    }

    /// <inheritdoc />
    public void OnPanBegin(float x, float y, Vector3? pickPosition = null)
    {
        _lastPanX = x;
        _lastPanY = y;

        // Compute the distance from the camera to the pan plane.
        // When a pick position is provided, use the distance to the picked point
        // so panning feels anchored to the geometry under the cursor.
        float panDepth;
        if (pickPosition.HasValue)
        {
            panDepth = (Camera.Position - pickPosition.Value).Length();
            if (panDepth < MathUtil.ZeroTolerance)
                panDepth = _radius;
        }
        else
        {
            panDepth = _radius;
        }

        // Compute the exact world-per-pixel ratio at the pan depth.
        // For perspective: worldPerPixel = 2 * d * tan(fov/2) / viewportHeight
        // For orthographic: worldPerPixel = orthoWidth / viewportWidth
        _panWorldPerPixel = ComputeWorldPerPixel(panDepth);
    }

    /// <inheritdoc />
    public void OnPanDelta(float x, float y)
    {
        float dx = x - _lastPanX;
        float dy = y - _lastPanY;

        // Compute the camera's local right and up vectors directly from spherical coordinates.
        // This avoids extracting them from the view matrix and is more robust.
        GetCameraRightUp(out var right, out var up);

        var panOffset = -right * dx * _panWorldPerPixel + up * dy * _panWorldPerPixel;
        Camera.Target += panOffset;

        _lastPanX = x;
        _lastPanY = y;

        UpdateCameraPosition();
    }

    /// <inheritdoc />
    public void OnZoomDelta(float delta, Vector3? pickPosition = null)
    {
        float oldRadius = _radius;

        // Additive zoom scaled by current radius for consistent feel at all distances.
        _radius -= delta * ZoomSensitivity * _radius;
        _radius = MathUtil.Clamp(_radius, MinRadius, MaxRadius);

        // When a pick position is provided, shift the orbit target toward the picked
        // point proportionally to the zoom change. This gives "zoom toward cursor" feel.
        if (pickPosition.HasValue && oldRadius > MathUtil.ZeroTolerance)
        {
            float zoomRatio = 1f - _radius / oldRadius;
            Camera.Target += (pickPosition.Value - Camera.Target) * zoomRatio;
        }

        UpdateCameraPosition();
    }

    /// <inheritdoc />
    public void Update(float deltaTime)
    {
        // No-op for orbit controller; state is driven by input events.
        // Could be extended with inertia/smoothing here.
    }

    /// <inheritdoc />
    public void Reset()
    {
        _theta = _initialTheta;
        _phi = _initialPhi;
        _radius = _initialRadius;
        Camera.Target = _initialTarget;

        UpdateCameraPosition();
    }

    /// <summary>
    /// Re-centers the orbit on a new target point and optionally adjusts the distance.
    /// Use this to focus the camera on a specific object or point after panning has
    /// drifted the orbit center away from the scene.
    /// </summary>
    /// <param name="target">The new orbit center / look-at point.</param>
    /// <param name="distance">
    /// Optional distance from the new target. If <c>null</c>, the current radius is preserved.
    /// </param>
    public void FocusOn(Vector3 target, float? distance = null)
    {
        Camera.Target = target;
        if (distance.HasValue)
        {
            _radius = MathUtil.Clamp(distance.Value, MinRadius, MaxRadius);
        }

        UpdateCameraPosition();
    }

    /// <summary>
    /// Recomputes <see cref="Camera.Position"/> from the current spherical coordinates
    /// (theta, phi, radius) relative to <see cref="Camera.Target"/>.
    /// </summary>
    private void UpdateCameraPosition()
    {
        float sinPhi = MathF.Sin(_phi);
        float cosPhi = MathF.Cos(_phi);
        float sinTheta = MathF.Sin(_theta);
        float cosTheta = MathF.Cos(_theta);

        var offset = new Vector3(
            _radius * sinPhi * sinTheta,
            _radius * cosPhi,
            _radius * sinPhi * cosTheta
        );

        Camera.Position = Camera.Target + offset;
        GetCameraRightUp(out _, out var up);
        Camera.Up = up;
    }

    /// <summary>
    /// Computes the camera's local right and up vectors from the current spherical coordinates.
    /// </summary>
    private void GetCameraRightUp(out Vector3 right, out Vector3 up)
    {
        // The camera forward direction (from camera toward target) is -offset/radius.
        // We derive right and up from theta/phi directly:
        //   offset direction = (sinPhi*sinTheta, cosPhi, sinPhi*cosTheta)
        //   right = d(offset)/d(theta) normalized = (cosTheta, 0, -sinTheta)
        //   up = cross(offset_dir, right)
        float sinPhi = MathF.Sin(_phi);
        float cosPhi = MathF.Cos(_phi);
        float sinTheta = MathF.Sin(_theta);
        float cosTheta = MathF.Cos(_theta);

        right = new Vector3(cosTheta, 0, -sinTheta);
        // up = cross(offset_direction, right)
        var offsetDir = new Vector3(sinPhi * sinTheta, cosPhi, sinPhi * cosTheta);
        up = Vector3.Cross(offsetDir, right);
        // Normalize for safety (should already be unit length when phi is not at poles)
        float upLen = up.Length();
        if (upLen > MathUtil.ZeroTolerance)
            up /= upLen;
        else
            up = Vector3.UnitY;
    }

    /// <summary>
    /// Computes the world-space units per screen pixel at the given depth from the camera.
    /// For perspective cameras this uses the vertical FOV; for orthographic cameras this
    /// uses the orthographic width.
    /// </summary>
    private float ComputeWorldPerPixel(float depth)
    {
        if (Camera is PerspectiveCamera persp)
        {
            // worldPerPixel = 2 * depth * tan(fov/2) / viewportHeight
            return 2f * depth * MathF.Tan(persp.Fov * 0.5f) / MathF.Max(ViewportHeight, 1f);
        }
        else if (Camera is OrthographicCamera ortho)
        {
            return ortho.Width / MathF.Max(ViewportWidth, 1f);
        }

        // Fallback: use PanSensitivity as a rough scale
        return PanSensitivity * depth;
    }
}
