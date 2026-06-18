namespace HelixToolkit.Nex.glTF.Internal;

/// <summary>
/// Identifies the kind of punctual light described by a <c>KHR_lights_punctual</c>
/// light definition. Maps directly to the glTF <c>type</c> string values
/// (<c>"directional"</c>, <c>"point"</c>, <c>"spot"</c>).
/// </summary>
internal enum LightKind
{
    /// <summary>
    /// A directional light that emits along its local -Z axis with parallel rays
    /// (e.g. sunlight). Intensity is expressed in lux (lm/m^2).
    /// </summary>
    Directional,

    /// <summary>
    /// A point light that emits omnidirectionally from its position.
    /// Intensity is expressed in candela (lm/sr).
    /// </summary>
    Point,

    /// <summary>
    /// A spot light that emits within a cone along its local -Z axis.
    /// Intensity is expressed in candela (lm/sr).
    /// </summary>
    Spot,
}
