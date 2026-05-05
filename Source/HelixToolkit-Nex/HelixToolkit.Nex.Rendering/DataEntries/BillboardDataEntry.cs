using HelixToolkit.Nex.ECS;
using HelixToolkit.Nex.Rendering.Components;
using HelixToolkit.Nex.Scene;

namespace HelixToolkit.Nex.Rendering.DataEntries;

public sealed class BillboardDataEntry : IDisposable
{
    private bool _disposed;
    public bool IsDisposed => _disposed;
    public bool Valid => !_disposed && Entities.Count > 0;
    public MaterialTypeId MaterialId { get; }
    public FastList<Entity> Entities { get; } = [];
    public ElementBuffer<BillboardDrawData> DrawDataBuffer { get; }

    public ElementBuffer<BillboardInfo> InfoBuffer { get; }
    public ElementBuffer<BillboardVertex> VertexBuffer { get; }
    public BufferResource DrawArgsBuffer { get; }

    public int TotalCount { get; private set; }

    public BillboardDataEntry(IContext context, int initialCapacity, MaterialTypeId id)
    {
        MaterialId = id;
        DrawDataBuffer = new ElementBuffer<BillboardDrawData>(
            context,
            initialCapacity,
            BufferUsageBits.Storage,
            debugName: $"{id}"
        );

        DrawArgsBuffer = context.CreateBuffer(
            new BufferDesc
            {
                DataSize = BillboardDrawIndirectArgs.SizeInBytes,
                Usage = BufferUsageBits.Storage | BufferUsageBits.Indirect,
                Storage = StorageType.Device,
                DebugName = $"BillboardDrawArgs_{id}",
            }
        );
        VertexBuffer = new ElementBuffer<BillboardVertex>(context, initialCapacity, hostVisible: true);
        InfoBuffer = new ElementBuffer<BillboardInfo>(context, initialCapacity / 4, hostVisible: true);
    }

    public void AddEntity(Entity entity)
    {
        ref var comp = ref entity.Get<BillboardComponent>();
        if (comp.BillboardGeometry is null || comp.BillboardCount == 0)
        {
            return;
        }
        Entities.Add(entity);
        TotalCount += comp.BillboardCount;
    }

    public void UploadDrawData()
    {
        InfoBuffer.WriteDynamic(Entities.Count, ctx =>
        {
            for (int i = 0; i < Entities.Count; ++i)
            {
                ref var comp = ref Entities[i].Get<BillboardComponent>();
                if (comp.BillboardGeometry is null || comp.BillboardCount == 0)
                {
                    continue;
                }
                var info = comp.ToInfo(Entities[i], Entities[i].Get<WorldTransform>().Value);
                ctx.Write(info);
            }
        });
        VertexBuffer.WriteDynamic(TotalCount, ctx =>
        {
            for (int i = 0; i < Entities.Count; ++i)
            {
                ref var comp = ref Entities[i].Get<BillboardComponent>();
                if (comp.BillboardGeometry is null || comp.BillboardCount == 0)
                {
                    continue;
                }
                for (int j = 0; j < comp.BillboardGeometry.Count; ++j)
                {
                    var vertex = comp.BillboardGeometry.Vertices[j];
                    vertex.InfoIndex = (uint)i;
                    ctx.Write(ref vertex);
                }
            }
        });
    }

    public void Clear()
    {
        Entities.Clear();
        TotalCount = 0;
    }

    public void EnsureCapacity()
    {
        DrawDataBuffer.EnsureCapacity(TotalCount);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        DrawDataBuffer.Dispose();
        DrawArgsBuffer.Dispose();
        VertexBuffer.Dispose();
        InfoBuffer.Dispose();
        _disposed = true;
    }
}

