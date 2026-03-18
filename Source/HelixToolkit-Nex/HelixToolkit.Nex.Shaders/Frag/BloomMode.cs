namespace HelixToolkit.Nex.Shaders.Frag;

/// <summary>
/// Selects the active stage of the bloom shader (specialization constant 0).
/// </summary>
public enum BloomMode : uint
{
    /// <summary>
    /// Isolate pixels whose luminance exceeds the configured threshold.
    /// </summary>
    BrightnessExtract = 0,

    /// <summary>
    /// 9-tap Gaussian blur along the horizontal axis.
    /// </summary>
    BlurHorizontal = 1,

    /// <summary>
    /// 9-tap Gaussian blur along the vertical axis.
    /// </summary>
    BlurVertical = 2,

    /// <summary>
    /// Additively blend the blurred bloom texture onto the scene color.
    /// </summary>
    Composite = 3,
}
