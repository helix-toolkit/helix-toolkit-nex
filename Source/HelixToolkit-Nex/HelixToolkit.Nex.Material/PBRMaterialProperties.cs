namespace HelixToolkit.Nex.Material;

/// <summary>
/// Marker struct used as the resource type tag for material property pool entries.
/// </summary>
public struct MaterialPropertyResource { }

/// <summary>
/// Specifies the operation that was performed on a material property entry.
/// </summary>
public enum MaterialPropertyOp
{
    /// <summary>A new material property entry was created.</summary>
    Create,

    /// <summary>An existing material property entry was updated.</summary>
    Update,

    /// <summary>An existing material property entry was destroyed.</summary>
    Destroy,
}

/// <summary>
/// Event published on the <see cref="EventBus"/> whenever a material property entry is
/// created, updated, or destroyed.
/// </summary>
public readonly struct MaterialPropsUpdatedEvent(
    MaterialTypeId materialTypeId,
    uint index,
    MaterialPropertyOp operation
) : IEvent
{
    /// <summary>Gets the identifier of the material type that owns this entry.</summary>
    public MaterialTypeId MaterialTypeId { get; } = materialTypeId;

    /// <summary>Gets the pool index of the material property entry that was affected.</summary>
    public uint Index { get; } = index;

    /// <summary>Gets the operation that triggered this event.</summary>
    public MaterialPropertyOp Operation { get; } = operation;
}

/// <summary>
/// Manages the PBR (Physically Based Rendering) material properties for a single material
/// instance, backed by a pooled <see cref="PBRProperties"/> entry.
/// Publish change notifications via the <see cref="EventBus"/> whenever a property value is modified.
/// Implements <see cref="IDisposable"/> to release the pooled entry when no longer needed.
/// </summary>
public sealed class PBRMaterialProperties : IDisposable
{
    private static readonly EventBus _eventBus = EventBus.Instance;
    private readonly Pool<MaterialPropertyResource, PBRProperties>? _pool;
    private readonly Handle<MaterialPropertyResource> _handle =
        Handle<MaterialPropertyResource>.Null;
    internal static readonly PBRProperties DefaultProperties = new()
    {
        Albedo = new(1, 1, 1),
        Opacity = 1,
        DisplaceScale = 1,
        BumpScale = 1,
        DisplaceBase = 0.5f,
    };

    /// <summary>Gets the identifier of the material type that owns this instance.</summary>
    public readonly MaterialTypeId MaterialTypeId = 0;

    /// <summary>Gets a reference to the underlying <see cref="PBRProperties"/> stored in the pool.</summary>
    public ref PBRProperties Properties => ref _pool!.GetRef(_handle)!;

    /// <summary>Gets a value indicating whether this instance is backed by a valid pool entry.</summary>
    public bool Valid => _pool is not null && _handle.Valid;

    /// <summary>Gets the zero-based index of this material's entry within the pool.</summary>
    public uint Index => _handle.Index;

    /// <summary>
    /// Gets or sets the base (albedo) color of the material.
    /// </summary>
    /// <value>A <see cref="Color4"/> representing the diffuse reflectance of the surface.</value>
    public Color4 Albedo
    {
        set
        {
            var newValue = value.ToVector3();
            if (Properties.Albedo == newValue)
            {
                return;
            }
            Properties.Albedo = newValue;
            NotifyUpdated();
        }
        get => new(Properties.Albedo);
    }

    /// <summary>
    /// Gets or sets the opacity of the material.
    /// </summary>
    /// <value>A value in the range [0, 1], where <c>0</c> is fully transparent and <c>1</c> is fully opaque.</value>
    public float Opacity
    {
        set
        {
            if (Properties.Opacity == value)
            {
                return;
            }
            Properties.Opacity = value;
            NotifyUpdated();
        }
        get => Properties.Opacity;
    }

