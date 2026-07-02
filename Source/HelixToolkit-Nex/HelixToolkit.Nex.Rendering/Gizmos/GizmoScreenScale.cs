namespace HelixToolkit.Nex.Rendering.Gizmos;

/// <summary>
/// Computes the constant on-screen sizing factor (<em>Screen_Scale</em>) for gizmo handles.
/// <para>
/// A handle's geometry is authored in gizmo-local space; the value returned by
/// <see cref="ScreenScaleAt(Vector3, in Matrix4x4, float, float)"/> is the number of world units
/// that map to the configured desired pixel size at a gizmo origin. Multiplying the local geometry
/// by this factor (in the vertex shader) keeps the projected handle a constant pixel size
/// regardless of the camera distance.
/// </para>
/// <para>
/// This type is deliberately self-contained and free of any renderer/manager coupling so the pure
/// math can be unit- and property-tested in isolation.
/// </para>
/// </summary>
public sealed class GizmoScreenScale
{
    /// <summary>The smallest allowed desired pixel size, in pixels.</summary>
    public const float MinDesiredPixelSize = 1f;

    /// <summary>The largest allowed desired pixel size, in pixels.</summary>
    public const float MaxDesiredPixelSize = 1000f;

    /// <summary>The default desired pixel size applied when none is configured, in pixels.</summary>
    public const float DefaultDesiredPixelSize = 100f;

    /// <summary>
    /// Positive epsilon that clip-space <c>w</c> is clamped to when a gizmo origin projects to a
    /// <c>w</c> value less than or equal to zero (origin at or behind the camera).
    /// </summary>
    public const float WEpsilon = 1e-4f;

    private float _desiredPixelSize = DefaultDesiredPixelSize;

    /// <summary>
    /// Initializes a new instance using the <see cref="DefaultDesiredPixelSize"/> (100 pixels).
    /// </summary>
    public GizmoScreenScale()
    {
    }

    /// <summary>
    /// Initializes a new instance with an explicit desired pixel size.
    /// </summary>
    /// <param name="desiredPixelSize">
    /// The desired on-screen handle size, in pixels. The value is clamped to
    /// <c>[<see cref="MinDesiredPixelSize"/>, <see cref="MaxDesiredPixelSize"/>]</c>.
    /// </param>
    public GizmoScreenScale(float desiredPixelSize)
    {
        DesiredPixelSize = desiredPixelSize;
    }

    /// <summary>
    /// Gets or sets the desired on-screen handle size, in pixels. Assigned values are clamped to
    /// the range <c>[<see cref="MinDesiredPixelSize"/>, <see cref="MaxDesiredPixelSize"/>]</c>
    /// (1 to 1000 pixels).
    /// </summary>
    public float DesiredPixelSize
    {
        get => _desiredPixelSize;
        set => _desiredPixelSize = float.IsNaN(value)
            ? DefaultDesiredPixelSize
            : Math.Clamp(value, MinDesiredPixelSize, MaxDesiredPixelSize);
    }

    /// <summary>
    /// Computes the world units per desired-pixel at a gizmo origin.
    /// </summary>
    /// <param name="originWorld">The gizmo origin in world space.</param>
    /// <param name="viewProjection">The camera view-projection matrix.</param>
    /// <param name="viewportHeight">The viewport height in pixels.</param>
    /// <param name="projectionScaleY">
    /// The projection Y scale (element <c>M22</c> of the projection matrix, i.e.
    /// <c>1 / tan(verticalFov / 2)</c> for a perspective projection).
    /// </param>
    /// <returns>
    /// The world size that spans <see cref="DesiredPixelSize"/> pixels at <paramref name="originWorld"/>.
    /// </returns>
    /// <remarks>
    /// When the origin projects to a clip-space <c>w</c> of zero or less (at or behind the camera),
    /// <c>w</c> is clamped to <see cref="WEpsilon"/> so the result stays finite and positive.
    /// </remarks>
    public float ScreenScaleAt(Vector3 originWorld, in Matrix4x4 viewProjection, float viewportHeight, float projectionScaleY)
    {
        var clip = Vector4.Transform(new Vector4(originWorld, 1f), viewProjection);
        float w = MathF.Max(clip.W, WEpsilon);          // guard against w <= 0 behind camera
        float pixelsPerWorld = (viewportHeight * projectionScaleY) / w;

        // Guard against a non-positive pixels-per-world (e.g. zero viewport/projection scale) so the
        // returned world size stays finite and positive.
        if (pixelsPerWorld <= WEpsilon)
        {
            pixelsPerWorld = WEpsilon;
        }

        return _desiredPixelSize / pixelsPerWorld;       // world size for the desired pixel count
    }

    /// <summary>
    /// Computes the world units per desired-pixel at a gizmo origin using the supplied camera.
    /// </summary>
    /// <param name="originWorld">The gizmo origin in world space.</param>
    /// <param name="camera">The current camera parameters (supplies the view-projection and projection Y scale).</param>
    /// <param name="viewportHeight">The viewport height in pixels.</param>
    /// <returns>
    /// The world size that spans <see cref="DesiredPixelSize"/> pixels at <paramref name="originWorld"/>.
    /// </returns>
    public float ScreenScaleAt(Vector3 originWorld, in CameraParams camera, float viewportHeight)
        => ScreenScaleAt(originWorld, camera.ViewProjection, viewportHeight, camera.Projection.M22);
}
