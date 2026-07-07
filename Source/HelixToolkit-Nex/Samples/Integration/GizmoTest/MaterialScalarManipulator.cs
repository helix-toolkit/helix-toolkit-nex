using System.Numerics;
using HelixToolkit.Nex.Material;
using HelixToolkit.Nex.Rendering.Gizmos;
using HelixToolkit.Nex.Scene;

/// <summary>
/// A demo <see cref="IGizmoManipulator"/> that drives a <em>non-transform</em> property of a scene
/// node instead of its TRS transform (Req 9.4, 9.5). It binds to a <see cref="MeshNode"/> and maps
/// each incremental drag delta onto the node's PBR material <c>Metallic</c> scalar, demonstrating that
/// a custom manipulator can edit any node-exposed value the manager knows nothing about.
/// </summary>
/// <remarks>
/// <para>
/// The manager only ever calls <see cref="GetTargetTransform"/> (for gizmo origin placement) and
/// <see cref="ApplyDelta"/> (forwarding the raw, uninterpreted delta). This manipulator therefore:
/// </para>
/// <list type="bullet">
/// <item>
/// reports a translation matrix at the bound node's world position from <see cref="GetTargetTransform"/>
/// so the gizmo has a sensible origin (the manipulator never moves the node);
/// </item>
/// <item>
/// extracts a signed scalar from the delta's translation component in <see cref="ApplyDelta"/> and
/// accumulates it into the material's <c>Metallic</c> factor, clamped to the valid [0, 1] range.
/// </item>
/// </list>
/// </remarks>
internal sealed class MaterialScalarManipulator : IGizmoManipulator
{
    // Sensitivity mapping a drag-delta translation unit onto the [0, 1] material scalar range.
    private const float Sensitivity = 0.02f;

    private readonly MeshNode _target;
    private float _value;

    /// <summary>
    /// Initializes a new <see cref="MaterialScalarManipulator"/> bound to <paramref name="target"/>,
    /// seeding its scalar from the node's current material <c>Metallic</c> value.
    /// </summary>
    /// <param name="target">The mesh node whose material scalar this manipulator drives.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="target"/> is <c>null</c>.</exception>
    public MaterialScalarManipulator(MeshNode target)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _value = _target.MaterialProperties?.Metallic ?? 0f;
    }

    /// <summary>Gets the current driven material scalar value (for GUI display).</summary>
    public float Value => _value;

    /// <summary>
    /// Returns a translation matrix at the bound node's world position so the gizmo origin sits on the
    /// node. This manipulator never edits the transform, so only the translation is meaningful here.
    /// </summary>
    /// <returns>A translation-only transform at the bound node's position.</returns>
    public Matrix4x4 GetTargetTransform() =>
        Matrix4x4.CreateTranslation(_target.Transform.Translation);

    /// <summary>
    /// Interprets the drag delta as a scalar edit: it takes the signed magnitude of the delta's
    /// translation component, scales it by <see cref="Sensitivity"/>, accumulates it into the material
    /// <c>Metallic</c> factor, and clamps the result to [0, 1]. The node transform is left untouched.
    /// </summary>
    /// <param name="dragDelta">The incremental transform delta produced by the drag lifecycle.</param>
    public void ApplyDelta(in Matrix4x4 dragDelta)
    {
        // Collapse the delta's translation into a single signed scalar so any axis drag drives the value.
        Vector3 t = dragDelta.Translation;
        float scalar = t.X + t.Y + t.Z;

        _value = Math.Clamp(_value + scalar * Sensitivity, 0f, 1f);

        PBRMaterialProperties? material = _target.MaterialProperties;
        if (material is not null)
        {
            material.Metallic = _value;
        }
    }
}
