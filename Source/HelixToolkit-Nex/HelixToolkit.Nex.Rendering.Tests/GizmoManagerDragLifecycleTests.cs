using System.Numerics;
using HelixToolkit.Nex.Maths;
using HelixToolkit.Nex.Rendering.Gizmos;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HelixToolkit.Nex.Rendering.Tests;

/// <summary>
/// Example-based unit tests for the <see cref="GizmoManager"/> drag lifecycle edge cases.
///
/// Verifies that <see cref="GizmoManager.UpdateDrag(in Ray, out Matrix4x4)"/>:
/// <list type="bullet">
/// <item>outputs an identity delta and reports no update when no drag is active (Requirement 7.6);</item>
/// <item>outputs an identity delta, preserves the active drag state, and reports no transform when no
/// valid axis translation can be determined because the pointer ray is parallel to the constrained
/// axis (Requirement 7.5);</item>
/// <item>reports no update with an identity delta after <see cref="GizmoManager.EndDrag"/> deactivates
/// the drag (Requirements 7.4, 7.6).</item>
/// </list>
///
/// Validates: Requirements 7.5, 7.6.
/// </summary>
[TestClass]
public class GizmoManagerDragLifecycleTests
{
    // The X-axis translate handle used to anchor a valid drag; gizmo origin is world zero.
    private static readonly GizmoHandleId XHandle = new(GizmoMode.Translate, GizmoAxis.X);

    /// <summary>
    /// Creates a manager with an active translate gizmo at the world origin (target transform identity),
    /// so a subsequent <see cref="GizmoManager.BeginDrag"/> on an axis handle has a well-defined origin.
    /// </summary>
    private static GizmoManager CreateActiveManager()
    {
        var manager = new GizmoManager { Mode = GizmoMode.Translate, Space = GizmoSpace.World };
        manager.Update(CameraParams.Identity, new Size(1920, 1080), Matrix4x4.Identity);
        return manager;
    }

    // Ray from +Z looking at the origin along -Z; perpendicular to the X axis (not parallel).
    private static Ray PerpendicularRay() => new(new Vector3(0f, 0f, 5f), new Vector3(0f, 0f, -1f));

    // Ray whose direction is parallel to the world X axis (the constrained drag axis).
    private static Ray ParallelToXRay() => new(new Vector3(0f, 1f, 0f), Vector3.UnitX);

    // --- Requirement 7.6: UpdateDrag with no active drag ---

    [TestMethod]
    [TestCategory("Gizmo")]
    public void UpdateDrag_NoActiveDrag_ReturnsFalseWithIdentityAndStaysInactive()
    {
        var manager = new GizmoManager();

        Assert.IsFalse(manager.IsDragging, "A fresh manager must not be dragging.");

        bool updated = manager.UpdateDrag(PerpendicularRay(), out Matrix4x4 delta);

        Assert.IsFalse(updated, "UpdateDrag must report no update when no drag is active (Requirement 7.6).");
        Assert.AreEqual(Matrix4x4.Identity, delta, "Delta must be identity when no drag is active.");
        Assert.IsFalse(manager.IsDragging, "IsDragging must remain false when no drag is active.");
    }

    // --- Requirement 7.5: no valid axis translation determinable (ray parallel to axis) ---

    [TestMethod]
    [TestCategory("Gizmo")]
    public void UpdateDrag_RayParallelToConstrainedAxis_ReturnsFalseWithIdentityAndPreservesDrag()
    {
        var manager = CreateActiveManager();

        // Begin a valid drag along X with a ray that is not parallel to the X axis.
        bool began = manager.BeginDrag(XHandle, PerpendicularRay());
        Assert.IsTrue(began, "BeginDrag should succeed for an X handle with a non-parallel pointer ray.");
        Assert.IsTrue(manager.IsDragging, "Drag must be active after a successful BeginDrag.");

        // Update with a ray parallel to the constrained X axis: closest point is not determinable.
        bool updated = manager.UpdateDrag(ParallelToXRay(), out Matrix4x4 delta);

        Assert.IsFalse(updated, "UpdateDrag must report no transform when no valid axis translation exists (Requirement 7.5).");
        Assert.AreEqual(Matrix4x4.Identity, delta, "Delta must be identity when no valid axis translation exists.");
        Assert.IsTrue(manager.IsDragging, "Drag state must be preserved when no valid axis translation exists (Requirement 7.5).");
    }

    // --- Requirements 7.4, 7.6: EndDrag deactivates; subsequent UpdateDrag is a no-op ---

    [TestMethod]
    [TestCategory("Gizmo")]
    public void EndDrag_ThenUpdateDrag_ReturnsFalseWithIdentityAndStaysInactive()
    {
        var manager = CreateActiveManager();

        Assert.IsTrue(manager.BeginDrag(XHandle, PerpendicularRay()), "BeginDrag should succeed.");
        Assert.IsTrue(manager.IsDragging, "Drag must be active after BeginDrag.");

        manager.EndDrag();
        Assert.IsFalse(manager.IsDragging, "EndDrag must set the dragging state inactive (Requirement 7.4).");

        bool updated = manager.UpdateDrag(PerpendicularRay(), out Matrix4x4 delta);

        Assert.IsFalse(updated, "UpdateDrag after EndDrag must report no update (Requirement 7.6).");
        Assert.AreEqual(Matrix4x4.Identity, delta, "Delta must be identity after EndDrag.");
        Assert.IsFalse(manager.IsDragging, "IsDragging must remain false after EndDrag.");
    }
}
