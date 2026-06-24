using HelixToolkit.Nex.ECS;

namespace HelixToolkit.Nex.Rendering.DrawStreams;

/// <summary>
/// Represents a named draw stream that owns a GPU buffer of <see cref="DRAW_TYPE"/> commands
/// or <see cref="LineDraw"/> commands) sharing the same rendering characteristics (index buffer strategy, culling granularity, hitability).
/// Each stream manages stable slot assignment, lazy compaction, and material-type sub-ranges.
/// </summary>
public interface IDrawStream<DRAW_TYPE> : IRenderData, IDisposable where DRAW_TYPE : unmanaged
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
    /// Enumerates all distinct material types present in this stream.
    /// Returns an empty enumerable if the stream contains no draws.
    /// </summary>
    /// <returns>The distinct set of <see cref="MaterialTypeId"/> values across all active draws.</returns>
    ReadOnlySpan<MaterialTypeId> GetMaterialTypes();

    /// <summary>
    /// Gets the draw range (start index and count) for a specific material type within this stream.
    /// Returns <see cref="DrawRange.Zero"/> if the material type is not present.
    /// </summary>
    /// <param name="materialType">The material type identifier to query.</param>
    /// <returns>The contiguous draw range for the specified material type.</returns>
    DrawRange GetRangeByMaterial(MaterialTypeId materialType);

    /// <summary>
    /// Attempts to retrieve the <see cref="DRAW_TYPE"/> command at the given draw index.
    /// </summary>
    /// <param name="drawIndex">The slot index to look up.</param>
    /// <param name="draw">When this method returns <c>true</c>, contains the draw command at the specified index.</param>
    /// <returns><c>true</c> if a valid draw command exists at the specified index; otherwise, <c>false</c>.</returns>
    bool TryGetDraw(int drawIndex, out DRAW_TYPE draw);

    /// <summary>
    /// Gets the <see cref="DRAW_TYPE"/> command and slot index for a specific entity.
    /// Returns a default <see cref="DRAW_TYPE"/> with <c>SlotIndex == -1</c> if the entity is not in this stream.
    /// </summary>
    /// <param name="entity">The entity to look up.</param>
    /// <returns>A tuple containing the draw command and its stable slot index.</returns>
    (DRAW_TYPE Draw, int SlotIndex) GetDraw(Entity entity);

    /// <summary>
    /// Inserts a buffer memory barrier on this stream's GPU buffer for synchronization
    /// between compute passes and subsequent draw passes.
    /// </summary>
    /// <param name="cmdBuf">The command buffer in which to record the barrier.</param>
    /// <param name="preset">The barrier preset to use.</param>
    /// <param name="force">If set to <see langword="true"/>, the barrier will be created even if the buffer is not dirty.</param>
    void Barrier(ICommandBuffer cmdBuf, BarrierPreset preset = BarrierPreset.WriteToIndirectDrawRead, bool force = false);
}


public static class DrawStreamExtensions
{
    public static void Barrier<DRAW_TYPE>(this IEnumerable<IDrawStream<DRAW_TYPE>> streams, ICommandBuffer cmdBuf) where DRAW_TYPE : unmanaged
    {
        foreach (var stream in streams)
        {
            if (stream.Count == 0)
            { continue; }
            stream.Barrier(cmdBuf, BarrierPreset.WriteToIndirectDrawRead);
        }
    }
}
