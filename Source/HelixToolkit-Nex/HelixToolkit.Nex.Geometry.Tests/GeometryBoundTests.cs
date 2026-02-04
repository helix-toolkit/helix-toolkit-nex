using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Maths;

namespace HelixToolkit.Nex.Geometries.Tests;

[TestClass]
public class GeometryBoundTests
{
    [TestMethod]
    public void CreateBoundingBox_EmptyGeometry_ReturnsEmptyBoundingBox()
    {
        // Arrange
        var geometry = new Geometry();

        // Act
        geometry.CreateBoundingBox();

        // Assert
        Assert.IsTrue(geometry.BoundingBoxLocal.IsEmpty);
        Assert.AreEqual(BoundingBox.Empty, geometry.BoundingBoxLocal);
    }

    [TestMethod]
    public void CreateBoundingBox_SingleVertex_ReturnsPointBoundingBox()
    {
        // Arrange
        var vertex = new Vector4(1.0f, 2.0f, 3.0f, 1.0f);
        var geometry = new Geometry(new[] { vertex }, Topology.Point);

        // Act
        geometry.CreateBoundingBox();

        // Assert
        Assert.IsFalse(geometry.BoundingBoxLocal.IsEmpty);
        Assert.AreEqual(new Vector3(1.0f, 2.0f, 3.0f), geometry.BoundingBoxLocal.Minimum);
        Assert.AreEqual(new Vector3(1.0f, 2.0f, 3.0f), geometry.BoundingBoxLocal.Maximum);
    }

    [TestMethod]
    public void CreateBoundingBox_MultipleVertices_ReturnsCorrectBounds()
    {
        // Arrange
        var vertices = new[]
        {
            new Vector4(-1.0f, -2.0f, -3.0f, 1.0f),
            new Vector4(1.0f, 2.0f, 3.0f, 1.0f),
            new Vector4(0.0f, 0.0f, 0.0f, 1.0f),
            new Vector4(-0.5f, 1.5f, -1.5f, 1.0f),
        };
        var geometry = new Geometry(vertices, Topology.Point);

        // Act
        geometry.CreateBoundingBox();

        // Assert
        Assert.IsFalse(geometry.BoundingBoxLocal.IsEmpty);
        Assert.AreEqual(new Vector3(-1.0f, -2.0f, -3.0f), geometry.BoundingBoxLocal.Minimum);
        Assert.AreEqual(new Vector3(1.0f, 2.0f, 3.0f), geometry.BoundingBoxLocal.Maximum);
    }

    [TestMethod]
    public void CreateBoundingBox_TriangleMesh_ReturnsCorrectBounds()
    {
        // Arrange
        var vertices = new[]
        {
            new Vector4(0.0f, 0.0f, 0.0f, 1.0f),
            new Vector4(5.0f, 0.0f, 0.0f, 1.0f),
            new Vector4(2.5f, 5.0f, 0.0f, 1.0f),
            new Vector4(2.5f, 2.5f, 5.0f, 1.0f),
        };
        var indices = new uint[] { 0, 1, 2, 0, 2, 3, 0, 3, 1, 1, 3, 2 };
        var geometry = new Geometry(vertices, indices, topology: Topology.Triangle);

        // Act
        geometry.CreateBoundingBox();

        // Assert
        Assert.IsFalse(geometry.BoundingBoxLocal.IsEmpty);
        Assert.AreEqual(new Vector3(0.0f, 0.0f, 0.0f), geometry.BoundingBoxLocal.Minimum);
        Assert.AreEqual(new Vector3(5.0f, 5.0f, 5.0f), geometry.BoundingBoxLocal.Maximum);
    }

    [TestMethod]
    public void CreateBoundingBox_AllPositiveCoordinates_ReturnsCorrectBounds()
    {
        // Arrange
        var vertices = new[]
        {
            new Vector4(1.0f, 1.0f, 1.0f, 1.0f),
            new Vector4(5.0f, 3.0f, 2.0f, 1.0f),
            new Vector4(2.0f, 4.0f, 6.0f, 1.0f),
        };
        var geometry = new Geometry(vertices, Topology.Point);

        // Act
        geometry.CreateBoundingBox();

        // Assert
        Assert.AreEqual(new Vector3(1.0f, 1.0f, 1.0f), geometry.BoundingBoxLocal.Minimum);
        Assert.AreEqual(new Vector3(5.0f, 4.0f, 6.0f), geometry.BoundingBoxLocal.Maximum);
    }

