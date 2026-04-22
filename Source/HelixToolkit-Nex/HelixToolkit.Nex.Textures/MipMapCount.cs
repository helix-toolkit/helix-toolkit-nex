using System.Runtime.InteropServices;

namespace HelixToolkit.Nex.Textures;

/// <summary>
/// Specifies the number of mipmap levels for a texture.
/// Count == 0 means all mipmaps (auto), Count == 1 means a single level, Count == N means exactly N levels.
/// </summary>
[StructLayout(LayoutKind.Sequential, Size = 4)]
public readonly struct MipMapCount : IEquatable<MipMapCount>
{
    /// <summary>
    /// Automatically generate all mipmap levels (Count == 0).
    /// </summary>
    public static readonly MipMapCount Auto = new(true);

    /// <summary>
    /// The mipmap count. 0 = all mipmaps, 1 = single level, N = exact count.
    /// </summary>
    public readonly int Count;

    /// <summary>
    /// Initializes a new <see cref="MipMapCount"/> from a boolean.
    /// <c>true</c> → Count = 0 (all mipmaps); <c>false</c> → Count = 1 (single level).
    /// </summary>
    public MipMapCount(bool allMipMaps)
    {
        Count = allMipMaps ? 0 : 1;
    }

    /// <summary>
    /// Initializes a new <see cref="MipMapCount"/> with an explicit count.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when <paramref name="count"/> is negative.</exception>
    public MipMapCount(int count)
    {
        if (count < 0)
            throw new ArgumentException("MipMapCount cannot be negative.", nameof(count));
        Count = count;
    }

    // --- Implicit conversions ---

    /// <summary>Converts a boolean to a <see cref="MipMapCount"/>.</summary>
    public static implicit operator MipMapCount(bool allMipMaps) => new(allMipMaps);

    /// <summary>Converts a <see cref="MipMapCount"/> to a boolean. Returns <c>true</c> if Count == 0 (all mipmaps).</summary>
    public static implicit operator bool(MipMapCount mipMapCount) => mipMapCount.Count == 0;

    /// <summary>Converts an integer to a <see cref="MipMapCount"/>.</summary>
    public static implicit operator MipMapCount(int count) => new(count);

    /// <summary>Converts a <see cref="MipMapCount"/> to an integer.</summary>
    public static implicit operator int(MipMapCount mipMapCount) => mipMapCount.Count;

    // --- Equality ---

    /// <inheritdoc/>
    public bool Equals(MipMapCount other) => Count == other.Count;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is MipMapCount other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => Count;

    /// <summary>Equality operator.</summary>
    public static bool operator ==(MipMapCount left, MipMapCount right) => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(MipMapCount left, MipMapCount right) => !left.Equals(right);

    /// <inheritdoc/>
    public override string ToString() => Count == 0 ? "Auto" : Count.ToString();
}