    /// <summary>
    /// Gets or sets the metallic factor of the PBR material.
    /// </summary>
    /// <value>
    /// A value in the range [0, 1], where <c>0</c> represents a fully dielectric (non-metallic)
    /// surface and <c>1</c> represents a fully metallic surface.
    /// </value>
    public float Metallic
    {
        set
        {
            if (Properties.Metallic == value)
            {
                return;
            }
            Properties.Metallic = value;
            NotifyUpdated();
        }
        get => Properties.Metallic;
    }

    /// <summary>
    /// Gets or sets the emissive color of the material, representing light emitted by the surface.
    /// </summary>
    public Color4 Emissive
    {
        set
        {
            var newValue = value.ToVector3();
            if (Properties.Emissive == newValue)
            {
                return;
            }
            Properties.Emissive = value.ToVector3();
            NotifyUpdated();
        }
        get => new(Properties.Emissive);
    }

    /// <summary>
    /// Gets or sets the roughness of the material surface.
    /// </summary>
    /// <value>A value in the range [0, 1], where <c>0</c> is perfectly smooth and <c>1</c> is fully rough.</value>
    public float Roughness
    {
        set
        {
            if (Properties.Roughness == value)
            {
                return;
            }
            Properties.Roughness = value;
            NotifyUpdated();
        }
        get => Properties.Roughness;
    }

    /// <summary>
    /// Gets or sets the ambient color contribution of the material.
    /// </summary>
    public Color4 Ambient
    {
        set
        {
            var newValue = value.ToVector3();
            if (Properties.Ambient == newValue)
            {
                return;
            }
            Properties.Ambient = value.ToVector3();
            NotifyUpdated();
        }
        get => new(Properties.Ambient);
    }

    /// <summary>
    /// Gets or sets the ambient occlusion (AO) factor of the material.
    /// </summary>
    /// <value>A value in the range [0, 1], where <c>0</c> means fully occluded and <c>1</c> means no occlusion.</value>
    public float Ao
    {
        set
        {
            if (Properties.Ao == value)
            {
                return;
            }
            Properties.Ao = value;
            NotifyUpdated();
        }
        get => Properties.Ao;
    }

    /// <summary>
    /// Gets or sets the blend factor between the material's albedo color and per-vertex colors.
    /// </summary>
    /// <value>A value in the range [0, 1], where <c>0</c> uses only the albedo and <c>1</c> uses only vertex colors.</value>
    public float VertexColorMix
    {
        set
        {
            if (Properties.VertexColorMix == value)
            {
                return;
            }
            Properties.VertexColorMix = value;
            NotifyUpdated();
        }
        get => Properties.VertexColorMix;
    }

    /// <summary>
    /// Gets or sets the strength of the clear-coat layer applied on top of the base material.
    /// </summary>
    /// <value>A value in the range [0, 1], where <c>0</c> disables the clear coat and <c>1</c> applies it at full strength.</value>
    public float ClearCoatStrength
    {
        set
        {
            if (Properties.ClearCoatStrength == value)
            {
                return;
            }
            Properties.ClearCoatStrength = value;
            NotifyUpdated();
        }
        get => Properties.ClearCoatStrength;
    }

    /// <summary>
    /// Gets or sets the roughness of the clear-coat layer.
    /// </summary>
    /// <value>A value in the range [0, 1], where <c>0</c> is a perfectly smooth coat and <c>1</c> is fully rough.</value>
    public float ClearCoatRoughness
    {
        set
        {
            if (Properties.ClearCoatRoughness == value)
            {
                return;
            }
            Properties.ClearCoatRoughness = value;
            NotifyUpdated();
        }
        get => Properties.ClearCoatRoughness;
    }

    /// <summary>
    /// Gets or sets the reflectance of the material at normal incidence (Fresnel F0 for dielectrics).
    /// </summary>
    /// <value>A value in the range [0, 1] controlling the specular reflectivity of non-metallic surfaces.</value>
    public float Reflectance
    {
        set
        {
            if (Properties.Reflectance == value)
            {
                return;
            }
            Properties.Reflectance = value;
            NotifyUpdated();
        }
        get => Properties.Reflectance;
    }

