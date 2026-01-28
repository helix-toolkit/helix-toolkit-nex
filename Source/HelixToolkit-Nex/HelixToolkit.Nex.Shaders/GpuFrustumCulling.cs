namespace HelixToolkit.Nex.Shaders;

public static class GpuFrustumCulling
{
    public const string ShaderPath = "Compute/FrustumCull.glsl";
    public const uint WorkGroupSize = 64;

    public static uint GetGroupSize(uint itemCount)
    {
        return (itemCount + WorkGroupSize - 1) / WorkGroupSize;
    }

    public static string GenerateComputeShader()
    {
        var shader = GlslUtils.GetEmbeddedGlslShader(ShaderPath);
        var builder = new ShaderBuilder(ShaderStage.Compute, new ShaderBuildOptions());
        var result = builder.Build(shader);
        if (!result.Success)
        {
            throw new InvalidOperationException(
                "Failed to build Frustum culling compute shader: "
                    + string.Join("\n", result.Errors)
            );
        }
        return result.Source!;
    }
}
