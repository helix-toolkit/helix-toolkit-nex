namespace HelixToolkit.Nex.Tests.Vulkan;
[TestClass]
[TestCategory("GPURequired")]
public class Shader
{
    private static IContext? vkContext;

    [ClassInitialize]
    public static void ClassInit(TestContext context)
    {
        var config = new VulkanContextConfig
        {
            TerminateOnValidationError = true
        };
        vkContext = VulkanBuilder.CreateHeadless(config);
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        vkContext?.Dispose();
    }

    static byte[] GetGlslShaderCode(string shaderName)
    {
        var assembly = typeof(Shader).Assembly;
        var assemblyName = assembly.GetName().Name ?? throw new InvalidOperationException("Assembly name cannot be null.");
        using var stream = assembly.GetManifestResourceStream($"{assemblyName}.Shaders.{shaderName}") ?? throw new FileNotFoundException($"Shader file '{shaderName}' not found in embedded resources.");
        using var reader = new BinaryReader(stream);
        return reader.ReadBytes((int)stream.Length);
    }

    [DataTestMethod]
    [DataRow("simple_vs", ShaderStage.Vertex, "simple.glsl", "VERTEX_SHADER")]
    [DataRow("simple_fs", ShaderStage.Fragment, "simple.glsl", "FRAGMENT_SHADER")]
    [DataRow("complex_vs", ShaderStage.Vertex, "complex.glsl", "VERTEX_SHADER")]
    [DataRow("complex_fs", ShaderStage.Fragment, "complex.glsl", "FRAGMENT_SHADER")]
    public unsafe void CreateShaderModule(string shaderName, ShaderStage stage, string expectedFileName, string defines)
    {
        var shaderCode = GetGlslShaderCode(expectedFileName);
        using var pData = shaderCode.Pin(); // Pin the byte array to prevent garbage collection

        var shaderDesc = new ShaderModuleDesc
        {
            Data = (nint)pData.Pointer,
            DataSize = (size_t)shaderCode.Length,
            DataType = ShaderDataType.Glsl,
            Stage = stage,
            DebugName = shaderName,
            Defines = defines.ToShaderDefines(), // Convert the defines string to ShaderDefines
        };
        ShaderModuleResource? shaderModule = null;
        var result = vkContext?.CreateShaderModule(shaderDesc, out shaderModule).CheckResult();
        Assert.IsTrue(result == ResultCode.Ok, "Shader module creation failed with error: " + result.ToString());
        Assert.IsNotNull(shaderModule, "Shader module should not be null after creation.");
        Assert.IsTrue(shaderModule.Valid, "Shader module should be valid after creation.");
        // Clean up the shader module after the test
        vkContext?.Destroy(shaderModule);
    }
}
