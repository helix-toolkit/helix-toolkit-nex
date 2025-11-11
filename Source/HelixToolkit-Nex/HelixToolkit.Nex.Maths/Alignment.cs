namespace HelixToolkit.Nex.Maths;

/// <summary>
/// Provides utility methods for memory alignment calculations.
/// </summary>
/// <remarks>
/// This class contains methods for aligning sizes and addresses to specified alignment boundaries,
/// which is commonly needed for GPU buffer alignment requirements and memory optimization.
/// </remarks>
public static class Alignment
{
    /// <summary>
    /// Calculates the aligned size for a given value and alignment.
    /// </summary>
    /// <param name="value">The size value to align.</param>
    /// <param name="alignment">The alignment boundary (must be a power of 2).</param>
    /// <returns>The smallest size greater than or equal to <paramref name="value"/> that is aligned to <paramref name="alignment"/>.</returns>
    /// <remarks>
    /// This method rounds up the value to the next multiple of the alignment.
    /// For example, GetAlignedSize(13, 16) returns 16, and GetAlignedSize(17, 16) returns 32.
    /// The alignment parameter must be a power of 2 for the calculation to work correctly.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint GetAlignedSize(uint value, uint alignment)
    {
        return (value + alignment - 1) & ~(alignment - 1);
    }

    /// <summary>
    /// Calculates the aligned address for a given address and alignment.
    /// </summary>
    /// <param name="addr">The memory address to align.</param>
    /// <param name="align">The alignment boundary (must be a power of 2).</param>
    /// <returns>The smallest address greater than or equal to <paramref name="addr"/> that is aligned to <paramref name="align"/>.</returns>
    /// <remarks>
    /// This method rounds up the address to the next multiple of the alignment.
    /// If the address is already aligned, it returns the same address.
    /// The alignment parameter must be a power of 2 for optimal performance.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong GetAlignedAddress(ulong addr, uint align)
    {
        ulong offs = addr % align;
        return offs != 0 ? addr + (align - offs) : addr;
    }
}
