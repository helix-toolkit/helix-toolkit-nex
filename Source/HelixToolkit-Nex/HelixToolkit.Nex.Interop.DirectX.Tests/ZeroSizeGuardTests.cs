using HelixToolkit.Nex.Interop.DirectX;

namespace HelixToolkit.Nex.Tests.Interop;

/// <summary>
/// Unit tests verifying that SharedTextureFactory.CreateForWinUI rejects
/// zero-width or zero-height dimensions with ArgumentOutOfRangeException.
/// Validates: Requirement 8.3
/// </summary>
[TestClass]
[TestCategory("GPURequired")]
public class ZeroSizeGuardTests
{
    private D3D11DeviceManager _d3d11 = null!;

    [TestInitialize]
    public void Setup()
    {
        _d3d11 = new D3D11DeviceManager();
    }

    [TestCleanup]
    public void Cleanup()
    {
        _d3d11?.Dispose();
    }

    /// <summary>
    /// Width=0 must throw ArgumentOutOfRangeException.
    /// </summary>
    [TestMethod]
    public void CreateForWinUI_ZeroWidth_ThrowsArgumentOutOfRange()
    {
        Assert.ThrowsException<ArgumentOutOfRangeException>(() =>
            SharedTextureFactory.CreateForWinUI(_d3d11, width: 0, height: 600)
        );
    }

    /// <summary>
    /// Height=0 must throw ArgumentOutOfRangeException.
    /// </summary>
    [TestMethod]
    public void CreateForWinUI_ZeroHeight_ThrowsArgumentOutOfRange()
    {
        Assert.ThrowsException<ArgumentOutOfRangeException>(() =>
            SharedTextureFactory.CreateForWinUI(_d3d11, width: 800, height: 0)
        );
    }

    /// <summary>
    /// Both width=0 and height=0 must throw ArgumentOutOfRangeException
    /// (the width check fires first).
    /// </summary>
    [TestMethod]
    public void CreateForWinUI_BothZero_ThrowsArgumentOutOfRange()
    {
        Assert.ThrowsException<ArgumentOutOfRangeException>(() =>
            SharedTextureFactory.CreateForWinUI(_d3d11, width: 0, height: 0)
        );
    }
}
