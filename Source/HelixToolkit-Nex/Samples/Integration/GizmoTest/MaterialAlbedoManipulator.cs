using System.Numerics;
using HelixToolkit.Nex.Material;
using HelixToolkit.Nex.Maths;
using HelixToolkit.Nex.Rendering.Gizmos;
using HelixToolkit.Nex.Scene;

/// <summary>
/// A demo <see cref="IGizmoManipulator"/> that drives a <em>non-transform</em> property of a scene
/// node instead of its TRS transform. It binds to a <see cref="MeshNode"/> and maps each incremental
/// drag delta onto the node's PBR material <c>Albedo</c> color, demonstrating that a custom
/// manipulator can edit any node-exposed value the manager knows nothing about.
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
/// maps the delta's translation components onto the material's <c>Albedo</c> RGB channels in
/// <see cref="ApplyDelta"/> (X to Red, Y to Green, Z to Blue), accumulating each scaled component and
/// clamping the result to the valid [0, 1] range while preserving the alpha channel.
/// </item>
/// </list>
/// </remarks>
internal sealed class MaterialAlbedoManipulator : IGizmoManipulator
{
    // Sensitivity mapping a drag-delta translation unit onto the [0, 1] color channel range.
    private const float Sensitivity = 0.02f;

    private readonly MeshNode _target;

    // Accumulated Albedo color (RGB in [0, 1]; alpha carried through from the seed).
    private Color4 _color;

    /// <summary>
    /// Initializes a new <see cref="MaterialAlbedoManipulator"/> bound to <paramref name="target"/>,
    /// seeding its accumulated color from the node's current material <c>Albedo</c> when a material is
    /// present, otherwise <see cref="Color4.Black"/>. Construction performs no writes to the material
    /// or the node transform.
    /// </summary>
    /// <param name="target">The mesh node whose material Albedo color this manipulator drives.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="target"/> is <c>null</c>.</exception>
    public MaterialAlbedoManipulator(MeshNode target)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));

        PBRMaterialProperties? material = _target.MaterialProperties;
        _color = material is not null ? material.Albedo : Color4.Black;
    }

    /// <summary>Gets the current accumulated Albedo color (for GUI display).</summary>
    public Color4 Value => _color;

    /// <summary>
    /// Returns a translation matrix at the bound node's world position so the gizmo origin sits on the
    /// node. This manipulator never edits the transform, so only the translation is meaningful here.
    /// </summary>
    /// <returns>A translation-only transform at the bound node's position.</returns>
    public Matrix4x4 GetTargetTransform() =>
        Matrix4x4.CreateTranslation(_target.Transform.Translation);

    /// <summary>
    /// Interprets the drag delta as an incremental edit to the material <c>Albedo</c> color. Only the
    /// delta's translation component is read (rotation and scale are ignored): its X, Y, and Z values
    /// are each scaled by <see cref="Sensitivity"/> and accumulated onto the Red, Green, and Blue
    /// channels respectively, with each channel clamped to the valid [0, 1] range. The alpha channel is
    /// preserved. When the bound node has a material, the resulting RGB is written back to its
    /// <c>Albedo</c>; when there is no material, the accumulated color is updated in memory only. The
    /// node transform is never modified.
    /// </summary>
    /// <param name="dragDelta">The incremental transform delta produced by the drag lifecycle.</param>
    public void ApplyDelta(in Matrix4x4 dragDelta)
    {
        Vector3 t = dragDelta.Translation;

        _color.Red = Math.Clamp(_color.Red + t.X * Sensitivity, 0f, 1f);
        _color.Green = Math.Clamp(_color.Green + t.Y * Sensitivity, 0f, 1f);
        _color.Blue = Math.Clamp(_color.Blue + t.Z * Sensitivity, 0f, 1f);
        // Alpha is intentionally left unchanged.

        PBRMaterialProperties? material = _target.MaterialProperties;
        if (material is not null)
        {
            material.Albedo = _color;
        }
    }
}
