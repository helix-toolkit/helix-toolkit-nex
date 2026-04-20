using System.Collections.Concurrent;
using HelixToolkit.Nex.Shaders.Frag;

namespace HelixToolkit.Nex.Material;

/// <summary>
/// Represents a registered material type with its unique ID and shader implementation.
/// </summary>
public sealed class PBRMaterialRegistration : IMaterialRegistration
{
    /// <summary>
    /// Unique identifier for this material type. Used as specialization constant value.
    /// </summary>
    public required MaterialTypeId TypeId { get; init; }

    /// <summary>
    /// Unique name for this material type (e.g., "PBR", "Unlit", "DebugTiles").
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// GLSL implementation for the createPBRMaterial() function.
    /// If null, uses the default template implementation.
    /// </summary>
    public string? CreateMaterialImplementation { get; init; }

    /// <inheritdoc/>
    public string? GetColorOutputImplCode() => OutputColorImplementation;

    /// <summary>
    /// GLSL implementation for the outputColor() case.
    /// This is the main shading logic for this material type.
    /// </summary>
    public required string OutputColorImplementation { get; init; }

    /// <summary>
    /// Optional custom main function override.
    /// If provided, completely replaces the main function.
    /// </summary>
    public string? CustomMainImplementation { get; init; }

    /// <summary>
    /// Optional additional GLSL code to inject (helper functions, etc.)
    /// </summary>
    public string? AdditionalCode { get; init; }

    /// <summary>
    /// Optional GLSL code declaring the custom properties struct and its
    /// <c>buffer_reference</c> block for this material type.
    /// <para>
    /// When provided, this block is injected into the shader at the
    /// <c>// TEMPLATE_CUSTOM_STRUCTS</c> injection point so the
    /// <see cref="OutputColorImplementation"/> can cast
    /// <c>getCustomBufferAddress()</c> to the declared buffer reference and
    /// read per-material custom data.
    /// </para>
    /// <example>
    /// <code>
    /// struct MyCustomProps {
    ///     vec4 tintColor;
    ///     float intensity;
    ///     float _pad0, _pad1, _pad2;
    /// };
    ///
    /// layout(buffer_reference, std430, buffer_reference_align = 16) readonly buffer MyCustomBuffer {
    ///     MyCustomProps props;
    /// };
    /// </code>
    /// </example>
    /// </summary>
    public string? CustomBufferGlsl { get; init; }

    /// <inheritdoc/>
    public bool SupportPointerRing { get; set; } = false;

    /// <summary>
    /// Describes the custom GPU buffer layout for this material type so the C# side can
    /// manage uploads correctly.  This is optional metadata — the shader only needs
    /// <see cref="CustomBufferGlsl"/>; this description helps tooling and the
    /// <see cref="ICustomMaterialBuffer"/> implementation document expected alignment and size.
    /// </summary>
    public CustomBufferDescription? CustomBuffer { get; init; }

    public Func<string, PBRMaterial> BuilderFunction { get; init; } =
        (name) => new PBRMaterial(name);

    public PBRMaterialRegistration WithPointerRingSupport()
    {
        SupportPointerRing = true;
        return this;
    }
}

/// <summary>
/// Describes the expected GPU layout of a custom material buffer declared via
/// <see cref="PBRMaterialRegistration.CustomBufferGlsl"/>.
/// </summary>
public sealed class CustomBufferDescription
{
    /// <summary>
    /// Human-readable name used for GPU debug labels (e.g., "ToonMaterialBuffer").
    /// </summary>
    public required string DebugName { get; init; }

    /// <summary>
    /// Expected size of one element in bytes.  Must match the <c>std430</c> layout
    /// of the GLSL struct declared in <see cref="PBRMaterialRegistration.CustomBufferGlsl"/>.
    /// </summary>
    public required uint ElementSizeBytes { get; init; }

    /// <summary>
    /// Required buffer alignment in bytes (must be a power of two, typically 16).
    /// Matches the <c>buffer_reference_align</c> value in the GLSL declaration.
    /// </summary>
    public uint AlignmentBytes { get; init; } = 16;
}

