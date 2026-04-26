namespace HelixToolkit.Nex.Shaders;

public static class BuildFlags
{
    /// <summary>
    /// Excluding mesh properties (Normal/Tangent/TexCoord) from shader generation. This can be used to reduce shader complexity and improve performance when mesh properties are not needed.
    /// Usefully when generating shaders for simple materials or effects that do not require detailed surface information, such as unlit/line/point shaders or basic color shaders.
    /// </summary>
    public const string EXCLUDE_MESH_PROPS = "EXCLUDE_MESH_PROPS";

    /// <summary>
    /// Used to compile vertex shaders for depth pass rendering that outputs draw index id. This can be used to implement features like object picking or selection by encoding the draw index into the shader output.
    /// </summary>
    public const string OUTPUT_DRAW_ID = "OUTPUT_DRAW_ID";

    /// <summary>
    /// Used to compile shaders for depth pre-pass rendering.
    /// This can be used to optimize rendering by performing a depth-only pass before the main rendering pass, allowing for early depth testing and reducing overdraw.
    /// </summary>
    public const string DEPTH_PREPASS = "DEPTH_PREPASS";
}
