using HelixToolkit.Nex.ECS;
using HelixToolkit.Nex.Scene;

namespace HelixToolkit.Nex.Rendering;

/// <summary>
/// Binds a skinned mesh to the scene nodes that drive its joints and owns the GPU buffers of
/// the compute skinning pass.
/// </summary>
/// <remarks>
/// <para>
/// Assign an instance to <see cref="Components.MeshDrawInfo.Skinning"/>. When world transforms
/// change, the engine uploads the world transforms of <see cref="Joints"/>; every frame it computes
/// the skinning palette and skins the mesh's vertices in a compute pass ahead of rendering. All render passes then read the skinned
/// vertices, so a mesh is skinned once per frame regardless of how many passes draw it.
/// </para>
/// <para>
/// A skinning instance holds the skinned output of a single geometry, so it must not be shared
/// between mesh entities. Create one per skinned mesh (they may share the same <see cref="Skeleton"/>).
/// Skinned meshes are never frustum culled, because their bind-pose bounds do not cover the pose.
/// </para>
/// </remarks>
public sealed class Skinning : IDisposable
{
    private readonly Entity[] _joints;
    private readonly FastList<Matrix4x4> _jointMatrices;
    private RingElementBuffer<Matrix4x4>? _jointMatrixBuffer;
    private ElementBuffer<Matrix4x4>? _paletteBuffer;
    private ElementBuffer<Vector4>? _skinnedVertexBuffer;
    private ElementBuffer<VertexProperties>? _skinnedVertexPropsBuffer;
    private bool _disposedValue;

    /// <summary>
    /// Gets the skeleton providing the inverse bind matrices.
    /// </summary>
    public Skeleton Skeleton { get; }

    /// <summary>
    /// Gets the joint entities, index-aligned with <see cref="Skeleton.InverseBindMatrices"/>.
    /// Each joint's <see cref="WorldTransform"/> drives the corresponding bone.
    /// </summary>
    public IReadOnlyList<Entity> Joints => _joints;

    /// <summary>
    /// Gets the number of skinned vertices the output buffers hold.
    /// </summary>
    public int VertexCount { get; private set; }

    /// <summary>
    /// Gets the buffer holding the joint matrices uploaded for the current frame.
    /// </summary>
    public BufferResource JointMatrixBuffer => _jointMatrixBuffer?.Buffer ?? BufferResource.Null;

    /// <summary>
    /// Gets the buffer holding the skinning palette computed by the compute pass.
    /// </summary>
    public BufferResource PaletteBuffer => _paletteBuffer?.Buffer ?? BufferResource.Null;

    /// <summary>
    /// Gets the buffer holding the skinned vertex positions.
    /// </summary>
    public BufferResource SkinnedVertexBuffer =>
        _skinnedVertexBuffer?.Buffer ?? BufferResource.Null;

    /// <summary>
    /// Gets the buffer holding the skinned vertex properties (normal, tangent, texture coordinate).
    /// Null when the geometry has no vertex properties.
    /// </summary>
    public BufferResource SkinnedVertexPropsBuffer =>
        _skinnedVertexPropsBuffer?.Buffer ?? BufferResource.Null;

    /// <summary>
    /// Gets a value indicating whether the GPU buffers are ready for the skinning pass.
    /// </summary>
    public bool IsReady =>
        !_disposedValue
        && _jointMatrixBuffer is not null
        && SkinnedVertexBuffer.Valid
        && PaletteBuffer.Valid
        && Skeleton.InverseBindBuffer.Valid;

    /// <summary>
    /// Initializes a new instance of the <see cref="Skinning"/> class.
    /// </summary>
    /// <param name="skeleton">The skeleton providing the inverse bind matrices.</param>
    /// <param name="joints">
    /// The joint entities, one per skeleton joint and in the same order. Each joint must have a
    /// <see cref="WorldTransform"/> (i.e. be a scene <see cref="Node"/>).
    /// </param>
    public Skinning(Skeleton skeleton, IReadOnlyList<Entity> joints)
    {
        ArgumentNullException.ThrowIfNull(skeleton);
        ArgumentNullException.ThrowIfNull(joints);
        if (joints.Count != skeleton.JointCount)
        {
            throw new ArgumentException(
                $"Joint count {joints.Count} does not match skeleton joint count {skeleton.JointCount}.",
                nameof(joints)
            );
        }
        Skeleton = skeleton;
        _joints = [.. joints];
        _jointMatrices = new FastList<Matrix4x4>(_joints.Length);
    }

