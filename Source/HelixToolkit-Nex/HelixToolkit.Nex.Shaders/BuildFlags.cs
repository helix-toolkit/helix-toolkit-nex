namespace HelixToolkit.Nex.Shaders;

public static class BuildFlags
{
    /// <summary>
    /// Excluding mesh properties (Normal/Tangent/TexCoord) from shader generation. This can be used to reduce shader complexity and improve performance when mesh properties are not needed.
    /// Usefully when generating shaders for simple materials or effects that do not require detailed surface information, such as unlit/line/point shaders or basic color shaders.
    /// </summary>
    public const string EXCLUDE_MESH_PROPS = "EXCLUDE_MESH_PROPS";
}
