using FsCheck;
using FsCheck.Fluent;
using HelixToolkit.Nex.Graphics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HelixToolkit.Nex.Textures.Tests;

[TestClass]
public class FormatMapperTests
{
    // -------------------------------------------------------------------------
    // Property 9: Format mapper round-trip
    // Feature: texture-loading, Property 9: For any Nex Format with a valid DXGI
    // mapping, DxgiToNex(NexToDxgi(f)) == f
    // Validates: Requirements 7.16, 7.17
    // -------------------------------------------------------------------------

    // The 15 formats that have valid mappings
    private static readonly Format[] MappedFormats =
    [
        Format.RGBA_UN8,
        Format.RGBA_SRGB8,
        Format.BGRA_UN8,
        Format.BGRA_SRGB8,
        Format.R_UN8,
        Format.R_UN16,
        Format.R_F16,
        Format.R_F32,
        Format.RG_UN8,
        Format.RG_F16,
        Format.RG_F32,
        Format.RGBA_F16,
        Format.RGBA_F32,
        Format.BC7_RGBA,
        Format.A2R10G10B10_UN,
    ];

    [TestMethod]
    public void Property9_FormatMapper_RoundTrip()
    {
        // Feature: texture-loading, Property 9: For any Nex Format with a valid DXGI mapping, DxgiToNex(NexToDxgi(f)) == f
        Prop.ForAll(
                Arb.From(Gen.Elements(MappedFormats)),
                (Format f) => FormatMapper.DxgiToNex(FormatMapper.NexToDxgi(f)) == f
            )
            .QuickCheckThrowOnFailure();
    }

    // -------------------------------------------------------------------------
    // Unit tests — Requirements 7.1–7.15: one test per explicit mapping pair
    // -------------------------------------------------------------------------

    // Req 7.1: R8G8B8A8_UNorm ↔ RGBA_UN8
    [TestMethod]
    public void DxgiToNex_R8G8B8A8_UNorm_Returns_RGBA_UN8()
    {
        Assert.AreEqual(Format.RGBA_UN8, FormatMapper.DxgiToNex(DxgiFormat.R8G8B8A8_UNorm));
    }

    [TestMethod]
    public void NexToDxgi_RGBA_UN8_Returns_R8G8B8A8_UNorm()
    {
        Assert.AreEqual(DxgiFormat.R8G8B8A8_UNorm, FormatMapper.NexToDxgi(Format.RGBA_UN8));
    }

    // Req 7.2: R8G8B8A8_UNorm_SRgb ↔ RGBA_SRGB8
    [TestMethod]
    public void DxgiToNex_R8G8B8A8_UNorm_SRgb_Returns_RGBA_SRGB8()
    {
        Assert.AreEqual(Format.RGBA_SRGB8, FormatMapper.DxgiToNex(DxgiFormat.R8G8B8A8_UNorm_SRgb));
    }

    [TestMethod]
    public void NexToDxgi_RGBA_SRGB8_Returns_R8G8B8A8_UNorm_SRgb()
    {
        Assert.AreEqual(DxgiFormat.R8G8B8A8_UNorm_SRgb, FormatMapper.NexToDxgi(Format.RGBA_SRGB8));
    }

    // Req 7.3: B8G8R8A8_UNorm ↔ BGRA_UN8
    [TestMethod]
    public void DxgiToNex_B8G8R8A8_UNorm_Returns_BGRA_UN8()
    {
        Assert.AreEqual(Format.BGRA_UN8, FormatMapper.DxgiToNex(DxgiFormat.B8G8R8A8_UNorm));
    }

    [TestMethod]
    public void NexToDxgi_BGRA_UN8_Returns_B8G8R8A8_UNorm()
    {
        Assert.AreEqual(DxgiFormat.B8G8R8A8_UNorm, FormatMapper.NexToDxgi(Format.BGRA_UN8));
    }

    // Req 7.4: B8G8R8A8_UNorm_SRgb ↔ BGRA_SRGB8
    [TestMethod]
    public void DxgiToNex_B8G8R8A8_UNorm_SRgb_Returns_BGRA_SRGB8()
    {
        Assert.AreEqual(Format.BGRA_SRGB8, FormatMapper.DxgiToNex(DxgiFormat.B8G8R8A8_UNorm_SRgb));
    }

    [TestMethod]
    public void NexToDxgi_BGRA_SRGB8_Returns_B8G8R8A8_UNorm_SRgb()
    {
        Assert.AreEqual(DxgiFormat.B8G8R8A8_UNorm_SRgb, FormatMapper.NexToDxgi(Format.BGRA_SRGB8));
    }

