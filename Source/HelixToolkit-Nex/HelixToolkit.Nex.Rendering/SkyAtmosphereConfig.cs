namespace HelixToolkit.Nex.Rendering;

/// <summary>
/// Per-<see cref="RenderContext"/> configuration for the dynamic sky atmosphere.
/// <para>
/// When <see cref="Enabled"/>, <see cref="RenderNodes.SkyAtmosphereNode"/> draws a raymarched,
/// physically based sky (Rayleigh, Mie and ozone single scattering over a flat ground, with world
/// +Y as up) as the scene background in place of <see cref="RenderNodes.EnvironmentMapNode"/>.
/// The PBR shading models (PBR, Flat, Unlit) also apply aerial perspective (distance haze) to
/// geometry, and directional light 0 is tinted by the sunlight transmittance when
/// <see cref="SunFromDirectionalLight"/> is set.
/// </para>
/// <para>
/// Atmosphere lengths are in kilometers. World-space distances are converted with
/// <see cref="KmPerUnit"/> (default: one world unit is one meter). All fields can be changed at
/// any time between frames.
/// </para>
/// </summary>
public sealed class SkyAtmosphereConfig
{
    /// <summary>
    /// Master switch. When <see langword="false"/> the sky is not drawn, geometry is not hazed
    /// and directional light 0 is left untinted. Default: <see langword="false"/>.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// When <see langword="true"/>, the sun direction follows directional light 0 (the reverse of
    /// its direction) and that light's colour is tinted by the atmosphere, so a low sun lights the
    /// scene warmly. Falls back to <see cref="SunDirection"/> when the scene has no directional
    /// light. Default: <see langword="true"/>.
    /// </summary>
    public bool SunFromDirectionalLight { get; set; } = true;

    /// <summary>
    /// World-space direction toward the sun, used when <see cref="SunFromDirectionalLight"/> is
    /// <see langword="false"/> or the scene has no directional light. Normalized on upload.
    /// Default: 30° above the horizon.
    /// </summary>
    public Vector3 SunDirection { get; set; } = Vector3.Normalize(new Vector3(0f, 0.5f, -0.866f));

    /// <summary>
    /// Sun radiance multiplier driving sky, sun disc and haze brightness. Ignored while
    /// <see cref="SunIntensityFromDirectionalLight"/> is set and the scene has a directional
    /// light. Default: 20.
    /// </summary>
    public float SunIntensity { get; set; } = 20f;

    /// <summary>
    /// When <see langword="true"/>, the sky's sun intensity follows directional light 0: it is the
    /// light's intensity times <see cref="DirectionalLightIntensityScale"/>, so dimming the light
    /// also darkens the sky and haze. Falls back to <see cref="SunIntensity"/> when the scene has
    /// no directional light. Default: <see langword="true"/>.
    /// </summary>
    public bool SunIntensityFromDirectionalLight { get; set; } = true;

    /// <summary>
    /// Sky sun intensity per unit of directional light intensity, used while
    /// <see cref="SunIntensityFromDirectionalLight"/> is set. The sky works in a much brighter range
    /// than typical light intensities; the default maps a light intensity of 0.8 to a sun intensity
    /// of 20. Default: 25.
    /// </summary>
    public float DirectionalLightIntensityScale { get; set; } = 25f;

    /// <summary>
    /// Linear RGB sun colour multiplying the sky, sun disc and haze radiance. Ignored while
    /// <see cref="SunColorFromDirectionalLight"/> is set and the scene has a directional light.
    /// Default: white.
    /// </summary>
    public Vector3 SunColor { get; set; } = Vector3.One;

    /// <summary>
    /// When <see langword="true"/>, the sky's sun colour follows directional light 0's colour, so a
    /// tinted light also tints the sky and haze. Falls back to <see cref="SunColor"/> when the scene
    /// has no directional light. Default: <see langword="true"/>.
    /// </summary>
    public bool SunColorFromDirectionalLight { get; set; } = true;

    /// <summary>
    /// Linear RGB tint of the ground shown below the horizon. The ground is drawn as this colour
    /// multiplied by the sky radiance at the horizon, so it brightens, darkens and warms with the
    /// time of day; white makes the ground match the horizon. Only affects the background drawn by
    /// <see cref="RenderNodes.SkyAtmosphereNode"/>. Default: (0.3, 0.28, 0.25).
    /// </summary>
    public Vector3 GroundColor { get; set; } = new(0.3f, 0.28f, 0.25f);

    /// <summary>
    /// Angle below the horizon, in radians, over which the horizon sky fades into
    /// <see cref="GroundColor"/>. Larger values give a softer, hazier horizon. Default: 5°.
    /// </summary>
    public float GroundBlendAngle { get; set; } = 5f * MathF.PI / 180f;

    /// <summary>Sun disc radius scale; 1 is the default size. Default: 1.</summary>
    public float SunDiscSize { get; set; } = 1f;

