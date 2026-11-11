using HelixToolkit.Nex.Scene;

namespace HelixToolkit.Nex.Rendering;

/// <summary>
/// Binds a mesh with morph targets (<see cref="Geometry.MorphTargets"/>) to its
/// <see cref="MorphWeights"/> and owns the GPU buffers of the morph part of the vertex
/// deformation pass.
/// </summary>
/// <remarks>
/// <para>
/// Assign an instance to <see cref="Components.MeshDrawInfo.Morphing"/>. When the weights change,
/// the engine uploads the non-zero weights; every frame the deformation compute pass adds the
/// weighted target deltas to the base vertices, before skinning when the mesh is also skinned.
/// All render passes then read the deformed vertices.
/// </para>
/// <para>
/// A morphing instance holds the deformed output of a single geometry when the mesh is not
/// skinned, so it must not be shared between mesh entities. Create one per mesh; they may share the
/// same <see cref="MorphWeights"/> (as the primitives of a glTF mesh do).
/// Morphed meshes are never frustum culled, because their base bounds do not cover the deformation.
/// </para>
/// </remarks>
public sealed class Morphing : IDisposable
{
    /// <summary>
    /// Weights with a smaller magnitude are treated as inactive and not uploaded.
    /// </summary>
    public const float MinActiveWeight = 1e-6f;

    private readonly DeformedVertexBuffers _output = new();
    private readonly FastList<GpuMorphWeight> _activeWeights = [];
    private RingElementBuffer<GpuMorphWeight>? _weightBuffer;
    private ulong _uploadedVersion;
    private int _uploadedTargetCount = -1;
    private bool _disposedValue;

    /// <summary>
    /// Gets the morph target weights.
    /// </summary>
    public MorphWeights Weights { get; }

    /// <summary>
    /// Gets the name used for the debug names of the GPU buffers.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the buffer holding the active (non-zero) weights uploaded for the current frame.
    /// </summary>
    public BufferResource WeightBuffer => _weightBuffer?.Buffer ?? BufferResource.Null;

    /// <summary>
    /// Gets the number of active (non-zero) weights in <see cref="WeightBuffer"/>.
    /// </summary>
    public int ActiveWeightCount => _activeWeights.Count;

    /// <summary>
    /// Gets the number of vertices the output buffers hold. Zero when the mesh is skinned, since the
    /// skinning owns the deformed output then.
    /// </summary>
    public int VertexCount => _output.VertexCount;

    /// <summary>
    /// Gets the buffer holding the morphed vertex positions. Null when the mesh is skinned.
    /// </summary>
    public BufferResource MorphedVertexBuffer => _output.VertexBuffer;

    /// <summary>
    /// Gets the buffer holding the morphed vertex properties (normal, tangent, texture coordinate).
    /// Null when the mesh is skinned or the geometry has no vertex properties.
    /// </summary>
    public BufferResource MorphedVertexPropsBuffer => _output.VertexPropsBuffer;

    /// <summary>
    /// Gets a value indicating whether the weights have been uploaded for the deformation pass.
    /// </summary>
    public bool IsReady => !_disposedValue && _weightBuffer is not null && _uploadedTargetCount >= 0;

    /// <summary>
    /// Initializes a new instance of the <see cref="Morphing"/> class.
    /// </summary>
    /// <param name="weights">The morph target weights, one per morph target of the geometry.</param>
    /// <param name="name">Optional name used for the debug names of the GPU buffers.</param>
    public Morphing(MorphWeights weights, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(weights);
        Weights = weights;
        Name = name ?? $"Morphing_{GetHashCode():X}";
    }

