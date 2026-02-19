using System.Collections.Concurrent;
using HelixToolkit.Nex.Shaders.Frag;

namespace HelixToolkit.Nex.Material;

/// <summary>
/// Represents a registered material type with its unique ID and shader implementation.
/// </summary>
public sealed class MaterialTypeRegistration
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
}

/// <summary>
/// Global registry for material types. Maps material type names to unique IDs
/// and their shader implementations for uber shader generation.
/// </summary>
public static class MaterialTypeRegistry
{
    private static readonly ConcurrentDictionary<string, MaterialTypeRegistration> _registrations =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly ConcurrentDictionary<MaterialTypeId, string> _idToName = new();
    private static uint _nextTypeId = 1; // Reserve 0 for "undefined" material type
    private static readonly object _lockObj = new();

    static MaterialTypeRegistry()
    {
        // Register built-in material types
        RegisterBuiltInTypes();
    }

    private static void RegisterBuiltInTypes()
    {
        // PBR material type (default)
        Register(
            new MaterialTypeRegistration
            {
                TypeId = PBRShadingMode.PBR,
                Name = PBRShadingMode.PBR.ToString(),
                OutputColorImplementation =
                    @"
    PBRMaterial material = createPBRMaterial();
    forwardPlusLighting(material, finalColor);
    return;",
            }
        );

        // Unlit material type
        Register(
            new MaterialTypeRegistration
            {
                TypeId = PBRShadingMode.Unlit,
                Name = PBRShadingMode.Unlit.ToString(),
                OutputColorImplementation =
                    @"
    PBRMaterial material = createPBRMaterial();
    nonLitOutputColor(material, finalColor);
    return;",
            }
        );

        // Debug tile light count visualization
        Register(
            new MaterialTypeRegistration
            {
                TypeId = PBRShadingMode.DebugTileLightCount,
                Name = PBRShadingMode.DebugTileLightCount.ToString(),
                OutputColorImplementation =
                    @"
    debugTileLighting(finalColor);
    return;",
            }
        );

        // Normal visualization
        Register(
            new MaterialTypeRegistration
            {
                TypeId = PBRShadingMode.Normal,
                Name = PBRShadingMode.Normal.ToString(),
                OutputColorImplementation =
                    @"
    finalColor = vec4(fragNormal, 1.0);
    return;",
            }
        );
    }

    /// <summary>
    /// Registers a new material type with the system.
    /// </summary>
    /// <param name="registration">Material type registration information.</param>
    /// <exception cref="ArgumentException">Thrown if a material with the same name or ID is already registered.</exception>
    public static void Register(MaterialTypeRegistration registration)
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
    }

    /// <summary>
    /// Registers a new material type with an auto-assigned ID.
    /// </summary>
    /// <param name="name">Unique name for the material type.</param>
    /// <param name="outputColorImpl">GLSL implementation for outputColor case.</param>
    /// <param name="createMaterialImpl">Optional GLSL for createPBRMaterial override.</param>
    /// <param name="additionalCode">Optional additional GLSL code.</param>
    /// <returns>The assigned type ID.</returns>
    public static uint Register(
        string name,
        string outputColorImpl,
        string? createMaterialImpl = null,
        string? additionalCode = null
    )
    {
        lock (_lockObj)
        {
            uint typeId = _nextTypeId++;

            var registration = new MaterialTypeRegistration
            {
                TypeId = typeId,
                Name = name,
                OutputColorImplementation = outputColorImpl,
                CreateMaterialImplementation = createMaterialImpl,
                AdditionalCode = additionalCode,
            };

            Register(registration);
            return typeId;
        }
    }

    /// <summary>
    /// Gets a material type registration by name.
    /// </summary>
    /// <param name="name">Material type name.</param>
    /// <param name="registration">The registration if found.</param>
    /// <returns>True if the material type exists.</returns>
    public static bool TryGetByName(string name, out MaterialTypeRegistration? registration)
    {
        return _registrations.TryGetValue(name, out registration);
    }

    /// <summary>
    /// Gets a material type registration by ID.
    /// </summary>
    /// <param name="typeId">Material type ID.</param>
    /// <param name="registration">The registration if found.</param>
    /// <returns>True if the material type exists.</returns>
    public static bool TryGetById(MaterialTypeId typeId, out MaterialTypeRegistration? registration)
    {
        registration = null;
        return _idToName.TryGetValue(typeId, out var name)
            && _registrations.TryGetValue(name, out registration);
    }

    /// <summary>
    /// Gets all registered material types.
    /// </summary>
    /// <returns>Collection of all material type registrations.</returns>
    public static IReadOnlyCollection<MaterialTypeRegistration> GetAllRegistrations()
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
