using System.Numerics;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Graphics.Vulkan;

namespace HelixToolkit.Nex.Geometries.Tests;

[TestClass]
[TestCategory("GPURequired")]
public sealed class GeometryBufferCreation
{
    private static IContext? _vkContext;

    [ClassInitialize]
    public static void ClassInit(TestContext context)
    {
        var config = new VulkanContextConfig { TerminateOnValidationError = true };
        _vkContext = VulkanBuilder.CreateHeadless(config);
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        _vkContext?.Dispose();
    }

    [TestMethod]
    public void TestVertexBufferUpload()
    {
        using var geometry = new Geometry
        {
            Vertices = [.. Enumerable.Repeat(new Vertex(new Vector3(1, 2, 3)), 1024)],
        };
        var result = geometry.UpdateBuffers(_vkContext!, GeometryBufferType.Vertex);
        Assert.AreEqual(
            ResultCode.Ok,
            result,
            "Vertex buffer creation failed with error: " + result.ToString()
        );
    }

    [TestMethod]
    public void TestIndexBufferUpload()
    {
        using var geometry = new Geometry
        {
            Indices = [.. Enumerable.Range(0, 1024).Select(i => (uint)i)],
        };
        var result = geometry.UpdateBuffers(_vkContext!, GeometryBufferType.Index);
        Assert.AreEqual(
            ResultCode.Ok,
            result,
            "Index buffer creation failed with error: " + result.ToString()
        );
    }

    [TestMethod]
    public void TestBiNormalBufferUpload()
    {
        using var geometry = new Geometry
        {
            Vertices = [.. Enumerable.Repeat(new Vertex(new Vector3(1, 2, 3)), 1024)],
        };
        geometry.BiNormals =
        [
            .. Enumerable.Repeat(new BiNormal(new Vector3(1, 0, 0), new Vector3(0, 1, 0)), 1024),
        ];
        var result = geometry.UpdateBuffers(_vkContext!, GeometryBufferType.BiNormal);
        Assert.AreEqual(
            ResultCode.Ok,
            result,
            "BiNormal buffer creation failed with error: " + result.ToString()
        );
    }
}
