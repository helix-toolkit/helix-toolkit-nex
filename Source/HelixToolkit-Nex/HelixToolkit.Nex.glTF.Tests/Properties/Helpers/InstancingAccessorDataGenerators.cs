using System.Numerics;
using FsCheck;
using FsCheck.Fluent;
using glTFLoader.Schema;

namespace HelixToolkit.Nex.glTF.Tests.Properties.Helpers;

/// <summary>
/// FsCheck generators that produce per-instance accessor data for the
/// <c>EXT_mesh_gpu_instancing</c> reader properties (Properties 8–18). They yield typed element
/// arrays (so a test can assert exact read fidelity) plus the building blocks for the malformed
/// variants the reader must reject: zero-count accessors, too-short backing buffers, non-finite
/// FLOAT payloads, and wrong element/component types.
/// </summary>
internal static class InstancingAccessorDataGenerators
{
    /// <summary>The default coordinate magnitude used for finite component generation.</summary>
    public const float DefaultComponentRange = 10_000f;

    /// <summary>
    /// Generates a finite single-precision float in <c>[-range, range]</c> (never NaN or infinity).
    /// </summary>
    /// <param name="range">The inclusive magnitude bound; defaults to <see cref="DefaultComponentRange"/>.</param>
    public static Gen<float> FiniteFloat(float range = DefaultComponentRange) =>
        Gen.Choose(-1_000_000, 1_000_000).Select(v => v / 1_000_000f * range);

    /// <summary>
    /// Generates a non-finite float: <see cref="float.NaN"/>, <see cref="float.PositiveInfinity"/>,
    /// or <see cref="float.NegativeInfinity"/>. Feeds the TRANSLATION non-finite path (Property 9).
    /// </summary>
    public static Gen<float> NonFiniteFloat() =>
        Gen.Elements(float.NaN, float.PositiveInfinity, float.NegativeInfinity);

    /// <summary>
    /// Generates an instance count in <c>[min, max]</c>. Pass <paramref name="min"/> = 0 to include
    /// the zero-count edge that feeds the zero-instance behavior (Property 19).
    /// </summary>
    /// <param name="min">The inclusive minimum count.</param>
    /// <param name="max">The inclusive maximum count.</param>
    public static Gen<int> InstanceCount(int min = 1, int max = 64) => Gen.Choose(min, max);

    /// <summary>Generates a single finite <see cref="Vector3"/>.</summary>
    public static Gen<Vector3> Vector3Gen(float range = DefaultComponentRange) =>
        from x in FiniteFloat(range)
        from y in FiniteFloat(range)
        from z in FiniteFloat(range)
        select new Vector3(x, y, z);

    /// <summary>Generates a single finite <see cref="Vector4"/>.</summary>
    public static Gen<Vector4> Vector4Gen(float range = DefaultComponentRange) =>
        from x in FiniteFloat(range)
        from y in FiniteFloat(range)
        from z in FiniteFloat(range)
        from w in FiniteFloat(range)
        select new Vector4(x, y, z, w);

    /// <summary>
    /// Generates an array of <paramref name="count"/> finite VEC3 elements (e.g. TRANSLATION or SCALE
    /// element data). Use with <see cref="InstancingModelBuilder.AddTranslationAccessor"/> or
    /// <see cref="InstancingModelBuilder.AddScaleAccessor"/>.
    /// </summary>
    public static Gen<Vector3[]> Vec3FloatElements(int count, float range = DefaultComponentRange) =>
        Gen.ArrayOf(Vector3Gen(range), count);

    /// <summary>Generates an array of <paramref name="count"/> finite VEC4 elements.</summary>
    public static Gen<Vector4[]> Vec4FloatElements(int count, float range = DefaultComponentRange) =>
        Gen.ArrayOf(Vector4Gen(range), count);

