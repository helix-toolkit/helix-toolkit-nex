namespace HelixToolkit.Nex.Geometries;

/// <summary>
/// Helpers that build line-list <see cref="Geometry"/> for common visual indicators (direction
/// arrows, wireframe spot cones, etc.). All geometry is emitted as a LINE LIST of disjoint 2-vertex
/// segments (the vertex buffer holds pairs [A0,A1, B0,B1, ...] so <c>LineCount = Vertices.Count / 2</c>)
/// with a per-vertex color, matching the convention consumed by the line render node.
/// </summary>
public static class LineGeometryBuilder
{
    /// <summary>Default number of radial segments used to approximate a cone ring.</summary>
    public const int DefaultConeSegments = 24;

    /// <summary>
    /// Appends one disjoint 2-vertex line segment (with a uniform per-vertex color) to a line-list
    /// <see cref="Geometry"/>.
    /// </summary>
    /// <param name="geometry">The line-list geometry to append to.</param>
    /// <param name="start">Segment start position.</param>
    /// <param name="end">Segment end position.</param>
    /// <param name="color">The color applied to both segment endpoints.</param>
    public static void AddSegment(Geometry geometry, Vector3 start, Vector3 end, Vector4 color)
    {
        geometry.Vertices.Add(start.ToVector4(1f));
        geometry.Vertices.Add(end.ToVector4(1f));
        geometry.VertexColors.Add(color);
        geometry.VertexColors.Add(color);
    }

    /// <summary>
    /// Builds a directional-light style indicator: a central arrow plus four parallel rays pointing
    /// along <paramref name="direction"/> (the classic "sun" glyph). The geometry is dynamic so it can
    /// be refilled in place via <see cref="FillDirectionArrow"/>.
    /// </summary>
    /// <param name="color">The line color.</param>
    /// <param name="direction">The (unit) direction the arrow points along, in local space.</param>
    /// <param name="length">The arrow length.</param>
    /// <param name="parallelOffset">Perpendicular offset of the four parallel rays from the center ray.</param>
    /// <param name="headLength">Length of the arrowhead measured back from the tip.</param>
    /// <param name="headWidth">Half-width of the arrowhead fins.</param>
    /// <returns>A dynamic line-list <see cref="Geometry"/> containing the arrow.</returns>
    public static Geometry BuildDirectionArrow(
        Color4 color,
        Vector3 direction,
        float length = 12f,
        float parallelOffset = 3f,
        float headLength = 2.2f,
        float headWidth = 1.2f
    )
    {
        var geometry = new Geometry(isDynamic: true);
        FillDirectionArrow(
            geometry,
            color,
            direction,
            length,
            parallelOffset,
            headLength,
            headWidth
        );
        return geometry;
    }

    /// <summary>
    /// Fills (in place, clearing existing vertices) a direction-arrow indicator. Reuse this to update
    /// an existing dynamic arrow geometry (e.g. on a color change) without allocating a new geometry.
    /// </summary>
    public static void FillDirectionArrow(
        Geometry geometry,
        Color4 color,
        Vector3 direction,
        float length = 12f,
        float parallelOffset = 3f,
        float headLength = 2.2f,
        float headWidth = 1.2f
    )
    {
        geometry.Vertices.Clear();
        geometry.VertexColors.Clear();

        Vector4 c = color.ToVector4();
        Vector3 dir = Normalize(direction);
        BuildPerpendicularBasis(dir, out Vector3 u, out Vector3 v);
        Vector3 tip = dir * length;

        // Central ray plus four parallel rays offset in the plane perpendicular to the direction.
        Vector3[] offsets =
        [
            Vector3.Zero,
            u * parallelOffset,
            u * -parallelOffset,
            v * parallelOffset,
            v * -parallelOffset,
        ];
        foreach (Vector3 o in offsets)
        {
            AddSegment(geometry, o, o + tip, c);
        }

        // Arrowhead on the central ray: four short fins angling back from the tip.
        Vector3 headBase = dir * (length - headLength);
        Vector3[] fins = [u * headWidth, u * -headWidth, v * headWidth, v * -headWidth];
        foreach (Vector3 f in fins)
        {
            AddSegment(geometry, tip, headBase + f, c);
        }

        geometry.MarkDirty(GeometryBufferType.Vertex | GeometryBufferType.VertexColor);
    }

