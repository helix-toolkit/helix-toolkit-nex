using HelixToolkit.Nex.Engine.Cameras;

namespace HelixToolkit.Nex.Engine.CameraControllers;

/// <summary>
/// A first-person camera controller that allows free-look movement.
/// The camera rotates in place via yaw/pitch and moves using keyboard-style input.
/// </summary>
public class FirstPersonCameraController : ICameraController
{
    private float _lastRotateX;
    private float _lastRotateY;

    private float _yaw; // Horizontal rotation in radians
    private float _pitch; // Vertical rotation in radians

    private readonly float _initialYaw;
    private readonly float _initialPitch;
    private readonly Vector3 _initialPosition;

    // Movement flags set by the consumer
    private bool _moveForward;
    private bool _moveBackward;
    private bool _moveLeft;
    private bool _moveRight;
    private bool _moveUp;
    private bool _moveDown;

    /// <summary>
    /// Gets the camera being controlled.
    /// </summary>
    public Camera Camera { get; }

    /// <inheritdoc />
    public float ViewportWidth { get; set; } = 1;

    /// <inheritdoc />
    public float ViewportHeight { get; set; } = 1;

    /// <summary>
    /// Gets or sets the look sensitivity multiplier.
    /// Higher values mean faster rotation per pixel of mouse movement.
    /// </summary>
    public float LookSensitivity { get; set; } = 0.003f;

    /// <summary>
    /// Gets or sets the movement speed in world units per second.
    /// </summary>
    public float MoveSpeed { get; set; } = 5f;

    /// <summary>
    /// Gets or sets the sprint speed multiplier applied when sprinting is active.
    /// </summary>
    public float SprintMultiplier { get; set; } = 3f;

    /// <summary>
    /// Gets or sets whether sprinting is currently active.
    /// </summary>
    public bool IsSprinting { get; set; }

    /// <summary>
    /// Gets or sets the minimum pitch angle in radians (looking down limit).
    /// Defaults to -89° to prevent gimbal lock.
    /// </summary>
    public float MinPitch { get; set; } = -MathUtil.DegreesToRadians(89f);

    /// <summary>
    /// Gets or sets the maximum pitch angle in radians (looking up limit).
    /// Defaults to +89° to prevent gimbal lock.
    /// </summary>
    public float MaxPitch { get; set; } = MathUtil.DegreesToRadians(89f);

    /// <summary>
    /// Gets or sets whether to invert the vertical look direction.
    /// </summary>
    public bool InvertY { get; set; } = true;

    /// <summary>
    /// Initializes a new <see cref="FirstPersonCameraController"/> from the current state of the given camera.
    /// The initial yaw and pitch are derived from the camera's look direction
    /// (from <see cref="Camera.Position"/> toward <see cref="Camera.Target"/>).
    /// </summary>
    /// <param name="camera">The camera to control. Must not be null.</param>
    public FirstPersonCameraController(Camera camera)
    {
        Camera = camera ?? throw new ArgumentNullException(nameof(camera));

        var forward = Vector3.Normalize(camera.Target - camera.Position);
        _yaw = MathF.Atan2(forward.X, forward.Z);
        _pitch = MathF.Asin(MathUtil.Clamp(forward.Y, -1f, 1f));

        _initialYaw = _yaw;
        _initialPitch = _pitch;
        _initialPosition = camera.Position;

        // UpdateCameraTarget();
    }

    /// <summary>
    /// Sets the movement flags for the current frame. Call this each frame with the
    /// current keyboard state.
    /// </summary>
    /// <param name="forward">True if the forward key is pressed.</param>
    /// <param name="backward">True if the backward key is pressed.</param>
    /// <param name="left">True if the left strafe key is pressed.</param>
    /// <param name="right">True if the right strafe key is pressed.</param>
    /// <param name="up">True if the ascend key is pressed.</param>
    /// <param name="down">True if the descend key is pressed.</param>
    public void SetMovementInput(
        bool forward = false,
        bool backward = false,
        bool left = false,
        bool right = false,
        bool up = false,
        bool down = false
    )
    {
        _moveForward = forward;
        _moveBackward = backward;
        _moveLeft = left;
        _moveRight = right;
        _moveUp = up;
        _moveDown = down;
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

        _yaw -= dx * LookSensitivity;
        _pitch += (InvertY ? 1f : -1f) * dy * LookSensitivity;

        // Clamp pitch to prevent flipping
        _pitch = MathUtil.Clamp(_pitch, MinPitch, MaxPitch);

        _lastRotateX = x;
        _lastRotateY = y;

        UpdateCameraTarget();
    }

    /// <inheritdoc />
    public void OnPanBegin(float x, float y, Vector3? pickPosition = null)
    {
        // Not applicable for first-person controller; use movement keys instead.
    }

    /// <inheritdoc />
    public void OnPanDelta(float x, float y)
    {
        // Not applicable for first-person controller; use movement keys instead.
    }

    /// <inheritdoc />
    public void OnZoomDelta(float delta, Vector3? pickPosition = null)
    {
        // In first-person mode, zoom acts as a forward/backward dolly
        var forward = GetForwardDirection();
        Camera.Position += forward * delta * MoveSpeed;
        UpdateCameraTarget();
    }

    /// <inheritdoc />
    public void Update(float deltaTime)
    {
        float speed = MoveSpeed * (IsSprinting ? SprintMultiplier : 1f) * deltaTime;

        var forward = GetForwardDirection();
        var right = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, forward));
        var up = Vector3.UnitY;

        var movement = Vector3.Zero;

        if (_moveForward)
            movement += forward;
        if (_moveBackward)
            movement -= forward;
        if (_moveRight)
            movement += right;
        if (_moveLeft)
            movement -= right;
        if (_moveUp)
            movement += up;
        if (_moveDown)
            movement -= up;

        if (movement.LengthSquared() > MathUtil.ZeroTolerance)
        {
            movement = Vector3.Normalize(movement) * speed;
            Camera.Position += movement;
            UpdateCameraTarget();
        }
    }

    /// <inheritdoc />
    public void Reset()
    {
        _yaw = _initialYaw;
        _pitch = _initialPitch;
        Camera.Position = _initialPosition;

        _moveForward = _moveBackward = _moveLeft = _moveRight = _moveUp = _moveDown = false;
        IsSprinting = false;

        UpdateCameraTarget();
    }

    /// <summary>
    /// Computes the forward direction from the current yaw and pitch angles.
    /// </summary>
    private Vector3 GetForwardDirection()
    {
        float cosPitch = MathF.Cos(_pitch);
        return new Vector3(
            MathF.Sin(_yaw) * cosPitch,
            MathF.Sin(_pitch),
            MathF.Cos(_yaw) * cosPitch
        );
    }

    /// <summary>
    /// Updates <see cref="Camera.Target"/> from the current yaw/pitch and position.
    /// </summary>
    private void UpdateCameraTarget()
    {
        var forward = GetForwardDirection();
        Camera.Target = Camera.Position + forward;
        Camera.Up = Vector3.UnitY;
    }
}
