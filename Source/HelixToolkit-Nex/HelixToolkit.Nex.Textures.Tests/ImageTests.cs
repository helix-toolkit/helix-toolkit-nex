using System.Runtime.InteropServices;
using FsCheck;
using FsCheck.Fluent;
using HelixToolkit.Nex.Graphics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HelixToolkit.Nex.Textures.Tests;

[TestClass]
public class ImageTests
{
    // =========================================================================
    // Property 1: Image creation preserves description
    // Feature: texture-loading, Property 1: For any valid ImageDescription,
    // Image.New(desc).Description equals the input description (with MipLevels resolved)
    // Validates: Requirements 1.1, 2.1
    // =========================================================================

    [TestMethod]
    public void Property1_ImageNew_PreservesDescription()
    {
        // Use ValidImageDescription from TestBase.DefaultArbMap.
        // The generator produces descriptions with MipLevels in [1,8] and dimensions in [1,256].
        // We clamp MipLevels to the valid maximum for the given dimensions before creating the image,
        // so the resolved description should match.
        var gen =
            from dim in Gen.Elements(TextureDimension.Texture2D, TextureDimension.TextureCube)
            from w in Gen.Choose(1, 128)
            from h in Gen.Choose(1, 128)
            from fmt in Gen.Elements(
                Format.RGBA_UN8,
                Format.R_UN8,
                Format.RG_UN8,
                Format.RGBA_F16,
                Format.R_F32
            )
            let arraySize = dim == TextureDimension.TextureCube ? 6 : 1
            let maxMips = PitchCalculator.CountMips(w, h)
            from mips in Gen.Choose(1, maxMips)
            select new ImageDescription
            {
                Dimension = dim,
                Width = w,
                Height = h,
                Depth = 1,
                ArraySize = arraySize,
                Format = fmt,
                MipLevels = mips,
            };

        Prop.ForAll(
                Arb.From(gen),
                (ImageDescription desc) =>
                {
                    using var image = Image.New(desc);
                    // MipLevels should be preserved (already valid, so no change expected)
                    return image.Description.Width == desc.Width
                        && image.Description.Height == desc.Height
                        && image.Description.Depth == desc.Depth
                        && image.Description.ArraySize == desc.ArraySize
                        && image.Description.Format == desc.Format
                        && image.Description.Dimension == desc.Dimension
                        && image.Description.MipLevels == desc.MipLevels;
                }
            )
            .QuickCheckThrowOnFailure();
    }

    [TestMethod]
    public void Property1_ImageNew_AutoMipLevels_ResolvedCorrectly()
    {
        // When MipLevels == 0 (Auto), the resolved count should equal CountMips(w, h).
        var gen =
            from w in Gen.Choose(1, 128)
            from h in Gen.Choose(1, 128)
            from fmt in Gen.Elements(Format.RGBA_UN8, Format.R_UN8, Format.RGBA_F16)
            select (w, h, fmt);

        Prop.ForAll(
                Arb.From(gen),
                ((int w, int h, Format fmt) t) =>
                {
                    var desc = new ImageDescription
                    {
                        Dimension = TextureDimension.Texture2D,
                        Width = t.w,
                        Height = t.h,
                        Depth = 1,
                        ArraySize = 1,
                        Format = t.fmt,
                        MipLevels = 0, // Auto
                    };
                    int expectedMips = PitchCalculator.CountMips(t.w, t.h);
                    using var image = Image.New(desc);
                    return image.Description.MipLevels == expectedMips;
                }
            )
            .QuickCheckThrowOnFailure();
    }

    // =========================================================================
    // Property 2: PixelBuffer access returns correct mip-level dimensions
    // Feature: texture-loading, Property 2: For any valid Image and valid
    // (arrayOrZSlice, mipLevel), GetPixelBuffer returns buffer with correct halved dimensions
    // Validates: Requirements 1.5, 1.6
    // =========================================================================

