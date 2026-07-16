using System.Numerics;
using FsCheck;
using FsCheck.Fluent;

namespace HelixToolkit.Nex.glTF.Tests.Properties.Helpers;

/// <summary>
/// FsCheck generators producing quaternion payloads across the full magnitude spectrum the
/// <c>EXT_mesh_gpu_instancing</c> reader must handle (Properties 10–14): unit quaternions, non-unit
/// quaternions whose magnitude is at least the <see cref="DegenerateThreshold"/> but differs from
/// 1.0, degenerate quaternions below the threshold, and normalized signed BYTE/SHORT encodings for
/// the dequantization property (Property 11), including the clamping extremes.
/// </summary>
internal static class InstancingQuaternionGenerators
{
    /// <summary>The magnitude threshold below which a quaternion is treated as degenerate (Requirement 5.6).</summary>
    public const float DegenerateThreshold = 1e-6f;

    /// <summary>The normalized signed BYTE divisor (Requirement 5.3).</summary>
    public const float ByteDivisor = 127f;

    /// <summary>The normalized signed SHORT divisor (Requirement 5.3).</summary>
    public const float ShortDivisor = 32767f;

    /// <summary>
    /// Generates a unit-length quaternion stored as <c>(x, y, z, w)</c>. Built from an axis-angle so
    /// the result is always normalized within floating-point tolerance.
    /// </summary>
    public static Gen<Vector4> UnitQuaternion() =>
        from ax in Gen.Choose(-1000, 1000)
        from ay in Gen.Choose(-1000, 1000)
        from az in Gen.Choose(-1000, 1000)
        from angle in Gen.Choose(0, 6283).Select(a => a / 1000f)
        select FromAxisAngle(new Vector3(ax, ay, az), angle);

    /// <summary>
    /// Generates a non-unit quaternion whose magnitude is at least <see cref="DegenerateThreshold"/>
    /// but differs from 1.0 by more than <see cref="DegenerateThreshold"/>. Feeds the normalization
    /// property (Property 13).
    /// </summary>
    public static Gen<Vector4> NonUnitQuaternion() =>
        from unit in UnitQuaternion()
        from scale in Gen.OneOf(
            Gen.Choose(2, 1000).Select(v => v / 1000f), // shrink toward zero but >= threshold
            Gen.Choose(1001, 100_000).Select(v => v / 1000f) // grow above unit length
        )
        select Vector4.Multiply(unit, scale);

    /// <summary>
    /// Generates a degenerate quaternion whose magnitude is strictly less than
    /// <see cref="DegenerateThreshold"/> (including exactly zero). Feeds the identity-substitution
    /// property (Property 14).
    /// </summary>
    public static Gen<Vector4> DegenerateQuaternion() =>
        from unit in UnitQuaternion()
        from tinyScale in Gen.Choose(0, 900).Select(v => v / 1000f * DegenerateThreshold)
        select Vector4.Multiply(unit, tinyScale);

    /// <summary>
    /// Generates a quaternion exactly at the degenerate magnitude boundary (magnitude equal to
    /// <see cref="DegenerateThreshold"/>), the boundary case for Properties 13 and 14.
    /// </summary>
    public static Gen<Vector4> BoundaryMagnitudeQuaternion() =>
        from unit in UnitQuaternion()
        select Vector4.Multiply(Vector4.Normalize(unit), DegenerateThreshold);

    /// <summary>
    /// Generates an array of <paramref name="count"/> signed-byte VEC4 encodings together with the
    /// dequantized expected components (<c>max(v / 127, -1)</c>), including the <c>-128</c> extreme
    /// that clamps to <c>-1.0</c>. Feeds Property 11.
    /// </summary>
    /// <param name="count">The number of quaternion elements (must be at least 1).</param>
    public static Gen<((sbyte X, sbyte Y, sbyte Z, sbyte W)[] Encoded, Vector4[] Expected)> NormalizedByteQuaternions(
        int count
    ) =>
        from encoded in Gen.ArrayOf(SignedByteQuad(), Math.Max(1, count))
        select (encoded, encoded.Select(DequantizeByte).ToArray());

    /// <summary>
    /// Generates an array of <paramref name="count"/> signed-short VEC4 encodings together with the
    /// dequantized expected components (<c>max(v / 32767, -1)</c>), including the <c>-32768</c>
    /// extreme that clamps to <c>-1.0</c>. Feeds Property 11.
    /// </summary>
    /// <param name="count">The number of quaternion elements (must be at least 1).</param>
    public static Gen<((short X, short Y, short Z, short W)[] Encoded, Vector4[] Expected)> NormalizedShortQuaternions(
        int count
    ) =>
        from encoded in Gen.ArrayOf(SignedShortQuad(), Math.Max(1, count))
        select (encoded, encoded.Select(DequantizeShort).ToArray());

    /// <summary>Dequantizes a signed-byte quad to a <see cref="Vector4"/> per the glTF convention.</summary>
    public static Vector4 DequantizeByte((sbyte X, sbyte Y, sbyte Z, sbyte W) q) =>
        new(
            MathF.Max(q.X / ByteDivisor, -1f),
            MathF.Max(q.Y / ByteDivisor, -1f),
            MathF.Max(q.Z / ByteDivisor, -1f),
            MathF.Max(q.W / ByteDivisor, -1f)
        );

    /// <summary>Dequantizes a signed-short quad to a <see cref="Vector4"/> per the glTF convention.</summary>
    public static Vector4 DequantizeShort((short X, short Y, short Z, short W) q) =>
        new(
            MathF.Max(q.X / ShortDivisor, -1f),
            MathF.Max(q.Y / ShortDivisor, -1f),
            MathF.Max(q.Z / ShortDivisor, -1f),
            MathF.Max(q.W / ShortDivisor, -1f)
        );

    private static Gen<(sbyte, sbyte, sbyte, sbyte)> SignedByteQuad() =>
        from x in SignedByte()
        from y in SignedByte()
        from z in SignedByte()
        from w in SignedByte()
        select (x, y, z, w);

    private static Gen<(short, short, short, short)> SignedShortQuad() =>
        from x in SignedShort()
        from y in SignedShort()
        from z in SignedShort()
        from w in SignedShort()
        select (x, y, z, w);

    // Bias the distribution toward the clamping extreme (-128 / -32768) so Property 11 exercises it.
    private static Gen<sbyte> SignedByte() =>
        Gen.OneOf(
            Gen.Choose(sbyte.MinValue, sbyte.MaxValue).Select(v => (sbyte)v),
            Gen.Constant(sbyte.MinValue),
            Gen.Constant(sbyte.MaxValue)
        );

    private static Gen<short> SignedShort() =>
        Gen.OneOf(
            Gen.Choose(short.MinValue, short.MaxValue).Select(v => (short)v),
            Gen.Constant(short.MinValue),
            Gen.Constant(short.MaxValue)
        );

    private static Vector4 FromAxisAngle(Vector3 axis, float angle)
    {
        if (axis.LengthSquared() < 1e-12f)
        {
            // Degenerate axis → identity quaternion.
            return new Vector4(0, 0, 0, 1);
        }

        var q = Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), angle);
        return new Vector4(q.X, q.Y, q.Z, q.W);
    }
}
