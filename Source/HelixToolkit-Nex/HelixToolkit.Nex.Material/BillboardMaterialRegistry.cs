using System.Collections.Concurrent;

namespace HelixToolkit.Nex.Material;

/// <summary>
/// Represents a registered billboard material type with its unique ID and shader implementation.
/// <para>
/// Each registration provides the GLSL code for the <c>outputColor()</c> function
/// that determines how a billboard is shaded. The fragment shader template injects this
/// code between the template markers in <c>psBillboardTemplate.glsl</c>.
/// </para>
/// </summary>
public sealed class BillboardMaterialRegistration : IMaterialRegistration
{
    /// <summary>
    /// Unique identifier for this billboard material type.
    /// </summary>
    public required MaterialTypeId TypeId { get; init; }

    /// <summary>
    /// Unique name for this billboard material type (e.g., "Default", "SDFFont").
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// GLSL implementation for the <c>outputColor()</c> function.
    /// <para>
    /// Available accessors: <c>getUV()</c>, <c>getColor()</c>, <c>getBillboardWidth()</c>,
    /// <c>getBillboardHeight()</c>, <c>getTextureId()</c>, <c>getSamplerId()</c>.
    /// Bindless texture helpers from <c>HeaderFrag.glsl</c> are available.
    /// </para>
    /// </summary>
    public required string OutputColorImplementation { get; init; }

    /// <inheritdoc/>
    public string? GetColorOutputImplCode() => OutputColorImplementation;

    /// <summary>
    /// Optional additional GLSL code to inject before the <c>outputColor()</c> function
    /// (helper functions, buffer references, etc.).
    /// </summary>
    public string? AdditionalCode { get; init; }

    /// <summary>
    /// Gets the blend configuration for the color attachment. If null, the default blend state (opaque) is used.
    /// No need to set <see cref="ColorAttachment.Format"/>. The pipeline will use the format of the render target's color attachment.
    /// </summary>
    public ColorAttachment? BlendConfig { get; init; } = null;

    /// <inheritdoc/>
    public bool SupportPointerRing { get; set; } = false;

    public BillboardMaterialRegistration WithPointerRingSupport()
    {
        SupportPointerRing = true;
        return this;
    }
}

/// <summary>
/// Global registry for billboard material types. Maps billboard material type names to unique IDs
/// and their shader implementations for pipeline generation.
/// </summary>
public static class BillboardMaterialRegistry
{
    private static readonly ConcurrentDictionary<
        string,
        BillboardMaterialRegistration
    > _registrations = new(StringComparer.OrdinalIgnoreCase);

    private static readonly ConcurrentDictionary<MaterialTypeId, string> _idToName = new();
    private static uint _nextTypeId = 1; // Reserve 0 for "Default" material type
    private static readonly object _lockObj = new();

    static BillboardMaterialRegistry()
    {
        RegisterBuiltInTypes();
    }

    private static void RegisterBuiltInTypes()
    {
        // Default billboard material (ID = 0) — samples bindless texture and multiplies by vertex color
        Register(
            new BillboardMaterialRegistration
            {
                TypeId = 0,
                Name = "Default",
                OutputColorImplementation = """
                    vec4 color = getColor();

                    // Sample bindless texture when available
                    if (getTextureId() > 0u) {
                        vec4 texColor = textureBindless2D(getTextureId(), getSamplerId(), getUV());
                        color *= texColor;
                    }

                    return color;
                """,
            }
        );

        // SDFFont billboard material (ID = 1) — MSDF-based text rendering
        // Following the approach from https://www.redblobgames.com/articles/sdf-fonts/
        // Converts texel to distance_em, then uses screen_px_scale for anti-aliasing.
        // Applies 2×2 supersampling for small text and premultiplied alpha.
        Register(
            new BillboardMaterialRegistration
            {
                TypeId = 1,
                Name = "SDFFont",
                OutputColorImplementation = """
                    vec2 aemrange = getSdfAemrange();
                    vec2 atlas_size = getSdfAtlasSize();
                    float glyph_cell_size = getSdfGlyphCellSize();
                    const float threshold_em = 0.0;
                    const float SUPERSAMPLE_THRESHOLD = 20.0;

                    vec2 uv = getUV();

                    float screen_px_scale = max(length(atlas_size * fwidth(uv)), 1.0);
                    float inverse_width = screen_px_scale * glyph_cell_size;

                    float opacity;
                    float maxDim = max(getBillboardWidth(), getBillboardHeight());

                    if (maxDim < SUPERSAMPLE_THRESHOLD) {
                        vec2 step = fwidth(uv) * 0.25;
                        float sum = 0.0;
                        for (int dy = -1; dy <= 1; dy += 2) {
                            for (int dx = -1; dx <= 1; dx += 2) {
                                vec2 suv = uv + vec2(float(dx), float(dy)) * step;
                                vec3 s = textureBindless2D(getTextureId(), getSamplerId(), suv).rgb;
                                float texel = max(min(s.r, s.g), min(max(s.r, s.g), s.b));
                                float dist_em = mix(aemrange[1], aemrange[0], texel);
                                sum += clamp((threshold_em - dist_em) * inverse_width + 0.5, 0.0, 1.0);
                            }
                        }
                        opacity = sum * 0.25;
                    } else {
                        vec3 s = textureBindless2D(getTextureId(), getSamplerId(), uv).rgb;
                        float texel = max(min(s.r, s.g), min(max(s.r, s.g), s.b));
                        float dist_em = mix(aemrange[1], aemrange[0], texel);
                        opacity = clamp((threshold_em - dist_em) * inverse_width + 0.5, 0.0, 1.0);
                    }

                    vec4 color = getColor();
                    color.a *= opacity;
                    color.rgb *= color.a;
                    return color;
                """,
                BlendConfig = new ColorAttachment
                {
                    BlendEnabled = true,
                    RgbBlendOp = BlendOp.Add,
                    AlphaBlendOp = BlendOp.Add,
                    SrcRGBBlendFactor = BlendFactor.One,
                    SrcAlphaBlendFactor = BlendFactor.One,
                    DstRGBBlendFactor = BlendFactor.OneMinusSrcAlpha,
                    DstAlphaBlendFactor = BlendFactor.OneMinusSrcAlpha,
                },
            }
        );
    }

