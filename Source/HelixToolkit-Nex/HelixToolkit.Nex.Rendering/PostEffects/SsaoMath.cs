namespace HelixToolkit.Nex.Rendering.PostEffects;

/// <summary>
/// Pure, GPU-free reference implementations of the <see cref="SsaoPostEffect"/> math.
///
/// These helpers are the single source of truth for the effect's host-side parameter
/// validation, AO-buffer resolution derivation, projection-matrix recovery, and depth /
/// normal reconstruction. The GLSL shader (<c>psSsao.glsl</c>) mirrors the same math, so
/// this class doubles as a shader oracle and is the target of the property-based test suite.
///
/// All members are deliberately deterministic and side-effect free so they can be exercised
/// across large input spaces by property tests without a GPU or render graph.
/// </summary>
internal static class SsaoMath
{
    // ---- Parameter clamp ranges (single source of truth) — Requirement 5, 6.7 ----

    /// <summary>Minimum <c>Radius</c> in world units (Req 5.6).</summary>
    internal const float MinRadius = 0.01f;

    /// <summary>Maximum <c>Radius</c> in world units (Req 5.6).</summary>
    internal const float MaxRadius = 100.0f;

    /// <summary>Minimum <c>Intensity</c> multiplier (Req 5.7).</summary>
    internal const float MinIntensity = 0.0f;

    /// <summary>Maximum <c>Intensity</c> multiplier (Req 5.7).</summary>
    internal const float MaxIntensity = 10.0f;

    /// <summary>Minimum <c>Bias</c> in view-space units (Req 5.9).</summary>
    internal const float MinBias = 0.0f;

    /// <summary>Maximum <c>Bias</c> in view-space units (Req 5.9).</summary>
    internal const float MaxBias = 1.0f;

    /// <summary>Minimum <c>Power</c> exponent (authoritative lower bound, Req 5.10).</summary>
    internal const float MinPower = 0.1f;

    /// <summary>Maximum <c>Power</c> exponent (Req 5.10).</summary>
    internal const float MaxPower = 16.0f;

    /// <summary>Minimum hemisphere <c>Sample_Count</c> (Req 5.8).</summary>
    internal const int MinSampleCount = 1;

    /// <summary>Maximum hemisphere <c>Sample_Count</c> (Req 5.8).</summary>
    internal const int MaxSampleCount = 256;

    /// <summary>
    /// Minimum positive <c>Blur_Depth_Threshold</c> a value ≤ 0 is clamped up to (Req 6.7).
    /// </summary>
    internal const float MinBlurDepthThreshold = 1e-4f;

    // ---- Parameter clamps — Requirement 5.6–5.10, 6.7 ----

    /// <summary>Clamps <c>Radius</c> to <c>[MinRadius, MaxRadius]</c> (Req 5.6).</summary>
    internal static float ClampRadius(float value) => Math.Clamp(value, MinRadius, MaxRadius);

    /// <summary>Clamps <c>Intensity</c> to <c>[MinIntensity, MaxIntensity]</c> (Req 5.7).</summary>
    internal static float ClampIntensity(float value) => Math.Clamp(value, MinIntensity, MaxIntensity);

    /// <summary>Clamps <c>Bias</c> to <c>[MinBias, MaxBias]</c> (Req 5.9).</summary>
    internal static float ClampBias(float value) => Math.Clamp(value, MinBias, MaxBias);

    /// <summary>Clamps <c>Power</c> to <c>[MinPower, MaxPower]</c> (Req 5.10).</summary>
    internal static float ClampPower(float value) => Math.Clamp(value, MinPower, MaxPower);

    /// <summary>Clamps <c>Sample_Count</c> to <c>[MinSampleCount, MaxSampleCount]</c> (Req 5.8).</summary>
    internal static int ClampSampleCount(int value) => Math.Clamp(value, MinSampleCount, MaxSampleCount);

    /// <summary>
    /// Clamps <c>Blur_Depth_Threshold</c> so any value ≤ 0 becomes the fixed positive minimum
    /// <see cref="MinBlurDepthThreshold"/>; in-range positive values are returned unchanged (Req 6.7).
    /// </summary>
    internal static float ClampBlurDepthThreshold(float value) =>
        value <= 0.0f ? MinBlurDepthThreshold : value;

    // ---- AO buffer resolution — Requirement 8.2, 8.4, 11.1 ----

