using HelixToolkit.Nex.Engine.Cameras;

namespace HelixToolkit.Nex.Engine
{
    public static class CameraExtensions
    {
        /// <summary>
        /// Re-centers the camera on a new target point and optionally adjusts the distance.
        /// Use this to focus the camera on a specific object or point.
        /// The camera maintains its current viewing direction (look direction) while
        /// repositioning to look at the new target.
        /// </summary>
        /// <param name="camera">The camera to adjust.</param>
        /// <param name="target">The new look-at point / focus target.</param>
        /// <param name="distance">
        /// Optional distance from the new target. If <c>null</c>, the current distance
        /// (radius from camera to its current target) is preserved.
        /// </param>
        public static void FocusOn(this Camera camera, Vector3 target, float? distance = null)
        {
            // Calculate the current look direction and distance
            var currentOffset = camera.Position - camera.Target;
            var currentDistance = currentOffset.Length();

            // Use the provided distance or preserve the current one
            var newDistance = distance ?? currentDistance;

            // Ensure we have a valid distance
            if (newDistance < MathUtil.ZeroTolerance)
            {
                newDistance = 1.0f;
            }

            // Calculate the direction from target to camera (opposite of look direction)
            Vector3 offsetDirection;
            if (currentDistance > MathUtil.ZeroTolerance)
            {
                // Use the current offset direction (camera position relative to target)
                offsetDirection = currentOffset / currentDistance;
            }
            else
            {
                // Fallback: place camera along negative Z axis from target
                offsetDirection = -Vector3.UnitZ;
            }

            // Update target and position
            camera.Target = target;
            camera.Position = target + offsetDirection * newDistance;
        }

        /// <summary>
        /// Focuses the camera on the specified bounding box, adjusting the distance
        /// so that the entire box is visible within the camera's field of view.
        /// </summary>
        /// <param name="camera">The camera to adjust.</param>
        /// <param name="boundingBox">The bounding box to focus on.</param>
        /// <param name="marginFactor">
        /// A multiplier applied to the calculated distance to add margin around the object.
        /// Default is 1.2 (20% margin). Use 1.0 for a tight fit.
        /// </param>
        public static void FocusOn(this Camera camera, BoundingBox boundingBox, float marginFactor = 1.2f)
        {
            // Calculate the center of the bounding box
            var center = (boundingBox.Minimum + boundingBox.Maximum) * 0.5f;

            // Calculate the radius of the bounding sphere
            var extents = (boundingBox.Maximum - boundingBox.Minimum) * 0.5f;
            var boundingSphereRadius = extents.Length();

            // Calculate the required distance based on camera type
            float requiredDistance;

            if (camera is PerspectiveCamera perspectiveCamera)
            {
                // For perspective camera, calculate distance based on FOV
                // distance = radius / sin(fov/2) to ensure the sphere fits in view
                var halfFov = perspectiveCamera.Fov * 0.5f;
                var sinHalfFov = MathF.Sin(halfFov);

                if (sinHalfFov > MathUtil.ZeroTolerance)
                {
                    requiredDistance = boundingSphereRadius / sinHalfFov;
                }
                else
                {
                    requiredDistance = boundingSphereRadius * 2.0f;
                }
            }
            else if (camera is OrthographicCamera orthoCamera)
            {
                // For orthographic camera, the distance doesn't affect the view size,
                // but we still position the camera at a reasonable distance
                requiredDistance = boundingSphereRadius * 2.0f;

                // Adjust orthographic width to fit the bounding box
                orthoCamera.Width = boundingSphereRadius * 2.0f * marginFactor;
            }
            else
            {
                // Fallback for other camera types
                requiredDistance = boundingSphereRadius * 2.0f;
            }

            // Apply margin factor
            requiredDistance *= marginFactor;

            // Ensure minimum distance
            requiredDistance = MathF.Max(requiredDistance, camera.NearPlane + 0.01f);

            // Focus on the center with the calculated distance
            camera.FocusOn(center, requiredDistance);
        }

        /// <summary>
        /// Focuses the camera on the specified bounding sphere, adjusting the distance
        /// so that the entire sphere is visible within the camera's field of view.
        /// </summary>
        /// <param name="camera">The camera to adjust.</param>
        /// <param name="center">The center of the bounding sphere.</param>
        /// <param name="radius">The radius of the bounding sphere.</param>
        /// <param name="marginFactor">
        /// A multiplier applied to the calculated distance to add margin around the object.
        /// Default is 1.2 (20% margin). Use 1.0 for a tight fit.
        /// </param>
        public static void FocusOn(this Camera camera, Vector3 center, float radius, float marginFactor = 1.2f)
        {
            // Create a bounding box from the sphere and delegate
            var extents = new Vector3(radius);
            var boundingBox = new BoundingBox(center - extents, center + extents);
            camera.FocusOn(boundingBox, marginFactor);
        }
    }
}
