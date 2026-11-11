namespace HelixToolkit.Nex.Scene;

/// <summary>
/// The morph target weights of a mesh (glTF <c>mesh.weights</c> / <c>node.weights</c>): one weight
/// per morph target, shared by every primitive of the mesh and written by weight animation channels.
/// </summary>
/// <remarks>
/// <see cref="Version"/> increments on every change, so consumers (such as the engine's morph pass)
/// only re-upload the weights when they actually changed. Must be modified on the owning thread of
/// the world rendering it.
/// </remarks>
public sealed class MorphWeights
{
    private readonly float[] _values;

    /// <summary>
    /// Gets the weights, one per morph target.
    /// </summary>
    public ReadOnlySpan<float> Values => _values;

    /// <summary>
    /// Gets the number of weights.
    /// </summary>
    public int Count => _values.Length;

    /// <summary>
    /// Gets a number that changes whenever a weight changes.
    /// </summary>
    public ulong Version { get; private set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="MorphWeights"/> class with all weights zero.
    /// </summary>
    /// <param name="count">The number of morph targets.</param>
    public MorphWeights(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        _values = new float[count];
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MorphWeights"/> class.
    /// </summary>
    /// <param name="weights">The initial weights, one per morph target.</param>
    public MorphWeights(ReadOnlySpan<float> weights)
    {
        _values = weights.ToArray();
    }

    /// <summary>
    /// Gets or sets the weight of a morph target.
    /// </summary>
    /// <param name="index">The morph target index.</param>
    public float this[int index]
    {
        get => _values[index];
        set
        {
            // Exact change detection: any change must bump the version (an epsilon would drop
            // small changes), and Equals treats NaN as unchanged so it cannot bump forever.
            if (!_values[index].Equals(value))
            {
                _values[index] = value;
                ++Version;
            }
        }
    }

    /// <summary>
    /// Sets up to four consecutive weights from the components of <paramref name="values"/>,
    /// ignoring components past the last weight. Used by weight animation channels.
    /// </summary>
    /// <param name="startIndex">The index of the first weight to set.</param>
    /// <param name="values">The weights, in X, Y, Z, W order.</param>
    public void Set(int startIndex, Vector4 values)
    {
        Set(startIndex, values.X);
        Set(startIndex + 1, values.Y);
        Set(startIndex + 2, values.Z);
        Set(startIndex + 3, values.W);
    }

    /// <summary>
    /// Copies <paramref name="weights"/> into the weights, starting at the first one. Extra
    /// values are ignored.
    /// </summary>
    /// <param name="weights">The weights.</param>
    public void Set(ReadOnlySpan<float> weights)
    {
        var count = Math.Min(weights.Length, _values.Length);
        for (var i = 0; i < count; ++i)
        {
            this[i] = weights[i];
        }
    }

    private void Set(int index, float value)
    {
        if ((uint)index < (uint)_values.Length)
        {
            this[index] = value;
        }
    }
}
