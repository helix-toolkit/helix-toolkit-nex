using HelixToolkit.Nex.ECS;

namespace HelixToolkit.Nex.Rendering.DrawStreams;

/// <summary>
/// Represents a named draw stream that owns a GPU buffer of <see cref="MeshDraw"/> commands
/// sharing the same rendering characteristics (index buffer strategy, culling granularity, hitability).
/// Each stream manages stable slot assignment, lazy compaction, and material-type sub-ranges.
/// </summary>
public interface IDrawStream : IRenderData, IDisposable
{
    /// <summary>
    /// Gets the registered name of this stream.
    /// </summary>
    DrawStreamName StreamName { get; }

    /// <summary>
    /// Gets the stream type.
    /// </summary>
    DrawStreamType StreamType { get; }

    /// <summary>
    /// Gets the categories this stream belongs to, used for batch queries
    /// (e.g., retrieve all opaque streams, all instancing streams).
    /// </summary>
    DrawStreamVariants Variants { get; }

    /// <summary>
    /// Gets a value indicating whether this stream uses GPU instancing.
    /// Affects culling dispatch (per-mesh vs per-instance).
    /// </summary>
    bool IsInstancing { get; }

    /// <summary>
    /// Gets the index buffer binding strategy for consumers of this stream.
    /// </summary>
    IndexBufferStrategy IndexBufferStrategy { get; }

    /// <summary>
    /// Gets or sets the fragmentation threshold (0.0–1.0).
    /// Compaction is triggered when <see cref="Fragmentation"/> exceeds this value.
    /// Default is 0.25 (25% free slots).
    /// </summary>
    float FragmentationThreshold { get; set; }

    /// <summary>
    /// Gets the current fragmentation ratio (free slots / total allocated slots).
    /// A value of 0 means no fragmentation; higher values indicate more wasted buffer space.
    /// </summary>
    float Fragmentation { get; }

    /// <summary>
    /// Enumerates all distinct material types present in this stream.
    /// Returns an empty enumerable if the stream contains no draws.
    /// </summary>
    /// <returns>The distinct set of <see cref="MaterialTypeId"/> values across all active draws.</returns>
    IEnumerable<MaterialTypeId> GetMaterialTypes();

    /// <summary>
    /// Provides a low-level enumerator over the distinct material types in this stream without heap allocations.
    /// </summary>
    /// <returns>A struct enumerator over the distinct material types.</returns>
    ReadOnlySpan<MaterialTypeId> GetMaterialTypesCore();

    /// <summary>
    /// Gets the draw range (start index and count) for a specific material type within this stream.
    /// Returns <see cref="DrawRange.Zero"/> if the material type is not present.
    /// </summary>
    /// <param name="materialType">The material type identifier to query.</param>
    /// <returns>The contiguous draw range for the specified material type.</returns>
    DrawRange GetRangeByMaterial(MaterialTypeId materialType);

    /// <summary>
    /// Attempts to retrieve the <see cref="MeshDraw"/> command at the given draw index.
    /// </summary>
    /// <param name="drawIndex">The slot index to look up.</param>
    /// <param name="meshDraw">When this method returns <c>true</c>, contains the draw command at the specified index.</param>
    /// <returns><c>true</c> if a valid draw command exists at the specified index; otherwise, <c>false</c>.</returns>
    bool TryGetMeshDraw(int drawIndex, out MeshDraw meshDraw);

    /// <summary>
    /// Gets the <see cref="MeshDraw"/> command and slot index for a specific entity.
    /// Returns a default <see cref="MeshDraw"/> with <c>SlotIndex == -1</c> if the entity is not in this stream.
    /// </summary>
    /// <param name="entity">The entity to look up.</param>
    /// <returns>A tuple containing the draw command and its stable slot index.</returns>
    (MeshDraw Draw, int SlotIndex) GetMeshDraw(Entity entity);

    /// <summary>
    /// Inserts a buffer memory barrier on this stream's GPU buffer for synchronization
    /// between compute passes and subsequent draw passes.
    /// </summary>
    /// <param name="cmdBuf">The command buffer in which to record the barrier.</param>
    void Barrier(ICommandBuffer cmdBuf);
}


public static class DrawStreamExtensions
{
    public static void Barrier(this IEnumerable<IDrawStream> streams, ICommandBuffer cmdBuf)
    {
        foreach (var stream in streams)
        {
            if (stream.Count == 0)
            { continue; }
            stream.Barrier(cmdBuf);
        }
    }
}