    [TestMethod]
    public void CreateBoundingBox_AllNegativeCoordinates_ReturnsCorrectBounds()
    {
        // Arrange
        var vertices = new[]
        {
            new Vector4(-5.0f, -4.0f, -6.0f, 1.0f),
            new Vector4(-1.0f, -1.0f, -1.0f, 1.0f),
            new Vector4(-3.0f, -2.0f, -3.0f, 1.0f),
        };
        var geometry = new Geometry(vertices, Topology.Point);

        // Act
        geometry.CreateBoundingBox();

        // Assert
        Assert.AreEqual(new Vector3(-5.0f, -4.0f, -6.0f), geometry.BoundingBoxLocal.Minimum);
        Assert.AreEqual(new Vector3(-1.0f, -1.0f, -1.0f), geometry.BoundingBoxLocal.Maximum);
    }

    [TestMethod]
    public void CreateBoundingSphere_EmptyGeometry_ReturnsEmptySphere()
    {
        // Arrange
        var geometry = new Geometry();

        // Act
        geometry.CreateBoundingSphere();

        // Assert
        Assert.AreEqual(BoundingSphere.Empty, geometry.BoundingSphereLocal);
    }

    [TestMethod]
    public void CreateBoundingSphere_SingleVertex_ReturnsPointSphere()
    {
        // Arrange
        var vertex = new Vector4(1.0f, 2.0f, 3.0f, 1.0f);
        var geometry = new Geometry(new[] { vertex }, Topology.Point);

        // Act
        geometry.CreateBoundingSphere();

        // Assert
        Assert.AreEqual(new Vector3(1.0f, 2.0f, 3.0f), geometry.BoundingSphereLocal.Center);
        Assert.IsTrue(MathUtil.IsZero(geometry.BoundingSphereLocal.Radius));
    }

    [TestMethod]
    public void CreateBoundingSphere_MultipleVertices_ReturnsEnclosingSphere()
    {
        // Arrange
        var vertices = new[]
        {
            new Vector4(-1.0f, 0.0f, 0.0f, 1.0f),
            new Vector4(1.0f, 0.0f, 0.0f, 1.0f),
            new Vector4(0.0f, -1.0f, 0.0f, 1.0f),
            new Vector4(0.0f, 1.0f, 0.0f, 1.0f),
            new Vector4(0.0f, 0.0f, -1.0f, 1.0f),
            new Vector4(0.0f, 0.0f, 1.0f, 1.0f),
        };
        var geometry = new Geometry(vertices, Topology.Point);

        // Act
        geometry.CreateBoundingSphere();

        // Assert
        // The sphere should be centered at origin
        Assert.IsTrue(
            MathUtil.WithinEpsilon(geometry.BoundingSphereLocal.Center.X, 0.0f, 0.001f),
            $"Center.X expected 0, actual {geometry.BoundingSphereLocal.Center.X}"
        );
        Assert.IsTrue(
            MathUtil.WithinEpsilon(geometry.BoundingSphereLocal.Center.Y, 0.0f, 0.001f),
            $"Center.Y expected 0, actual {geometry.BoundingSphereLocal.Center.Y}"
        );
        Assert.IsTrue(
            MathUtil.WithinEpsilon(geometry.BoundingSphereLocal.Center.Z, 0.0f, 0.001f),
            $"Center.Z expected 0, actual {geometry.BoundingSphereLocal.Center.Z}"
        );

        // All vertices should be within or on the sphere
        foreach (var vertex in vertices)
        {
            var vertexPos = new Vector3(vertex.X, vertex.Y, vertex.Z);
            var distance = Vector3.Distance(vertexPos, geometry.BoundingSphereLocal.Center);
            Assert.IsTrue(
                distance <= geometry.BoundingSphereLocal.Radius + 0.001f,
                $"Vertex {vertexPos} is outside the bounding sphere"
            );
        }
    }

