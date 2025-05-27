namespace HelixToolkit.Nex.Maths;

public static class Alignment
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint GetAlignedSize(uint value, uint alignment)
    {
        return (value + alignment - 1) & ~(alignment - 1);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong GetAlignedAddress(ulong addr, uint align)
    {
        ulong offs = addr % align;
        return offs != 0 ? addr + (align - offs) : addr;
    }
}
