namespace HelixToolkit.Nex.Textures;

/// <summary>
/// Identifies a single color channel within a pixel.
/// </summary>
public enum ChannelComponent : byte
{
    /// <summary>The red channel (byte offset 0 in RGBA layout).</summary>
    R = 0,

    /// <summary>The green channel (byte offset 1 in RGBA layout).</summary>
    G = 1,

    /// <summary>The blue channel (byte offset 2 in RGBA layout).</summary>
    B = 2,

    /// <summary>The alpha channel (byte offset 3 in RGBA layout).</summary>
    A = 3,
}
