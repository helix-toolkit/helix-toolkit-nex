using System.Numerics;
using HelixToolkit.Nex.Shaders;

namespace HelixToolkit.Nex.Rendering.Tests;

[TestClass]
public class SkyAtmosphereConfigTests
{
    [TestMethod]
    [TestCategory("SkyAtmosphere")]
    public void ToConstants_Disabled_ReturnsDefault()
    {
        var config = new SkyAtmosphereConfig { Enabled = false };

        var constants = config.ToConstants();

        Assert.AreEqual(default, constants);
        Assert.AreEqual(0u, constants.Enabled);
    }

    [TestMethod]
    [TestCategory("SkyAtmosphere")]
    public void ToConstants_Enabled_PacksFlagsAndParameters()
    {
        var config = new SkyAtmosphereConfig
        {
            Enabled = true,
            MieEnabled = false,
            OzoneEnabled = true,
            SunFromDirectionalLight = false,
            KmPerUnit = 0.5f,
            GroundAltitude = 1.5f,
            SunIntensity = 7f,
        };

        var constants = config.ToConstants();

        Assert.AreEqual(1u, constants.Enabled);
        Assert.AreEqual(0u, constants.MieEnabled);
        Assert.AreEqual(1u, constants.OzoneEnabled);
        Assert.AreEqual(0u, constants.SunFromDirectionalLight);
        Assert.AreEqual(1u, constants.SunIntensityFromDirectionalLight);
        Assert.AreEqual(config.DirectionalLightIntensityScale, constants.DirectionalLightIntensityScale);
        Assert.AreEqual(1u, constants.SunColorFromDirectionalLight);
        Assert.AreEqual(Vector3.One, constants.SunColor);
        Assert.AreEqual(0.5f, constants.KmPerUnit);
        Assert.AreEqual(1.5f, constants.GroundAltitude);
        Assert.AreEqual(7f, constants.SunIntensity);
        Assert.AreEqual(config.RayleighBeta, constants.RayleighBeta);
        Assert.AreEqual(config.OzoneBetaAbs, constants.OzoneBetaAbs);
    }

    [TestMethod]
    [TestCategory("SkyAtmosphere")]
    public void Defaults_UseMetersAsWorldUnits()
    {
        var config = new SkyAtmosphereConfig();

        Assert.IsFalse(config.Enabled);
        Assert.AreEqual(0.001f, config.KmPerUnit);
    }

    [TestMethod]
    [TestCategory("SkyAtmosphere")]
    public void ToConstants_NormalizesSunDirection()
    {
        var config = new SkyAtmosphereConfig
        {
            Enabled = true,
            SunDirection = new Vector3(0f, 10f, 0f),
        };

        var constants = config.ToConstants();

        Assert.AreEqual(Vector3.UnitY, constants.SunDirection);
    }

    [TestMethod]
    [TestCategory("SkyAtmosphere")]
    public void ToConstants_ZeroSunDirection_FallsBackToUp()
    {
        var config = new SkyAtmosphereConfig { Enabled = true, SunDirection = Vector3.Zero };

        var constants = config.ToConstants();

        Assert.AreEqual(Vector3.UnitY, constants.SunDirection);
    }

    [TestMethod]
    [TestCategory("SkyAtmosphere")]
    public void ToConstants_ClampsMieAnisotropy()
    {
        var config = new SkyAtmosphereConfig { Enabled = true, MieG = 1.5f };

        var constants = config.ToConstants();

        Assert.IsTrue(constants.MieG < 1f);
    }

    [TestMethod]
    [TestCategory("SkyAtmosphere")]
    public void AtmosphereConstants_MatchesStd430Layout()
    {
        // 5 rows of 16 bytes (vec3 + scalar) plus 3 rows of 4 scalars.
        Assert.AreEqual(128u, AtmosphereConstants.SizeInBytes);
        Assert.AreEqual(0u, FPConstants.SizeInBytes % 16u);
    }

    [TestMethod]
    [TestCategory("SkyAtmosphere")]
    public void SkyAtmospherePushConstants_MatchesStd430Layout()
    {
        // uint64 + 2 uints, then vec3 groundColor (16-byte aligned) + float.
        Assert.AreEqual(32u, SkyAtmospherePushConstants.SizeInBytes);
    }
}
