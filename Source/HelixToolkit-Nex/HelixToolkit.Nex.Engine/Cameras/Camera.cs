namespace HelixToolkit.Nex.Engine.Cameras;

/// <summary>
/// Simple camera structure for the example.
/// Supports perspective projection with reverse-Z depth buffer.
/// </summary>
public abstract class Camera
{
    public Vector3 Position { set; get; }
    public Vector3 Target { set; get; }
    public Vector3 Up { set; get; } = Vector3.UnitY;
    public float NearPlane { set; get; } = 0.01f;
    public float FarPlane { set; get; } = 1000;

    public Vector3 LookDir => Vector3.Normalize(Target - Position);

    /// <summary>
    /// Creates a right-handed view matrix looking from Position to Target.
    /// </summary>
    public Matrix4x4 CreateView()
    {
        return MatrixHelper.LookAtRH(Position, Target, Up);
    }

    /// <summary>
    /// Creates a projection matrix based on the specified aspect ratio.
    /// </summary>
    /// <param name="aspectRatio">The ratio of width to height for the viewing area. Must be a positive value.</param>
    /// <returns>A <see cref="Matrix4x4"/> representing the projection transformation configured for the given aspect ratio.</returns>
    public abstract Matrix4x4 CreateProjection(float aspectRatio);

    /// <summary>
    /// Creates an inverse projection matrix for the specified aspect ratio.
    /// </summary>
    /// <param name="aspectRatio">The ratio of width to height for the projection. Must be a positive value.</param>
    /// <returns>A <see cref="Matrix4x4"/> representing the inverse of the projection matrix for the given aspect ratio.</returns>
    public abstract Matrix4x4 CreateInverseProjection(float aspectRatio);

    /// <summary>
    /// Calculates and returns the camera parameters optimized for the specified aspect ratio.
    /// </summary>
    /// <param name="aspectRatio">The width-to-height ratio of the viewport for which to compute camera parameters. Must be a positive value.</param>
    /// <returns>A <see cref="CameraParams"/> instance containing the calculated parameters for the given aspect ratio.</returns>
    public CameraParams ToCameraParams(float aspectRatio)
    {
        var view = CreateView();
        var proj = CreateProjection(aspectRatio);
        var invView = MatrixHelper.PsudoInvert(ref view);
        var invProj = CreateInverseProjection(aspectRatio);

        return new CameraParams(
            view,
            proj,
            invView,
            invProj,
            Position,
            Target,
            Up,
            NearPlane,
            FarPlane
        );
    }
}
