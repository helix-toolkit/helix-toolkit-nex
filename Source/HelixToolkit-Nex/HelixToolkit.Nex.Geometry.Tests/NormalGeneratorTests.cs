using System.Numerics;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Shaders;

namespace HelixToolkit.Nex.Geometries.Tests;

[TestClass]
public sealed class NormalGeneratorTests
{
    private const float Tolerance = 1e-5f;

    // Two triangles sharing the edge 1-2: one in the XY plane, one folded down along -Z.
    private static Geometry CreateFoldedQuad()
    {
        var geometry = new Geometry(Topology.Triangle);
        geometry.Vertices.Add(new Vector4(0, 0, 0, 1));
        geometry.Vertices.Add(new Vector4(1, 0, 0, 1));
        geometry.Vertices.Add(new Vector4(0, 1, 0, 1));
        geometry.Vertices.Add(new Vector4(1, 0, -1, 1));
        geometry.Indices.Add(0);
        geometry.Indices.Add(1);
        geometry.Indices.Add(2);
        geometry.Indices.Add(1);
        geometry.Indices.Add(3);
        geometry.Indices.Add(2);
        return geometry;
    }

    private static void AssertVector(Vector3 expected, Vector3 actual)
    {
        Assert.IsTrue(
            Vector3.Distance(expected, actual) < Tolerance,
            $"Expected {expected}, got {actual}."
        );
    }

    [TestMethod]
    public void ComputeFlatNormals_Indexed_UnweldsAndAssignsFaceNormals()
    {
        var geometry = CreateFoldedQuad();

        NormalGenerator.ComputeFlatNormals(geometry);

        Assert.AreEqual(6, geometry.Vertices.Count);
        Assert.AreEqual(6, geometry.VertexProps.Count);
        CollectionAssert.AreEqual(new uint[] { 0, 1, 2, 3, 4, 5 }, geometry.Indices.ToArray());
        Assert.AreEqual(new Vector4(1, 0, -1, 1), geometry.Vertices[4]);
        for (var i = 0; i < 3; ++i)
        {
            AssertVector(Vector3.UnitZ, geometry.VertexProps[i].Normal);
        }
        var second = Vector3.Normalize(new Vector3(1, 1, 0));
        for (var i = 3; i < 6; ++i)
        {
            AssertVector(second, geometry.VertexProps[i].Normal);
        }
    }

    [TestMethod]
    public void ComputeFlatNormals_Indexed_DuplicatesMorphTargetDeltas()
    {
        var geometry = CreateFoldedQuad();
        var target = new MorphTarget("Lift");
        for (var i = 0; i < geometry.Vertices.Count; ++i)
        {
            target.PositionDeltas.Add(new Vector3(0, 0, i));
        }
        geometry.MorphTargets.Add(target);

        NormalGenerator.ComputeFlatNormals(geometry);

        Assert.AreEqual(1, geometry.MorphTargets.Count);
        var unwelded = geometry.MorphTargets[0];
        Assert.AreEqual("Lift", unwelded.Name);
        Assert.AreEqual(0, unwelded.NormalDeltas.Count, "Missing deltas stay missing.");
        // Corners follow the indices 0, 1, 2, 1, 3, 2.
        CollectionAssert.AreEqual(
            new[] { 0f, 1f, 2f, 1f, 3f, 2f },
            unwelded.PositionDeltas.ToArray().Select(d => d.Z).ToArray()
        );
        Assert.IsTrue(unwelded.IsValidFor(geometry.Vertices.Count));
    }

    [TestMethod]
    public void ComputeFlatNormals_MarksGeometryWithFlatNormals()
    {
        var geometry = CreateFoldedQuad();
        Assert.IsFalse(geometry.HasFlatNormals);

        NormalGenerator.ComputeFlatNormals(geometry);

        Assert.IsTrue(geometry.HasFlatNormals);
    }