    /// <summary>
    /// Derives an AO-buffer dimension from a viewport dimension: <c>max(1, ceil(dim / 2))</c>
    /// when <paramref name="halfRes"/> is enabled, otherwise <c>max(1, dim)</c> (Req 8.2, 8.4).
    /// </summary>
    /// <param name="viewportDim">The viewport width or height in pixels.</param>
    /// <param name="halfRes">Whether half-resolution mode is enabled.</param>
    /// <returns>The AO-buffer dimension, always ≥ 1.</returns>
    internal static uint AoDim(int viewportDim, bool halfRes) =>
        halfRes ? (uint)Math.Max(1, (viewportDim + 1) / 2) : (uint)Math.Max(1, viewportDim);

    // ---- Projection matrix recovery — Requirement 2.2 ----
    //
    // FPConstants exposes viewProjection, inverseViewProjection, view, and inverseView, but no
    // standalone projection / inverse-projection matrix. The GLSL shader (matching vsPoint.glsl)
    // recovers them, in column-major GLSL order, as:
    //     projection        = viewProjection * inverseView
    //     inverseProjection = view * inverseViewProjection
    // System.Numerics is row-vector (v' = v * M), which is the transpose of the GLSL convention,
    // so the equivalent C# products reverse the operand order. This matches RenderContext's own
    // definitions ViewProjection = view * projection and InvViewProjection = invProjection * invView:
    //     inverseView * viewProjection            = V^-1 * (V * P)            = P
    //     inverseViewProjection * view            = (P^-1 * V^-1) * V         = P^-1

    /// <summary>
    /// Recovers the projection matrix as <c>inverseView * viewProjection</c> (the row-vector
    /// System.Numerics equivalent of the shader's <c>viewProjection * inverseView</c>) (Req 2.2).
    /// </summary>
    internal static Matrix4x4 RecoverProjection(in Matrix4x4 viewProjection, in Matrix4x4 inverseView) =>
        Matrix4x4.Multiply(inverseView, viewProjection);

    /// <summary>
    /// Recovers the inverse-projection matrix as <c>inverseViewProjection * view</c> (the row-vector
    /// System.Numerics equivalent of the shader's <c>view * inverseViewProjection</c>) (Req 2.2).
    /// </summary>
    internal static Matrix4x4 RecoverInverseProjection(in Matrix4x4 inverseViewProjection, in Matrix4x4 view) =>
        Matrix4x4.Multiply(inverseViewProjection, view);

    // ---- Depth / normal reconstruction — Requirement 2.3, 2.4, 2.6 ----

    /// <summary>
    /// Reconstructs a view-space position from a normalized-device-coordinate (NDC) location and a
    /// reverse-Z depth value using the inverse-projection matrix (Req 2.3).
    ///
    /// <paramref name="ndc"/> holds the NDC x/y in <c>[-1, 1]</c> and <paramref name="depth"/> is the
    /// reverse-Z depth sampled from the depth buffer (far plane = <c>0.0</c>). The homogeneous point
    /// <c>(ndc.x, ndc.y, depth, 1)</c> is transformed by the inverse projection and perspective-divided.
    /// </summary>
    internal static Vector3 ReconstructViewPosition(Vector2 ndc, float depth, in Matrix4x4 inverseProjection)
    {
        var h = Vector4.Transform(new Vector4(ndc.X, ndc.Y, depth, 1.0f), inverseProjection);
        return new Vector3(h.X, h.Y, h.Z) / h.W;
    }

    /// <summary>
    /// Reconstructs a unit-length view-space surface normal from the view-space positions of the
    /// center pixel and its horizontally and vertically adjacent samples (Req 2.4).
    ///
    /// The normal is the normalized cross product of the horizontal and vertical position
    /// derivatives, oriented to face toward the camera (the view-space origin).
    /// </summary>
    /// <param name="center">View-space position of the current pixel.</param>
    /// <param name="right">View-space position of the horizontally adjacent sample.</param>
    /// <param name="up">View-space position of the vertically adjacent sample.</param>
    internal static Vector3 ReconstructNormal(Vector3 center, Vector3 right, Vector3 up)
    {
        var ddx = right - center;
        var ddy = up - center;
        var n = Vector3.Cross(ddx, ddy);
        var lengthSq = n.LengthSquared();
        if (lengthSq <= float.Epsilon)
        {
            // Degenerate (collinear) samples: fall back to a stable camera-facing normal.
            return Vector3.UnitZ;
        }

        n = Vector3.Normalize(n);

        // Orient toward the camera. In view space the camera sits at the origin, so the direction
        // from the surface to the camera is -center; flip the normal if it points away.
        if (Vector3.Dot(n, -center) < 0.0f)
        {
            n = -n;
        }

        return n;
    }

