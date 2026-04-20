namespace HelixToolkit.Nex.Rendering;

/// <summary>
/// Provides extension methods for <see cref="RenderContext"/> to perform projection and unprojection operations.
/// </summary>
public static class Extensions
{
    /// <summary>
    /// Attempts to unproject screen coordinates into a world-space ray.
    /// </summary>
    /// <param name="context">The render context containing camera and viewport information.</param>
    /// <param name="x">The screen x-coordinate in pixels.</param>
    /// <param name="y">The screen y-coordinate in pixels.</param>
    /// <param name="ray">When successful, contains the world-space ray originating from the near plane through the specified screen point.</param>
    /// <returns><c>true</c> if the unprojection succeeded; <c>false</c> if the viewport or camera parameters are invalid.</returns>
    public static bool TryUnProject(this RenderContext context, float x, float y, out Ray ray)
    {
        if (context.WindowSize.Width <= 0 || context.WindowSize.Height <= 0 || context.CameraParams.Equals(CameraParams.Identity))
        {
            ray = default;
            return false;
        }
        var p = new Vector2(x, y);
        float ndcX = (2.0f * x / context.WindowSize.Width) - 1.0f;
        float ndcY = 1.0f - (2.0f * y / context.WindowSize.Height);

        var nearPoint = Vector4.Transform(new Vector4(ndcX, ndcY, 1.0f, 1.0f), context.CameraParams.InvViewProjection);
        var farPoint = Vector4.Transform(new Vector4(ndcX, ndcY, 0.0f, 1.0f), context.CameraParams.InvViewProjection);

        var nearWorld = new Vector3(nearPoint.X, nearPoint.Y, nearPoint.Z) / nearPoint.W;
        var farWorld = new Vector3(farPoint.X, farPoint.Y, farPoint.Z) / farPoint.W;

        var direction = Vector3.Normalize(farWorld - nearWorld);
        ray = new Ray(nearWorld, direction);
        return true;
    }

    /// <summary>
    /// Attempts to unproject a screen position into a world-space ray.
    /// </summary>
    /// <param name="context">The render context containing camera and viewport information.</param>
    /// <param name="screenPos">The screen position in pixels.</param>
    /// <param name="ray">When successful, contains the world-space ray originating from the near plane through the specified screen point.</param>
    /// <returns><c>true</c> if the unprojection succeeded; <c>false</c> if the viewport or camera parameters are invalid.</returns>
    public static bool TryUnProject(this RenderContext context, Vector2 screenPos, out Ray ray)
    {
        return TryUnProject(context, screenPos.X, screenPos.Y, out ray);
    }

    /// <summary>
    /// Attempts to project a world-space position onto screen coordinates.
    /// </summary>
    /// <param name="context">The render context containing camera and viewport information.</param>
    /// <param name="worldPos">The world-space position to project.</param>
    /// <param name="screenPos">When successful, contains the resulting screen position in pixels.</param>
    /// <returns><c>true</c> if the projection succeeded; <c>false</c> if the viewport or camera parameters are invalid, or the clip-space W component is zero.</returns>
    public static bool TryProject(this RenderContext context, Vector3 worldPos, out Vector2 screenPos)
    {
        if (context.WindowSize.Width <= 0 || context.WindowSize.Height <= 0 || context.CameraParams.Equals(CameraParams.Identity))
        {
            screenPos = default;
            return false;
        }
        var clipSpacePos = Vector4.Transform(new Vector4(worldPos, 1.0f), context.CameraParams.ViewProjection);
        if (clipSpacePos.W == 0)
        {
            screenPos = default;
            return false;
        }
        var ndcX = clipSpacePos.X / clipSpacePos.W;
        var ndcY = clipSpacePos.Y / clipSpacePos.W;
        screenPos = new Vector2(
            (ndcX + 1.0f) * 0.5f * context.WindowSize.Width,
            (1.0f - (ndcY + 1.0f) * 0.5f) * context.WindowSize.Height
        );
        return true;
    }
}
