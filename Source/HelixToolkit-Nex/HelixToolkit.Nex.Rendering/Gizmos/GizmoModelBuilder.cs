namespace HelixToolkit.Nex.Rendering.Gizmos;

/// <summary>
/// Pure geometry generation for the three standard gizmo modes (translate, rotate, scale).
/// </summary>
/// <remarks>
/// <para>
/// The builder produces <see cref="GizmoHandle"/> descriptors only. It has no rendering side
/// effects: it issues no draw calls, binds no GPU resources, and modifies no render target.
/// This keeps handle generation deterministic and unit-testable.
/// </para>
/// <para>
/// Each handle is expressed in gizmo-local space. Constant-screen-size scaling is applied later
/// in the vertex shader and is deliberately not baked into <see cref="GizmoHandle.LocalTransform"/>.
/// </para>
/// <para>
/// Generated handles are <em>appended</em> to the supplied output list; the caller owns clearing
/// or reusing that list between frames.
/// </para>
/// </remarks>
public static class GizmoModelBuilder
{
    /// <summary>
    /// Threshold at or below which a handle is treated as degenerate. Applies to the absolute
    /// determinant of the local transform, a line/arrow handle's length, and a ring handle's
    /// radius, all measured in canonical local units.
    /// </summary>
    private const float DegeneracyEpsilon = 1e-6f;

    /// <summary>Distinct color for the X axis (red).</summary>
    private static readonly Color4 AxisColorX = new(1f, 0f, 0f, 1f);

    /// <summary>Distinct color for the Y axis (green).</summary>
    private static readonly Color4 AxisColorY = new(0f, 1f, 0f, 1f);

    /// <summary>Distinct color for the Z axis (blue).</summary>
    private static readonly Color4 AxisColorZ = new(0f, 0f, 1f, 1f);

    /// <summary>
    /// Generates the translate handle set: one directional arrow handle per supported translate
    /// axis (X, Y, Z), each oriented along its corresponding gizmo-local axis and assigned a color
    /// distinct from every other axis in the set.
    /// </summary>
    /// <param name="outHandles">The list the generated handles are appended to.</param>
    public static void BuildTranslate(List<GizmoHandle> outHandles)
    {
        ArgumentNullException.ThrowIfNull(outHandles);

        AppendAxisHandle(outHandles, GizmoMode.Translate, GizmoAxis.X, GizmoHandleShape.Arrow, AxisColorX);
        AppendAxisHandle(outHandles, GizmoMode.Translate, GizmoAxis.Y, GizmoHandleShape.Arrow, AxisColorY);
        AppendAxisHandle(outHandles, GizmoMode.Translate, GizmoAxis.Z, GizmoHandleShape.Arrow, AxisColorZ);
    }

    /// <summary>
    /// Generates the rotate handle set: one ring handle per supported rotation axis (X, Y, Z),
    /// each oriented so its plane normal aligns with its corresponding gizmo-local axis and
    /// assigned a color distinct from every other axis in the set.
    /// </summary>
    /// <param name="outHandles">The list the generated handles are appended to.</param>
    public static void BuildRotate(List<GizmoHandle> outHandles)
    {
        ArgumentNullException.ThrowIfNull(outHandles);

        AppendRingHandle(outHandles, GizmoAxis.X, AxisColorX);
        AppendRingHandle(outHandles, GizmoAxis.Y, AxisColorY);
        AppendRingHandle(outHandles, GizmoAxis.Z, AxisColorZ);
    }

    /// <summary>
    /// Generates the scale handle set: one box handle per supported scale axis (X, Y, Z), each
    /// oriented along its corresponding gizmo-local axis and assigned a color distinct from every
    /// other axis in the set.
    /// </summary>
    /// <param name="outHandles">The list the generated handles are appended to.</param>
    public static void BuildScale(List<GizmoHandle> outHandles)
    {
        ArgumentNullException.ThrowIfNull(outHandles);

        AppendAxisHandle(outHandles, GizmoMode.Scale, GizmoAxis.X, GizmoHandleShape.Box, AxisColorX);
        AppendAxisHandle(outHandles, GizmoMode.Scale, GizmoAxis.Y, GizmoHandleShape.Box, AxisColorY);
        AppendAxisHandle(outHandles, GizmoMode.Scale, GizmoAxis.Z, GizmoHandleShape.Box, AxisColorZ);
    }

    /// <summary>
    /// Builds a directional (arrow / box) handle whose canonical local +X direction is rotated to
    /// point along the requested axis. The transform is rotation-only: the shaft/head geometry is
    /// authored in the vertex shader extending from the gizmo origin along local +X to x = 1, and
    /// <c>ScreenScale</c> (applied in the shader) sizes both the handle length and its distance from
    /// the origin uniformly. Baking a positional offset here would leave that offset un-scaled by
    /// <c>ScreenScale</c>, clustering the handles on top of one another at the origin.
    /// </summary>
    private static void AppendAxisHandle(
        List<GizmoHandle> outHandles,
        GizmoMode mode,
        GizmoAxis axis,
        GizmoHandleShape shape,
        Color4 color)
    {
        // Rotation that maps the canonical local +X direction onto the target axis direction.
        Matrix4x4 local = axis switch
        {
            GizmoAxis.X => Matrix4x4.Identity,
            GizmoAxis.Y => Matrix4x4.CreateRotationZ(MathF.PI / 2f),   // +X -> +Y
            GizmoAxis.Z => Matrix4x4.CreateRotationY(-MathF.PI / 2f),  // +X -> +Z
            _ => Matrix4x4.Identity,
        };

        CreateHandle(outHandles, mode, axis, shape, color, local);
    }