    // Req 7.5: R8_UNorm ↔ R_UN8
    [TestMethod]
    public void DxgiToNex_R8_UNorm_Returns_R_UN8()
    {
        Assert.AreEqual(Format.R_UN8, FormatMapper.DxgiToNex(DxgiFormat.R8_UNorm));
    }

    [TestMethod]
    public void NexToDxgi_R_UN8_Returns_R8_UNorm()
    {
        Assert.AreEqual(DxgiFormat.R8_UNorm, FormatMapper.NexToDxgi(Format.R_UN8));
    }

    // Req 7.6: R16_UNorm ↔ R_UN16
    [TestMethod]
    public void DxgiToNex_R16_UNorm_Returns_R_UN16()
    {
        Assert.AreEqual(Format.R_UN16, FormatMapper.DxgiToNex(DxgiFormat.R16_UNorm));
    }

    [TestMethod]
    public void NexToDxgi_R_UN16_Returns_R16_UNorm()
    {
        Assert.AreEqual(DxgiFormat.R16_UNorm, FormatMapper.NexToDxgi(Format.R_UN16));
    }

    // Req 7.7: R16_Float ↔ R_F16
    [TestMethod]
    public void DxgiToNex_R16_Float_Returns_R_F16()
    {
        Assert.AreEqual(Format.R_F16, FormatMapper.DxgiToNex(DxgiFormat.R16_Float));
    }

    [TestMethod]
    public void NexToDxgi_R_F16_Returns_R16_Float()
    {
        Assert.AreEqual(DxgiFormat.R16_Float, FormatMapper.NexToDxgi(Format.R_F16));
    }

    // Req 7.8: R32_Float ↔ R_F32
    [TestMethod]
    public void DxgiToNex_R32_Float_Returns_R_F32()
    {
        Assert.AreEqual(Format.R_F32, FormatMapper.DxgiToNex(DxgiFormat.R32_Float));
    }

    [TestMethod]
    public void NexToDxgi_R_F32_Returns_R32_Float()
    {
        Assert.AreEqual(DxgiFormat.R32_Float, FormatMapper.NexToDxgi(Format.R_F32));
    }

    // Req 7.9: R8G8_UNorm ↔ RG_UN8
    [TestMethod]
    public void DxgiToNex_R8G8_UNorm_Returns_RG_UN8()
    {
        Assert.AreEqual(Format.RG_UN8, FormatMapper.DxgiToNex(DxgiFormat.R8G8_UNorm));
    }

    [TestMethod]
    public void NexToDxgi_RG_UN8_Returns_R8G8_UNorm()
    {
        Assert.AreEqual(DxgiFormat.R8G8_UNorm, FormatMapper.NexToDxgi(Format.RG_UN8));
    }

    // Req 7.10: R16G16_Float ↔ RG_F16
    [TestMethod]
    public void DxgiToNex_R16G16_Float_Returns_RG_F16()
    {
        Assert.AreEqual(Format.RG_F16, FormatMapper.DxgiToNex(DxgiFormat.R16G16_Float));
    }

    [TestMethod]
    public void NexToDxgi_RG_F16_Returns_R16G16_Float()
    {
        Assert.AreEqual(DxgiFormat.R16G16_Float, FormatMapper.NexToDxgi(Format.RG_F16));
    }

    // Req 7.11: R32G32_Float ↔ RG_F32
    [TestMethod]
    public void DxgiToNex_R32G32_Float_Returns_RG_F32()
    {
        Assert.AreEqual(Format.RG_F32, FormatMapper.DxgiToNex(DxgiFormat.R32G32_Float));
    }

    [TestMethod]
    public void NexToDxgi_RG_F32_Returns_R32G32_Float()
    {
        Assert.AreEqual(DxgiFormat.R32G32_Float, FormatMapper.NexToDxgi(Format.RG_F32));
    }

    // Req 7.12: R16G16B16A16_Float ↔ RGBA_F16
    [TestMethod]
    public void DxgiToNex_R16G16B16A16_Float_Returns_RGBA_F16()
    {
        Assert.AreEqual(Format.RGBA_F16, FormatMapper.DxgiToNex(DxgiFormat.R16G16B16A16_Float));
    }

    [TestMethod]
    public void NexToDxgi_RGBA_F16_Returns_R16G16B16A16_Float()
    {
        Assert.AreEqual(DxgiFormat.R16G16B16A16_Float, FormatMapper.NexToDxgi(Format.RGBA_F16));
    }

    // Req 7.13: R32G32B32A32_Float ↔ RGBA_F32
    [TestMethod]
    public void DxgiToNex_R32G32B32A32_Float_Returns_RGBA_F32()
    {
        Assert.AreEqual(Format.RGBA_F32, FormatMapper.DxgiToNex(DxgiFormat.R32G32B32A32_Float));
    }

