using System.Collections.Concurrent;

namespace HelixToolkit.Nex.Material;

/// <summary>
/// Represents a registered line material type with its unique ID and shader implementation.
/// <para>
/// Each registration provides the GLSL code for the <c>getPointColor()</c> function
/// that determines how a line is shaded. The fragment shader template injects this
/// code between the <c>TEMPLATE_POINT_COLOR_START</c> / <c>TEMPLATE_POINT_COLOR_END</c>
/// markers.
/// </para>
/// </summary>
public sealed class LineMaterialRegistration : IMaterialRegistration
{
    /// <summary>
    /// Unique identifier for this line material type.
    /// </summary>
    public required MaterialTypeId TypeId { get; init; }

    /// <summary>
    /// Unique name for this line material type (e.g., "Solid", "Dashed", "Textured").
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// GLSL implementation for the <c>getLineColor()</c> function.
    /// <para>
    /// Available varyings: <c>v_uv</c>, <c>v_color</c>, <c>v_screenSize</c>,
    /// <c>v_entityId</c>, <c>v_textureIndex</c>, <c>v_samplerIndex</c>.
    /// Bindless texture helpers from <c>HeaderFrag.glsl</c> are available.
    /// </para>
    /// </summary>
    public required string OutputColorImplementation { get; init; }

    /// <inheritdoc/>
    public string? GetColorOutputImplCode() => OutputColorImplementation;

    /// <summary>
    /// Optional additional GLSL code to inject before the <c>getPointColor()</c> function
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

    public LineMaterialRegistration WithPointerRingSupport()
    {
        SupportPointerRing = true;
        return this;
    }
}

/// <summary>
/// Global registry for line material types. Maps line material type names to unique IDs
/// and their shader implementations for pipeline generation.
/// </summary>
public static class LineMaterialRegistry
{
    private static readonly ConcurrentDictionary<string, LineMaterialRegistration> _registrations =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly ConcurrentDictionary<MaterialTypeId, string> _idToName = new();
    private static uint _nextTypeId = 1; // Reserve 0 for "Default" material type
    private static readonly object _lockObj = new();

    static LineMaterialRegistry()
    {
        RegisterBuiltInTypes();
    }

    private static void RegisterBuiltInTypes()
    {
        // Default circle SDF material (ID = 0)
        Register(
            new LineMaterialRegistration
            {
                TypeId = 0,
                Name = "Default",
                OutputColorImplementation = """
                    vec4 color = getColor();
                    return color;
                """,
            }
        );
    }

    /// <summary>
    /// Registers a new line material type with the system.
    /// </summary>
    public static void Register(LineMaterialRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        if (string.IsNullOrWhiteSpace(registration.Name))
        {
            throw new ArgumentException(
                "Line material type name cannot be null or empty.",
                nameof(registration)
            );
        }

        lock (_lockObj)
        {
            if (_registrations.ContainsKey(registration.Name))
            {
                throw new ArgumentException(
                    $"Line material type '{registration.Name}' is already registered.",
                    nameof(registration)
                );
            }

            if (_idToName.ContainsKey(registration.TypeId))
            {
                throw new ArgumentException(
                    $"Line material type ID '{registration.TypeId}' is already used by '{_idToName[registration.TypeId]}'.",
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
    /// Registers a new line material type with an auto-assigned ID.
    /// </summary>
    /// <returns>The assigned type ID.</returns>
    public static MaterialTypeId Register(
        string name,
        string getLineColorImpl,
        string? additionalCode = null
    )
    {
        lock (_lockObj)
        {
            uint typeId = _nextTypeId++;

            var registration = new LineMaterialRegistration
            {
                TypeId = typeId,
                Name = name,
                OutputColorImplementation = getLineColorImpl,
                AdditionalCode = additionalCode,
            };

            Register(registration);
            return typeId;
        }
    }

    /// <summary>
    /// Gets a line material type registration by name.
    /// </summary>
    public static bool TryGetByName(string name, out LineMaterialRegistration? registration)
    {
        return _registrations.TryGetValue(name, out registration);
    }

    /// <summary>
    /// Gets a line material type registration by ID.
    /// </summary>
    public static bool TryGetById(
        MaterialTypeId typeId,
        out LineMaterialRegistration? registration
    )
    {
        registration = null;
        return _idToName.TryGetValue(typeId, out var name)
            && _registrations.TryGetValue(name, out registration);
    }

    /// <summary>
    /// Gets all registered line material types.
    /// </summary>
    public static IReadOnlyCollection<LineMaterialRegistration> GetAllRegistrations()
    {
        return _registrations.Values.ToArray();
    }

    /// <summary>
    /// Gets the line material type ID for a given name.
    /// </summary>
    public static MaterialTypeId? GetTypeId(string name)
    {
        return _registrations.TryGetValue(name, out var registration) ? registration.TypeId : null;
    }

    /// <summary>
    /// Attempts to retrieve the <see cref="MaterialTypeId"/> associated with the specified name.
    /// </summary>
    /// <remarks>This method does not throw an exception if the specified name is not found. Instead, it
    /// returns  <see langword="false"/> and sets <paramref name="typeId"/> to its default value.</remarks>
    /// <param name="name">The name of the material type to look up. This value cannot be <see langword="null"/>.</param>
    /// <param name="typeId">When this method returns, contains the <see cref="MaterialTypeId"/> associated with the specified name,  if the
    /// lookup succeeds; otherwise, contains the default value of <see cref="MaterialTypeId"/>.</param>
    /// <returns><see langword="true"/> if the lookup succeeds and a <see cref="MaterialTypeId"/> is found for the specified
    /// name;  otherwise, <see langword="false"/>.</returns>
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
    /// Gets the line material type name for a given ID.
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