    [TestMethod]
    public void Property2_GetPixelBuffer_ReturnsCorrectMipDimensions()
    {
        var gen =
            from w in Gen.Choose(1, 64)
            from h in Gen.Choose(1, 64)
            from fmt in Gen.Elements(Format.RGBA_UN8, Format.R_UN8, Format.RGBA_F16)
            let maxMips = PitchCalculator.CountMips(w, h)
            from mips in Gen.Choose(1, maxMips)
            from mipLevel in Gen.Choose(0, mips - 1)
            select (w, h, fmt, mips, mipLevel);

        Prop.ForAll(
                Arb.From(gen),
                ((int w, int h, Format fmt, int mips, int mipLevel) t) =>
                {
                    var desc = new ImageDescription
                    {
                        Dimension = TextureDimension.Texture2D,
                        Width = t.w,
                        Height = t.h,
                        Depth = 1,
                        ArraySize = 1,
                        Format = t.fmt,
                        MipLevels = t.mips,
                    };
                    using var image = Image.New(desc);
                    var pb = image.GetPixelBuffer(0, t.mipLevel);
                    int expectedW = Math.Max(1, t.w >> t.mipLevel);
                    int expectedH = Math.Max(1, t.h >> t.mipLevel);
                    return pb.Width == expectedW && pb.Height == expectedH;
                }
            )
            .QuickCheckThrowOnFailure();
    }

    // =========================================================================
    // Property 3: Out-of-range pixel buffer access throws
    // Feature: texture-loading, Property 3: For any valid Image and any
    // out-of-range index tuple, GetPixelBuffer throws ArgumentException
    // Validates: Requirements 1.7
    // =========================================================================

    [TestMethod]
    public void Property3_GetPixelBuffer_OutOfRange_ThrowsArgumentException()
    {
        // Test out-of-range mip level
        var genMip =
            from w in Gen.Choose(1, 64)
            from h in Gen.Choose(1, 64)
            from fmt in Gen.Elements(Format.RGBA_UN8, Format.R_UN8)
            let maxMips = PitchCalculator.CountMips(w, h)
            from mips in Gen.Choose(1, maxMips)
            from badMip in Gen.Choose(mips, mips + 10) // out of range
            select (w, h, fmt, mips, badMip);

        Prop.ForAll(
                Arb.From(genMip),
                ((int w, int h, Format fmt, int mips, int badMip) t) =>
                {
                    var desc = new ImageDescription
                    {
                        Dimension = TextureDimension.Texture2D,
                        Width = t.w,
                        Height = t.h,
                        Depth = 1,
                        ArraySize = 1,
                        Format = t.fmt,
                        MipLevels = t.mips,
                    };
                    using var image = Image.New(desc);
                    try
                    {
                        _ = image.GetPixelBuffer(0, t.badMip);
                        return false; // should have thrown
                    }
                    catch (ArgumentException)
                    {
                        return true;
                    }
                }
            )
            .QuickCheckThrowOnFailure();
    }

    [TestMethod]
    public void Property3_GetPixelBuffer_OutOfRangeArraySlice_ThrowsArgumentException()
    {
        // Test out-of-range array slice
        var genArray =
            from w in Gen.Choose(1, 64)
            from h in Gen.Choose(1, 64)
            from fmt in Gen.Elements(Format.RGBA_UN8, Format.R_UN8)
            from arraySize in Gen.Choose(1, 4)
            from badSlice in Gen.Choose(arraySize, arraySize + 10) // out of range
            select (w, h, fmt, arraySize, badSlice);

        Prop.ForAll(
                Arb.From(genArray),
                ((int w, int h, Format fmt, int arraySize, int badSlice) t) =>
                {
                    var desc = new ImageDescription
                    {
                        Dimension = TextureDimension.Texture2D,
                        Width = t.w,
                        Height = t.h,
                        Depth = 1,
                        ArraySize = t.arraySize,
                        Format = t.fmt,
                        MipLevels = 1,
                    };
                    using var image = Image.New(desc);
                    try
                    {
                        _ = image.GetPixelBuffer(t.badSlice, 0);
                        return false; // should have thrown
                    }
                    catch (ArgumentException)
                    {
                        return true;
                    }
                }
            )
            .QuickCheckThrowOnFailure();
    }