    /// <summary>
    /// Creates the weight buffer and creates, resizes or releases the morphed output buffers.
    /// Called by the engine before the deformation pass.
    /// </summary>
    /// <param name="context">The graphics context.</param>
    /// <param name="vertexCount">The vertex count of the morphed geometry.</param>
    /// <param name="hasVertexProps">Whether the geometry has vertex properties to morph.</param>
    /// <param name="ownsOutput">
    /// Whether this instance holds the deformed output. <see langword="false"/> when the mesh is
    /// also skinned, since the skinning owns the output then.
    /// </param>
    /// <param name="reallocated">
    /// Set to <see langword="true"/> when the output buffers were (re)created or released, which
    /// changes their GPU addresses.
    /// </param>
    /// <returns><see cref="ResultCode.Ok"/> on success.</returns>
    public ResultCode EnsureBuffers(
        IContext context,
        int vertexCount,
        bool hasVertexProps,
        bool ownsOutput,
        out bool reallocated
    )
    {
        ObjectDisposedException.ThrowIf(_disposedValue, this);
        _weightBuffer ??= new RingElementBuffer<GpuMorphWeight>(
            context,
            (int)GraphicsSettings.MaxFrameInFlight,
            Math.Max(1, Weights.Count),
            BufferUsageBits.Storage,
            hostVisible: true,
            debugName: $"{Name}_Weights"
        );
        reallocated = ownsOutput
            ? _output.Ensure(context, vertexCount, hasVertexProps, $"{Name}_Morphed")
            : _output.Release();
        return ResultCode.Ok;
    }

    /// <summary>
    /// Releases the morphed output buffers, for example when the geometry no longer has morph
    /// targets. Called by the engine; <see cref="EnsureBuffers"/> recreates them.
    /// </summary>
    /// <returns><see langword="true"/> when buffers were released, which changes their GPU addresses.</returns>
    public bool ReleaseOutput()
    {
        return _output.Release();
    }

    /// <summary>
    /// Uploads the active (see <see cref="MinActiveWeight"/>) weights of the first <paramref name="targetCount"/> morph targets when
    /// they changed since the last upload. Each upload advances the weight ring buffer.
    /// </summary>
    /// <param name="targetCount">The number of morph targets of the geometry.</param>
    /// <returns><see cref="ResultCode.Ok"/> on success.</returns>
    public ResultCode UpdateWeights(int targetCount)
    {
        ObjectDisposedException.ThrowIf(_disposedValue, this);
        if (_weightBuffer is null)
        {
            return ResultCode.InvalidState;
        }
        if (_uploadedTargetCount == targetCount && _uploadedVersion == Weights.Version)
        {
            return ResultCode.Ok;
        }
        _activeWeights.Clear();
        var weights = Weights.Values;
        var count = Math.Min(targetCount, weights.Length);
        for (var i = 0; i < count; ++i)
        {
            if (MathF.Abs(weights[i]) > MinActiveWeight)
            {
                _activeWeights.Add(new GpuMorphWeight { Target = (uint)i, Weight = weights[i] });
            }
        }
        _weightBuffer.Advance();
        var result = _weightBuffer.Upload(_activeWeights);
        if (result == ResultCode.Ok)
        {
            _uploadedVersion = Weights.Version;
            _uploadedTargetCount = targetCount;
        }
        return result;
    }

    /// <summary>
    /// Morphs a base position on the CPU with the weights of the most recent
    /// <see cref="UpdateWeights"/> call, matching the GPU deformation pass. Used for CPU-side
    /// queries such as resolving pick positions.
    /// </summary>
    /// <param name="geometry">The morphed geometry.</param>
    /// <param name="vertexIndex">The vertex index.</param>
    /// <param name="position">The base position of the vertex.</param>
    /// <returns>The morphed position in mesh-local space.</returns>
    public Vector3 MorphPosition(Geometry geometry, int vertexIndex, Vector3 position)
    {
        var targets = geometry.MorphTargets;
        foreach (var weight in _activeWeights)
        {
            if (weight.Target >= targets.Count)
            {
                continue;
            }
            var deltas = targets[(int)weight.Target].PositionDeltas;
            if ((uint)vertexIndex < (uint)deltas.Count)
            {
                position += deltas[vertexIndex] * weight.Weight;
            }
        }
        return position;
    }

    public void Dispose()
    {
        if (_disposedValue)
        {
            return;
        }
        Disposer.DisposeAndRemove(ref _weightBuffer);
        _output.Dispose();
        _activeWeights.Clear();
        _disposedValue = true;
    }
}