    /// <summary>
    /// Reconstructs a unit-length view-space surface normal from the center pixel and its four
    /// (left/right/down/up) adjacent samples, choosing per axis the neighbor with the smaller depth
    /// discontinuity (Req 2.4). Unlike the one-sided forward difference in
    /// <see cref="ReconstructNormal"/>, this best-neighbor derivative avoids the faceted, quantized
    /// normals that produce patchy occlusion on smooth curved surfaces and along silhouettes. This is
    /// the reconstruction the occlusion estimator (and the GLSL shader) actually uses.
    /// </summary>
    /// <param name="center">View-space position of the current pixel.</param>
    /// <param name="left">View-space position of the left neighbor.</param>
    /// <param name="right">View-space position of the right neighbor.</param>
    /// <param name="down">View-space position of the lower neighbor.</param>
    /// <param name="up">View-space position of the upper neighbor.</param>
    internal static Vector3 ReconstructNormalBestFit(Vector3 center, Vector3 left, Vector3 right, Vector3 down, Vector3 up)
    {
        var ddx = MathF.Abs(right.Z - center.Z) < MathF.Abs(center.Z - left.Z)
            ? right - center
            : center - left;
        var ddy = MathF.Abs(up.Z - center.Z) < MathF.Abs(center.Z - down.Z)
            ? up - center
            : center - down;

        var n = Vector3.Cross(ddx, ddy);
        var lengthSq = n.LengthSquared();
        if (lengthSq <= float.Epsilon)
        {
            return Vector3.UnitZ; // degenerate (collinear) samples
        }

        n = Vector3.Normalize(n);
        if (Vector3.Dot(n, center) > 0.0f)
        {
            n = -n;
        }

        return n;
    }

    /// <summary>
    /// GLSL-equivalent <c>smoothstep(edge0, edge1, x)</c>: returns 0 below <paramref name="edge0"/>,
    /// 1 above <paramref name="edge1"/>, and a Hermite interpolation in between.
    /// </summary>
    internal static float Smoothstep(float edge0, float edge1, float x)
    {
        var t = Math.Clamp((x - edge0) / (edge1 - edge0), 0.0f, 1.0f);
        return t * t * (3.0f - 2.0f * t);
    }

    // ---- Coordinate clamping — Requirement 2.6 ----

    /// <summary>
    /// Clamps an integer sample coordinate to the nearest edge pixel of a buffer, keeping it within
    /// <c>[0, width-1] × [0, height-1]</c>. In-bounds coordinates are returned unchanged (Req 2.6).
    /// </summary>
    /// <param name="x">Sample x coordinate.</param>
    /// <param name="y">Sample y coordinate.</param>
    /// <param name="width">Buffer width in pixels (≥ 1).</param>
    /// <param name="height">Buffer height in pixels (≥ 1).</param>
    internal static (int X, int Y) ClampCoord(int x, int y, int width, int height)
    {
        var cx = Math.Clamp(x, 0, width - 1);
        var cy = Math.Clamp(y, 0, height - 1);
        return (cx, cy);
    }

    // ---- Occlusion estimation — Requirement 3.1–3.7, 2.5, 8.3 ----

    /// <summary>The reverse-Z far-plane depth value: geometry at the far plane reads exactly 0.0.</summary>
    internal const float FarPlaneDepth = 0.0f;

    private const float TwoPi = 2.0f * MathF.PI;

    /// <summary>
    /// Hammersley low-discrepancy 2D point (radical inverse of <paramref name="i"/> over
    /// <paramref name="n"/>), bit-for-bit equivalent to the <c>hammersley()</c> GLSL helper in
    /// <c>psSsao.glsl</c> so the CPU reference matches the shader.
    /// </summary>
    internal static Vector2 Hammersley(uint i, uint n)
    {
        var bits = i;
        bits = (bits << 16) | (bits >> 16);
        bits = ((bits & 0x55555555u) << 1) | ((bits & 0xAAAAAAAAu) >> 1);
        bits = ((bits & 0x33333333u) << 2) | ((bits & 0xCCCCCCCCu) >> 2);
        bits = ((bits & 0x0F0F0F0Fu) << 4) | ((bits & 0xF0F0F0F0u) >> 4);
        bits = ((bits & 0x00FF00FFu) << 8) | ((bits & 0xFF00FF00u) >> 8);
        var rdi = bits * 2.3283064365386963e-10f; // / 2^32
        return new Vector2((float)i / n, rdi);
    }

