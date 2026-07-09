using FsCheck;
using FsCheck.Fluent;
using HelixToolkit.Nex.Rendering.SysEncoding;

namespace HelixToolkit.Nex.Engine.Tests;

/// <summary>
/// Property 4: Classification is total, mutually exclusive, and exception-free.
///
/// For any pixel (R, G), <see cref="Utils.UnpackEntityId(uint, uint)"/> returns exactly one of
/// <see cref="EntityIdPickKind.Scene"/>, <see cref="EntityIdPickKind.Gizmo"/>, or
/// <see cref="EntityIdPickKind.NoHit"/>, never throws, and reports
/// <see cref="EntityIdPickKind.NoHit"/> for every world-id-zero pixel whose encoding-type value is
/// not a recognized alternate encoding (including the <see cref="SysEncodingKind.None"/> /
/// cleared-to-zero pixel).
///
/// The <see cref="EntityIdPickKind"/> enum has exactly three values, so "exactly one" holds by
/// construction; the test additionally asserts the specific classification rules:
/// <list type="bullet">
///   <item>world id &gt; 0 =&gt; <see cref="EntityIdPickKind.Scene"/>.</item>
///   <item>world id == 0 and encoding == <see cref="SysEncodingKind.Gizmo"/> =&gt; <see cref="EntityIdPickKind.Gizmo"/>.</item>
///   <item>world id == 0 and encoding unrecognized (incl. <see cref="SysEncodingKind.None"/>) =&gt; <see cref="EntityIdPickKind.NoHit"/>.</item>
/// </list>
///
/// **Validates: Requirements 3.3, 3.5, 5.4, 5.5**
/// </summary>
[TestClass]
public sealed class ClassificationTotalityPropertyTests
{
    private static readonly Config DefaultConfig = Config.Default.WithMaxTest(2000);

    /// <summary>
    /// Generates full-range <c>uint</c> (R, G) pixels. FsCheck natively generates <c>int</c>, so the
    /// full 32-bit range is covered by reinterpreting the generated bits as <c>uint</c>. The three
    /// classification branches are all exercised by mixing:
    /// <list type="number">
    ///   <item>fully-arbitrary pixels (predominantly world id &gt; 0, i.e. Scene),</item>
    ///   <item>world-id-zero pixels with an arbitrary encoding-type field in <c>0 .. 2^EncodingTypeBits - 1</c>
    ///         (covers <c>None</c>, <c>Gizmo</c>, and unrecognized encodings), and</item>
    ///   <item>world-id-zero pixels forced to the <see cref="SysEncodingKind.Gizmo"/> encoding.</item>
    /// </list>
    /// </summary>
    private static Gen<(uint r, uint g)> PixelGen()
    {
        var fullRange =
            from rBits in Gen.Choose(int.MinValue, int.MaxValue)
            from gBits in Gen.Choose(int.MinValue, int.MaxValue)
            select (unchecked((uint)rBits), unchecked((uint)gBits));

        // World id forced to zero, arbitrary encoding-type field (0 .. EncodingTypeMask), the rest
        // of R and all of G arbitrary. Exercises None (0), Gizmo (1), and unrecognized (2..) values.
        var worldIdZeroArbitraryEncoding =
            from rBits in Gen.Choose(int.MinValue, int.MaxValue)
            from gBits in Gen.Choose(int.MinValue, int.MaxValue)
            from encoding in Gen.Choose(0, (int)GizmoEncodingConstants.EncodingTypeMask)
            let cleared = unchecked((uint)rBits)
                & ~LimitsShaderConstants.WorldIdMask
                & ~(GizmoEncodingConstants.EncodingTypeMask << GizmoEncodingConstants.EncodingTypeShift)
            let r = cleared | ((uint)encoding << GizmoEncodingConstants.EncodingTypeShift)
            select (r, unchecked((uint)gBits));

        // World id forced to zero, encoding forced to Gizmo; the owning-entity/handle bits arbitrary.
        var gizmoPixel =
            from rBits in Gen.Choose(int.MinValue, int.MaxValue)
            from gBits in Gen.Choose(int.MinValue, int.MaxValue)
            let cleared = unchecked((uint)rBits)
                & ~LimitsShaderConstants.WorldIdMask
                & ~(GizmoEncodingConstants.EncodingTypeMask << GizmoEncodingConstants.EncodingTypeShift)
            let r = cleared | ((uint)SysEncodingKind.Gizmo << GizmoEncodingConstants.EncodingTypeShift)
            select (r, unchecked((uint)gBits));

        return Gen.Frequency(
            (2, fullRange),
            (2, worldIdZeroArbitraryEncoding),
            (1, gizmoPixel)
        );
    }

    [TestMethod]
    public void Property4_Classification_IsTotal_MutuallyExclusive_AndExceptionFree()
    {
        Prop.ForAll(
                Arb.From(PixelGen()),
                ((uint r, uint g) pixel) =>
                {
                    var (r, g) = pixel;

                    // Requirement 5.5 / 3.5: the decode must never throw across the full input range.
                    EntityIdDecodeResult decoded;
                    try
                    {
                        decoded = Utils.UnpackEntityId(r, g);
                    }
                    catch
                    {
                        return false;
                    }

                    // Requirement 5.4 / 3.3: the result is exactly one of the three defined kinds.
                    if (!Enum.IsDefined(typeof(EntityIdPickKind), decoded.Kind))
                    {
                        return false;
                    }

                    // Independently compute the expected classification from the bit layout.
                    uint worldId = r & LimitsShaderConstants.WorldIdMask;
                    uint encoding =
                        (r >> LimitsShaderConstants.WorldIdBits)
                        & GizmoEncodingConstants.EncodingTypeMask;

                    EntityIdPickKind expected;
                    if (worldId > 0)
                    {
                        expected = EntityIdPickKind.Scene; // Req 3.2
                    }
                    else if (encoding == (uint)SysEncodingKind.Gizmo)
                    {
                        expected = EntityIdPickKind.Gizmo; // Req 4.5
                    }
                    else
                    {
                        // Req 3.5 / 5.5: world-id-zero + unrecognized encoding (incl. None) -> NoHit.
                        expected = EntityIdPickKind.NoHit;
                    }

                    return decoded.Kind == expected;
                }
            )
            .Check(DefaultConfig);
    }
}
