namespace HelixToolkit.Nex.Material;

/// <summary>
/// Base abstraction for all materials used by the rendering engine.
/// Concrete material types should inherit from <see cref="Material{TProperties}"/> to expose typed properties.
/// </summary>
public abstract class Material
{
    /// <summary>
    /// Optional per-material pipeline resource. Renderers may use this to cache a pipeline
    /// produced for this material (shaders, states, etc.). Default is <see cref="RenderPipelineResource.Null"/>.
    /// </summary>
    public virtual RenderPipelineResource Pipeline { get; } = RenderPipelineResource.Null;

    /// <summary>
    /// Optional friendly name useful for debugging or UI.
    /// Delegates to the underlying properties debug name when available.
    /// </summary>
    public virtual string? DebugName => null;
}

/// <summary>
/// Generic material base that exposes strongly-typed material properties.
/// TProperties must have a public parameterless ctor so the factory / serializers can create it.
/// </summary>
/// <typeparam name="TProperties">The concrete MaterialProperties type for this material.</typeparam>
public abstract class Material<TProperties> : Material
    where TProperties : MaterialProperties, new()
{
    private TProperties? _properties;

    /// <summary>
    /// Lazily-created properties instance for this material.
    /// Renderers and editors should mutate values on this object; it raises property change notifications.
    /// </summary>
    public TProperties Properties => _properties ??= new TProperties();

    /// <inheritdoc/>
    public override string? DebugName => Properties.DebugName;
}

/// <summary>
/// Base class for material properties. Designed to be lightweight and engine-agnostic.
/// Contains change-tracking so renderers can react when material data is modified.
/// </summary>
public abstract class MaterialProperties : ObservableObject
{
    /// <summary>
    /// Optional debug name.
    /// </summary>
    public string? DebugName { get; set; }

    /// <summary>
    /// Creates a shallow copy of the properties. Override for deeper copy semantics when needed.
    /// </summary>
    public virtual MaterialProperties Clone()
    {
        return (MaterialProperties)MemberwiseClone();
    }
}

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

/// <summary>
/// Physically Based Rendering (PBR) material properties (Metallic-Roughness workflow).
/// Keep this POCO-style so it can be serialized or uploaded to GPU easily.
/// </summary>
public partial class PbrMaterialProperties : MaterialProperties
{
    /// <summary>
    /// Base color used for rendering the material.
    /// </summary>
    [Observable]
    private Vector4 _baseColor = Vector4.One;

    /// <summary>
    /// Metallic value of the material.
    /// </summary>
    [Observable]
    private float _metallic = 1;

    /// <summary>
    /// Surface roughness value used in material rendering calculations.
    /// </summary>
    [Observable]
    private float _roughness = 1;

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