    /// <summary>
    /// Cosine-weighted hemisphere direction in tangent space (z = up), mirroring the
    /// <c>hemisphereSampleTS()</c> GLSL helper.
    /// </summary>
    internal static Vector3 HemisphereSampleTangent(Vector2 xi)
    {
        var phi = TwoPi * xi.X;
        var cosTheta = MathF.Sqrt(1.0f - xi.Y);
        var sinTheta = MathF.Sqrt(xi.Y);
        return new Vector3(MathF.Cos(phi) * sinTheta, MathF.Sin(phi) * sinTheta, cosTheta);
    }

    /// <summary>
    /// Generates exactly <paramref name="sampleCount"/> view-space hemisphere sample directions
    /// oriented by <paramref name="normal"/> (Property 8, Req 3.3). The tangent basis is rotated by
    /// <paramref name="rotationAngle"/> radians to match the per-pixel randomization the shader
    /// applies. The returned count is always exactly <paramref name="sampleCount"/>.
    /// </summary>
    internal static Vector3[] GenerateHemisphereSamples(uint sampleCount, Vector3 normal, float rotationAngle)
    {
        var count = (uint)ClampSampleCount((int)sampleCount);
        var (tangent, bitangent) = BuildTangentBasis(normal, rotationAngle);
        var samples = new Vector3[count];
        for (uint i = 0; i < count; i++)
        {
            var ts = HemisphereSampleTangent(Hammersley(i, count));
            // TBN * ts == tangent*ts.x + bitangent*ts.y + normal*ts.z (column-major GLSL basis).
            samples[i] = tangent * ts.X + bitangent * ts.Y + normal * ts.Z;
        }

        return samples;
    }

    /// <summary>
    /// Builds a tangent/bitangent basis for <paramref name="normal"/>, rotated about the normal by
    /// <paramref name="rotationAngle"/> radians (mirrors the shader's random-vector Gram-Schmidt basis).
    /// </summary>
    private static (Vector3 Tangent, Vector3 Bitangent) BuildTangentBasis(Vector3 normal, float rotationAngle)
    {
        var randomVec = new Vector3(MathF.Cos(rotationAngle), MathF.Sin(rotationAngle), 0.0f);
        var tangent = randomVec - normal * Vector3.Dot(randomVec, normal);
        var tLenSq = tangent.LengthSquared();
        if (tLenSq <= float.Epsilon)
        {
            // randomVec was (near) parallel to the normal: pick any orthogonal axis deterministically.
            tangent = MathF.Abs(normal.X) < 0.9f ? Vector3.UnitX : Vector3.UnitY;
            tangent -= normal * Vector3.Dot(tangent, normal);
        }

        tangent = Vector3.Normalize(tangent);
        var bitangent = Vector3.Cross(normal, tangent);
        return (tangent, bitangent);
    }

    /// <summary>
    /// The occluder inclusion rule (Property 7, Req 3.2, 3.4, 3.5): a neighbor contributes to a
    /// pixel's occlusion if and only if it is closer to the camera than the current pixel by more
    /// than <paramref name="bias"/> and its reconstructed position lies within <paramref name="radius"/>
    /// of the current pixel. View-space camera distance is <c>-z</c> (the camera looks down −Z).
    /// The buffer-bounds portion of the rule is enforced by the caller before this predicate is reached.
    /// </summary>
    internal static bool IsOccluder(Vector3 centerViewPos, Vector3 neighborViewPos, float radius, float bias)
    {
        if ((neighborViewPos - centerViewPos).Length() > radius)
        {
            return false; // outside the sampling radius (Req 3.5)
        }

        var centerDist = -centerViewPos.Z;
        var neighborDist = -neighborViewPos.Z;
        return centerDist - neighborDist > bias; // closer to camera by more than Bias (Req 3.2, 3.4)
    }

