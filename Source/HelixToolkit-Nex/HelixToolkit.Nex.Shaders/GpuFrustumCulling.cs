namespace HelixToolkit.Nex.Shaders;

public static class GpuFrustumCulling
{
    public const string ShaderPath = "Compute/FrustumCull.glsl";
    public const string CullInstancingShaderPath = "Compute/FrustumCullInstancing.glsl";
    public const uint WorkGroupSize = 64;

    public enum CullMode : uint
    {
        MultiMeshSingleInstance = 0,
        SingleMeshInstancing = 1,
    }

    public static uint GetGroupSize(uint itemCount)
    {
        return (itemCount + WorkGroupSize - 1) / WorkGroupSize;
    }

    public static string GenerateComputeShader(CullMode mode)
    {
        var path = mode == CullMode.MultiMeshSingleInstance ? ShaderPath : CullInstancingShaderPath;
        var shader = GlslUtils.GetEmbeddedGlslShader(path);
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