    [TestMethod]
    public void CreateBoundingSphere_CubicVertices_ReturnsEnclosingSphere()
    {
        // Arrange - 8 vertices of a unit cube centered at origin
        var vertices = new[]
        {
            new Vector4(-0.5f, -0.5f, -0.5f, 1.0f),
            new Vector4(0.5f, -0.5f, -0.5f, 1.0f),
            new Vector4(-0.5f, 0.5f, -0.5f, 1.0f),
            new Vector4(0.5f, 0.5f, -0.5f, 1.0f),
            new Vector4(-0.5f, -0.5f, 0.5f, 1.0f),
            new Vector4(0.5f, -0.5f, 0.5f, 1.0f),
            new Vector4(-0.5f, 0.5f, 0.5f, 1.0f),
            new Vector4(0.5f, 0.5f, 0.5f, 1.0f),
        };
        var geometry = new Geometry(vertices, Topology.Point);

        // Act
        geometry.CreateBoundingSphere();

        // Assert
        // The sphere should be centered at origin
        Assert.IsTrue(
            MathUtil.WithinEpsilon(geometry.BoundingSphereLocal.Center.X, 0.0f, 0.01f),
            $"Center.X expected 0, actual {geometry.BoundingSphereLocal.Center.X}"
        );
        Assert.IsTrue(
            MathUtil.WithinEpsilon(geometry.BoundingSphereLocal.Center.Y, 0.0f, 0.01f),
            $"Center.Y expected 0, actual {geometry.BoundingSphereLocal.Center.Y}"
        );
        Assert.IsTrue(
            MathUtil.WithinEpsilon(geometry.BoundingSphereLocal.Center.Z, 0.0f, 0.01f),
            $"Center.Z expected 0, actual {geometry.BoundingSphereLocal.Center.Z}"
        );

        // Expected radius is the distance from center to corner: sqrt(3 * 0.5^2) = sqrt(0.75)
        var expectedRadius = MathF.Sqrt(0.75f);
        Assert.IsTrue(
            geometry.BoundingSphereLocal.Radius >= expectedRadius - 0.01f,
            $"Radius {geometry.BoundingSphereLocal.Radius} should be at least {expectedRadius}"
        );

        // All vertices should be within or on the sphere
        foreach (var vertex in vertices)
        {
            var vertexPos = new Vector3(vertex.X, vertex.Y, vertex.Z);
            var distance = Vector3.Distance(vertexPos, geometry.BoundingSphereLocal.Center);
            Assert.IsTrue(
                distance <= geometry.BoundingSphereLocal.Radius + 0.01f,
                $"Vertex {vertexPos} is outside the bounding sphere"
            );
        }
    }

    [TestMethod]
    public void CreateBoundingSphere_TriangleMesh_AllVerticesEnclosed()
    {
        // Arrange
        var vertices = new[]
        {
            new Vector4(0.0f, 0.0f, 0.0f, 1.0f),
            new Vector4(5.0f, 0.0f, 0.0f, 1.0f),
            new Vector4(2.5f, 5.0f, 0.0f, 1.0f),
            new Vector4(2.5f, 2.5f, 5.0f, 1.0f),
        };
        var indices = new uint[] { 0, 1, 2, 0, 2, 3, 0, 3, 1, 1, 3, 2 };
        var geometry = new Geometry(vertices, indices, topology: Topology.Triangle);

        // Act
        geometry.CreateBoundingSphere();

        // Assert
        // All vertices should be within or on the sphere
        foreach (var vertex in vertices)
        {
            var vertexPos = new Vector3(vertex.X, vertex.Y, vertex.Z);
            var distance = Vector3.Distance(vertexPos, geometry.BoundingSphereLocal.Center);
            Assert.IsTrue(
                distance <= geometry.BoundingSphereLocal.Radius + 0.01f,
                $"Vertex {vertexPos} is outside the bounding sphere. Distance: {distance}, Radius: {geometry.BoundingSphereLocal.Radius}"
            );
        }
    }