    /// <summary>
    /// Estimates the ambient-occlusion factor for pixel (<paramref name="px"/>, <paramref name="py"/>)
    /// of a depth field, mirroring the occlusion stage of <c>psSsao.glsl</c> exactly so this doubles
    /// as a shader oracle.
    ///
    /// Reconstructs the view-space position and normal from depth, takes exactly
    /// <paramref name="sampleCount"/> hemisphere samples oriented by the normal, applies the occluder
    /// inclusion rule (<see cref="IsOccluder"/>) with a buffer-bounds check, applies the
    /// <paramref name="power"/> exponent, and returns a value in <c>[0, 1]</c> (1.0 = fully lit).
    ///
    /// Far-plane pixels (depth == <see cref="FarPlaneDepth"/>) have no geometry and return exactly
    /// <c>1.0</c> without normal reconstruction (Req 2.5, 3.6). Because the result is always clamped to
    /// <c>[0, 1]</c>, any subsequent bilinear half-resolution upsample (a convex blend of clamped
    /// values) also stays in <c>[0, 1]</c> (Req 8.3).
    /// </summary>
    /// <param name="depth">Depth field indexed as <c>depth[y, x]</c> (dimension 0 = height, 1 = width).</param>
    /// <param name="px">Current pixel x coordinate.</param>
    /// <param name="py">Current pixel y coordinate.</param>
    /// <param name="projection">Recovered projection matrix (row-vector convention).</param>
    /// <param name="inverseProjection">Recovered inverse-projection matrix (row-vector convention).</param>
    /// <param name="radius">Sampling radius in view-space world units (&gt; 0).</param>
    /// <param name="bias">Self-occlusion suppression offset in view-space units.</param>
    /// <param name="power">Falloff contrast exponent applied to the factor.</param>
    /// <param name="sampleCount">Number of hemisphere samples (clamped to <c>[1, 256]</c>).</param>
    /// <param name="rotationAngle">Per-pixel tangent-basis rotation in radians.</param>
    /// <returns>The occlusion factor in <c>[0, 1]</c>.</returns>
    internal static float EstimateOcclusion(
        float[,] depth,
        int px,
        int py,
        in Matrix4x4 projection,
        in Matrix4x4 inverseProjection,
        float radius,
        float bias,
        float power,
        uint sampleCount,
        float rotationAngle)
    {
        var height = depth.GetLength(0);
        var width = depth.GetLength(1);
        var (cx, cy) = ClampCoord(px, py, width, height);
        var centerDepth = depth[cy, cx];

        // Far-plane pixels have no geometry: fully lit, skip normal reconstruction (Req 2.5, 3.6).
        if (MathF.Abs(centerDepth - FarPlaneDepth) <= 1e-6f)
        {
            return 1.0f;
        }

        var invSize = new Vector2(1.0f / width, 1.0f / height);
        var centerUv = PixelToUv(cx, cy, width, height);
        var centerViewPos = ReconstructViewPosition(UvToNdc(centerUv), centerDepth, inverseProjection);

        // Reconstruct the view-space normal from the four adjacent samples using the best-neighbor
        // derivative (Req 2.4), mirroring the shader — avoids faceted normals on curved surfaces.
        var leftViewPos = SampleViewPos(depth, centerUv - new Vector2(invSize.X, 0.0f), width, height, inverseProjection);
        var rightViewPos = SampleViewPos(depth, centerUv + new Vector2(invSize.X, 0.0f), width, height, inverseProjection);
        var downViewPos = SampleViewPos(depth, centerUv - new Vector2(0.0f, invSize.Y), width, height, inverseProjection);
        var upViewPos = SampleViewPos(depth, centerUv + new Vector2(0.0f, invSize.Y), width, height, inverseProjection);
        var normal = ReconstructNormalBestFit(centerViewPos, leftViewPos, rightViewPos, downViewPos, upViewPos);

        var count = (uint)ClampSampleCount((int)sampleCount);
        var directions = GenerateHemisphereSamples(count, normal, rotationAngle);

        var occluded = 0.0f;
        for (uint i = 0; i < count; i++)
        {
            // Distribute sample distances within the radius (denser near the origin) — matches shader.
            var t = (float)i / count;
            var scale = 0.1f + 0.9f * (t * t); // mix(0.1, 1.0, t*t)
            var samplePosView = centerViewPos + directions[i] * radius * scale;

            // Project the sample into screen space to find its neighbor pixel.
            var clip = Vector4.Transform(new Vector4(samplePosView, 1.0f), projection);
            if (clip.W <= 0.0f)
            {
                continue;
            }

            var sampleUv = new Vector2(clip.X / clip.W, clip.Y / clip.W) * 0.5f + new Vector2(0.5f, 0.5f);

            // Neighbor outside the buffer bounds is excluded (Req 3.5).
            if (sampleUv.X < 0.0f || sampleUv.X > 1.0f || sampleUv.Y < 0.0f || sampleUv.Y > 1.0f)
            {
                continue;
            }

            var (sx, sy) = UvToPixel(sampleUv, width, height);
            var neighborDepth = depth[sy, sx];
            if (MathF.Abs(neighborDepth - FarPlaneDepth) <= 1e-6f)
            {
                continue; // background, no occluding geometry
            }

            var neighborViewPos = ReconstructViewPosition(UvToNdc(sampleUv), neighborDepth, inverseProjection);

            // Range gate: the neighbour geometry must lie within Radius of the current pixel (Req 3.5).
            if ((neighborViewPos - centerViewPos).Length() > radius)
            {
                continue;
            }

            // Compare the stored surface against the hemisphere SAMPLE POINT (not the centre pixel):
            // occlusion only when real geometry sits in front of the sample point. Comparing against
            // the centre reports false occlusion on any surface tilted in view (flat-surface artifact).
            // The smooth range check (matches the shader) attenuates occluders whose depth separation
            // exceeds the radius, so the accumulated occlusion is continuous rather than a 0/1 count.
            var sampleDist = -samplePosView.Z;
            var neighborDist = -neighborViewPos.Z;
            if (neighborDist < sampleDist - bias)
            {
                var dz = -centerViewPos.Z - neighborDist; // centerDist - neighborDist
                occluded += Smoothstep(0.0f, 1.0f, radius / MathF.Max(MathF.Abs(dz), 1e-4f));
            }
        }

        // 1.0 = fully lit; apply the Power exponent before use (Req 3.1, 3.7).
        var factor = 1.0f - occluded / count;
        return MathF.Pow(Math.Clamp(factor, 0.0f, 1.0f), power);
    }

