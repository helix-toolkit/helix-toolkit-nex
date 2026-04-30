namespace HelixToolkit.Nex;

public static class RenderSettings
{
    public static bool LogFPSInDebug { get; set; } = false;
    public static Format IntermediateTargetFormat = Format.RGBA_F16;
    public static Format DepthBufferFormat = Format.Z_F32;
    public const Format MeshIdTexFormat = Format.RG_F32;
    public const uint MaxFrameInFlight = 3;

    public static uint NumFrameInFlight(IContext context)
    {
        return Math.Max(1, Math.Min(context.GetNumSwapchainImages(), MaxFrameInFlight));
    }
}
