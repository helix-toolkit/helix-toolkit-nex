namespace HelixToolkit.Nex.Scene;

/// <summary>
/// Interpolation between the keyframes of an <see cref="AnimationSampler"/>.
/// </summary>
public enum AnimationInterpolation
{
    /// <summary>
    /// Linear interpolation. Rotations use spherical linear interpolation.
    /// </summary>
    Linear,

    /// <summary>
    /// The value of the previous keyframe is held until the next keyframe.
    /// </summary>
    Step,

    /// <summary>
    /// Cubic Hermite spline. Each keyframe stores an in-tangent, a value and an out-tangent.
    /// </summary>
    CubicSpline,
}

/// <summary>
/// Keyframe data of an animated property: keyframe times and the value at each keyframe.
/// </summary>
/// <remarks>
/// Values are stored as <see cref="Vector4"/>: translations and scales use XYZ, rotations store a
/// quaternion as XYZW. For <see cref="AnimationInterpolation.CubicSpline"/> each keyframe stores three
/// values (in-tangent, value, out-tangent), as in glTF. A sampler is immutable and may be shared by
/// any number of channels.
/// </remarks>
public sealed class AnimationSampler
{
    private readonly float[] _times;
    private readonly Vector4[] _values;

    /// <summary>
    /// Gets the interpolation mode.
    /// </summary>
    public AnimationInterpolation Interpolation { get; }

    /// <summary>
    /// Gets the keyframe times in seconds, in increasing order.
    /// </summary>
    public ReadOnlySpan<float> Times => _times;

    /// <summary>
    /// Gets the keyframe values. Three values per keyframe for <see cref="AnimationInterpolation.CubicSpline"/>.
    /// </summary>
    public ReadOnlySpan<Vector4> Values => _values;

    /// <summary>
    /// Gets the number of keyframes.
    /// </summary>
    public int KeyCount => _times.Length;

    /// <summary>
    /// Gets the time of the first keyframe.
    /// </summary>
    public float StartTime => _times[0];

    /// <summary>
    /// Gets the time of the last keyframe.
    /// </summary>
    public float EndTime => _times[^1];

    /// <summary>
    /// Initializes a new instance of the <see cref="AnimationSampler"/> class.
    /// </summary>
    /// <param name="times">The keyframe times in seconds, in non-decreasing order. Must not be empty.</param>
    /// <param name="values">
    /// The keyframe values: one per keyframe, or three per keyframe for <see cref="AnimationInterpolation.CubicSpline"/>.
    /// </param>
    /// <param name="interpolation">The interpolation mode.</param>
    public AnimationSampler(float[] times, Vector4[] values, AnimationInterpolation interpolation)
    {
        ArgumentNullException.ThrowIfNull(times);
        ArgumentNullException.ThrowIfNull(values);
        if (times.Length == 0)
        {
            throw new ArgumentException("A sampler must have at least one keyframe.", nameof(times));
        }
        var expected =
            interpolation == AnimationInterpolation.CubicSpline ? times.Length * 3 : times.Length;
        if (values.Length != expected)
        {
            throw new ArgumentException(
                $"Expected {expected} values for {times.Length} {interpolation} keyframes, got {values.Length}.",
                nameof(values)
            );
        }
        for (var i = 1; i < times.Length; ++i)
        {
            if (times[i] < times[i - 1])
            {
                throw new ArgumentException("Keyframe times must not decrease.", nameof(times));
            }
        }
        _times = times;
        _values = values;
        Interpolation = interpolation;
    }

    /// <summary>
    /// Samples a vector value (translation or scale) at the specified time. Times outside the
    /// keyframe range are clamped to the first or last keyframe.
    /// </summary>
    /// <param name="time">The time in seconds.</param>
    /// <returns>The interpolated value.</returns>
    public Vector4 Sample(float time)
    {
        return Sample(time, isRotation: false);
    }

    /// <summary>
    /// Samples a rotation at the specified time. Times outside the keyframe range are clamped to
    /// the first or last keyframe.
    /// </summary>
    /// <param name="time">The time in seconds.</param>
    /// <returns>The interpolated, normalized rotation.</returns>
    public Quaternion SampleRotation(float time)
    {
        var v = Sample(time, isRotation: true);
        return Quaternion.Normalize(new Quaternion(v.X, v.Y, v.Z, v.W));
    }

    private Vector4 Sample(float time, bool isRotation)
    {
        var last = _times.Length - 1;
        if (last == 0 || time <= _times[0])
        {
            return GetValue(0);
        }
        if (time >= _times[last])
        {
            return GetValue(last);
        }

        var k = FindKey(time);
        if (Interpolation == AnimationInterpolation.Step)
        {
            return GetValue(k);
        }
        var td = _times[k + 1] - _times[k];
        if (td <= 0f)
        {
            return GetValue(k + 1);
        }
        var s = (time - _times[k]) / td;

        if (Interpolation == AnimationInterpolation.Linear)
        {
            var v0 = _values[k];
            var v1 = _values[k + 1];
            if (!isRotation)
            {
                return Vector4.Lerp(v0, v1, s);
            }
            var q = Quaternion.Slerp(
                new Quaternion(v0.X, v0.Y, v0.Z, v0.W),
                new Quaternion(v1.X, v1.Y, v1.Z, v1.W),
                s
            );
            return new Vector4(q.X, q.Y, q.Z, q.W);
        }

        // Cubic Hermite spline (glTF 2.0 Appendix C).
        var s2 = s * s;
        var s3 = s2 * s;
        var p0 = _values[k * 3 + 1];
        var m0 = _values[k * 3 + 2] * td;
        var p1 = _values[(k + 1) * 3 + 1];
        var m1 = _values[(k + 1) * 3] * td;
        return (2 * s3 - 3 * s2 + 1) * p0
            + (s3 - 2 * s2 + s) * m0
            + (-2 * s3 + 3 * s2) * p1
            + (s3 - s2) * m1;
    }

    /// <summary>
    /// Returns the index of the last keyframe at or before <paramref name="time"/>, which must lie
    /// strictly inside the keyframe range.
    /// </summary>
    private int FindKey(float time)
    {
        var index = Array.BinarySearch(_times, time);
        if (index < 0)
        {
            return ~index - 1;
        }
        // Exact hit: with duplicate times, step forward past the duplicates to the last match
        // that still has a following keyframe.
        while (index + 1 < _times.Length - 1 && _times[index + 1] == time)
        {
            ++index;
        }
        return Math.Min(index, _times.Length - 2);
    }

    private Vector4 GetValue(int key)
    {
        return Interpolation == AnimationInterpolation.CubicSpline
            ? _values[key * 3 + 1]
            : _values[key];
    }
}
