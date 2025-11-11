namespace HelixToolkit.Nex.Graphics;

/// <summary>
/// Provides global configuration settings for the graphics system.
/// </summary>
/// <remarks>
/// These settings control various aspects of graphics rendering behavior including debugging,
/// mipmapping, and hardware feature support.
/// </remarks>
public static class GraphicsSettings
{
    /// <summary>
    /// Gets or sets a value indicating whether debug mode is enabled.
    /// </summary>
    /// <value>
    /// True to enable validation layers and debug output; false otherwise.
    /// Default is true.
    /// </value>
    public static bool EnableDebug { get; set; } = true; // Enable debug mode by default

    /// <summary>
    /// Gets or sets a value indicating whether sampler mipmapping is disabled.
    /// </summary>
    /// <value>
    /// True to disable mipmaps in samplers; false to enable them.
    /// Default is false (mipmaps are enabled).
    /// </value>
    public static bool SamplerMip_Disabled { get; set; } = false; // Enable mipmaps by default

    /// <summary>
    /// Gets or sets a value indicating whether mesh shaders are supported.
    /// </summary>
    /// <value>
    /// True if mesh shader support is enabled; false otherwise.
    /// Default is false.
    /// </value>
    /// <remarks>
    /// Mesh shaders are a modern GPU feature that may not be available on all hardware.
    /// </remarks>
    public static bool SupportMeshShader { get; set; } = false;
}
