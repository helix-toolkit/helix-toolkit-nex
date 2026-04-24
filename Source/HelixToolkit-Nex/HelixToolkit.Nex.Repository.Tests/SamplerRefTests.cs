using HelixToolkit.Nex.Graphics.Mock;
using HelixToolkit.Nex.Material;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HelixToolkit.Nex.Repository.Tests;

/// <summary>
/// Unit tests for <see cref="SamplerRef"/> API contracts and error paths.
/// </summary>
[TestClass]
public class SamplerRefTests
{
    // -------------------------------------------------------------------------
    // Requirement 1.1 — SamplerRef is a reference type
    // -------------------------------------------------------------------------

    [TestMethod]
    public void SamplerRef_IsClass()
    {
        Assert.IsTrue(typeof(SamplerRef).IsClass, "SamplerRef must be a reference type (class).");
    }

    // -------------------------------------------------------------------------
    // Requirement 1.9 — Null sentinel always returns invalid handle
    // -------------------------------------------------------------------------

    [TestMethod]
    public void SamplerRefNull_GetHandle_ReturnsInvalidHandle()
    {
        var handle = SamplerRef.Null.GetHandle();
        Assert.IsFalse(handle.Valid, "SamplerRef.Null.GetHandle() must return an invalid handle.");
    }

    [TestMethod]
    public void SamplerRefNull_GetHandle_DoesNotThrow_WhenCalledMultipleTimes()
    {
        // Should never throw regardless of how many times it is called.
        for (int i = 0; i < 10; i++)
        {
            var handle = SamplerRef.Null.GetHandle();
            Assert.IsFalse(handle.Valid);
        }
    }

    // -------------------------------------------------------------------------
    // Requirement 3.3 — Remove returns false for non-existent key
    // -------------------------------------------------------------------------

    [TestMethod]
    public void NullSamplerRepository_Remove_ReturnsFalse_ForAnyKey()
    {
        // NullSamplerRepository is the backing repo for SamplerRef.Null.
        // Its Remove must always return false.
        var result = SamplerRef.Null.Repository.Remove("non-existent-key");
        Assert.IsFalse(result, "NullSamplerRepository.Remove must return false for any key.");
    }

    // -------------------------------------------------------------------------
    // Requirement 5.4 — PBRMaterialProperties.Null has sampler fields == SamplerRef.Null
    // -------------------------------------------------------------------------

    [TestMethod]
    public void PBRMaterialPropertiesNull_SamplerFields_AreSamplerRefNull()
    {
        var mat = PBRMaterialProperties.Null;

        Assert.AreEqual(SamplerRef.Null, mat.Sampler, "Sampler should be SamplerRef.Null");
        Assert.AreEqual(
            SamplerRef.Null,
            mat.DisplaceSampler,
            "DisplaceSampler should be SamplerRef.Null"
        );
    }

    // -------------------------------------------------------------------------
    // Requirement 5.3 — PBRMaterialProperties.Dispose() does not throw
    // -------------------------------------------------------------------------

    [TestMethod]
    public void PBRMaterialPropertiesNull_Dispose_DoesNotThrow()
    {
        // PBRMaterialProperties.Null has no pool entry; Dispose must not throw.
        var mat = PBRMaterialProperties.Null;
        mat.Dispose(); // must not throw NullReferenceException or any other exception
    }

    // -------------------------------------------------------------------------
    // Requirement 3.4 — DisposeResource fires OnDisposed
    // -------------------------------------------------------------------------

    [TestMethod]
    public void DisposeResource_FiresOnDisposed()
    {
        var ctx = new MockContext();
        ctx.Initialize();
        ctx.CreateSampler(new SamplerStateDesc { }, out var sampler);
        var samplerRef = new SamplerRef("key", SamplerRef.Null.Repository, sampler);
        bool fired = false;
        samplerRef.OnDisposed += () => fired = true;
        samplerRef.DisposeResource();
        Assert.IsTrue(fired, "OnDisposed should have fired.");
    }

    [TestMethod]
    public void DisposeResource_WithNoSubscribers_DoesNotThrow()
    {
        var samplerRef = new SamplerRef("key", SamplerRef.Null.Repository, SamplerResource.Null);
        samplerRef.DisposeResource(); // must not throw
    }
}
