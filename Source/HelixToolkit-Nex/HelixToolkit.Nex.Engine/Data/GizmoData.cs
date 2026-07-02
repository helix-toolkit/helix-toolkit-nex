using HelixToolkit.Nex.Rendering.Gizmos;

namespace HelixToolkit.Nex.Engine.Data;

/// <summary>
/// Collects every entity carrying a valid <see cref="GizmoDrawInfo"/> in the active world each
/// frame and exposes them to the render node, mirroring the mesh/line/point gather pattern used by
/// <c>BoundingBoxPostEffect</c>, <c>WireframePostEffect</c>, and <c>BorderHighlightPostEffect</c>
/// (Requirement 2.2).
/// </summary>
public sealed class GizmoDataProvider(World world) : IGizmoDataProvider, IDisposable
{
    private readonly FastList<GatheredGizmo> _gizmos = [];
    private readonly EntityCollection _entities = world
        .CreateCollection()
        .Has<NodeInfo>()
        .Has<GizmoDrawInfo>()
        .Has<WorldTransform>()
        .Build();

    /// <inheritdoc />
    public IReadOnlyList<GatheredGizmo> Gizmos => _gizmos;


    public int Update()
    {
        _gizmos.Clear();
        foreach (var entity in _entities)
        {
            ref readonly var info = ref entity.Get<GizmoDrawInfo>();
            if (info.Valid)
            {
                _gizmos.Add(new GatheredGizmo(world.Id, (uint)entity.Id, info));
            }
        }
        return _gizmos.Count;
    }

    public void Dispose()
    {
        _entities.Dispose();
    }
}
