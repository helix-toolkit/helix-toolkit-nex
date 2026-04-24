using HelixToolkit.Nex.Graphics.Mock;
using HelixToolkit.Nex.Material;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HelixToolkit.Nex.Repository.Tests;

/// <summary>
/// Unit tests for <see cref="TextureRef"/> API contracts and error paths.
/// </summary>
[TestClass]
public class TextureRefTests
{
    // -------------------------------------------------------------------------
    // Requirement 1.1 — TextureRef is a reference type
    // -------------------------------------------------------------------------

    [TestMethod]
    public void TextureRef_IsClass()
    {
        Assert.IsTrue(typeof(TextureRef).IsClass, "TextureRef must be a reference type (class).");
    }

    // -------------------------------------------------------------------------
    // Requirement 1.9 — Null sentinel always returns invalid handle
    // -------------------------------------------------------------------------

    [TestMethod]
    public void TextureRefNull_GetHandle_ReturnsInvalidHandle()
    {
        var handle = TextureRef.Null.GetHandle();
        Assert.IsFalse(handle.Valid, "TextureRef.Null.GetHandle() must return an invalid handle.");
    }

    [TestMethod]
    public void TextureRefNull_GetHandle_DoesNotThrow_WhenCalledMultipleTimes()
    {
        // Should never throw regardless of how many times it is called.
        for (int i = 0; i < 10; i++)
        {
            var handle = TextureRef.Null.GetHandle();
            Assert.IsFalse(handle.Valid);
        }
    }

    // -------------------------------------------------------------------------
    // Requirement 4.3 — Remove returns false for non-existent key
    // -------------------------------------------------------------------------

    [TestMethod]
    public void NullTextureRepository_Remove_ReturnsFalse_ForAnyKey()
    {
        // NullTextureRepository is the backing repo for TextureRef.Null.
        // Its Remove must always return false.
        var result = TextureRef.Null.Repository.Remove("non-existent-key");
        Assert.IsFalse(result, "NullTextureRepository.Remove must return false for any key.");
    }

    // -------------------------------------------------------------------------
    // Requirement 6.4 — PBRMaterialProperties.Null has all texture maps == TextureRef.Null
    // -------------------------------------------------------------------------

    [TestMethod]
    public void PBRMaterialPropertiesNull_AllTextureMaps_AreTextureRefNull()
    {
        var mat = PBRMaterialProperties.Null;

        Assert.AreEqual(TextureRef.Null, mat.AlbedoMap, "AlbedoMap should be TextureRef.Null");
        Assert.AreEqual(TextureRef.Null, mat.NormalMap, "NormalMap should be TextureRef.Null");
        Assert.AreEqual(
            TextureRef.Null,
            mat.MetallicRoughnessMap,
            "MetallicRoughnessMap should be TextureRef.Null"
        );
        Assert.AreEqual(TextureRef.Null, mat.AoMap, "AoMap should be TextureRef.Null");
        Assert.AreEqual(TextureRef.Null, mat.BumpMap, "BumpMap should be TextureRef.Null");
        Assert.AreEqual(TextureRef.Null, mat.DisplaceMap, "DisplaceMap should be TextureRef.Null");
    }

    // -------------------------------------------------------------------------
    // Requirement 6.3 — PBRMaterialProperties.Dispose() does not throw
    // -------------------------------------------------------------------------

    [TestMethod]
    public void PBRMaterialPropertiesNull_Dispose_DoesNotThrow()
    {
        // PBRMaterialProperties.Null has no pool entry; Dispose must not throw.
        var mat = PBRMaterialProperties.Null;
        mat.Dispose(); // must not throw NullReferenceException or any other exception
    }

    // -------------------------------------------------------------------------
    // Requirement 1.3, 1.4 — DisposeResource fires OnDisposed
    // -------------------------------------------------------------------------

    [TestMethod]
    public void DisposeResource_FiresOnDisposed()
    {
        var ctx = new MockContext();
        ctx.Initialize();
        ctx.CreateTexture(
            new TextureDesc
            {
                Type = TextureType.Texture2D,
                Format = Format.RGBA_UN8,
                Dimensions = new Dimensions(1, 1, 1),
                NumMipLevels = 1,
                NumLayers = 1,
            },
            out var tex,
            "test"
        );
        var textureRef = new TextureRef("key", TextureRef.Null.Repository, tex);
        bool fired = false;
        textureRef.OnDisposed += () => fired = true;
        textureRef.DisposeResource();
        Assert.IsTrue(fired, "OnDisposed should have fired.");
    }

    [TestMethod]
    public void DisposeResource_WithNoSubscribers_DoesNotThrow()
    {
        var textureRef = new TextureRef("key", TextureRef.Null.Repository, TextureResource.Null);
        textureRef.DisposeResource(); // must not throw
    }

    [TestMethod]
    public void DisposeResource_FiresOnDisposed_Synchronously()
    {
        var ctx = new MockContext();
        ctx.Initialize();
        ctx.CreateTexture(
            new TextureDesc
            {
                Type = TextureType.Texture2D,
                Format = Format.RGBA_UN8,
                Dimensions = new Dimensions(1, 1, 1),
                NumMipLevels = 1,
                NumLayers = 1,
            },
            out var tex,
            "test"
        );
        var textureRef = new TextureRef("key", TextureRef.Null.Repository, tex);
        bool firedBeforeReturn = false;
        textureRef.OnDisposed += () => firedBeforeReturn = true;
        textureRef.DisposeResource();
        Assert.IsTrue(
            firedBeforeReturn,
            "OnDisposed must fire synchronously before DisposeResource returns."
        );
    }
}