    private TextureResource _albedoMap = TextureResource.Null;

    /// <summary>
    /// Gets or sets the albedo (base color) texture map.
    /// Setting this updates <see cref="PBRProperties.AlbedoTexIndex"/> in the pooled data.
    /// </summary>
    public TextureResource AlbedoMap
    {
        set
        {
            if (_albedoMap == value)
            {
                return;
            }
            _albedoMap = value;
            Properties.AlbedoTexIndex = value.Index;
            NotifyUpdated();
        }
        get => _albedoMap;
    }

    private TextureResource _normalMap = TextureResource.Null;

    /// <summary>
    /// Gets or sets the normal map texture used for surface detail lighting.
    /// Setting this updates <see cref="PBRProperties.NormalTexIndex"/> in the pooled data.
    /// </summary>
    public TextureResource NormalMap
    {
        set
        {
            if (_normalMap == value)
            {
                return;
            }
            _normalMap = value;
            Properties.NormalTexIndex = value.Index;
            NotifyUpdated();
        }
        get => _normalMap;
    }

    private TextureResource _metallicRoughnessMap = TextureResource.Null;

    /// <summary>
    /// Gets or sets the combined metallic-roughness texture map.
    /// The blue channel encodes metallic and the green channel encodes roughness (glTF convention).
    /// Setting this updates <see cref="PBRProperties.MetallicRoughnessTexIndex"/> in the pooled data.
    /// </summary>
    public TextureResource MetallicRoughnessMap
    {
        set
        {
            if (_metallicRoughnessMap == value)
            {
                return;
            }
            _metallicRoughnessMap = value;
            Properties.MetallicRoughnessTexIndex = value.Index;
            NotifyUpdated();
        }
        get => _metallicRoughnessMap;
    }

    private TextureResource _aoMap = TextureResource.Null;

    /// <summary>
    /// Gets or sets the ambient occlusion texture resource used for shading effects.
    /// </summary>
    /// <remarks>Changing this property updates the associated texture index and notifies listeners of the
    /// update. The ambient occlusion map enhances the perception of depth and surface detail in rendered
    /// materials.</remarks>
    public TextureResource AoMap
    {
        set
        {
            if (_aoMap == value)
            {
                return;
            }
            _aoMap = value;
            Properties.AoTexIndex = value.Index;
            NotifyUpdated();
        }
        get => _aoMap;
    }

    private TextureResource _bumpMap = TextureResource.Null;
    /// <summary>
    /// Gets or sets the bump map texture used for simulating surface detail through normal perturbation.
    /// </summary>
    public TextureResource BumpMap
    {
        set
        {
            if (_bumpMap == value)
            {
                return;
            }
            _bumpMap = value;
            Properties.BumpTexIndex = value.Index;
            NotifyUpdated();
        }
        get => _bumpMap;
    }

    private float _bumpScale = 1.0f;
    public float BumpScale
    {
        set
        {
            if (Properties.BumpScale == value)
            {
                return;
            }
            Properties.BumpScale = value;
            NotifyUpdated();
        }
        get => _bumpScale;
    }

    private SamplerResource _sampler = SamplerResource.Null;

    /// <summary>
    /// Gets or sets the texture sampler used when sampling all texture maps on this material.
    /// Setting this updates <see cref="PBRProperties.SamplerIndex"/> in the pooled data.
    /// </summary>
    public SamplerResource Sampler
    {
        set
        {
            if (_sampler == value)
            {
                return;
            }
            _sampler = value;
            Properties.SamplerIndex = value.Index;
            NotifyUpdated();
        }
        get => _sampler;
    }

    private SamplerResource _displaceSampler = SamplerResource.Null;

