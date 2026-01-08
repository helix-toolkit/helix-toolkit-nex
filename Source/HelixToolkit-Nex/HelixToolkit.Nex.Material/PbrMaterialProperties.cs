namespace HelixToolkit.Nex.Material;

[StructLayout(LayoutKind.Sequential)]
public struct PbrVariables
{
    public static readonly uint SizeInBytes = NativeHelper.SizeOf<PbrVariables>();

    static PbrVariables()
    {
        Debug.Assert(SizeInBytes == 32);
    }

    public Vector4 BaseColor;
    public float Metallic;
    public float Roughness;
    private Vector2 _padding;

    public PbrVariables(Vector4 baseColor, float metallic, float roughness)
    {
        BaseColor = baseColor;
        Metallic = metallic;
        Roughness = roughness;
        _padding = Vector2.Zero;
    }

    public static readonly PbrVariables Default = new(new Vector4(1, 1, 1, 1), 0.0f, 1.0f);
}

/// <summary>
/// Physically Based Rendering (PBR) material properties (Metallic-Roughness workflow).
/// Keep this POCO-style so it can be serialized or uploaded to GPU easily.
/// </summary>
public partial class PbrMaterialProperties : MaterialProperties
{
    [Observable]
    private PbrVariables _variables = PbrVariables.Default;

    /// <summary>
    /// Texture resource used for the material's base color.
    /// </summary>
    [Observable]
    private TextureResource _baseColorTexture = TextureResource.Null;

    /// <summary>
    /// Sampler resource used for sampling the base color texture.
    /// </summary>
    [Observable]
    private SamplerResource _baseColorSampler = SamplerResource.Null;

    /// <summary>
    /// Texture resource that defines the metallic and roughness properties of the material.
    /// </summary>
    [Observable]
    private TextureResource _metallicRoughnessTexture = TextureResource.Null;

    /// <summary>
    /// Sampler resource used for metallic-roughness texture sampling.
    /// </summary>
    [Observable]
    private SamplerResource _metallicRoughnessSampler = SamplerResource.Null;

    /// <summary>
    /// Normal map texture used to simulate surface details and lighting effects.
    /// </summary>
    [Observable]
    private TextureResource _normalTexture = TextureResource.Null;

    /// <summary>
    /// Sampler resource used for sampling the normal map texture.
    /// </summary>
    [Observable]
    private SamplerResource _normalSampler = SamplerResource.Null;
}