    /// <summary>
    /// Registers a new billboard material type with the system.
    /// </summary>
    public static void Register(BillboardMaterialRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        if (string.IsNullOrWhiteSpace(registration.Name))
        {
            throw new ArgumentException(
                "Billboard material type name cannot be null or empty.",
                nameof(registration)
            );
        }

        lock (_lockObj)
        {
            if (_registrations.ContainsKey(registration.Name))
            {
                throw new ArgumentException(
                    $"Billboard material type '{registration.Name}' is already registered.",
                    nameof(registration)
                );
            }

            if (_idToName.ContainsKey(registration.TypeId))
            {
                throw new ArgumentException(
                    $"Billboard material type ID '{registration.TypeId}' is already used by '{_idToName[registration.TypeId]}'.",
                    nameof(registration)
                );
            }

            _registrations[registration.Name] = registration;
            _idToName[registration.TypeId] = registration.Name;

            if (registration.TypeId >= _nextTypeId)
            {
                _nextTypeId = registration.TypeId + 1;
            }
        }
    }

    /// <summary>
    /// Registers a new billboard material type with an auto-assigned ID.
    /// </summary>
    /// <returns>The assigned type ID.</returns>
    public static MaterialTypeId Register(
        string name,
        string outputColorImpl,
        string? additionalCode = null
    )
    {
        lock (_lockObj)
        {
            uint typeId = _nextTypeId++;

            var registration = new BillboardMaterialRegistration
            {
                TypeId = typeId,
                Name = name,
                OutputColorImplementation = outputColorImpl,
                AdditionalCode = additionalCode,
            };

            Register(registration);
            return typeId;
        }
    }

    /// <summary>
    /// Gets a billboard material type registration by name.
    /// </summary>
    public static bool TryGetByName(string name, out BillboardMaterialRegistration? registration)
    {
        return _registrations.TryGetValue(name, out registration);
    }

    /// <summary>
    /// Gets a billboard material type registration by ID.
    /// </summary>
    public static bool TryGetById(
        MaterialTypeId typeId,
        out BillboardMaterialRegistration? registration
    )
    {
        registration = null;
        return _idToName.TryGetValue(typeId, out var name)
            && _registrations.TryGetValue(name, out registration);
    }

    /// <summary>
    /// Gets all registered billboard material types.
    /// </summary>
    public static IReadOnlyCollection<BillboardMaterialRegistration> GetAllRegistrations()
    {
        return _registrations.Values.ToArray();
    }

    /// <summary>
    /// Gets the billboard material type ID for a given name.
    /// </summary>
    public static MaterialTypeId? GetTypeId(string name)
    {
        return _registrations.TryGetValue(name, out var registration) ? registration.TypeId : null;
    }

    /// <summary>
    /// Attempts to retrieve the <see cref="MaterialTypeId"/> associated with the specified name.
    /// </summary>
    /// <remarks>This method does not throw an exception if the specified name is not found. Instead, it
    /// returns <see langword="false"/> and sets <paramref name="typeId"/> to its default value.</remarks>
    /// <param name="name">The name of the material type to look up. This value cannot be <see langword="null"/>.</param>
    /// <param name="typeId">When this method returns, contains the <see cref="MaterialTypeId"/> associated with the specified name,
    /// if the lookup succeeds; otherwise, contains the default value of <see cref="MaterialTypeId"/>.</param>
    /// <returns><see langword="true"/> if the lookup succeeds and a <see cref="MaterialTypeId"/> is found for the specified
    /// name; otherwise, <see langword="false"/>.</returns>
    public static bool TryGetTypeId(string name, out MaterialTypeId typeId)
    {
        typeId = default;
        if (_registrations.TryGetValue(name, out var registration))
        {
            typeId = registration.TypeId;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Gets the billboard material type name for a given ID.
    /// </summary>
    public static string? GetTypeName(MaterialTypeId typeId)
    {
        return _idToName.TryGetValue(typeId, out var name) ? name : null;
    }

    /// <summary>
    /// Determines whether the specified <see cref="MaterialTypeId"/> is registered.
    /// </summary>
    public static bool HasTypeId(MaterialTypeId typeId)
    {
        return _idToName.ContainsKey(typeId);
    }
}