    /// <summary>
    /// Creates or resizes the palette and skinned output buffers for a geometry.
    /// Called by the engine before the skinning pass.
    /// </summary>
    /// <param name="context">The graphics context.</param>
    /// <param name="vertexCount">The vertex count of the skinned geometry.</param>
    /// <param name="hasVertexProps">Whether the geometry has vertex properties to skin.</param>
    /// <param name="reallocated">
    /// Set to <see langword="true"/> when the output buffers were (re)created, which changes their GPU addresses.
    /// </param>
    /// <returns><see cref="ResultCode.Ok"/> on success.</returns>
    public ResultCode EnsureBuffers(
        IContext context,
        int vertexCount,
        bool hasVertexProps,
        out bool reallocated
    )
    {
        ObjectDisposedException.ThrowIf(_disposedValue, this);
        reallocated = false;
        var result = Skeleton.UpdateBuffer(context);
        if (result != ResultCode.Ok)
        {
            return result;
        }

        _jointMatrixBuffer ??= new RingElementBuffer<Matrix4x4>(
            context,
            (int)GraphicsSettings.MaxFrameInFlight,
            _joints.Length,
            BufferUsageBits.Storage,
            hostVisible: true,
            debugName: $"{Skeleton.Name}_Joints"
        );
        _paletteBuffer ??= new ElementBuffer<Matrix4x4>(
            context,
            _joints.Length,
            BufferUsageBits.Storage,
            hostVisible: false,
            debugName: $"{Skeleton.Name}_Palette"
        );

        if (_skinnedVertexBuffer is null || VertexCount != vertexCount)
        {
            Disposer.DisposeAndRemove(ref _skinnedVertexBuffer);
            Disposer.DisposeAndRemove(ref _skinnedVertexPropsBuffer);
            VertexCount = vertexCount;
            reallocated = true;
            if (vertexCount == 0)
            {
                return ResultCode.Ok;
            }
            _skinnedVertexBuffer = new ElementBuffer<Vector4>(
                context,
                vertexCount,
                BufferUsageBits.Storage,
                hostVisible: false,
                debugName: $"{Skeleton.Name}_SkinnedVert"
            );
        }

        if (hasVertexProps != (_skinnedVertexPropsBuffer is not null))
        {
            reallocated = true;
            if (hasVertexProps)
            {
                _skinnedVertexPropsBuffer = new ElementBuffer<VertexProperties>(
                    context,
                    vertexCount,
                    BufferUsageBits.Storage,
                    hostVisible: false,
                    debugName: $"{Skeleton.Name}_SkinnedVertProps"
                );
            }
            else
            {
                Disposer.DisposeAndRemove(ref _skinnedVertexPropsBuffer);
            }
        }
        return ResultCode.Ok;
    }

    /// <summary>
    /// Uploads the joint matrices for the current frame. Each joint matrix is the joint's world
    /// transform expressed relative to the skinned mesh node, so that the regular node transform
    /// applied in the vertex shader yields the joint-driven world position.
    /// Called by the engine after the scene transforms have been updated, at most once per frame and
    /// only when a world transform changed. Each call advances the joint-matrix ring buffer.
    /// </summary>
    /// <param name="meshWorld">The world transform of the skinned mesh entity.</param>
    /// <param name="worldTransforms">The world transform components of the world the joints belong to.</param>
    /// <returns><see cref="ResultCode.Ok"/> on success.</returns>
    public ResultCode UpdateJointMatrices(
        in Matrix4x4 meshWorld,
        IComponents<WorldTransform> worldTransforms
    )
    {
        ObjectDisposedException.ThrowIf(_disposedValue, this);
        if (_jointMatrixBuffer is null)
        {
            return ResultCode.InvalidState;
        }
        if (!Matrix4x4.Invert(meshWorld, out var invMeshWorld))
        {
            invMeshWorld = Matrix4x4.Identity;
        }
        _jointMatrices.Clear();
        var inverseBinds = Skeleton.InverseBindMatrices;
        for (var i = 0; i < _joints.Length; ++i)
        {
            var joint = _joints[i];
            if (joint.Valid && joint.Has<WorldTransform>())
            {
                _jointMatrices.Add(worldTransforms[joint].Value * invMeshWorld);
            }
            else
            {
                // Missing joint: keep the bone in its bind pose (palette = identity).
                _jointMatrices.Add(
                    Matrix4x4.Invert(inverseBinds[i], out var bind) ? bind : Matrix4x4.Identity
                );
            }
        }
        _jointMatrixBuffer.Advance();
        return _jointMatrixBuffer.Upload(_jointMatrices);
    }

    /// <summary>
    /// Skins a bind-pose position on the CPU with the joint matrices of the most recent
    /// <see cref="UpdateJointMatrices"/> call, matching the GPU skinning pass. Used for CPU-side
    /// queries such as resolving pick positions.
    /// </summary>
    /// <param name="position">The bind-pose position in mesh-local space.</param>
    /// <param name="influence">The joint influence of the vertex.</param>
    /// <returns>The skinned position in mesh-local space, or <paramref name="position"/> if no joint matrices were uploaded yet.</returns>
    public Vector3 SkinPosition(Vector3 position, in GpuJointInfluence influence)
    {
        if (_jointMatrices.Count != _joints.Length)
        {
            return position;
        }
        var weights = influence.Weights;
        var skin =
            GetPalette(influence.Joint0) * weights.X
            + GetPalette(influence.Joint1) * weights.Y
            + GetPalette(influence.Joint2) * weights.Z
            + GetPalette(influence.Joint3) * weights.W;
        return Vector3.Transform(position, skin);
    }

    private Matrix4x4 GetPalette(uint joint)
    {
        return joint < (uint)_jointMatrices.Count
            ? Skeleton.InverseBindMatrices[(int)joint] * _jointMatrices[(int)joint]
            : Matrix4x4.Identity;
    }

    public void Dispose()
    {
        if (_disposedValue)
        {
            return;
        }
        Disposer.DisposeAndRemove(ref _jointMatrixBuffer);
        Disposer.DisposeAndRemove(ref _paletteBuffer);
        Disposer.DisposeAndRemove(ref _skinnedVertexBuffer);
        Disposer.DisposeAndRemove(ref _skinnedVertexPropsBuffer);
        VertexCount = 0;
        _disposedValue = true;
    }
}
