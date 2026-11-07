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
