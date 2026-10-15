namespace HelixToolkit.Nex.Rendering;

/// <summary>
/// Per-<see cref="RenderContext"/> configuration for environment-map (skybox) rendering.
/// <para>
/// The environment is drawn by <see cref="RenderNodes.EnvironmentMapNode"/> as a
/// geometry-free full-screen background that samples an HDR cubemap along the
/// reconstructed world-space view ray. It fills only the pixels left uncovered by
/// opaque geometry (reversed-Z depth test, no depth writes) and writes linear HDR
/// radiance into the scene colour target so it is tone-mapped together with lit
/// geometry.
/// </para>
/// <para>
/// Assign a cubemap <see cref="Texture"/> (e.g. a loaded <c>.dds</c> cubemap) to
/// enable the background. All fields can be changed at any time between frames.
/// </para>
/// </summary>
public sealed class EnvironmentMapConfig
{
    /// <summary>
    /// Master switch for environment-map rendering. When <see langword="false"/> the
    /// <see cref="RenderNodes.EnvironmentMapNode"/> is skipped entirely. Default: <see langword="true"/>.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// The environment cubemap to render. Must be a <c>TextureCube</c> texture; ideally an
    /// HDR format (e.g. <see cref="Format.RGBA_F16"/>) with mipmaps for the <see cref="Blur"/>
    /// option to take effect. When this is <see cref="TextureRef.Null"/> or invalid, the
    /// background is not drawn. Default: <see cref="TextureRef.Null"/>.
    /// </summary>
    public TextureRef Texture { get; set; } = TextureRef.Null;

    /// <summary>
    /// Optional sampler used to read <see cref="Texture"/>. When this is
    /// <see cref="SamplerRef.Null"/> or invalid, <see cref="RenderNodes.EnvironmentMapNode"/>
    /// falls back to a linear-clamp sampler with mipmapping. Default: <see cref="SamplerRef.Null"/>.
    /// </summary>
    public SamplerRef Sampler { get; set; } = SamplerRef.Null;

    /// <summary>
    /// Linear radiance multiplier applied to the sampled environment colour. Values above 1
    /// brighten the background (and any IBL derived from it); below 1 darken it. Default: 1.
    /// </summary>
    public float Intensity { get; set; } = 1f;

    /// <summary>
    /// Yaw rotation of the environment about the world Y axis, in radians. Lets the
    /// environment be re-oriented without re-baking the cubemap. Default: 0.
    /// </summary>
    public float RotationY { get; set; } = 0f;

    /// <summary>
    /// When <see langword="true"/>, the PBR shader samples this cubemap for image-based
    /// reflections/irradiance on lit geometry. Only takes effect while a valid
    /// <see cref="Texture"/> is assigned (see <see cref="ShouldRenderCubeMap"/>).
    /// Independent of <see cref="Enabled"/>, which controls only the skybox background.
    /// Default: <see langword="true"/>.
    /// </summary>
    public bool RenderCubeMap { get; set; } = true;

    /// <summary>
    /// Explicit cubemap mip level to sample, providing a cheap pre-blurred background.
    /// 0 samples the sharpest mip; larger values yield a softer, defocused backdrop. The
    /// value is clamped to the cubemap's available mip range. Requires a mipmapped cubemap.
    /// Default: 0.
    /// </summary>
    public float Blur { get; set; } = 0f;

    /// <summary>
    /// Gets a value indicating whether a valid environment cubemap is currently assigned.
    /// </summary>
    public bool HasValidTexture => Texture is { Valid: true };

    /// <summary>
    /// Gets a value indicating whether the environment map should be rendered this frame
    /// (enabled and backed by a valid cubemap texture).
    /// </summary>
    public bool ShouldRender => Enabled && HasValidTexture;

    /// <summary>
    /// Gets a value indicating whether the PBR shader should sample this cubemap for
    /// image-based lighting this frame (<see cref="RenderCubeMap"/> enabled and backed by
    /// a valid cubemap texture).
    /// </summary>
    public bool ShouldRenderCubeMap => RenderCubeMap && HasValidTexture;
}
