namespace HelixToolkit.Nex.Shaders;

/// <summary>
/// Generates compute shaders for Forward+ light culling.
/// </summary>
public static class ForwardPlusLightCulling
{
    public const string ShaderPath = "Compute.ForwardPlusCull.glsl";

    /// <summary>
    /// Configuration for Forward+ rendering.
    /// </summary>
    public sealed class Config
    {
        /// <summary>
        /// Size of each tile in pixels (typically 16x16 or 32x32).
        /// </summary>
        public uint TileSize;

        /// <summary>
        /// Maximum number of lights per tile.
        /// </summary>
        public uint MaxLightsPerTile;

        public static Config Default => new() { TileSize = 16, MaxLightsPerTile = 16 };
    }

    /// <summary>
    /// Generates a compute shader for tile-based light culling.
    /// </summary>
    /// <param name="config">Forward+ configuration</param>
    /// <returns>GLSL compute shader source code</returns>
    public static string GenerateComputeShader(in Config config)
    {
        var shader = GlslUtils.GetEmbeddedGlslShader(ShaderPath);
        shader = $"""

            #define TILE_SIZE {config.TileSize}
            #define MAX_LIGHTS_PER_TILE {config.MaxLightsPerTile}

            {shader}
            """;
        var builder = new ShaderBuilder(ShaderStage.Compute, new ShaderBuildOptions());
        var result = builder.Build(shader);
        if (!result.Success)
        {
            throw new InvalidOperationException(
                "Failed to build Forward+ light culling compute shader: "
                    + string.Join("\n", result.Errors)
            );
        }
        return result.Source!;
    }
}
