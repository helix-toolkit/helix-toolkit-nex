using System.Numerics;
using HelixToolkit.Nex.Maths;

namespace HelixToolkit.Nex.Shaders;

public static class Helpers
{
    public static CullingConstants CreateCullConstants(in Matrix4x4 view, in Matrix4x4 proj)
    {
        var viewProj = Matrix4x4.Multiply(view, proj);
        var frustum = BoundingFrustum.FromViewProjectInversedZ(viewProj);

        return new CullingConstants
        {
            ViewMatrix = view,
            ViewProjectionMatrix = viewProj,
            ProjectionMatrix = proj,
            PlaneCount = frustum.Far.Normal == Vector3.Zero ? 5u : 6u,

            // Pack frustum planes for the shader
            FrustumPlanes_0 = frustum.Left.ToVector4(),
            FrustumPlanes_1 = frustum.Right.ToVector4(),
            FrustumPlanes_2 = frustum.Top.ToVector4(),
            FrustumPlanes_3 = frustum.Bottom.ToVector4(),
            FrustumPlanes_4 = frustum.Near.ToVector4(),
            FrustumPlanes_5 = frustum.Far.ToVector4(),
        };
    }

    public static void UpdateCullConstants(
        ref CullingConstants cullConstants,
        in Matrix4x4 view,
        in Matrix4x4 proj
    )
    {
        var viewProj = Matrix4x4.Multiply(view, proj);
        var frustum = BoundingFrustum.FromViewProjectInversedZ(viewProj);
        cullConstants.ViewMatrix = view;
        cullConstants.ViewProjectionMatrix = viewProj;
        cullConstants.ProjectionMatrix = proj;
        cullConstants.PlaneCount = frustum.Far.Normal == Vector3.Zero ? 5u : 6u;
        cullConstants.FrustumPlanes_0 = frustum.Left.ToVector4();
        cullConstants.FrustumPlanes_1 = frustum.Right.ToVector4();
        cullConstants.FrustumPlanes_2 = frustum.Top.ToVector4();
        cullConstants.FrustumPlanes_3 = frustum.Bottom.ToVector4();
        cullConstants.FrustumPlanes_4 = frustum.Near.ToVector4();
        cullConstants.FrustumPlanes_5 = frustum.Far.ToVector4();
    }
}