    /// <summary>
    /// Generates a finite VEC3 element array of <paramref name="count"/> elements where exactly one
    /// component (at a generated element/component position) is replaced with a non-finite value.
    /// Returns the corrupted elements together with the flattened component index that was poisoned,
    /// feeding the TRANSLATION non-finite rejection path (Property 9).
    /// </summary>
    /// <param name="count">The element count (must be at least 1).</param>
    public static Gen<(Vector3[] Elements, int CorruptedComponentIndex)> Vec3WithOneNonFinite(int count) =>
        from elements in Vec3FloatElements(Math.Max(1, count))
        from badValue in NonFiniteFloat()
        from componentIndex in Gen.Choose(0, Math.Max(1, count) * 3 - 1)
        select (Poison(elements, componentIndex, badValue), componentIndex);

    /// <summary>
    /// Generates an element type that is NOT VEC3 (for TRANSLATION/SCALE wrong-type tests,
    /// Properties 9 and 16).
    /// </summary>
    public static Gen<Accessor.TypeEnum> NonVec3Type() =>
        Gen.Elements(
            Accessor.TypeEnum.SCALAR,
            Accessor.TypeEnum.VEC2,
            Accessor.TypeEnum.VEC4,
            Accessor.TypeEnum.MAT2,
            Accessor.TypeEnum.MAT3,
            Accessor.TypeEnum.MAT4
        );

    /// <summary>
    /// Generates an element type that is NOT VEC4 (for ROTATION wrong-type tests, Property 12).
    /// </summary>
    public static Gen<Accessor.TypeEnum> NonVec4Type() =>
        Gen.Elements(
            Accessor.TypeEnum.SCALAR,
            Accessor.TypeEnum.VEC2,
            Accessor.TypeEnum.VEC3,
            Accessor.TypeEnum.MAT2,
            Accessor.TypeEnum.MAT3,
            Accessor.TypeEnum.MAT4
        );

    /// <summary>
    /// Generates a component type that is NOT FLOAT (for TRANSLATION/SCALE wrong-type tests,
    /// Properties 9 and 16).
    /// </summary>
    public static Gen<Accessor.ComponentTypeEnum> NonFloatComponentType() =>
        Gen.Elements(
            Accessor.ComponentTypeEnum.BYTE,
            Accessor.ComponentTypeEnum.UNSIGNED_BYTE,
            Accessor.ComponentTypeEnum.SHORT,
            Accessor.ComponentTypeEnum.UNSIGNED_SHORT,
            Accessor.ComponentTypeEnum.UNSIGNED_INT
        );

    /// <summary>
    /// Generates a component type that is invalid for a ROTATION accessor (anything other than FLOAT,
    /// signed BYTE, or signed SHORT), feeding Property 12.
    /// </summary>
    public static Gen<Accessor.ComponentTypeEnum> InvalidRotationComponentType() =>
        Gen.Elements(
            Accessor.ComponentTypeEnum.UNSIGNED_BYTE,
            Accessor.ComponentTypeEnum.UNSIGNED_SHORT,
            Accessor.ComponentTypeEnum.UNSIGNED_INT
        );

    /// <summary>
    /// Generates a "too short" backing buffer for a VEC3 FLOAT accessor of <paramref name="count"/>
    /// elements: the array length is at least one byte short of the required size, exercising the
    /// out-of-range read path (Requirement 4.4, Property 9).
    /// </summary>
    /// <param name="count">The accessor element count (must be at least 1).</param>
    public static Gen<byte[]> TooShortVec3FloatBuffer(int count)
    {
        int required = Math.Max(1, count) * 3 * sizeof(float);
        return Gen.Choose(0, required - 1).Select(len => new byte[len]);
    }

    private static Vector3[] Poison(Vector3[] elements, int componentIndex, float badValue)
    {
        int element = componentIndex / 3;
        int component = componentIndex % 3;
        var v = elements[element];
        elements[element] = component switch
        {
            0 => new Vector3(badValue, v.Y, v.Z),
            1 => new Vector3(v.X, badValue, v.Z),
            _ => new Vector3(v.X, v.Y, badValue),
        };
        return elements;
    }
}
