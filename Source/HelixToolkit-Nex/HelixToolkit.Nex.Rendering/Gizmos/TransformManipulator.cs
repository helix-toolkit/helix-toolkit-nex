using HelixToolkit.Nex.Scene;

namespace HelixToolkit.Nex.Rendering.Gizmos;

/// <summary>
/// The default <see cref="IGizmoManipulator"/>. Binds to a scene-graph <see cref="Node"/> (including
/// <c>MeshNode</c>) and edits its TRS transform, reproducing the pre-change gizmo apply behavior.
/// </summary>
/// <remarks>
/// <para>
/// The manipulator keeps its own authoritative world matrix (<c>_current</c>), seeded from the
/// node's <see cref="Transform.Value"/>. Composition and decomposition go through this matrix so the
/// resulting node transform matches the pre-change caller path byte-for-byte, even for the
/// non-decomposable fallback where decomposition loses information.
/// </para>
/// <para>
/// <see cref="GetTargetTransform"/> reports <c>_current</c> for gizmo origin placement;
/// <see cref="ApplyDelta"/> composes <c>dragDelta * _current</c> (row-vector convention), then
/// TRS-decomposes and writes the result to the node, signalling the change.
/// </para>
/// </remarks>
public sealed class TransformManipulator : IGizmoManipulator
{
    private readonly Node _target;
    private Matrix4x4 _current;

    /// <summary>
    /// Initializes a new <see cref="TransformManipulator"/> bound to <paramref name="target"/>.
    /// </summary>
    /// <param name="target">The scene-graph node (or mesh node) whose transform this edits.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="target"/> is <c>null</c>.</exception>
    public TransformManipulator(Node target)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        // TRS = Scale * Rotation * Translation (row-vector); matches the pre-change authoritative matrix.
        _current = _target.Transform.Value;
    }

    /// <summary>Gets the scene-graph node this manipulator is bound to.</summary>
    public Node Target => _target;

    /// <summary>
    /// Returns the current authoritative target transform for gizmo origin placement.
    /// </summary>
    /// <returns>The current target world transform.</returns>
    public Matrix4x4 GetTargetTransform() => _current;

    /// <summary>
    /// Composes the drag delta with the current transform (<c>dragDelta * _current</c>) and writes the
    /// result back to the bound node via TRS decomposition, signalling the transform change.
    /// </summary>
    /// <param name="dragDelta">The incremental transform delta produced by the drag lifecycle.</param>
    public void ApplyDelta(in Matrix4x4 dragDelta)
    {
        // Same composition order as the pre-change path: delta * current (row-vector convention).
        _current = dragDelta * _current;
        WriteBack();
    }

    /// <summary>
    /// Decomposes <c>_current</c> and assigns Scale/Rotation/Translation to the node's transform; on a
    /// non-decomposable matrix, assigns translation only (pre-change fallback). Signals the change.
    /// </summary>
    private void WriteBack()
    {
        if (Matrix4x4.Decompose(_current, out Vector3 scale, out Quaternion rotation, out Vector3 translation))
        {
            _target.Transform.Scale = scale;
            _target.Transform.Rotation = rotation;
            _target.Transform.Translation = translation;
        }
        else
        {
            // Pre-change fallback: assign translation only when decomposition fails.
            _target.Transform.Translation = _current.Translation;
        }

        _target.NotifyTransformChanged();
    }
}
