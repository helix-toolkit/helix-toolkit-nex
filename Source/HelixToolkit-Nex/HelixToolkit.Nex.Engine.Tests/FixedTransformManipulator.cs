using System.Numerics;
using HelixToolkit.Nex.Rendering.Gizmos;

namespace HelixToolkit.Nex.Engine.Tests;

/// <summary>
/// A minimal <see cref="IGizmoManipulator"/> test stub that reports a fixed transform for gizmo
/// origin placement and records the last applied drag delta. Used to drive
/// <see cref="GizmoManager.UpdateInstance"/> after the raw target-transform argument was removed, so
/// a test that previously passed a matrix now binds a manipulator returning that same matrix.
/// </summary>
internal sealed class FixedTransformManipulator : IGizmoManipulator
{
    /// <summary>The transform returned for gizmo origin placement.</summary>
    public Matrix4x4 Transform { get; set; }

    /// <summary>The most recent delta forwarded through <see cref="ApplyDelta"/>, if any.</summary>
    public Matrix4x4? LastDelta { get; private set; }

    public FixedTransformManipulator(Matrix4x4 transform) => Transform = transform;

    public Matrix4x4 GetTargetTransform() => Transform;

    public void ApplyDelta(in Matrix4x4 dragDelta) => LastDelta = dragDelta;
}
