using System.Numerics;
using System.Runtime.InteropServices;

namespace HelixToolkit.Nex.Shaders;

///// <summary>
///// Light culling compute shader constants.
///// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 16)]
public struct LightCullingConstants
{
    public Matrix4x4 InverseProjection;
    public Vector2 ScreenDimensions;
    public Vector2 TileCount;
    public uint LightCount;
    public float ZNear;
    public float ZFar;
    public uint DepthTextureIndex;
    public uint SamplerIndex;
    public ulong LightBufferAddress;
    public ulong LightGridBufferAddress;
    public ulong LightIndexBufferAddress;
    public ulong GlobalCounterBufferAddress;

    public static readonly uint SizeInBytes = (uint)Marshal.SizeOf<LightCullingConstants>();
}

/// <summary>
/// Configuration for Forward+ rendering.
/// </summary>
public struct ForwardPlusConfig
{
    /// <summary>
    /// Size of each tile in pixels (typically 16x16 or 32x32).
    /// </summary>
    public uint TileSize;

    /// <summary>
    /// Maximum number of lights per tile.
    /// </summary>
    public uint MaxLightsPerTile;

    /// <summary>
    /// Whether to use compute shader for light culling.
    /// </summary>
    public bool UseComputeCulling;

    public static ForwardPlusConfig Default =>
        new()
        {
            TileSize = 16,
            MaxLightsPerTile = 256,
            UseComputeCulling = true,
        };
}
