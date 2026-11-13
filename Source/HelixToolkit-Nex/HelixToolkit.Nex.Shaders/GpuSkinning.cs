namespace HelixToolkit.Nex.Shaders;

/// <summary>
/// Compute shaders used for GPU vertex deformation: bone skinning and morph targets.
/// </summary>
public static class GpuSkinning
{
    public const string SkinPaletteShaderPath = "Compute/SkinPalette.glsl";
    public const string SkinVerticesShaderPath = "Compute/SkinVertices.glsl";

    public const uint WorkGroupSize = 64;

    /// <summary>
    /// <see cref="SkinVertexPC.Flags"/> bit: the geometry has generated flat normals with the
    /// vertices of each triangle stored consecutively; the face normals are recomputed from the
    /// morphed triangles. Matches <c>SKIN_VERTEX_FLAG_FLAT_NORMALS</c>.
    /// </summary>
    public const uint FlagFlatNormals = 1;

    /// <summary>
    /// <see cref="SkinVertexPC.Flags"/> bit: the geometry has generated (not authored) tangents,
    /// which are kept consistent with the morphed surface. Matches
    /// <c>SKIN_VERTEX_FLAG_GENERATED_TANGENTS</c>.
    /// </summary>
    public const uint FlagGeneratedTangents = 2;

    /// <summary>
    /// <see cref="SkinVertexPC.Flags"/> bit (with <see cref="FlagGeneratedTangents"/>): the
    /// generated tangents come from the second texture coordinate set. Matches
    /// <c>SKIN_VERTEX_FLAG_GENERATED_TANGENTS_UV1</c>.
    /// </summary>
    public const uint FlagGeneratedTangentsUv1 = 4;

    public enum Pass : uint
    {
        /// <summary>
        /// Computes the skinning palette: one thread per joint.
        /// </summary>
        Palette = 0,

        /// <summary>
        /// Applies the morph targets, then skins the vertices with the palette: one thread per vertex.
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