    [TestMethod]
    public void ComputeTangents_MarksGeometryWithGeneratedTangents()
    {
        var geometry = CreateFoldedQuad();
        NormalGenerator.ComputeFlatNormals(geometry);
        for (var i = 0; i < geometry.VertexProps.Count; ++i)
        {
            var prop = geometry.VertexProps[i];
            prop.TexCoord = new Vector2(geometry.Vertices[i].X, geometry.Vertices[i].Y);
            prop.Tangent = Vector4.Zero;
            geometry.VertexProps[i] = prop;
        }
        Assert.IsFalse(geometry.HasGeneratedTangents);

        TangentGenerator.ComputeTangents(geometry);

        Assert.IsTrue(geometry.HasGeneratedTangents);
        Assert.IsTrue(new Geometry(geometry).HasGeneratedTangents, "Copies keep the flag.");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ComputeTangents_FollowsTheSelectedUvSet(bool useSecondUv)
    {
        // A flat quad in the XY plane. The first UV set maps u along +X; the second UV set is
        // rotated a quarter turn so that u runs along +Y.
        var geometry = new Geometry(Topology.Triangle);
        Vector2[] positions = [new(0, 0), new(1, 0), new(0, 1), new(1, 1)];
        foreach (var p in positions)
        {
            geometry.Vertices.Add(new Vector4(p.X, p.Y, 0, 1));
            geometry.VertexProps.Add(
                new VertexProperties(Vector3.UnitZ, new Vector2(p.X, p.Y))
                {
                    TexCoord1 = new Vector2(p.Y, -p.X),
                    Tangent = Vector4.Zero,
                }
            );
        }
        uint[] indices = [0, 1, 2, 1, 3, 2];
        foreach (var index in indices)
        {
            geometry.Indices.Add(index);
        }

        TangentGenerator.ComputeTangents(geometry, useSecondUv);

        Assert.IsTrue(geometry.HasGeneratedTangents);
        Assert.AreEqual(useSecondUv, geometry.GeneratedTangentsUseSecondUv);
        Assert.IsTrue(new Geometry(geometry).GeneratedTangentsUseSecondUv == useSecondUv, "Copies keep the flag.");
        var expected = useSecondUv ? Vector3.UnitY : Vector3.UnitX;
        for (var i = 0; i < geometry.VertexProps.Count; ++i)
        {
            var tangent = geometry.VertexProps[i].Tangent;
            AssertVector(expected, new Vector3(tangent.X, tangent.Y, tangent.Z));
        }
    }

    [TestMethod]
    public void CopyConstructor_ClonesMorphTargets()
    {
        var geometry = CreateFoldedQuad();
        var target = new MorphTarget("Lift");
        for (var i = 0; i < geometry.Vertices.Count; ++i)
        {
            target.PositionDeltas.Add(Vector3.UnitZ);
        }
        geometry.MorphTargets.Add(target);
        geometry.HasFlatNormals = true;

        var copy = new Geometry(geometry);
        copy.MorphTargets[0].PositionDeltas[0] = Vector3.UnitX;

        Assert.AreNotSame(target, copy.MorphTargets[0]);
        Assert.AreEqual("Lift", copy.MorphTargets[0].Name);
        Assert.AreEqual(Vector3.UnitZ, target.PositionDeltas[0], "The original deltas are unchanged.");
        Assert.IsTrue(copy.HasFlatNormals);
    }

    [TestMethod]
    public void ComputeFlatNormals_Indexed_DuplicatesPerVertexAttributes()
    {
        var geometry = CreateFoldedQuad();
        for (var i = 0; i < 4; ++i)
        {
            geometry.VertexProps.Add(new VertexProperties(Vector3.Zero, new Vector2(i, 0)));
            geometry.VertexColors.Add(new Vector4(i, 0, 0, 1));
            geometry.JointInfluences.Add(new GpuJointInfluence { Joint0 = (uint)i, Weights = Vector4.UnitX });
        }

        NormalGenerator.ComputeFlatNormals(geometry);

        var expectedSource = new[] { 0, 1, 2, 1, 3, 2 };
        Assert.AreEqual(6, geometry.VertexColors.Count);
        Assert.AreEqual(6, geometry.JointInfluences.Count);
        for (var i = 0; i < 6; ++i)
        {
            Assert.AreEqual(expectedSource[i], geometry.VertexProps[i].TexCoord.X);
            Assert.AreEqual(expectedSource[i], geometry.VertexColors[i].X);
            Assert.AreEqual((uint)expectedSource[i], geometry.JointInfluences[i].Joint0);
        }
    }

    [TestMethod]
    public void ComputeFlatNormals_NonIndexed_KeepsVertices()
    {
        var geometry = new Geometry(Topology.Triangle);
        geometry.Vertices.Add(new Vector4(0, 0, 0, 1));
        geometry.Vertices.Add(new Vector4(0, 1, 0, 1));
        geometry.Vertices.Add(new Vector4(1, 0, 0, 1));

        NormalGenerator.ComputeFlatNormals(geometry);

        Assert.AreEqual(3, geometry.Vertices.Count);
        Assert.AreEqual(0, geometry.Indices.Count);
        for (var i = 0; i < 3; ++i)
        {
            AssertVector(-Vector3.UnitZ, geometry.VertexProps[i].Normal);
            Assert.AreEqual(VertexProperties.DefaultTangent, geometry.VertexProps[i].Tangent);
        }
    }

    [TestMethod]
    public void ComputeFlatNormals_DegenerateTriangle_GetsZeroNormal()
    {
        var geometry = new Geometry(Topology.Triangle);
        geometry.Vertices.Add(new Vector4(0, 0, 0, 1));
        geometry.Vertices.Add(new Vector4(1, 0, 0, 1));
        geometry.Vertices.Add(new Vector4(2, 0, 0, 1));

        NormalGenerator.ComputeFlatNormals(geometry);

        Assert.AreEqual(Vector3.Zero, geometry.VertexProps[0].Normal);
    }

    [TestMethod]
    public void ComputeFlatNormals_IndexOutOfRange_DropsTriangle()
    {
        var geometry = CreateFoldedQuad();
        geometry.Indices[4] = 99;

        NormalGenerator.ComputeFlatNormals(geometry);

        Assert.AreEqual(3, geometry.Vertices.Count);
        Assert.AreEqual(3, geometry.Indices.Count);
    }

    [TestMethod]
    public void ComputeFlatNormals_NonTriangleTopology_DoesNothing()
    {
        var geometry = new Geometry(Topology.Line);
        geometry.Vertices.Add(new Vector4(0, 0, 0, 1));
        geometry.Vertices.Add(new Vector4(1, 0, 0, 1));
        geometry.Vertices.Add(new Vector4(0, 1, 0, 1));
        geometry.Indices.Add(0);
        geometry.Indices.Add(1);
        geometry.Indices.Add(2);

        NormalGenerator.ComputeFlatNormals(geometry);

        Assert.AreEqual(0, geometry.VertexProps.Count);
        Assert.AreEqual(3, geometry.Vertices.Count);
    }
}
