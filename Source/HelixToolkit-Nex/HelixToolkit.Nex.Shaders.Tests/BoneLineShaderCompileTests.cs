using System.Runtime.CompilerServices;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Graphics.Vulkan;

namespace HelixToolkit.Nex.Shaders.Tests;

/// <summary>
/// Compiles the skeleton bone visualization vertex shader used by <c>SkeletonPostEffect</c>.
/// </summary>
[TestClass]
[TestCategory("GPURequired")]
[TestCategory("Integration")]
public class BoneLineShaderCompileTests
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
    public void BoneLineVertexShader_Compiles()
    {
        var result = new ShaderCompiler().CompileVertexShader(
            GlslUtils.GetEmbeddedGlslShader("Vert/vsBoneLine.glsl")
        );
        Assert.IsTrue(result.Success, string.Join("\n", result.Errors));
        Assert.IsNotNull(result.Source);

        using var module = _context!.CreateShaderModuleGlsl(
            result.Source,
            ShaderStage.Vertex,
            "BoneLine_Vertex"
        );
        Assert.IsTrue(module.Valid, "Shader module should be valid");
    }

    /// <summary>
    /// The generated structs must match the std430 layout of <c>vsBoneLine.glsl</c>.
    /// </summary>
    [TestMethod]
    public void BoneLineStructs_MatchGlslLayout()
    {
        Assert.AreEqual(48, Unsafe.SizeOf<BoneLine>());
        Assert.AreEqual(24, Unsafe.SizeOf<BoneLinePushConstant>());
    }
}
