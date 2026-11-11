using System.Runtime.CompilerServices;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Graphics.Vulkan;

namespace HelixToolkit.Nex.Shaders.Tests;

/// <summary>
/// Compiles the morph displacement visualization vertex shader used by <c>MorphPostEffect</c>.
/// </summary>
[TestClass]
[TestCategory("GPURequired")]
[TestCategory("Integration")]
public class MorphLineShaderCompileTests
{
    private static IContext? _context;

    [ClassInitialize]
    public static void ClassInit(TestContext context)
    {
        var config = new VulkanContextConfig
        {
            TerminateOnValidationError = true,
            EnableValidation = false,
        };
        _context = VulkanBuilder.CreateHeadless(config);
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        _context?.Dispose();
    }

    [TestMethod]
    public void MorphLineVertexShader_Compiles()
    {
        var result = new ShaderCompiler().CompileVertexShader(
            GlslUtils.GetEmbeddedGlslShader("Vert/vsMorphLine.glsl")
        );
        Assert.IsTrue(result.Success, string.Join("\n", result.Errors));
        Assert.IsNotNull(result.Source);

        using var module = _context!.CreateShaderModuleGlsl(
            result.Source,
            ShaderStage.Vertex,
            "MorphLine_Vertex"
        );
        Assert.IsTrue(module.Valid, "Shader module should be valid");
    }

    /// <summary>
    /// The generated structs must match the std430 layout of <c>vsMorphLine.glsl</c>.
    /// </summary>
    [TestMethod]
    public void MorphLineStructs_MatchGlslLayout()
    {
        Assert.AreEqual(128, Unsafe.SizeOf<MorphLineMesh>());
        Assert.AreEqual(16, Unsafe.SizeOf<MorphLinePushConstant>());
    }
}
