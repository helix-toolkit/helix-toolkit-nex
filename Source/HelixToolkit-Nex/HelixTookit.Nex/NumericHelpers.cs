using System.Runtime.CompilerServices;

namespace HelixToolkit.Nex;

/// <summary>
/// Provides helper methods for parsing numeric types from <see cref="ReadOnlySpan{T}"/> of characters.
/// </summary>
/// <remarks>
/// These methods provide compatibility across different .NET versions by using optimized parsing
/// when available (.NET 8+) or fallback implementations for earlier versions.
/// </remarks>
public static class NumericHelpers
{
    /// <summary>
    /// Parses a span of characters into a 32-bit signed integer.
    /// </summary>
    /// <param name="s">The span of characters to parse.</param>
    /// <param name="provider">An object that supplies culture-specific formatting information.</param>
    /// <returns>The parsed integer value.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ParseInt32(ReadOnlySpan<char> s, IFormatProvider? provider)
    {
#if NET8_0_OR_GREATER
        return int.Parse(s, provider);
#else
 return int.Parse(s.ToString(), provider);
#endif
    }

    /// <summary>
    /// Parses a span of characters into a single-precision floating-point number.
    /// </summary>
    /// <param name="s">The span of characters to parse.</param>
    /// <param name="provider">An object that supplies culture-specific formatting information.</param>
    /// <returns>The parsed float value.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float ParseSingle(ReadOnlySpan<char> s, IFormatProvider? provider)
    {
#if NET8_0_OR_GREATER
        return float.Parse(s, provider);
#else
        return float.Parse(s.ToString(), provider);
#endif
    }

    /// <summary>
    /// Parses a span of characters into a double-precision floating-point number.
    /// </summary>
    /// <param name="s">The span of characters to parse.</param>
    /// <param name="provider">An object that supplies culture-specific formatting information.</param>
    /// <returns>The parsed double value.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double ParseDouble(ReadOnlySpan<char> s, IFormatProvider? provider)
    {
#if NET8_0_OR_GREATER
        return double.Parse(s, provider);
#else
        return double.Parse(s.ToString(), provider);
#endif
    }
}