    /// <summary>
    /// Gets or sets the sampler resource used for displacement mapping operations.
    /// </summary>
    /// <remarks>Changing this property updates the associated displacement sampler index and notifies
    /// listeners of the update. The sampler resource determines how texture sampling is performed during displacement
    /// mapping.</remarks>
    public SamplerResource DisplaceSampler
    {
        set
        {
            if (_displaceSampler == value)
            {
                return;
            }
            _displaceSampler = value;
            Properties.DisplaceSamplerIndex = value.Index;
            NotifyUpdated();
        }
        get => _displaceSampler;
    }

    private TextureResource _displaceMap = TextureResource.Null;

    /// <summary>
    /// Gets or sets the displacement map texture used for surface deformation effects.
    /// </summary>
    /// <remarks>Changing this property updates the associated displacement texture index and notifies
    /// listeners of the update. The displacement map is typically used in rendering to alter the appearance of a
    /// surface based on the provided texture.</remarks>
    public TextureResource DisplaceMap
    {
        set
        {
            if (_displaceMap == value)
            {
                return;
            }
            _displaceMap = value;
            Properties.DisplaceTexIndex = value.Index;
            NotifyUpdated();
        }
        get => _displaceMap;
    }

    private float _displaceScale = 1.0f;
    public float DisplaceScale
    {
        set
        {
            if (Properties.DisplaceScale == value)
            {
                return;
            }
            Properties.DisplaceScale = value;
            NotifyUpdated();
        }
        get => _displaceScale;
    }

    /// <summary>
    /// Initializes a new <see cref="PBRMaterialProperties"/> instance, allocating a pool entry
    /// and publishing a <see cref="MaterialPropertyOp.Create"/> event.
    /// </summary>
    /// <param name="materialTypeId">The identifier of the owning material type.</param>
    /// <param name="properties">Initial property values to store in the pool.</param>
    /// <param name="pool">The pool that manages <see cref="PBRProperties"/> entries.</param>
    internal PBRMaterialProperties(
        MaterialTypeId materialTypeId,
        ref PBRProperties properties,
        Pool<MaterialPropertyResource, PBRProperties> pool
    )
    {
        MaterialTypeId = materialTypeId;
        _pool = pool;
        _handle = _pool.Create(properties);
        _eventBus.Publish(
            new MaterialPropsUpdatedEvent(MaterialTypeId, Index, MaterialPropertyOp.Create)
        );
    }

    /// <summary>
    /// Publishes a <see cref="MaterialPropertyOp.Update"/> event on the <see cref="EventBus"/>
    /// to notify listeners that one or more properties have changed.
    /// Does nothing if this instance is not <see cref="Valid"/>.
    /// </summary>
    public void NotifyUpdated()
    {
        if (Valid)
        {
            _eventBus.Publish(
                new MaterialPropsUpdatedEvent(MaterialTypeId, Index, MaterialPropertyOp.Update)
            );
        }
    }

    private PBRMaterialProperties() { }

    private bool _disposedValue;

    private void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                var index = Index;
                AlbedoMap.Dispose();
                NormalMap.Dispose();
                MetallicRoughnessMap.Dispose();
                Sampler.Dispose();
                DisplaceMap.Dispose();
                DisplaceSampler.Dispose();
                _pool?.Destroy(_handle);
                _eventBus.Publish(
                    new MaterialPropsUpdatedEvent(MaterialTypeId, index, MaterialPropertyOp.Destroy)
                );
            }

            // TODO: free unmanaged resources (unmanaged objects) and override finalizer
            // TODO: set large fields to null
            _disposedValue = true;
        }
    }

    // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
    // ~MaterialProperties()
    // {
    //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
    //     Dispose(disposing: false);
    // }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Gets a sentinel <see cref="PBRMaterialProperties"/> instance that represents the absence
    /// of a material. <see cref="Valid"/> returns <see langword="false"/> for this instance.
    /// </summary>
    public static readonly PBRMaterialProperties Null = new();
}
