namespace HelixToolkit.Nex.Material;

/// <summary>
/// Physically Based Rendering (PBR) material properties (Metallic-Roughness workflow).
/// Keep this POCO-style so it can be serialized or uploaded to GPU easily.
/// </summary>
public partial class PbrMaterialProperties : MaterialProperties
{
    public static readonly PBRMaterial Default = new()
    {
        Albedo = new Vector3(1.0f, 1.0f, 1.0f),
        Metallic = 0.0f,
        Roughness = 1.0f,
        Ao = 1.0f,
        Normal = new Vector3(0.0f, 0.0f, 1.0f),
        Emissive = new Vector3(0.0f, 0.0f, 0.0f),
        Opacity = 1.0f,
    };

    [Observable]
    private PBRMaterial _variables = Default;

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
