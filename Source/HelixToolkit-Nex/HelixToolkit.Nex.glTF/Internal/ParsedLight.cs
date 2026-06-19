using System.Numerics;

namespace HelixToolkit.Nex.glTF.Internal;

/// <summary>
/// An immutable, engine-agnostic description of a single parsed
/// <c>KHR_lights_punctual</c> light definition.
/// </summary>
/// <remarks>
/// Decoupling parsing from engine component construction is what enables per-node
/// independence (a fresh engine component is materialized per referencing node) and
/// isolated property testing of the parsing layer.
/// </remarks>
/// <param name="Kind">The kind of light (directional, point, or spot).</param>
/// <param name="Color">
/// Linear RGB color, each channel in the inclusive range [0, 1].
/// </param>
/// <param name="Intensity">
/// Light intensity, &gt;= 0. Lux (lm/m^2) for directional lights;
/// candela (lm/sr) for point and spot lights.
/// </param>
/// <param name="Range">
/// Distance cutoff for point and spot lights, &gt; 0. When <c>range</c> is omitted,
/// point and spot lights fall back to the configured finite default
/// (<c>DefaultPointLightRange</c> / <c>DefaultSpotLightRange</c>). Directional lights
/// use <c>0</c> and ignore this value.
/// </param>
/// <param name="InnerConeAngle">
/// Spot light inner cone angle in radians. Ignored for non-spot lights.
/// </param>
/// <param name="OuterConeAngle">
/// Spot light outer cone angle in radians. Ignored for non-spot lights.
/// </param>
internal readonly record struct ParsedLight(
    LightKind Kind,
    Vector3 Color,
    float Intensity,
    float Range,
    float InnerConeAngle,
    float OuterConeAngle
)
{
    /// <summary>
    /// Default linear RGB color applied when <c>color</c> is omitted or malformed:
    /// <c>(1, 1, 1)</c>.
    /// </summary>
    public static readonly Vector3 DefaultColor = new(1.0f, 1.0f, 1.0f);

    /// <summary>
    /// Default intensity applied when <c>intensity</c> is omitted or invalid: <c>1.0</c>.
    /// </summary>
    public const float DefaultIntensity = 1.0f;

    /// <summary>
    /// Default spot inner cone angle in radians, applied when omitted or invalid: <c>0.0</c>.
    /// </summary>
    public const float DefaultInnerConeAngle = 0.0f;

    /// <summary>
    /// Default spot outer cone angle in radians, applied when omitted or invalid: <c>PI / 4</c>.
    /// </summary>
    public const float DefaultOuterConeAngle = MathF.PI / 4.0f;

    /// <summary>
    /// Maximum permitted spot outer cone angle in radians: <c>PI / 2</c>. Values above
    /// this are treated as invalid and replaced with <see cref="DefaultOuterConeAngle"/>.
    /// </summary>
    public const float MaxOuterConeAngle = MathF.PI / 2.0f;
}
