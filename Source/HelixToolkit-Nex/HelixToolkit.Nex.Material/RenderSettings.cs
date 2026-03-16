namespace HelixToolkit.Nex;

public static class RenderSettings
{
    public static bool LogFPSInDebug { get; set; } = false;
    public static Format IntermediateTargetFormat = Format.RGBA_F16;
    public static Format DepthBufferFormat = Format.Z_F32;
}