    [TestMethod]
    public void CreateBoundingBox_CalledMultipleTimes_UpdatesCorrectly()
    {
        // Arrange
        var geometry = new Geometry();

        // Act & Assert - First call with no vertices
        geometry.CreateBoundingBox();
        Assert.IsTrue(geometry.BoundingBoxLocal.IsEmpty);

        // Add vertices and recalculate
        geometry.Vertices.Add(new Vector4(1.0f, 1.0f, 1.0f, 1.0f));
        geometry.Vertices.Add(new Vector4(2.0f, 2.0f, 2.0f, 1.0f));
        geometry.CreateBoundingBox();

        Assert.IsFalse(geometry.BoundingBoxLocal.IsEmpty);
        Assert.AreEqual(new Vector3(1.0f, 1.0f, 1.0f), geometry.BoundingBoxLocal.Minimum);
        Assert.AreEqual(new Vector3(2.0f, 2.0f, 2.0f), geometry.BoundingBoxLocal.Maximum);
    }

    [TestMethod]
    public void CreateBoundingSphere_CalledMultipleTimes_UpdatesCorrectly()
    {
        // Arrange
        var geometry = new Geometry();

        // Act & Assert - First call with no vertices
        geometry.CreateBoundingSphere();
        Assert.AreEqual(BoundingSphere.Empty, geometry.BoundingSphereLocal);

        // Add vertices and recalculate
        geometry.Vertices.Add(new Vector4(-1.0f, 0.0f, 0.0f, 1.0f));
        geometry.Vertices.Add(new Vector4(1.0f, 0.0f, 0.0f, 1.0f));
        geometry.CreateBoundingSphere();

        Assert.AreNotEqual(BoundingSphere.Empty, geometry.BoundingSphereLocal);
        Assert.IsTrue(geometry.BoundingSphereLocal.Radius > 0.0f);
    }

    [TestMethod]
    public void CreateBoundingBox_WithVertexProps_IgnoresPropsUsesOnlyVertices()
    {
        // Arrange
        var vertices = new[]
        {
            new Vector4(0.0f, 0.0f, 0.0f, 1.0f),
            new Vector4(1.0f, 1.0f, 1.0f, 1.0f),
        };
        var vertexProps = new[]
        {
            new VertexProperties(Vector3.UnitY, Vector2.Zero),
            new VertexProperties(Vector3.UnitX, Vector2.One),
        };
        var geometry = new Geometry(vertices, vertexProps, Topology.Point);

        // Act
        geometry.CreateBoundingBox();

        // Assert - Should only use vertex positions, not properties
        Assert.AreEqual(new Vector3(0.0f, 0.0f, 0.0f), geometry.BoundingBoxLocal.Minimum);
        Assert.AreEqual(new Vector3(1.0f, 1.0f, 1.0f), geometry.BoundingBoxLocal.Maximum);
    }

    [TestMethod]
    public void CreateBoundingSphere_WithIndices_UsesOnlyVertexPositions()
    {
        // Arrange
        var vertices = new[]
        {
            new Vector4(0.0f, 0.0f, 0.0f, 1.0f),
            new Vector4(10.0f, 0.0f, 0.0f, 1.0f),
            new Vector4(0.0f, 10.0f, 0.0f, 1.0f),
        };
        // Indices reference only first two vertices
        var indices = new uint[] { 0, 1 };
        var geometry = new Geometry(vertices, indices, topology: Topology.Line);

        // Act
        geometry.CreateBoundingSphere();

        // Assert - Should use ALL vertices, not just indexed ones
        foreach (var vertex in vertices)
        {
            var vertexPos = new Vector3(vertex.X, vertex.Y, vertex.Z);
            var distance = Vector3.Distance(vertexPos, geometry.BoundingSphereLocal.Center);
            Assert.IsTrue(
                distance <= geometry.BoundingSphereLocal.Radius + 0.01f,
                $"Vertex {vertexPos} is outside the bounding sphere"
            );
        }
    }
}
