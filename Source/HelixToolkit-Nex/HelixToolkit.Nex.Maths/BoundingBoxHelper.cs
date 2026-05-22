namespace HelixToolkit.Nex.Maths
{
    /// <summary>
    ///
    /// </summary>
    public static class BoundingBoxHelper
    {
        /// <summary>
        /// Get bounding box from list of points
        /// </summary>
        /// <param name="points">The points.</param>
        /// <returns></returns>
        public static BoundingBox FromPoints(IList<Vector3>? points)
        {
            if (points == null || points.Count == 0)
            {
                return new BoundingBox();
            }
            points.MinMax(out var min, out var max);
            Vector3 diff = max - min;
            return diff.AnySmallerOrEqual(0.0001f) // Avoid bound too small on one dimension.
                ? new BoundingBox(min - new Vector3(0.1f), max + new Vector3(0.1f))
                : new BoundingBox(min, max);
        }

        /// <summary>
        /// Transform AABB with Affine Transformation matrix
        /// </summary>
        /// <param name="box"></param>
        /// <param name="transform"></param>
        /// <returns></returns>
        public static BoundingBox Transform(this BoundingBox box, Matrix transform)
        {
            return Transform(box, ref transform);
        }

        /// <summary>
        /// Transform AABB with Affine Transformation matrix
        /// </summary>
        /// <param name="box"></param>
        /// <param name="transform"></param>
        /// <returns></returns>
        public static BoundingBox Transform(this BoundingBox box, ref Matrix transform)
        {
            // For each local axis, compute the two extremes and pick min/max component-wise.
            // This eliminates all branches and allows the JIT to use SIMD instructions.
            Vector3 xa = new Vector3(transform.M11, transform.M12, transform.M13) * box.Minimum.X;
            Vector3 xb = new Vector3(transform.M11, transform.M12, transform.M13) * box.Maximum.X;

            Vector3 ya = new Vector3(transform.M21, transform.M22, transform.M23) * box.Minimum.Y;
            Vector3 yb = new Vector3(transform.M21, transform.M22, transform.M23) * box.Maximum.Y;

            Vector3 za = new Vector3(transform.M31, transform.M32, transform.M33) * box.Minimum.Z;
            Vector3 zb = new Vector3(transform.M31, transform.M32, transform.M33) * box.Maximum.Z;

            Vector3 t = transform.Translation;
            return new BoundingBox(
                t + Vector3.Min(xa, xb) + Vector3.Min(ya, yb) + Vector3.Min(za, zb),
                t + Vector3.Max(xa, xb) + Vector3.Max(ya, yb) + Vector3.Max(za, zb)
            );
        }
    }
}