    /// <summary>
    /// Builds a rotation ring handle whose canonical plane (local XY, normal +Z) is rotated so its
    /// plane normal aligns with the requested axis.
    /// </summary>
    private static void AppendRingHandle(
        List<GizmoHandle> outHandles,
        GizmoAxis axis,
        Color4 color)
    {
        // Rotation that maps the canonical ring normal (+Z) onto the target axis direction.
        Matrix4x4 local = axis switch
        {
            GizmoAxis.X => Matrix4x4.CreateRotationY(MathF.PI / 2f),   // +Z -> +X
            GizmoAxis.Y => Matrix4x4.CreateRotationX(-MathF.PI / 2f),  // +Z -> +Y
            GizmoAxis.Z => Matrix4x4.Identity,                         // +Z stays +Z
            _ => Matrix4x4.Identity,
        };

        CreateHandle(outHandles, GizmoMode.Rotate, axis, GizmoHandleShape.Ring, color, local);
    }

    /// <summary>
    /// Assembles a <see cref="GizmoHandle"/> and appends it to <paramref name="outHandles"/>.
    /// Handle identity is the <see cref="GizmoHandleId"/> (mode + axis); no reserved-band entity id
    /// is assigned (Requirement 9.3). Degenerate handles (see
    /// <see cref="IsDegenerate(GizmoHandleShape, in Matrix4x4)"/>) are excluded entirely: they are
    /// neither appended nor drawn (Requirement 2.5).
    /// </summary>
    private static void CreateHandle(
        List<GizmoHandle> outHandles,
        GizmoMode mode,
        GizmoAxis axis,
        GizmoHandleShape shape,
        Color4 color,
        Matrix4x4 localTransform)
    {
        // Exclude degenerate handles from every generated handle set.
        if (IsDegenerate(shape, localTransform))
        {
            return;
        }

        outHandles.Add(new GizmoHandle(new GizmoHandleId(mode, axis), shape, axis, color, localTransform));
    }

    /// <summary>
    /// Determines whether a handle is degenerate and must be excluded from the generated set.
    /// </summary>
    /// <remarks>
    /// A handle is degenerate when any of the following hold, all measured in canonical local units:
    /// <list type="bullet">
    /// <item>Its local transform is non-invertible, i.e. the absolute value of its determinant is
    /// <c>1e-6</c> or less.</item>
    /// <item>It is a line or arrow handle whose length (the canonical unit +X extent scaled by the
    /// local transform) is <c>1e-6</c> or less.</item>
    /// <item>It is a ring handle whose radius (the canonical unit in-plane extent scaled by the
    /// local transform) is <c>1e-6</c> or less.</item>
    /// </list>
    /// Canonical handle geometry is unit-sized in local space, so the effective length/radius is the
    /// magnitude of the corresponding canonical basis direction after the local transform is applied.
    /// </remarks>
    /// <param name="handle">The handle to test.</param>
    /// <returns><see langword="true"/> if the handle is degenerate; otherwise <see langword="false"/>.</returns>
    public static bool IsDegenerate(in GizmoHandle handle)
        => IsDegenerate(handle.Shape, handle.LocalTransform);

    /// <summary>
    /// Determines whether a handle of the given shape and local transform is degenerate.
    /// See <see cref="IsDegenerate(in GizmoHandle)"/> for the exact criteria.
    /// </summary>
    /// <param name="shape">The handle's shape, which determines the length/radius check applied.</param>
    /// <param name="localTransform">The handle's gizmo-local transform.</param>
    /// <returns><see langword="true"/> if the handle is degenerate; otherwise <see langword="false"/>.</returns>
    public static bool IsDegenerate(GizmoHandleShape shape, in Matrix4x4 localTransform)
    {
        // Non-invertible local transform: |det| <= 1e-6.
        if (MathF.Abs(localTransform.GetDeterminant()) <= DegeneracyEpsilon)
        {
            return true;
        }

        switch (shape)
        {
            case GizmoHandleShape.Line:
            case GizmoHandleShape.Arrow:
                // Length = canonical unit +X extent scaled by the local transform.
                if (Vector3.TransformNormal(Vector3.UnitX, localTransform).Length() <= DegeneracyEpsilon)
                {
                    return true;
                }
                break;

            case GizmoHandleShape.Ring:
                // Radius = canonical unit in-plane (+X) extent scaled by the local transform.
                if (Vector3.TransformNormal(Vector3.UnitX, localTransform).Length() <= DegeneracyEpsilon)
                {
                    return true;
                }
                break;
        }

        return false;
    }
}