    /// <summary>Maps an integer pixel to its texel-center UV (y = 0 at the top edge).</summary>
    private static Vector2 PixelToUv(int px, int py, int width, int height) =>
        new((px + 0.5f) / width, (py + 0.5f) / height);

    /// <summary>Maps a UV in <c>[0, 1]</c> to the nearest in-bounds integer pixel.</summary>
    private static (int X, int Y) UvToPixel(Vector2 uv, int width, int height)
    {
        var x = (int)MathF.Floor(uv.X * width);
        var y = (int)MathF.Floor(uv.Y * height);
        return ClampCoord(x, y, width, height);
    }

    /// <summary>Maps a UV in <c>[0, 1]</c> to normalized device coordinates in <c>[-1, 1]</c>.</summary>
    private static Vector2 UvToNdc(Vector2 uv) => uv * 2.0f - new Vector2(1.0f, 1.0f);

    /// <summary>
    /// Reconstructs the view-space position for a (clamped) UV by fetching the nearest depth texel,
    /// matching the shader's edge-clamped adjacent-sample fetch (Req 2.6).
    /// </summary>
    private static Vector3 SampleViewPos(float[,] depth, Vector2 uv, int width, int height, in Matrix4x4 inverseProjection)
    {
        var clampedUv = new Vector2(Math.Clamp(uv.X, 0.0f, 1.0f), Math.Clamp(uv.Y, 0.0f, 1.0f));
        var (sx, sy) = UvToPixel(clampedUv, width, height);
        return ReconstructViewPosition(UvToNdc(clampedUv), depth[sy, sx], inverseProjection);
    }

    // ---- Edge-aware separable blur — Requirement 6.1, 6.4, 6.5 ----

