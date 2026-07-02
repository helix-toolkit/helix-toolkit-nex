using HelixToolkit.Nex.Graphics;

namespace HelixToolkit.Nex.Rendering.Tests;

[TestClass]
public class GraphicsSettingsTests
{
    // Feature: multi-request-picking, Requirement 1.1: constant value
    [TestMethod]
    [TestCategory("Picking")]
    public void MaxRequestsPerFrame_HasExpectedConstantValue()
    {
        Assert.AreEqual(32u, GraphicsSettings.MaxRequestsPerFrame);
    }
}
