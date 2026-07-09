using FsCheck;
using FsCheck.Fluent;
using HelixToolkit.Nex.Rendering.Gizmos;
using HelixToolkit.Nex.Rendering.SysEncoding;

namespace HelixToolkit.Nex.Engine.Tests;

/// <summary>
/// Property 3: Gizmo pixels are never misclassified as scene.
///
/// For any gizmo pick encoded by <see cref="Utils.PackGizmoInfo(uint, GizmoHandleId, out uint, out uint)"/>,
/// <see cref="Utils.UnpackEntityId(uint, uint)"/> yields <see cref="EntityIdPickKind.Gizmo"/> and
/// never <see cref="EntityIdPickKind.Scene"/>; and for any world-id-zero pixel that is not a valid
/// <see cref="SysEncodingKind.Gizmo"/> encoding, the result is <see cref="EntityIdPickKind.NoHit"/>
/// rather than a scene pick, without raising an exception.
///
/// **Validates: Requirements 4.1, 5.2**
/// </summary>
[TestClass]
public sealed class GizmoPixelsNeverScenePropertyTests
{
    /// <summary>All eight axis/plane values a handle may constrain to.</summary>
    private static readonly GizmoAxis[] AllAxes =
    [
        GizmoAxis.X,
        GizmoAxis.Y,
        GizmoAxis.Z,
        GizmoAxis.XY,
        GizmoAxis.YZ,
        GizmoAxis.XZ,
        GizmoAxis.Screen,
        GizmoAxis.Uniform,
    ];

    /// <summary>The three transform-manipulation modes a gizmo represents.</summary>
    private static readonly GizmoMode[] AllModes =
    [
        GizmoMode.Translate,
        GizmoMode.Rotate,
        GizmoMode.Scale,
    ];

    /// <summary>
    /// All encoding-type values that a world-id-zero pixel can carry EXCEPT the recognized
    /// <see cref="SysEncodingKind.Gizmo"/> (<c>1</c>). The encoding-type field is
    /// <see cref="GizmoEncodingConstants.EncodingTypeBits"/> bits wide, so it ranges 0..15;
    /// removing <c>1</c> leaves <c>None</c> (0) and every currently-unrecognized value (2..15).
    /// </summary>
    private static readonly uint[] NonGizmoEncodingValues =
        Enumerable
            .Range(0, (int)GizmoEncodingConstants.EncodingTypeMask + 1)
            .Select(v => (uint)v)
            .Where(v => v != (uint)SysEncodingKind.Gizmo)
            .ToArray();

    /// <summary>
    /// Generates gizmo pixels by packing a random owning entity id (across the full 24-bit
    /// owning-entity field, including the <c>0</c> and max boundaries) together with every
    /// <see cref="GizmoMode"/> x <see cref="GizmoAxis"/> handle identity via
    /// <see cref="Utils.PackGizmoInfo(uint, GizmoHandleId, out uint, out uint)"/>.
    /// </summary>
    private static Gen<(uint r, uint g, uint owningId, GizmoMode mode, GizmoAxis axis)> GizmoPixelGen() =>
        from idBits in Gen.Choose(int.MinValue, int.MaxValue)
        from mode in Gen.Elements(AllModes)
        from axis in Gen.Elements(AllAxes)
            // Reinterpret int bits as uint for full-range coverage, then mask to the owning-entity
            // field so the value survives the pack/unpack round-trip unchanged.
        let owningId = unchecked((uint)idBits) & GizmoEncodingConstants.OwningEntityMask
        let handle = new GizmoHandleId(mode, axis)
        let packed = Pack(owningId, handle)
        select (packed.r, packed.g, owningId, mode, axis);

    /// <summary>
    /// Generates world-id-zero pixels whose encoding-type field is deliberately NOT the recognized
    /// <see cref="SysEncodingKind.Gizmo"/> value. The world-id bits are forced to zero (the
    /// discriminator trigger) and the encoding-type field is overwritten with a non-gizmo value,
    /// while every other bit of R and all of G remain arbitrary.
    /// </summary>
    private static Gen<(uint r, uint g)> WorldIdZeroNonGizmoPixelGen() =>
        from rBits in Gen.Choose(int.MinValue, int.MaxValue)
        from gBits in Gen.Choose(int.MinValue, int.MaxValue)
        from encoding in Gen.Elements(NonGizmoEncodingValues)
        let rArbitrary = unchecked((uint)rBits)
        let gArbitrary = unchecked((uint)gBits)
        // Force world id = 0 (alternate-encoding trigger).
        let rNoWorld = rArbitrary & ~LimitsShaderConstants.WorldIdMask
        // Clear the encoding-type field, then set a non-gizmo encoding value.
        let rClearedEnc =
            rNoWorld
            & ~(GizmoEncodingConstants.EncodingTypeMask << GizmoEncodingConstants.EncodingTypeShift)
        let r = rClearedEnc | (encoding << GizmoEncodingConstants.EncodingTypeShift)
        select (r, gArbitrary);

    private static (uint r, uint g) Pack(uint owningId, GizmoHandleId handle)
    {
        Utils.PackGizmoInfo(owningId, handle, out uint r, out uint g);
        return (r, g);
    }

    /// <summary>
    /// For any pixel produced by <see cref="Utils.PackGizmoInfo(uint, GizmoHandleId, out uint, out uint)"/>,
    /// the unified decode classifies it as <see cref="EntityIdPickKind.Gizmo"/> and never as
    /// <see cref="EntityIdPickKind.Scene"/>.
    /// </summary>
    [TestMethod]
    [TestCategory("Gizmo")]
    public void Property3_GizmoPackedPixels_AreGizmo_NeverScene()
    {
        Prop.ForAll(
                Arb.From(GizmoPixelGen()),
                ((uint r, uint g, uint owningId, GizmoMode mode, GizmoAxis axis) t) =>
                {
                    var decoded = Utils.UnpackEntityId(t.r, t.g);

                    // Classified as a gizmo pick, and NEVER as a scene pick.
                    return decoded.Kind == EntityIdPickKind.Gizmo
                        && decoded.Kind != EntityIdPickKind.Scene;
                }
            )
            .QuickCheckThrowOnFailure();
    }

    /// <summary>
    /// For any world-id-zero pixel whose encoding-type field is not the recognized gizmo value,
    /// the unified decode reports <see cref="EntityIdPickKind.NoHit"/> (never a scene pick) and
    /// never throws.
    /// </summary>
    [TestMethod]
    [TestCategory("Gizmo")]
    public void Property3_WorldIdZero_NonGizmoEncoding_IsNoHit_WithoutException()
    {
        Prop.ForAll(
                Arb.From(WorldIdZeroNonGizmoPixelGen()),
                ((uint r, uint g) pixel) =>
                {
                    var (r, g) = pixel;

                    // Sanity: world id really is zero and the encoding is not the gizmo value.
                    uint worldId = r & LimitsShaderConstants.WorldIdMask;
                    uint encoding =
                        (r >> LimitsShaderConstants.WorldIdBits)
                        & GizmoEncodingConstants.EncodingTypeMask;
                    if (worldId != 0 || encoding == (uint)SysEncodingKind.Gizmo)
                    {
                        return false;
                    }

                    // Must not throw, and must classify as NoHit (never Scene, never Gizmo).
                    var decoded = Utils.UnpackEntityId(r, g);
                    return decoded.Kind == EntityIdPickKind.NoHit;
                }
            )
            .QuickCheckThrowOnFailure();
    }
}
