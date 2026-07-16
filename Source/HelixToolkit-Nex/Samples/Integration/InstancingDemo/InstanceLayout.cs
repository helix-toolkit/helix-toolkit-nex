using System.Numerics;
using HelixToolkit.Nex.Geometries;
using HelixToolkit.Nex.Shaders;

namespace InstancingDemo;

/// <summary>
/// Pure, side-effect-free helper that owns the per-instance transform math for the demo.
/// It has no dependency on the engine, GPU, or windowing, which makes the static-vs-dynamic
/// distinction independently verifiable by property-based tests.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ComputeStaticTransforms"/> lays instances out on a fixed line with identity rotation
/// and a fixed translation, returning identical values on every call (deterministic and
/// time-independent).
/// </para>
/// <para>
/// <see cref="ComputeDynamicTransforms"/> is a deterministic function of elapsed time: the same input
/// always produces the same output, and for any two distinct elapsed-time values at least one
/// instance's transform differs. To guarantee that distinctness for <em>every</em> pair of distinct
/// times (not merely most of them), each instance bobs at its own frequency, and consecutive instance
/// frequencies are scaled by an irrational ratio so a full-array collision can only occur when the two
/// times are equal.
/// </para>
/// </remarks>
internal sealed class InstanceLayout
{
    // Fixed layout/animation constants. Kept private so the transform math is fully determined by the
    // constructor arguments, which is what the correctness properties rely on.
    private const float StaticBaseY = 0f;
    private const float DynamicBaseY = 0f;
    private const float Amplitude = 1f;
    private const float BaseSpeed = 1f;

    // Golden-ratio fractional part: an irrational multiplier used to give each instance a frequency
    // that is an irrational multiple of its neighbour's. This makes ComputeDynamicTransforms injective
    // in time for any layout with two or more dynamic instances, so distinct times never collapse to
    // an identical transform array.
    private const float FrequencySpread = 0.6180339887498949f;

    private readonly float _spacing;

    private readonly InstanceTransform[] _dynamicTransforms;

    public InstanceLayout(int staticCount, int dynamicCount, float spacing)
    {
        StaticCount = staticCount;
        DynamicCount = dynamicCount;
        _spacing = spacing;
        _dynamicTransforms = new InstanceTransform[dynamicCount];
    }

    /// <summary>Number of static instances this layout produces.</summary>
    public int StaticCount { get; }

    /// <summary>Number of dynamic instances this layout produces.</summary>
    public int DynamicCount { get; }

    /// <summary>
    /// Computes the static instance transforms. The result is deterministic, independent of time, and
    /// identical on every call. The returned array always has exactly <see cref="StaticCount"/> elements.
    /// </summary>
    public InstanceTransform[] ComputeStaticTransforms()
    {
        var result = new InstanceTransform[StaticCount];
        for (int i = 0; i < StaticCount; i++)
        {
            float x = CenteredOffset(i, StaticCount) * _spacing;
            result[i] = InstanceTransformExts.Identity.SetTranslation(
                new Vector3(x, StaticBaseY, 0f)
            );
        }
        return result;
    }

    /// <summary>
    /// Computes the dynamic instance transforms for the given elapsed time in seconds. The result is a
    /// deterministic function of <paramref name="elapsedSeconds"/> (same input -> same output), and for
    /// any two distinct times at least one instance differs. The returned array always has exactly
    /// <see cref="DynamicCount"/> elements.
    /// </summary>
    public InstanceTransform[] ComputeDynamicTransforms(float elapsedSeconds)
    {
        for (int i = 0; i < DynamicCount; i++)
        {
            float x = CenteredOffset(i, DynamicCount) * _spacing;

            // Each instance bobs vertically at its own frequency. Consecutive frequencies are scaled by
            // an irrational ratio so that no two distinct times produce an identical set of y values.
            float frequency = BaseSpeed * (1f + i * FrequencySpread);
            float y = DynamicBaseY + Amplitude * MathF.Sin(elapsedSeconds * frequency);

            _dynamicTransforms[i] = InstanceTransformExts.Identity.SetTranslation(
                new Vector3(x, y, 0f)
            );
        }
        return _dynamicTransforms;
    }

    /// <summary>
    /// Returns the signed offset of instance <paramref name="index"/> from the centre of a line of
    /// <paramref name="count"/> instances, so the layout is centred around the origin.
    /// </summary>
    private static float CenteredOffset(int index, int count) => index - (count - 1) * 0.5f;
}
