using HelixToolkit.Nex.ECS;
using HelixToolkit.Nex.Rendering.Components;

namespace HelixToolkit.Nex.Rendering.DataEntries;

public sealed class PointCloudDataEntry : IDisposable
{
    private bool _disposed;
    public bool IsDisposed => _disposed;
    public bool Valid => !_disposed && Entities.Count > 0;
    public MaterialTypeId MaterialId { get; }
    public FastList<Entity> Entities { get; } = [];
    public ElementBuffer<PointDrawData> DrawDataBuffer { get; }
    public BufferResource DrawArgsBuffer { get; }

    public int PointCount { get; private set; }

    public PointCloudDataEntry(IContext context, int initialCapacity, MaterialTypeId id)
    {
        MaterialId = id;
        DrawDataBuffer = new ElementBuffer<PointDrawData>(
            context,
            initialCapacity,
            BufferUsageBits.Storage,
            debugName: $"{id}"
        );

        DrawArgsBuffer = context.CreateBuffer(
            new BufferDesc
            {
                DataSize = PointDrawIndirectArgs.SizeInBytes,
                Usage = BufferUsageBits.Storage | BufferUsageBits.Indirect,
                Storage = StorageType.Device,
                DebugName = $"PointDrawArgs_{id}",
            }
        );
    }

    public void AddEntity(Entity entity)
    {
        Entities.Add(entity);
        ref var comp = ref entity.Get<PointCloudComponent>();
        PointCount += comp.PointCount;
    }

    public void Clear()
    {
        Entities.Clear();
        PointCount = 0;
    }

    public void EnsureCapacity()
    {
        DrawDataBuffer.EnsureCapacity(PointCount);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        DrawDataBuffer.Dispose();
        DrawArgsBuffer.Dispose();
        _disposed = true;
    }
}