/// <summary>
/// Global registry for material types. Maps material type names to unique IDs
/// and their shader implementations for uber shader generation.
/// </summary>
public static class PBRMaterialTypeRegistry
{
    private static readonly ConcurrentDictionary<string, PBRMaterialRegistration> _registrations =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly ConcurrentDictionary<MaterialTypeId, string> _idToName = new();
    private static uint _nextTypeId = 1; // Reserve 0 for "undefined" material type
    private static readonly object _lockObj = new();

    static PBRMaterialTypeRegistry()
    {
        // Register built-in material types
        RegisterBuiltInTypes();
    }

    private static void RegisterBuiltInTypes()
    {
        // PBR material type (default)
        Register(
            new PBRMaterialRegistration
            {
                TypeId = PBRShadingMode.PBR,
                Name = PBRShadingMode.PBR.ToString(),
                OutputColorImplementation =
                    @"
                    PBRMaterial material = createPBRMaterial();
                    vec4 color = forwardPlusLighting(material);
                    color.rgb += material.emissive; // Add emissive after lighting
                    return color;
                    ",
            }
        ).WithPointerRingSupport();

        // Unlit material type
        Register(
            new PBRMaterialRegistration
            {
                TypeId = PBRShadingMode.Unlit,
                Name = PBRShadingMode.Unlit.ToString(),
                OutputColorImplementation =
                    @"
                    PBRMaterial material = createPBRMaterial();
                    return nonLitOutputColor(material);
                    ",
            }
        ).WithPointerRingSupport();

        // Debug tile light count visualization
        Register(
            new PBRMaterialRegistration
            {
                TypeId = PBRShadingMode.DebugTileLightCount,
                Name = PBRShadingMode.DebugTileLightCount.ToString(),
                OutputColorImplementation =
                    @"
                    return debugTileLighting();
                    ",
            }
        );

        // Normal visualization
        Register(
            new PBRMaterialRegistration
            {
                TypeId = PBRShadingMode.Normal,
                Name = PBRShadingMode.Normal.ToString(),
                OutputColorImplementation =
                    @"
                    return vec4(fragNormal, 1.0);
                    ",
            }
        );

        Register(
            new PBRMaterialRegistration
            {
                TypeId = PBRShadingMode.Flat,
                Name = PBRShadingMode.Flat.ToString(),
                OutputColorImplementation =
                    @"
                    PBRMaterial material = createPBRMaterialFlatNormal();
                    vec4 color = forwardPlusLighting(material);
                    color.rgb += material.emissive; // Add emissive after lighting
                    return color;
                    ",
            }
        ).WithPointerRingSupport();
    }

    /// <summary>
    /// Registers a new material type with the system.
    /// </summary>
    /// <param name="registration">Material type registration information.</param>
    /// <exception cref="ArgumentException">Thrown if a material with the same name or ID is already registered.</exception>
    public static PBRMaterialRegistration Register(PBRMaterialRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        if (string.IsNullOrWhiteSpace(registration.Name))
        {
            throw new ArgumentException(
                "Material type name cannot be null or empty.",
                nameof(registration)
            );
        }

        lock (_lockObj)
        {
            if (_registrations.ContainsKey(registration.Name))
            {
                throw new ArgumentException(
                    $"Material type '{registration.Name}' is already registered.",
                    nameof(registration)
                );
            }

            if (_idToName.ContainsKey(registration.TypeId))
            {
                throw new ArgumentException(
                    $"Material type ID '{registration.TypeId}' is already used by '{_idToName[registration.TypeId]}'.",
                    nameof(registration)
                );
            }

            _registrations[registration.Name] = registration;
            _idToName[registration.TypeId] = registration.Name;

            // Update next available ID
            if (registration.TypeId >= _nextTypeId)
            {
                _nextTypeId = registration.TypeId + 1;
            }
        }
        return registration;
    }