    // =========================================================================
    // Task 7.6: Unit tests for Image factory methods
    // =========================================================================

    // ---- New2D ----

    [TestMethod]
    public void New2D_ProducesCorrectImageDescription()
    {
        using var image = Image.New2D(64, 32, 1, Format.RGBA_UN8);
        Assert.AreEqual(TextureDimension.Texture2D, image.Description.Dimension);
        Assert.AreEqual(64, image.Description.Width);
        Assert.AreEqual(32, image.Description.Height);
        Assert.AreEqual(1, image.Description.Depth);
        Assert.AreEqual(1, image.Description.ArraySize);
        Assert.AreEqual(Format.RGBA_UN8, image.Description.Format);
        Assert.AreEqual(1, image.Description.MipLevels);
    }

    [TestMethod]
    public void New2D_WithArraySize_ProducesCorrectArraySize()
    {
        using var image = Image.New2D(16, 16, 1, Format.RGBA_UN8, arraySize: 4);
        Assert.AreEqual(4, image.Description.ArraySize);
        Assert.AreEqual(TextureDimension.Texture2D, image.Description.Dimension);
    }

    [TestMethod]
    public void New2D_WithExplicitIntPtr_DoesNotAllocateNewBuffer()
    {
        // Allocate a buffer externally and pass it in; the image should use it directly
        int w = 4,
            h = 4;
        PitchCalculator.ComputePitch(
            Format.RGBA_UN8,
            w,
            h,
            out _,
            out int slicePitch,
            out _,
            out _
        );
        IntPtr ptr = Marshal.AllocHGlobal(slicePitch);
        try
        {
            using var image = Image.New2D(w, h, 1, Format.RGBA_UN8, 1, ptr);
            // DataPointer should be the same pointer we passed in
            Assert.AreEqual(ptr, image.DataPointer);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    // ---- NewCube ----

    [TestMethod]
    public void NewCube_ProducesCorrectImageDescription()
    {
        using var image = Image.NewCube(64, 1, Format.RGBA_UN8);
        Assert.AreEqual(TextureDimension.TextureCube, image.Description.Dimension);
        Assert.AreEqual(64, image.Description.Width);
        Assert.AreEqual(64, image.Description.Height);
        Assert.AreEqual(1, image.Description.Depth);
        Assert.AreEqual(6, image.Description.ArraySize);
        Assert.AreEqual(Format.RGBA_UN8, image.Description.Format);
        Assert.AreEqual(1, image.Description.MipLevels);
    }

    [TestMethod]
    public void NewCube_ArraySizeIsAlways6()
    {
        using var image = Image.NewCube(32, 1, Format.R_UN8);
        Assert.AreEqual(6, image.Description.ArraySize);
    }

    // ---- New3D ----

    [TestMethod]
    public void New3D_ProducesCorrectImageDescription()
    {
        using var image = Image.New3D(8, 8, 4, 1, Format.RGBA_UN8);
        Assert.AreEqual(TextureDimension.Texture3D, image.Description.Dimension);
        Assert.AreEqual(8, image.Description.Width);
        Assert.AreEqual(8, image.Description.Height);
        Assert.AreEqual(4, image.Description.Depth);
        Assert.AreEqual(1, image.Description.ArraySize);
        Assert.AreEqual(Format.RGBA_UN8, image.Description.Format);
        Assert.AreEqual(1, image.Description.MipLevels);
    }

    // ---- MipMapCount.Auto resolution ----

    [TestMethod]
    public void New2D_AutoMipCount_ResolvesToFullMipChain()
    {
        // 64x64 → 7 mip levels (64, 32, 16, 8, 4, 2, 1)
        using var image = Image.New2D(64, 64, MipMapCount.Auto, Format.RGBA_UN8);
        int expected = PitchCalculator.CountMips(64, 64);
        Assert.AreEqual(expected, image.Description.MipLevels);
    }

    [TestMethod]
    public void New2D_AutoMipCount_NonSquare_ResolvesToFullMipChain()
    {
        // 128x32 → max(128,32)=128 → 1+floor(log2(128)) = 8 levels
        using var image = Image.New2D(128, 32, MipMapCount.Auto, Format.RGBA_UN8);
        int expected = PitchCalculator.CountMips(128, 32);
        Assert.AreEqual(expected, image.Description.MipLevels);
    }

    [TestMethod]
    public void New3D_AutoMipCount_Pow2_ResolvesToFullMipChain()
    {
        // 8x8x8 → 4 mip levels
        using var image = Image.New3D(8, 8, 8, MipMapCount.Auto, Format.RGBA_UN8);
        int expected = PitchCalculator.CountMips(8, 8, 8);
        Assert.AreEqual(expected, image.Description.MipLevels);
    }

    // ---- Cube array size validation ----

    [TestMethod]
    public void New_TextureCube_NonMultipleOf6ArraySize_ThrowsInvalidOperationException()
    {
        // Directly create a TextureCube description with arraySize=5 (not multiple of 6)
        var desc = new ImageDescription
        {
            Dimension = TextureDimension.TextureCube,
            Width = 16,
            Height = 16,
            Depth = 1,
            ArraySize = 5,
            Format = Format.RGBA_UN8,
            MipLevels = 1,
        };
        Assert.ThrowsException<InvalidOperationException>(() => Image.New(desc));
    }

    [TestMethod]
    public void New_TextureCube_ArraySize12_IsValid()
    {
        // 12 is a multiple of 6 (2 cube maps)
        var desc = new ImageDescription
        {
            Dimension = TextureDimension.TextureCube,
            Width = 16,
            Height = 16,
            Depth = 1,
            ArraySize = 12,
            Format = Format.RGBA_UN8,
            MipLevels = 1,
        };
        using var image = Image.New(desc);
        Assert.AreEqual(12, image.Description.ArraySize);
    }

    // ---- Invalid dimension validation ----

    [TestMethod]
    public void New2D_ZeroWidth_ThrowsInvalidOperationException()
    {
        var desc = new ImageDescription
        {
            Dimension = TextureDimension.Texture2D,
            Width = 0,
            Height = 16,
            Depth = 1,
            ArraySize = 1,
            Format = Format.RGBA_UN8,
            MipLevels = 1,
        };
        Assert.ThrowsException<InvalidOperationException>(() => Image.New(desc));
    }

    [TestMethod]
    public void New3D_ArraySizeGreaterThan1_ThrowsInvalidOperationException()
    {
        var desc = new ImageDescription
        {
            Dimension = TextureDimension.Texture3D,
            Width = 4,
            Height = 4,
            Depth = 4,
            ArraySize = 2, // invalid for 3D
            Format = Format.RGBA_UN8,
            MipLevels = 1,
        };
        Assert.ThrowsException<InvalidOperationException>(() => Image.New(desc));
    }

    // ---- IDisposable ----

    [TestMethod]
    public void Dispose_ReleasesUnmanagedMemory_NoExceptionAfterDispose()
    {
        // Create an image that allocates its own buffer, then dispose it.
        // Verify no crash and that subsequent access to the image object doesn't throw.
        var image = Image.New2D(16, 16, 1, Format.RGBA_UN8);
        Assert.AreNotEqual(IntPtr.Zero, image.DataPointer);
        image.Dispose();
        // Double-dispose should be safe
        image.Dispose();
    }

    [TestMethod]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var image = Image.New2D(8, 8, 1, Format.RGBA_UN8);
        image.Dispose();
        // Should not throw on second dispose
        image.Dispose();
    }

    [TestMethod]
    public void New_AllocatesBuffer_DataPointerIsNonZero()
    {
        using var image = Image.New2D(4, 4, 1, Format.RGBA_UN8);
        Assert.AreNotEqual(IntPtr.Zero, image.DataPointer);
    }

    // ---- TotalSizeInBytes ----

    [TestMethod]
    public void New2D_TotalSizeInBytes_MatchesExpected()
    {
        // 4x4 RGBA_UN8 (32bpp = 4 bytes/pixel), 1 mip level
        // rowPitch = 4*4 = 16, slicePitch = 16*4 = 64
        using var image = Image.New2D(4, 4, 1, Format.RGBA_UN8);
        Assert.AreEqual(64, image.TotalSizeInBytes);
    }

    [TestMethod]
    public void New2D_TotalSizeInBytes_WithMips_MatchesExpected()
    {
        // 4x4 RGBA_UN8 with full mip chain: 64 + 16 + 4 + 4 = 88 bytes
        // mip0: 4x4 = 64, mip1: 2x2 = 16, mip2: 1x1 = 4
        // CountMips(4,4) = 3
        using var image = Image.New2D(4, 4, MipMapCount.Auto, Format.RGBA_UN8);
        Assert.AreEqual(3, image.Description.MipLevels);
        // 64 + 16 + 4 = 84
        Assert.AreEqual(84, image.TotalSizeInBytes);
    }

    // ---- GetPixelBuffer bounds ----

    [TestMethod]
    public void GetPixelBuffer_ValidIndices_ReturnsBuffer()
    {
        using var image = Image.New2D(8, 8, 1, Format.RGBA_UN8);
        var pb = image.GetPixelBuffer(0, 0);
        Assert.IsNotNull(pb);
        Assert.AreEqual(8, pb.Width);
        Assert.AreEqual(8, pb.Height);
    }

    [TestMethod]
    public void GetPixelBuffer_OutOfRangeMip_ThrowsArgumentException()
    {
        using var image = Image.New2D(4, 4, 1, Format.RGBA_UN8);
        Assert.ThrowsException<ArgumentException>(() => image.GetPixelBuffer(0, 1));
    }

    [TestMethod]
    public void GetPixelBuffer_OutOfRangeArraySlice_ThrowsArgumentException()
    {
        using var image = Image.New2D(4, 4, 1, Format.RGBA_UN8, arraySize: 2);
        Assert.ThrowsException<ArgumentException>(() => image.GetPixelBuffer(2, 0));
    }

    [TestMethod]
    public void GetPixelBuffer_Texture3D_OutOfRangeZSlice_ThrowsArgumentException()
    {
        using var image = Image.New3D(4, 4, 2, 1, Format.RGBA_UN8);
        Assert.ThrowsException<ArgumentException>(() => image.GetPixelBuffer(2, 0));
    }

    // ---- GetMipMapDescription ----

    [TestMethod]
    public void GetMipMapDescription_Mip0_ReturnsFullDimensions()
    {
        using var image = Image.New2D(8, 4, 1, Format.RGBA_UN8);
        var mmd = image.GetMipMapDescription(0);
        Assert.AreEqual(8, mmd.Width);
        Assert.AreEqual(4, mmd.Height);
    }

    [TestMethod]
    public void GetMipMapDescription_Mip1_ReturnsHalvedDimensions()
    {
        using var image = Image.New2D(8, 4, 2, Format.RGBA_UN8);
        var mmd = image.GetMipMapDescription(1);
        Assert.AreEqual(4, mmd.Width);
        Assert.AreEqual(2, mmd.Height);
    }

    // ---- Register with both null delegates ----

    [TestMethod]
    public void Register_BothDelegatesNull_ThrowsArgumentNullException()
    {
        Assert.ThrowsException<ArgumentNullException>(() =>
            Image.Register(ImageFileType.Png, null, null)
        );
    }

    // ---- Cube pixel buffer access ----

    [TestMethod]
    public void NewCube_GetPixelBuffer_AllSixFaces_Accessible()
    {
        using var image = Image.NewCube(4, 1, Format.RGBA_UN8);
        for (int face = 0; face < 6; face++)
        {
            var pb = image.GetPixelBuffer(face, 0);
            Assert.IsNotNull(pb);
            Assert.AreEqual(4, pb.Width);
            Assert.AreEqual(4, pb.Height);
        }
    }

    // ---- 3D texture pixel buffer access ----

    [TestMethod]
    public void New3D_GetPixelBuffer_AllZSlices_Accessible()
    {
        using var image = Image.New3D(4, 4, 3, 1, Format.RGBA_UN8);
        for (int z = 0; z < 3; z++)
        {
            var pb = image.GetPixelBuffer(z, 0);
            Assert.IsNotNull(pb);
        }
    }
}
