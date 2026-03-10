namespace HelixToolkit.Nex.Engine.Cameras;

public sealed class PerspectiveCamera : Camera
{
    public float Fov { set; get; } = MathF.PI / 4;

    public override Matrix4x4 CreateProjection(float aspectRatio)
    {
        return MatrixHelper.PerspectiveFovRHReverseZ(Fov, aspectRatio, NearPlane, FarPlane);
    }

    public override Matrix4x4 CreateInverseProjection(float aspectRatio)
    {
        return MatrixHelper.InversePerspectiveFovRHReverseZ(Fov, aspectRatio, NearPlane, FarPlane);
    }
}
