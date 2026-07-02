using FsCheck;
using FsCheck.Fluent;
using HelixToolkit.Nex.Engine;

namespace HelixToolkit.Nex.Engine.Tests;

/// <summary>
/// Property 2: Scene decode is unchanged and never gizmo.
///
/// For any pixel (R, G) whose decoded world id is greater than zero,
/// <see cref="Utils.UnpackEntityId(uint, uint)"/> returns <see cref="EntityIdPickKind.Scene"/>
/// (never <see cref="EntityIdPickKind.Gizmo"/>) and yields the exact same world id, entity id,
/// instance id, and primitive id that the existing <see cref="Utils.UnpackMeshInfo(uint, uint, out uint, out uint, out uint, out uint)"/>
/// produces for that pixel.
///
/// **Validates: Requirements 3.2, 5.1, 5.3**
/// </summary>
[TestClass]
public sealed class SceneDecodeUnchangedPropertyTests
{
    /// <summary>
    /// Generates full-range <c>uint</c> (R, G) pairs whose decoded world id is guaranteed to be
    /// greater than zero, by forcing a non-zero world id into the low bits of R while leaving every
    /// other bit arbitrary. FsCheck natively generates <c>int</c>, so the full 32-bit range is
    /// covered by reinterpreting the generated bits as <c>uint</c>.
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

    [TestMethod]
    public void Property2_SceneDecode_IsUnchanged_AndNeverGizmo()
    {
        Prop.ForAll(
                Arb.From(WorldIdNonZeroPixelGen()),
                ((uint r, uint g) pixel) =>
                {
                    var (r, g) = pixel;

                    // Sanity: this pixel really does decode to a non-zero world id.
                    uint worldId = r & LimitsShaderConstants.WorldIdMask;
                    if (worldId == 0)
                    {
                        return false;
                    }

                    var decoded = Utils.UnpackEntityId(r, g);

                    // Must be classified as Scene, never Gizmo (nor NoHit).
                    if (decoded.Kind != EntityIdPickKind.Scene)
                    {
                        return false;
                    }

                    // Must match the unchanged mesh-info decode field-for-field.
                    Utils.UnpackMeshInfo(
                        r,
                        g,
                        out uint expectedWorld,
                        out uint expectedEntity,
                        out uint expectedInstance,
                        out uint expectedPrimitive
                    );

                    return decoded.WorldId == expectedWorld
                        && decoded.EntityId == expectedEntity
                        && decoded.InstanceId == expectedInstance
                        && decoded.PrimitiveId == expectedPrimitive;
                }
            )
            .QuickCheckThrowOnFailure();
    }
}
