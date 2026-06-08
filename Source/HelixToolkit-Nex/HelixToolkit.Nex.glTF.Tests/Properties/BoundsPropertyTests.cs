using System.Numerics;
using HelixToolkit.Nex.Geometries;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Maths;
using Vertex = System.Numerics.Vector4;

namespace HelixToolkit.Nex.glTF.Tests.Properties;

/// <summary>
/// Property-based tests for bounding volume containment (Property 7).
/// </summary>
[TestClass]
public class BoundsPropertyTests
{
    private static readonly Config FsCheckConfig = Config.Default.WithMaxTest(100);

    // Feature: gltf-importer, Property 7: Bounding volume containment

    /// <summary>
    /// Property 7: For any non-empty set of vertices in a Geometry, the computed BoundingBoxLocal
    /// SHALL contain every vertex position, and the computed BoundingSphereLocal SHALL contain
    /// every vertex position.
    /// **Validates: Requirements 3.11**
    /// </summary>
    [TestMethod]
    public void BoundingVolumes_ContainAllVertices_ForAnyNonEmptyVertexSet()
    {
        // Generate random arrays of N (1..200) vertex positions (Vector4 with w=1.0)
        var verticesGen =
            from count in Gen.Choose(1, 200)
            from coords in Gen.ArrayOf(
                Gen.Choose(-10000000, 10000000).Select(v => v / 1000.0f),
                count * 3
            )
            select (count, coords);

        Prop.ForAll(
                Arb.From(verticesGen),
                ((int count, float[] coords) input) =>
                {
                    int count = input.count;
                    float[] coords = input.coords;

                    // Create vertices as Vector4 with w=1.0
                    var vertices = new Vertex[count];
                    for (int i = 0; i < count; i++)
                    {
                        vertices[i] = new Vertex(
                            coords[i * 3],
                            coords[i * 3 + 1],
                            coords[i * 3 + 2],
                            1.0f
                        );
                    }

                    // Create Geometry and populate vertices
                    var geometry = new Geometry(vertices, Topology.Point);

                    // Compute bounding volumes
                    geometry.CreateBoundingBox();
                    geometry.CreateBoundingSphere();

                    var bb = geometry.BoundingBoxLocal;
                    var bs = geometry.BoundingSphereLocal;

                    // Verify every vertex is contained within BoundingBoxLocal
                    for (int i = 0; i < count; i++)
                    {
                        var v = vertices[i];

                        // BoundingBox containment: vertex >= Minimum and vertex <= Maximum
                        if (v.X < bb.Minimum.X || v.X > bb.Maximum.X)
                            return false;
                        if (v.Y < bb.Minimum.Y || v.Y > bb.Maximum.Y)
                            return false;
                        if (v.Z < bb.Minimum.Z || v.Z > bb.Maximum.Z)
                            return false;
                    }

                    // Verify every vertex is contained within BoundingSphereLocal
                    const float tolerance = 1e-4f;
                    for (int i = 0; i < count; i++)
                    {
                        var v = vertices[i];
                        var vertexPos = new Vector3(v.X, v.Y, v.Z);
                        float distance = Vector3.Distance(vertexPos, bs.Center);

                        if (distance > bs.Radius + tolerance)
                            return false;
                    }

                    return true;
                }
            )
            .Check(FsCheckConfig);
    }
}
