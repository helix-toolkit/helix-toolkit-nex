namespace HelixToolkit.Nex.Material;

/// <summary>
/// Simple unlit material properties (color + optional texture).
/// </summary>
public partial class UnlitMaterialProperties : MaterialProperties
{
    /// <summary>
    /// Base color (albedo) of the material as a four-component vector.
    /// </summary>
    [Observable]
    private Vector4 _albedo = Vector4.One;

    /// <summary>
    /// Optional albedo texture resource. Engine-specific type (TextureResource) is used to allow direct binding.
    /// </summary>
    [Observable]
    private TextureResource _albedoTexture = TextureResource.Null;

    /// <summary>
    /// Optional sampler for the texture.
    /// </summary>
    [Observable]
    private SamplerResource _albedoSampler = SamplerResource.Null;
}