    /// <summary>
    /// Builds a spot-light style indicator: a wireframe cone with its apex at the origin, opening
    /// along local -Y to the outer half-angle at <paramref name="range"/>, plus a dimmer inner
    /// (hotspot) ring and a center axis line. The geometry is dynamic so it can be refilled in place
    /// via <see cref="FillSpotCone"/>.
    /// </summary>
    /// <param name="range">The cone length (light range).</param>
    /// <param name="spotAngles">(cos(innerHalfAngle), cos(outerHalfAngle)).</param>
    /// <param name="color">The outer line color; the inner ring uses a dimmed alpha.</param>
    /// <param name="segments">The number of radial segments approximating each ring.</param>
    /// <returns>A dynamic line-list <see cref="Geometry"/> containing the cone.</returns>
    public static Geometry BuildSpotCone(
        float range,
        Vector2 spotAngles,
        Color4 color,
        int segments = DefaultConeSegments
    )
    {
        var geometry = new Geometry(isDynamic: true);
        FillSpotCone(geometry, range, spotAngles, color, segments);
        return geometry;
    }

    /// <summary>
    /// Fills (in place, clearing existing vertices) a spot-cone indicator. Reuse this to update an
    /// existing dynamic cone geometry when the range, cone angles, or color change.
    /// </summary>
    public static void FillSpotCone(
        Geometry geometry,
        float range,
        Vector2 spotAngles,
        Color4 color,
        int segments = DefaultConeSegments
    )
    {
        geometry.Vertices.Clear();
        geometry.VertexColors.Clear();

        if (segments < 3)
        {
            segments = 3;
        }

        Vector4 outerColor = color.ToVector4();
        var innerColor = new Vector4(color.Red, color.Green, color.Blue, color.Alpha * 0.4f);

        Vector3 endCenter = -Vector3.UnitY * range; // beam end along local -Y

        float outerHalf = MathF.Acos(Math.Clamp(spotAngles.Y, -1f, 1f));
        float innerHalf = MathF.Acos(Math.Clamp(spotAngles.X, -1f, 1f));
        float outerRadius = range * MathF.Tan(outerHalf);
        float innerRadius = range * MathF.Tan(innerHalf);

        Vector3 Ring(float radius, int i)
        {
            float a = i / (float)segments * MathF.PI * 2f;
            return endCenter + new Vector3(MathF.Cos(a) * radius, 0f, MathF.Sin(a) * radius);
        }

        // Center axis line (apex -> beam end).
        AddSegment(geometry, Vector3.Zero, endCenter, outerColor);

        int spokeStep = Math.Max(1, segments / 8);
        for (int i = 0; i < segments; i++)
        {
            int next = (i + 1) % segments;

            // Outer ring + a few generatrix lines from the apex to the outer ring.
            AddSegment(geometry, Ring(outerRadius, i), Ring(outerRadius, next), outerColor);
            if (i % spokeStep == 0)
            {
                AddSegment(geometry, Vector3.Zero, Ring(outerRadius, i), outerColor);
            }

            // Dimmer inner (hotspot) ring.
            AddSegment(geometry, Ring(innerRadius, i), Ring(innerRadius, next), innerColor);
        }

        geometry.MarkDirty(GeometryBufferType.Vertex | GeometryBufferType.VertexColor);
    }

    private static Vector3 Normalize(Vector3 v)
    {
        float length = v.Length();
        return length > 1e-6f ? v / length : -Vector3.UnitY;
    }

    /// <summary>
    /// Builds two orthonormal vectors perpendicular to <paramref name="dir"/> (assumed unit length).
    /// </summary>
    private static void BuildPerpendicularBasis(Vector3 dir, out Vector3 u, out Vector3 v)
    {
        Vector3 helper = MathF.Abs(dir.Y) < 0.99f ? Vector3.UnitY : Vector3.UnitX;
        u = Vector3.Normalize(Vector3.Cross(helper, dir));
        v = Vector3.Normalize(Vector3.Cross(dir, u));
    }
}