    /// <summary>
    /// Reference implementation of one axis of the edge-aware separable blur (Property 12, Req 6.1,
    /// 6.4, 6.5), mirroring <c>blurPass()</c> in <c>psSsao.glsl</c>.
    ///
    /// Computes a depth-weighted average over a 5-tap kernel (offsets −2..+2, weights
    /// <c>[1, 2, 3, 2, 1]</c>) along the horizontal or vertical axis with edge clamping. A neighbor
    /// whose view-space depth differs from the center by more than <paramref name="blurDepthThreshold"/>
    /// is excluded (weight 0.0, Req 6.4). When every tap is excluded the center's unblurred value is
    /// returned (Req 6.5). The result is clamped to <c>[0, 1]</c> (Req 6.1).
    /// </summary>
    /// <param name="ao">AO field indexed as <c>ao[y, x]</c>, values in <c>[0, 1]</c>.</param>
    /// <param name="viewZ">View-space depth (z) field, same dimensions as <paramref name="ao"/>.</param>
    /// <param name="px">Center pixel x coordinate.</param>
    /// <param name="py">Center pixel y coordinate.</param>
    /// <param name="horizontal"><c>true</c> to blur along x, <c>false</c> to blur along y.</param>
    /// <param name="blurDepthThreshold">Maximum allowed view-space depth difference before exclusion.</param>
    /// <returns>The smoothed occlusion factor in <c>[0, 1]</c>.</returns>
    internal static float SeparableBlur(
        float[,] ao,
        float[,] viewZ,
        int px,
        int py,
        bool horizontal,
        float blurDepthThreshold)
    {
        var height = ao.GetLength(0);
        var width = ao.GetLength(1);
        var (cx, cy) = ClampCoord(px, py, width, height);
        var centerAo = ao[cy, cx];
        var centerViewZ = viewZ[cy, cx];

        ReadOnlySpan<float> kernel = [1.0f, 2.0f, 3.0f, 2.0f, 1.0f];

        var sum = 0.0f;
        var wsum = 0.0f;
        for (var k = -2; k <= 2; k++)
        {
            var sx = horizontal ? cx + k : cx;
            var sy = horizontal ? cy : cy + k;
            var (nx, ny) = ClampCoord(sx, sy, width, height);

            var w = kernel[k + 2];
            if (MathF.Abs(viewZ[ny, nx] - centerViewZ) > blurDepthThreshold)
            {
                w = 0.0f; // exclude across a depth discontinuity (Req 6.4)
            }

            sum += ao[ny, nx] * w;
            wsum += w;
        }

        var result = wsum > 0.0f ? sum / wsum : centerAo; // all excluded -> center (Req 6.5)
        return Math.Clamp(result, 0.0f, 1.0f); // stay in [0, 1] (Req 6.1)
    }

    // ---- Composite — Requirement 7.2, 7.3, 7.4, 9.2, 9.3 ----

    /// <summary>
    /// Reference implementation of the composite stage (Properties 13, 14, 16; Req 7.2, 7.3, 7.4,
    /// 9.2, 9.3), mirroring <c>compositePass()</c> in <c>psSsao.glsl</c>.
    ///
    /// In <see cref="SsaoDebugMode.Normal"/> mode each RGB channel of <paramref name="sceneColor"/> is
    /// multiplied by <c>clamp(1 - intensity * (1 - factor), 0, 1)</c> (a factor of <c>1.0</c> leaves the
    /// color unchanged). In <see cref="SsaoDebugMode.RawAO"/> mode the (clamped) occlusion factor is
    /// written equally to R, G, and B, producing a grayscale output. The alpha channel is preserved in
    /// both modes.
    /// </summary>
    /// <param name="sceneColor">The scene color read from the ping-pong read slot (RGBA).</param>
    /// <param name="factor">The occlusion factor (clamped to <c>[0, 1]</c> before use).</param>
    /// <param name="intensity">The darkening strength multiplier.</param>
    /// <param name="debugMode">Selects normal compositing or raw-AO grayscale output.</param>
    /// <returns>The composited RGBA color with alpha preserved.</returns>
    internal static Vector4 Composite(Vector4 sceneColor, float factor, float intensity, SsaoDebugMode debugMode)
    {
        var f = Math.Clamp(factor, 0.0f, 1.0f);

        if (debugMode == SsaoDebugMode.RawAO)
        {
            // Grayscale AO to RGB, original alpha preserved (Req 9.2, 9.3, Property 16).
            return new Vector4(f, f, f, sceneColor.W);
        }

        // Multiplicative darkening, alpha preserved (Req 7.2, 7.3, 7.4). factor == 1.0 -> unchanged.
        var darken = Math.Clamp(1.0f - intensity * (1.0f - f), 0.0f, 1.0f);
        return new Vector4(sceneColor.X * darken, sceneColor.Y * darken, sceneColor.Z * darken, sceneColor.W);
    }
}