    /// <summary>
    /// World units to kilometers conversion. Default: 0.001 (one world unit is one meter).
    /// </summary>
    public float KmPerUnit { get; set; } = 0.001f;

    /// <summary>
    /// Altitude of the world origin plane (Y = 0) above the ground, in kilometers. The camera
    /// altitude is this value plus the camera's world Y converted with <see cref="KmPerUnit"/>.
    /// Default: 0.
    /// </summary>
    public float GroundAltitude { get; set; } = 0f;

    /// <summary>
    /// Multiplier applied to geometry distance for the aerial perspective. Values above 1
    /// exaggerate the haze, which helps small scenes read as large. Default: 1 (physical).
    /// </summary>
    public float AerialPerspectiveScale { get; set; } = 1f;

    /// <summary>Atmosphere thickness in kilometers. Default: 100.</summary>
    public float AtmosphereHeight { get; set; } = 100f;

    /// <summary>
    /// Rayleigh scattering coefficient per kilometer (RGB). Default: Earth (5.8e-3, 1.35e-2, 3.31e-2).
    /// </summary>
    public Vector3 RayleighBeta { get; set; } = new(5.8e-3f, 1.35e-2f, 3.31e-2f);

    /// <summary>Rayleigh density scale height in kilometers. Default: 8.</summary>
    public float RayleighScaleHeight { get; set; } = 8f;

    /// <summary>Enables Mie (aerosol) scattering. Default: <see langword="true"/>.</summary>
    public bool MieEnabled { get; set; } = true;

    /// <summary>Mie scattering coefficient per kilometer. Default: 3e-3.</summary>
    public float MieBeta { get; set; } = 3e-3f;

    /// <summary>Mie extinction coefficient per kilometer. Default: 4.4e-3.</summary>
    public float MieBetaExt { get; set; } = 4.4e-3f;

    /// <summary>Mie density scale height in kilometers. Default: 1.2.</summary>
    public float MieScaleHeight { get; set; } = 1.2f;

    /// <summary>
    /// Mie phase anisotropy in [0, 1). Higher values concentrate a brighter glow around the sun.
    /// Default: 0.8.
    /// </summary>
    public float MieG { get; set; } = 0.8f;

    /// <summary>Enables ozone absorption. Default: <see langword="true"/>.</summary>
    public bool OzoneEnabled { get; set; } = true;

    /// <summary>
    /// Ozone absorption coefficient per kilometer (RGB). Default: (6.5e-4, 1.88e-3, 8e-5).
    /// </summary>
    public Vector3 OzoneBetaAbs { get; set; } = new(6.5e-4f, 1.88e-3f, 8e-5f);

    /// <summary>
    /// When <see langword="true"/> and an environment cubemap is assigned to
    /// <see cref="RenderContext.EnvironmentMap"/>, the cubemap is drawn behind the atmosphere as a
    /// space backdrop (e.g. a star field) attenuated by the sky, so it shows through at night.
    /// Leave <see langword="false"/> when the cubemap is a daytime HDRI used only for image-based
    /// lighting. Default: <see langword="false"/>.
    /// </summary>
    public bool ShowEnvironmentMapAsSpace { get; set; } = false;

    /// <summary>
    /// Packs this configuration into the GPU-side <see cref="AtmosphereConstants"/>. Returns
    /// <see langword="default"/> (disabled) when <see cref="Enabled"/> is <see langword="false"/>.
    /// </summary>
    public AtmosphereConstants ToConstants()
    {
        if (!Enabled)
        {
            return default;
        }
        var sunDir = SunDirection.LengthSquared() > 1e-12f
            ? Vector3.Normalize(SunDirection)
            : Vector3.UnitY;
        return new AtmosphereConstants
        {
            RayleighBeta = RayleighBeta,
            RayleighScaleHeight = RayleighScaleHeight,
            OzoneBetaAbs = OzoneBetaAbs,
            MieScaleHeight = MieScaleHeight,
            SunDirection = sunDir,
            SunIntensity = SunIntensity,
            MieBeta = MieBeta,
            MieBetaExt = MieBetaExt,
            MieG = Math.Clamp(MieG, 0f, 0.999f),
            AtmosphereHeight = AtmosphereHeight,
            GroundAltitude = GroundAltitude,
            KmPerUnit = KmPerUnit,
            SunDiscSize = SunDiscSize,
            AerialPerspectiveScale = AerialPerspectiveScale,
            Enabled = 1u,
            MieEnabled = MieEnabled ? 1u : 0u,
            OzoneEnabled = OzoneEnabled ? 1u : 0u,
            SunFromDirectionalLight = SunFromDirectionalLight ? 1u : 0u,
            SunIntensityFromDirectionalLight = SunIntensityFromDirectionalLight ? 1u : 0u,
            DirectionalLightIntensityScale = DirectionalLightIntensityScale,
            SunColor = SunColor,
            SunColorFromDirectionalLight = SunColorFromDirectionalLight ? 1u : 0u,
        };
    }
}
