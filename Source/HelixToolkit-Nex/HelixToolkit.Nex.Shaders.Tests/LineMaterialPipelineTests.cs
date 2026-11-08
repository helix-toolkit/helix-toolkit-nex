using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Graphics.Vulkan;
using HelixToolkit.Nex.Material;
using HelixToolkit.Nex.Repository;

namespace HelixToolkit.Nex.Shaders.Tests;

/// <summary>
/// Compiles the line vertex shader and the line fragment uber shader (including the cap / join
/// shaping in <c>Line/LineShape.glsl</c>) and creates a render pipeline for every registered line
/// material.
/// </summary>
[TestClass]
[TestCategory("GPURequired")]
[TestCategory("ShaderBuilding")]
public sealed class LineMaterialPipelineTests
{
    [TestMethod]
    public void LineMaterialManager_CreatesPipelinesForAllRegisteredMaterials()
    {
        using var context = VulkanBuilder.CreateHeadless(
            new VulkanContextConfig { TerminateOnValidationError = true, EnableValidation = false }
        );
        using var shaderRepository = new ShaderRepository(context);
        using var manager = new LineMaterialManager(context, shaderRepository);

        int created = manager.CreatePipelinesFromRegistry();

        Assert.AreEqual(LineMaterialRegistry.GetAllRegistrations().Count, created);
        Assert.IsTrue(manager.GetPipeline(0).Valid, "Default line material pipeline should be valid");
    }
}