    /// <summary>
    /// Registers a new material type with an auto-assigned ID.
    /// </summary>
    /// <param name="name">Unique name for the material type.</param>
    /// <param name="outputColorImpl">GLSL implementation for outputColor case.</param>
    /// <param name="createMaterialImpl">Optional GLSL for createPBRMaterial override.</param>
    /// <param name="additionalCode">Optional additional GLSL code.</param>
    /// <returns>The assigned type ID.</returns>
    public static PBRMaterialRegistration Register(
        string name,
        string outputColorImpl,
        string? createMaterialImpl = null,
        string? additionalCode = null
    )
    {
        lock (_lockObj)
        {
            uint typeId = _nextTypeId++;

            var registration = new PBRMaterialRegistration
            {
                TypeId = typeId,
                Name = name,
                OutputColorImplementation = outputColorImpl,
                CreateMaterialImplementation = createMaterialImpl,
                AdditionalCode = additionalCode,
            };

            return Register(registration);
        }
    }

    /// <summary>
    /// Gets a material type registration by name.
    /// </summary>
    /// <param name="name">Material type name.</param>
    /// <param name="registration">The registration if found.</param>
    /// <returns>True if the material type exists.</returns>
    public static bool TryGetByName(string name, out PBRMaterialRegistration? registration)
    {
        return _registrations.TryGetValue(name, out registration);
    }

    /// <summary>
    /// Gets a material type registration by ID.
    /// </summary>
    /// <param name="typeId">Material type ID.</param>
    /// <param name="registration">The registration if found.</param>
    /// <returns>True if the material type exists.</returns>
    public static bool TryGetById(MaterialTypeId typeId, out PBRMaterialRegistration? registration)
    {
        registration = null;
        return _idToName.TryGetValue(typeId, out var name)
            && _registrations.TryGetValue(name, out registration);
    }

    /// <summary>
    /// Gets all registered material types.
    /// </summary>
    /// <returns>Collection of all material type registrations.</returns>
    public static IReadOnlyCollection<PBRMaterialRegistration> GetAllRegistrations()
    {
        return _registrations.Values.ToArray();
    }

    /// <summary>
    /// Gets the material type ID for a given name.
    /// </summary>
    /// <param name="name">Material type name.</param>
    /// <returns>The type ID, or null if not found.</returns>
    public static uint? GetTypeId(string name)
    {
        return _registrations.TryGetValue(name, out var registration) ? registration.TypeId : null;
    }

    /// <summary>
    /// Gets the material type name for a given ID.
    /// </summary>
    /// <param name="typeId">Material type ID.</param>
    /// <returns>The type name, or null if not found.</returns>
    public static string? GetTypeName(MaterialTypeId typeId)
    {
        return _idToName.TryGetValue(typeId, out var name) ? name : null;
    }

    /// <summary>
    /// Determines whether the specified <see cref="MaterialTypeId"/> exists in the collection.
    /// </summary>
    /// <param name="typeId">The material type identifier to check for existence.</param>
    /// <returns><see langword="true"/> if the collection contains the specified material type identifier; otherwise, <see
    /// langword="false"/>.</returns>
    public static bool HasTypeId(MaterialTypeId typeId)
    {
        return _idToName.ContainsKey(typeId);
    }

    /// <summary>
    /// Clears all custom registrations (keeps built-in types).
    /// Useful for testing.
    /// </summary>
    public static void ClearCustomRegistrations()
    {
        lock (_lockObj)
        {
            var builtInNames = new[]
            {
                PBRShadingMode.PBR.ToString(),
                PBRShadingMode.Unlit.ToString(),
                PBRShadingMode.DebugTileLightCount.ToString(),
                PBRShadingMode.Normal.ToString(),
            };
            var toRemove = _registrations
                .Keys.Where(k => !builtInNames.Contains(k, StringComparer.OrdinalIgnoreCase))
                .ToList();

            foreach (var key in toRemove)
            {
                if (_registrations.TryRemove(key, out var reg))
                {
                    _idToName.TryRemove(reg.TypeId, out _);
                }
            }

            // Reset next ID to first available after built-ins
            _nextTypeId = 4;
        }
    }
}
