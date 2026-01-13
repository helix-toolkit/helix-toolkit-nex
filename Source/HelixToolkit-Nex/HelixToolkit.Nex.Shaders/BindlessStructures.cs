using System.Numerics;
using System.Runtime.InteropServices;

namespace HelixToolkit.Nex.Shaders;

/// <summary>
/// Light grid tile data for Forward+ culling.
/// Each tile contains indices of lights affecting it.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct LightGridTile
{
    public uint LightCount;
    public uint LightIndexOffset;

    public static readonly uint SizeInBytes = (uint)Marshal.SizeOf<LightGridTile>();
}

/// <summary>
/// Forward+ rendering parameters passed via push constants.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 16)]
public struct ForwardPlusConstants
{
    public Matrix4x4 ViewProjection;
    public Matrix4x4 InverseViewProjection;
    public Vector3 CameraPosition;
    public float Time;
    public uint VertexBufferAddress; // Lower 32 bits of GPU address
    public uint LightBufferAddress; // Lower 32 bits of GPU address
    public uint LightCount;
    public uint TileSize; // Tile size in pixels (e.g., 16x16)
    public Vector2 ScreenDimensions;
    public Vector2 TileCount; // Number of tiles in X and Y
    public uint BaseColorTexIndex;
    public uint MetallicRoughnessTexIndex;
    public uint NormalTexIndex;
    public uint SamplerIndex;

    public static readonly uint SizeInBytes = (uint)Marshal.SizeOf<ForwardPlusConstants>();
}

/// <summary>
/// Light culling compute shader constants.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 16)]
public struct LightCullingConstants
{
    public Matrix4x4 InverseProjection;
    public Vector2 ScreenDimensions;
    public Vector2 TileCount;
    public uint LightCount;
    public float ZNear;
    public float ZFar;
    private readonly float _padding;

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
