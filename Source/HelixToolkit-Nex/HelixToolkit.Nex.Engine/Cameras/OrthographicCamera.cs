namespace HelixToolkit.Nex.Engine.Cameras;

public class OrthographicCamera : Camera
{
    /// <summary>
    /// Gets or sets the width of the orthographic viewing volume.
    /// The height is computed as <c>Width / aspectRatio</c>.
    /// </summary>
    public float Width { get; set; } = 10f;

    public override Matrix4x4 CreateProjection(float aspectRatio)
    {
        float height = Width / aspectRatio;
        return MatrixHelper.OrthoRHReverseZ(Width, height, NearPlane, FarPlane);
    }

    public override Matrix4x4 CreateInverseProjection(float aspectRatio)
    {
        float height = Width / aspectRatio;
        return MatrixHelper.InverseOrthoRHReverseZ(Width, height, NearPlane, FarPlane);
    }
}
