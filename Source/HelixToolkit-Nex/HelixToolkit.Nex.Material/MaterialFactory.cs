using System.Collections.Concurrent;
using System.Reflection;

namespace HelixToolkit.Nex.Material;

/// <summary>
/// Optional attribute to provide a friendly registration name for a material type.
/// If omitted the factory will use the type name with a trimmed "Material" suffix.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class MaterialNameAttribute(string name) : Attribute
{
    public string Name { get; } = name ?? throw new ArgumentNullException(nameof(name));
}

/// <summary>
/// Factory for creating materials by logical name.
/// - Supports explicit registration via <see cref="Register(string, Func{Material})"/>.
/// - Auto-registers all non-abstract types assignable to <see cref="Material"/> found in the factory assembly.
/// </summary>
public static class MaterialFactory
{
    private static readonly ConcurrentDictionary<string, Func<Material>> _registry = new(
        StringComparer.OrdinalIgnoreCase
    );

    static MaterialFactory()
    {
        // Auto-register material implementations from the same assembly as this factory.
        AutoRegisterFromAssembly(Assembly.GetExecutingAssembly());
    }

    /// <summary>
    /// Register a material constructor with a key.
    /// </summary>
    public static void Register(string key, Func<Material> ctor)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key cannot be null or whitespace.", nameof(key));
        ArgumentNullException.ThrowIfNull(ctor);
        _registry[key] = ctor;
    }

    /// <summary>
    /// Register a material type using its parameterless constructor.
    /// The key is derived from the type (see <see cref="GetDefaultName(Type)"/>).
    /// </summary>
    public static void Register<TMaterial>()
        where TMaterial : Material, new() =>
        Register(GetDefaultName(typeof(TMaterial)), () => new TMaterial());

    /// <summary>
    /// Try to create a material by key.
    /// </summary>
    public static bool TryCreate(string key, out Material? material)
    {
        material = null;
        if (key == null)
            return false;
        if (_registry.TryGetValue(key, out var ctor))
        {
            material = ctor();
            return true;
        }
        return false;
    }

    /// <summary>
    /// Create a material by key (throws if not registered).
    /// </summary>
    public static Material Create(string key)
    {
        if (!TryCreate(key, out var mat))
        {
            throw new InvalidOperationException($"Material '{key}' is not registered.");
        }
        return mat!;
    }

    /// <summary>
    /// Get a snapshot of registered keys.
    /// </summary>
    public static IReadOnlyCollection<string> GetRegisteredKeys() => _registry.Keys.ToArray();

    /// <summary>
    /// Attempts to create material properties for the given key.
    /// This creates the material instance and attempts to read its `Properties` property via reflection.
    /// Returns null if the material type does not expose a `Properties` property deriving from MaterialProperties.
    /// </summary>
    public static MaterialProperties? CreateProperties(string key)
    {
        if (!TryCreate(key, out var mat) || mat == null)
            return null;
        var propInfo = mat.GetType()
            .GetProperty("Properties", BindingFlags.Public | BindingFlags.Instance);
        if (propInfo == null)
            return null;
        if (!typeof(MaterialProperties).IsAssignableFrom(propInfo.PropertyType))
            return null;
        return propInfo.GetValue(mat) as MaterialProperties;
    }

    /// <summary>
    /// Scans the provided assembly and registers all concrete types assignable to <see cref="Material"/>.
    /// Registration key is taken from <see cref="MaterialNameAttribute"/> if present, otherwise the type name with
    /// a trimmed "Material" suffix.
    /// </summary>
    public static void AutoRegisterFromAssembly(Assembly asm)
    {
        if (asm == null)
            return;
        var materialBase = typeof(Material);
        foreach (var type in asm.GetTypes())
        {
            if (type.IsAbstract)
                continue;
            if (!materialBase.IsAssignableFrom(type))
                continue;

            // require parameterless ctor so factory can construct it
            if (type.GetConstructor(Type.EmptyTypes) == null)
                continue;

            var nameAttr = type.GetCustomAttribute<MaterialNameAttribute>(inherit: false);
            var key = nameAttr?.Name ?? GetDefaultName(type);

            // Use a closure capturing the specific type to create instances.
            Register(key, () => (Material)Activator.CreateInstance(type)!);
        }
    }

    private static string GetDefaultName(Type t)
    {
        var name = t.Name;
        const string suffix = "Material";
        if (
            name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            && name.Length > suffix.Length
        )
        {
            return name.Substring(0, name.Length - suffix.Length);
        }
        return name;
    }
}
