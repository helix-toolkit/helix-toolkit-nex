namespace HelixToolkit.Nex.Textures;

/// <summary>
/// Flags that control pitch computation behavior.
/// </summary>
[Flags]
public enum PitchFlags
{
    None = 0x0,
    LegacyDword = 0x1,
    Bpp24 = 0x10000,
    Bpp16 = 0x20000,
    Bpp8 = 0x40000,
}
