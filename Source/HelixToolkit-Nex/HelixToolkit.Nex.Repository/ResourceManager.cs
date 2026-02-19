using HelixToolkit.Nex.DependencyInjection;

namespace HelixToolkit.Nex.Repository;

/// <summary>
/// Manages all rendering resources including geometries and materials for the engine.
/// </summary>
/// <remarks>
/// The ResourceManager provides:
/// <list type="bullet">
/// <item>Centralized management of geometries and materials</item>
/// <item>Automatic GPU buffer creation and updates</item>
/// <item>Resource lifecycle management with proper disposal</item>
/// <item>ID-based resource referencing to enable sharing</item>
/// </list>
///
/// <para>
/// <b>Architecture Pattern:</b>
/// Scene nodes store only handles (IDs) to resources, not the actual data.
/// This enables:
/// - Multiple nodes sharing the same geometry/material (memory efficient)
/// - Easy runtime swapping of resources
/// - Better cache performance (data-oriented design)
/// - Simple serialization (just save IDs)
/// </para>
/// </remarks>
public sealed class ResourceManager(IServiceProvider services) : IDisposable
{
    public IMaterialManager Materials { get; } = services.GetRequiredService<IMaterialManager>();

    /// <summary>
    /// Gets the geometry pool for managing geometry resources.
    /// </summary>
    public IGeometryManager Geometries { get; } = services.GetRequiredService<IGeometryManager>();

    /// <summary>
    /// Gets the material pool for managing material resources.
    /// </summary>
    public IMaterialPropertyManager MaterialProperties { get; } =
        services.GetRequiredService<IMaterialPropertyManager>();

    /// <summary>
    /// Gets the repository used to manage and retrieve shader resources.
    /// </summary>
    public IShaderRepository ShaderRepository { get; } =
        services.GetRequiredService<IShaderRepository>();

    /// <summary>
    /// Gets statistics about resource usage.
    /// </summary>
    public ResourceStatistics GetStatistics()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return new ResourceStatistics
        {
            GeometryCount = Geometries.Count,
            MaterialCount = Materials.Count,
            MaterialPropertyCount = MaterialProperties.Count,
            ShaderCount = ShaderRepository.Count,
            DirtyGeometryCount = Geometries
                .GetAll()
                .Count(g => g.BufferDirty != GeometryBufferType.None),
        };
    }

    /// <summary>
    /// Clears all resources and frees their GPU data.
    /// </summary>
    public void Clear()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        Geometries.Clear();
        MaterialProperties.Clear();
        Materials.Clear();
    }

    #region IDisposable Support
    private bool _disposed;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Clear();
        _disposed = true;
    }
    #endregion
}

/// <summary>
/// Contains statistics about resource usage.
/// </summary>
public readonly record struct ResourceStatistics
{
    /// <summary>
    /// Total number of active geometries.
    /// </summary>
    public int GeometryCount { get; init; }

    /// <summary>
    /// Gets the number of material properties.
    /// </summary>
    public int MaterialPropertyCount { get; init; }

    /// <summary>
    /// Total number of active materials.
    /// </summary>
    public int MaterialCount { get; init; }

    /// <summary>
    /// Gets the number of shaders.
    /// </summary>
    public int ShaderCount { get; init; }

    /// <summary>
    /// Number of geometries with dirty buffers that need GPU updates.
    /// </summary>
    public int DirtyGeometryCount { get; init; }
}