    [TestMethod]
    public void NexToDxgi_RGBA_F32_Returns_R32G32B32A32_Float()
    {
        Assert.AreEqual(DxgiFormat.R32G32B32A32_Float, FormatMapper.NexToDxgi(Format.RGBA_F32));
    }

    // Req 7.14: BC7_UNorm ↔ BC7_RGBA
    [TestMethod]
    public void DxgiToNex_BC7_UNorm_Returns_BC7_RGBA()
    {
        Assert.AreEqual(Format.BC7_RGBA, FormatMapper.DxgiToNex(DxgiFormat.BC7_UNorm));
    }

    [TestMethod]
    public void NexToDxgi_BC7_RGBA_Returns_BC7_UNorm()
    {
        Assert.AreEqual(DxgiFormat.BC7_UNorm, FormatMapper.NexToDxgi(Format.BC7_RGBA));
    }

    // Req 7.15: R10G10B10A2_UNorm ↔ A2R10G10B10_UN
    [TestMethod]
    public void DxgiToNex_R10G10B10A2_UNorm_Returns_A2R10G10B10_UN()
    {
        Assert.AreEqual(
            Format.A2R10G10B10_UN,
            FormatMapper.DxgiToNex(DxgiFormat.R10G10B10A2_UNorm)
        );
    }

    [TestMethod]
    public void NexToDxgi_A2R10G10B10_UN_Returns_R10G10B10A2_UNorm()
    {
        Assert.AreEqual(
            DxgiFormat.R10G10B10A2_UNorm,
            FormatMapper.NexToDxgi(Format.A2R10G10B10_UN)
        );
    }

    // -------------------------------------------------------------------------
    // Req 7.16: Unmapped DXGI formats return Format.Invalid
    // -------------------------------------------------------------------------

    [TestMethod]
    public void DxgiToNex_Unknown_Returns_Invalid()
    {
        Assert.AreEqual(Format.Invalid, FormatMapper.DxgiToNex(DxgiFormat.Unknown));
    }

    [TestMethod]
    public void DxgiToNex_BC1_UNorm_Returns_Invalid()
    {
        Assert.AreEqual(Format.Invalid, FormatMapper.DxgiToNex(DxgiFormat.BC1_UNorm));
    }

    [TestMethod]
    public void DxgiToNex_BC3_UNorm_Returns_Invalid()
    {
        Assert.AreEqual(Format.Invalid, FormatMapper.DxgiToNex(DxgiFormat.BC3_UNorm));
    }

    [TestMethod]
    public void DxgiToNex_B5G6R5_UNorm_Returns_Invalid()
    {
        Assert.AreEqual(Format.Invalid, FormatMapper.DxgiToNex(DxgiFormat.B5G6R5_UNorm));
    }

    [TestMethod]
    public void DxgiToNex_R32G32B32_Float_Returns_Invalid()
    {
        Assert.AreEqual(Format.Invalid, FormatMapper.DxgiToNex(DxgiFormat.R32G32B32_Float));
    }

    // -------------------------------------------------------------------------
    // IsSupported tests
    // -------------------------------------------------------------------------

    [TestMethod]
    public void IsSupported_R8G8B8A8_UNorm_ReturnsTrue()
    {
        Assert.IsTrue(FormatMapper.IsSupported(DxgiFormat.R8G8B8A8_UNorm));
    }

    [TestMethod]
    public void IsSupported_BC1_UNorm_ReturnsFalse()
    {
        Assert.IsFalse(FormatMapper.IsSupported(DxgiFormat.BC1_UNorm));
    }

    [TestMethod]
    public void IsSupported_Unknown_ReturnsFalse()
    {
        Assert.IsFalse(FormatMapper.IsSupported(DxgiFormat.Unknown));
    }

    // -------------------------------------------------------------------------
    // Unmapped Nex formats return DxgiFormat.Unknown
    // -------------------------------------------------------------------------

    [TestMethod]
    public void NexToDxgi_ETC2_RGB8_Returns_Unknown()
    {
        Assert.AreEqual(DxgiFormat.Unknown, FormatMapper.NexToDxgi(Format.ETC2_RGB8));
    }

    [TestMethod]
    public void NexToDxgi_Invalid_Returns_Unknown()
    {
        Assert.AreEqual(DxgiFormat.Unknown, FormatMapper.NexToDxgi(Format.Invalid));
    }

    [TestMethod]
    public void NexToDxgi_Z_F32_Returns_Unknown()
    {
        Assert.AreEqual(DxgiFormat.Unknown, FormatMapper.NexToDxgi(Format.Z_F32));
    }
}
