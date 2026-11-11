namespace HelixToolkit.Nex.Rendering;

/// <summary>
/// The output buffers of the vertex deformation pass (morph targets and skinning) for one geometry:
/// deformed positions and, when the geometry has them, deformed vertex properties.
/// </summary>
internal sealed class DeformedVertexBuffers : IDisposable
{
    private ElementBuffer<Vector4>? _vertexBuffer;
    private ElementBuffer<VertexProperties>? _vertexPropsBuffer;

    /// <summary>
    /// Gets the number of vertices the buffers hold.
    /// </summary>
    public int VertexCount { get; private set; }

    public BufferResource VertexBuffer => _vertexBuffer?.Buffer ?? BufferResource.Null;

    public BufferResource VertexPropsBuffer => _vertexPropsBuffer?.Buffer ?? BufferResource.Null;

    /// <summary>
    /// Creates or resizes the buffers for a geometry.
    /// </summary>
    /// <param name="context">The graphics context.</param>
    /// <param name="vertexCount">The vertex count of the geometry.</param>
    /// <param name="hasVertexProps">Whether the geometry has vertex properties to deform.</param>
    /// <param name="debugName">The debug name prefix of the buffers.</param>
    /// <returns><see langword="true"/> when the buffers were (re)created, which changes their GPU addresses.</returns>
    public bool Ensure(IContext context, int vertexCount, bool hasVertexProps, string debugName)
    {
        var reallocated = false;
        if (_vertexBuffer is null || VertexCount != vertexCount)
        {
            Disposer.DisposeAndRemove(ref _vertexBuffer);
            Disposer.DisposeAndRemove(ref _vertexPropsBuffer);
            VertexCount = vertexCount;
            reallocated = true;
            if (vertexCount == 0)
            {
                return true;
            }
            _vertexBuffer = new ElementBuffer<Vector4>(
                context,
                vertexCount,
                BufferUsageBits.Storage,
                hostVisible: false,
                debugName: $"{debugName}_Vert"
            );
        }

        if (hasVertexProps != (_vertexPropsBuffer is not null))
        {
            reallocated = true;
            if (hasVertexProps)
            {
                _vertexPropsBuffer = new ElementBuffer<VertexProperties>(
                    context,
                    vertexCount,
                    BufferUsageBits.Storage,
                    hostVisible: false,
                    debugName: $"{debugName}_VertProps"
                );
            }
            else
            {
                Disposer.DisposeAndRemove(ref _vertexPropsBuffer);
            }
        }
        return reallocated;
    }

    /// <summary>
    /// Releases the buffers.
    /// </summary>
    /// <returns><see langword="true"/> when buffers were released, which changes their GPU addresses.</returns>
    public bool Release()
    {
        var released = _vertexBuffer is not null || _vertexPropsBuffer is not null;
        Disposer.DisposeAndRemove(ref _vertexBuffer);
        Disposer.DisposeAndRemove(ref _vertexPropsBuffer);
        VertexCount = 0;
        return released;
    }

    public void Dispose()
    {
        Release();
    }
}
