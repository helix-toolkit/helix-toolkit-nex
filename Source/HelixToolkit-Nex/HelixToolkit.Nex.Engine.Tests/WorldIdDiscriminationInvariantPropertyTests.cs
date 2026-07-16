using FsCheck;
using FsCheck.Fluent;
using HelixToolkit.Nex.Engine;

namespace HelixToolkit.Nex.Engine.Tests;

/// <summary>
/// Property 5: World-id discrimination is invariant to encoding-type changes.
///
/// For any pixel (R, G) whose decoded world id is greater than zero,
/// <see cref="Utils.UnpackEntityId(uint, uint)"/> returns <see cref="EntityIdPickKind.Scene"/>
/// regardless of the values of the encoding-type bits; and adding or changing alternate
/// <see cref="SysEncodingKind"/> values only affects the routing of world-id-zero pixels, never
/// the scene decode. Because the encoding-type field overlaps the scene entity-id field region,
/// this test asserts the scene decode remains byte-for-byte identical to the unchanged
/// <see cref="Utils.UnpackMeshInfo(uint, uint, out uint, out uint, out uint, out uint)"/> for
/// every encoding-type bit pattern.
///
/// **Validates: Requirements 8.2, 8.3**
/// </summary>
[TestClass]
public sealed class WorldIdDiscriminationInvariantPropertyTests
{
    /// <summary>
    /// Generates full-range <c>uint</c> (R, G) pairs whose decoded world id is guaranteed to be
    /// greater than zero, by forcing a non-zero world id (1..MaxWorldId) into the low bits of R
    /// while leaving every other bit of R and all of G arbitrary. FsCheck natively generates
    /// <c>int</c>, so the full 32-bit range is covered by reinterpreting the bits as <c>uint</c>.
    /// </summary>
    private static Gen<(uint r, uint g)> WorldIdNonZeroPixelGen() =>
        from rBits in Gen.Choose(int.MinValue, int.MaxValue)
        from gBits in Gen.Choose(int.MinValue, int.MaxValue)
        from worldId in Gen.Choose(1, (int)LimitsShaderConstants.WorldIdMask)
        let rArbitrary = unchecked((uint)rBits)
        let gArbitrary = unchecked((uint)gBits)
        // Clear the world-id field, then set a guaranteed non-zero world id (1..MaxWorldId).
        let r = (rArbitrary & ~LimitsShaderConstants.WorldIdMask) | (uint)worldId
        select (r, gArbitrary);

    /// <summary>
    /// For any world-id-nonzero pixel, overwriting the encoding-type field with EVERY possible
    /// value (<c>0 .. 2^EncodingTypeBits - 1</c>, i.e. every value the alternate-encoding routing
    /// could ever use) never changes the classification away from <see cref="EntityIdPickKind.Scene"/>,
    /// and the decoded scene fields stay identical to the unchanged
    /// <see cref="Utils.UnpackMeshInfo(uint, uint, out uint, out uint, out uint, out uint)"/> decode
    /// of the same bits. This demonstrates the world-id discriminator ignores the encoding-type
    /// field entirely for scene pixels.
    /// </summary>
    [TestMethod]
    [TestCategory("Gizmo")]
    public void Property5_WorldIdNonZero_IsScene_InvariantTo_EncodingTypeBits()
    {
        Prop.ForAll(
                Arb.From(WorldIdNonZeroPixelGen()),
                ((uint r, uint g) pixel) =>
                {
                    var (baseR, g) = pixel;

                    // Sweep every encoding-type bit pattern (0 .. 0xF). None of them may alter the
                    // fact that a world-id-nonzero pixel decodes as Scene, nor the decoded fields.
                    for (
                        uint encoding = 0;
                        encoding <= GizmoEncodingConstants.EncodingTypeMask;
                        encoding++
                    )
                    {
                        // Overwrite ONLY the encoding-type field, leaving the (non-zero) world id
                        // and all other bits untouched.
                        uint r =
                            (
                                baseR
                                & ~(
                                    GizmoEncodingConstants.EncodingTypeMask
                                    << GizmoEncodingConstants.EncodingTypeShift
                                )
                            )
                            | (encoding << GizmoEncodingConstants.EncodingTypeShift);

                        // World id must remain non-zero (the discriminator trigger for Scene).
                        if ((r & LimitsShaderConstants.WorldIdMask) == 0)
                        {
                            return false;
                        }

                        var decoded = Utils.UnpackEntityId(r, g);

                        // Regardless of the encoding-type bits, classification stays Scene.
                        if (decoded.Kind != EntityIdPickKind.Scene)
                        {
                            return false;
                        }

                        // And the scene decode is identical to the unchanged mesh-info decode:
                        // the encoding-type field is simply never consulted for scene pixels.
                        Utils.UnpackMeshInfo(
                            r,
                            g,
                            out uint expectedWorld,
                            out uint expectedEntity,
                            out uint expectedInstance,
                            out uint expectedPrimitive
                        );

                        bool matches =
                            decoded.WorldId == expectedWorld
                            && decoded.EntityId == expectedEntity
                            && decoded.InstanceId == expectedInstance
                            && decoded.PrimitiveId == expectedPrimitive;

                        if (!matches)
                        {
                            return false;
                        }
                    }

                    return true;
                }
            )
            .QuickCheckThrowOnFailure();
    }
}
