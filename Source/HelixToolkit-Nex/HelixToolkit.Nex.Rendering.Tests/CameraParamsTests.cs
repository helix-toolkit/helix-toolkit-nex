using System.Numerics;

namespace HelixToolkit.Nex.Rendering.Tests;

[TestClass]
public class CameraParamsTests
{
    [TestMethod]
    [TestCategory("RenderContext")]
    public void IsIdentity_ValueEqualInstance_ReturnsTrue()
    {
        var cameraParams = new CameraParams(
            Matrix4x4.Identity,
            Matrix4x4.Identity,
            Matrix4x4.Identity,
            Matrix4x4.Identity,
            Vector3.Zero,
            Vector3.Zero,
            Vector3.UnitY,
            0,
            0
        );

        Assert.IsTrue(cameraParams.IsIdentity);
    }
}
