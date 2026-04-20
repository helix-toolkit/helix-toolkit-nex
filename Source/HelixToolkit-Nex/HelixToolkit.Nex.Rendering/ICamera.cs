namespace HelixToolkit.Nex.Rendering;

public interface ICameraParamsProvider
{
    CameraParams ToCameraParams(float aspectRatio);
}
