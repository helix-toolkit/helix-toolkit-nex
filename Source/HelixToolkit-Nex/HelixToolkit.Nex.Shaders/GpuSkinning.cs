namespace HelixToolkit.Nex.Shaders;

/// <summary>
/// Compute shaders used for GPU bone skinning.
/// </summary>
public static class GpuSkinning
{
    public const string SkinPaletteShaderPath = "Compute/SkinPalette.glsl";
    public const string SkinVerticesShaderPath = "Compute/SkinVertices.glsl";

    public const uint WorkGroupSize = 64;

    public enum Pass : uint
    {
        /// <summary>
        /// Computes the skinning palette: one thread per joint.
        /// </summary>
        Palette = 0,

        /// <summary>
        /// Skins the vertices with the palette: one thread per vertex.
        /// </summary>
        Vertices,
    }

    public static uint GetGroupSize(uint itemCount)
    {
        return (itemCount + WorkGroupSize - 1) / WorkGroupSize;
    }

    public static string GenerateComputeShader(Pass pass)
    {
        var path = pass switch
        {
            Pass.Palette => SkinPaletteShaderPath,
            Pass.Vertices => SkinVerticesShaderPath,
            _ => throw new NotImplementedException(),
        };

        var shader = GlslUtils.GetEmbeddedGlslShader(path);
        var builder = new ShaderBuilder(ShaderStage.Compute, new ShaderBuildOptions());
        var result = builder.Build(shader);
        if (!result.Success)
        {
            throw new InvalidOperationException(
                "Failed to build skinning compute shader: " + string.Join("\n", result.Errors)
            );
        }
        return result.Source!;
    }
}
