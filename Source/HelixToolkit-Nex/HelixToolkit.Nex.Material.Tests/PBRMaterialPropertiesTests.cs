using FsCheck;
using FsCheck.Fluent;
using HelixToolkit.Nex.Graphics.Mock;
using HelixToolkit.Nex.Repository;
using HelixToolkit.Nex.Shaders;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HelixToolkit.Nex.Material.Tests;

/// <summary>
/// Tests for <see cref="PBRMaterialProperties"/> lifecycle, subscription management,
/// and index-zeroing behavior when texture/sampler refs are disposed.
/// </summary>
[TestClass]
public class PBRMaterialPropertiesTests
{
    private PBRMaterialPropertyManager? _manager;

    [TestInitialize]
    public void Setup()
    {
        _manager = new PBRMaterialPropertyManager();
    }

    [TestCleanup]
    public void Cleanup()
    {
        _manager?.Dispose();
        _manager = null;
    }

    // -------------------------------------------------------------------------
    // Helper: create a valid TextureRef backed by a MockContext texture
    // -------------------------------------------------------------------------

    private static TextureRef CreateValidTextureRef(string key = "test")
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
            key
        );
        return new TextureRef(key, TextureRef.Null.Repository, tex);
    }

    // -------------------------------------------------------------------------
    // Property 3: Index zeroed on dispose for all texture slots
    // Feature: resource-ref-lifecycle, Property 3: For any texture slot, assigning a valid ref
    // then disposing it zeros the index.
    // Validates: Requirements 4.6
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Property3_IndexZeroedOnDispose_ForAllTextureSlots()
    {
        // Slot indices: 0=AlbedoMap, 1=NormalMap, 2=MetallicRoughnessMap, 3=AoMap, 4=BumpMap, 5=DisplaceMap
        Prop.ForAll(
                Arb.From(Gen.Choose(0, 5)),
                (int slotIndex) =>
                {
                    var mat = _manager!.Create(PBRShadingMode.PBR);
                    try
                    {
                        var textureRef = CreateValidTextureRef($"slot-{slotIndex}");

                        // Assign to the appropriate slot
                        switch (slotIndex)
                        {
                            case 0:
                                mat.AlbedoMap = textureRef;
                                break;
                            case 1:
                                mat.NormalMap = textureRef;
                                break;
                            case 2:
                                mat.MetallicRoughnessMap = textureRef;
                                break;
                            case 3:
                                mat.AoMap = textureRef;
                                break;
                            case 4:
                                mat.BumpMap = textureRef;
                                break;
                            case 5:
                                mat.DisplaceMap = textureRef;
                                break;
                        }

                        // Dispose the ref — should zero the index
                        textureRef.DisposeResource();

                        // Verify the corresponding index is 0
                        return slotIndex switch
                        {
                            0 => mat.Properties.AlbedoTexIndex == 0,
                            1 => mat.Properties.NormalTexIndex == 0,
                            2 => mat.Properties.MetallicRoughnessTexIndex == 0,
                            3 => mat.Properties.AoTexIndex == 0,
                            4 => mat.Properties.BumpTexIndex == 0,
                            5 => mat.Properties.DisplaceTexIndex == 0,
                            _ => false,
                        };
                    }
                    finally
                    {
                        mat.Dispose();
                    }
                }
            )
            .QuickCheckThrowOnFailure();
    }

    // -------------------------------------------------------------------------
    // Example: Unsubscription effective — disposing old ref does NOT zero index
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Example_AssignRefAThenRefB_DisposingRefA_DoesNotZeroIndex()
    {
        var mat = _manager!.Create(PBRShadingMode.PBR);
        try
        {
            var refA = CreateValidTextureRef("refA");
            var refB = CreateValidTextureRef("refB");

            // Assign ref A first
            mat.AlbedoMap = refA;
            uint indexAfterA = mat.Properties.AlbedoTexIndex;
            Assert.AreNotEqual(0u, indexAfterA, "Index should be non-zero after assigning refA.");

            // Replace with ref B — this unsubscribes from refA
            mat.AlbedoMap = refB;
            uint indexAfterB = mat.Properties.AlbedoTexIndex;
            Assert.AreNotEqual(0u, indexAfterB, "Index should be non-zero after assigning refB.");

            // Dispose ref A — should NOT zero the index (unsubscription was effective)
            refA.DisposeResource();

            Assert.AreEqual(
                indexAfterB,
                mat.Properties.AlbedoTexIndex,
                "Disposing refA should NOT change the index after refB was assigned."
            );
        }
        finally
        {
            mat.Dispose();
        }
    }

    // -------------------------------------------------------------------------
    // Example: PBRMaterialProperties.Dispose() then disposing held ref does not fire callbacks
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Example_DisposeMaterial_ThenDisposeHeldRef_DoesNotFireCallbacks()
    {
        var mat = _manager!.Create(PBRShadingMode.PBR);
        var textureRef = CreateValidTextureRef("held");

        mat.AlbedoMap = textureRef;

        // Dispose the material — this unsubscribes all callbacks
        mat.Dispose();

        // Now dispose the held ref — should not throw and should not attempt to write to disposed pool
        // (the callback checks Valid before writing, and Valid is false after Dispose)
        bool threw = false;
        try
        {
            textureRef.DisposeResource();
        }
        catch
        {
            threw = true;
        }

        Assert.IsFalse(threw, "Disposing a held ref after material disposal must not throw.");
    }

    // -------------------------------------------------------------------------
    // Example: NotifyUpdated is called when OnDisposed fires
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Example_NotifyUpdated_IsCalledWhenOnDisposedFires()
    {
        var mat = _manager!.Create(PBRShadingMode.PBR);
        try
        {
            var textureRef = CreateValidTextureRef("notify-test");
            mat.AlbedoMap = textureRef;

            // Subscribe to the EventBus to detect MaterialPropsUpdatedEvent with Update operation
            int updateCount = 0;
            using var subscription = EventBus.Instance.Subscribe<MaterialPropsUpdatedEvent>(e =>
            {
                if (e.Operation == MaterialPropertyOp.Update && e.Index == mat.Index)
                    updateCount++;
            });

            // Dispose the ref — should trigger NotifyUpdated via the OnDisposed callback
            textureRef.DisposeResource();

            Assert.IsTrue(
                updateCount > 0,
                "NotifyUpdated (MaterialPropsUpdatedEvent with Update) should be published when OnDisposed fires."
            );
        }
        finally
        {
            mat.Dispose();
        }
    }
}
