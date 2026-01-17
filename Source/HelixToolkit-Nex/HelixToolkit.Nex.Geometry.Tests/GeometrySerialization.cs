using System.Numerics;
using System.Text.Json;
using HelixToolkit.Nex.Graphics;

namespace HelixToolkit.Nex.Geometries.Tests;

[TestClass]
public sealed class GeometrySerialization
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    [TestMethod]
    public void SerializeDeserialize_EmptyGeometry()
    {
        // Arrange
        var original = new Geometry();

        // Act
        string json = JsonSerializer.Serialize(original, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<Geometry>(json);

        // Assert
        Assert.IsNotNull(deserialized);
        Assert.AreEqual(original.Id, deserialized.Id);
        Assert.AreEqual(original.Topology, deserialized.Topology);
        Assert.AreEqual(0, deserialized.Vertices.Count);
        Assert.AreEqual(0, deserialized.Indices.Count);
        Assert.AreEqual(0, deserialized.VertexColors.Count);
        Assert.AreEqual(original.IsDynamic, deserialized.IsDynamic);
    }

    [TestMethod]
    public void SerializeDeserialize_GeometryWithVertices()
    {
        // Arrange
        var vertices = new[]
        {
            new Vertex(new Vector3(0, 0, 0), new Vector3(0, 1, 0), new Vector2(0, 0)),
            new Vertex(new Vector3(1, 0, 0), new Vector3(0, 1, 0), new Vector2(1, 0)),
            new Vertex(new Vector3(0, 1, 0), new Vector3(0, 1, 0), new Vector2(0, 1)),
        };
        var original = new Geometry(vertices, Topology.Triangle);

        // Act
        string json = JsonSerializer.Serialize(original, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<Geometry>(json);

        // Assert
        Assert.IsNotNull(deserialized);
        Assert.AreEqual(original.Id, deserialized.Id);
        Assert.AreEqual(Topology.Triangle, deserialized.Topology);
        Assert.AreEqual(3, deserialized.Vertices.Count);

        for (int i = 0; i < vertices.Length; i++)
        {
            AssertVertexEqual(vertices[i], deserialized.Vertices[i]);
        }
    }

    [TestMethod]
    public void SerializeDeserialize_GeometryWithIndices()
    {
        // Arrange
        var vertices = new[]
        {
            new Vertex(new Vector3(0, 0, 0), new Vector3(0, 1, 0)),
            new Vertex(new Vector3(1, 0, 0), new Vector3(0, 1, 0)),
            new Vertex(new Vector3(1, 1, 0), new Vector3(0, 1, 0)),
            new Vertex(new Vector3(0, 1, 0), new Vector3(0, 1, 0)),
        };
        var indices = new uint[] { 0, 1, 2, 0, 2, 3 };
        var original = new Geometry(vertices, indices, topology: Topology.Triangle);

        // Act
        string json = JsonSerializer.Serialize(original, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<Geometry>(json);

        // Assert
        Assert.IsNotNull(deserialized);
        Assert.AreEqual(4, deserialized.Vertices.Count);
        Assert.AreEqual(6, deserialized.Indices.Count);
        CollectionAssert.AreEqual(indices, deserialized.Indices.ToArray());
    }

    [TestMethod]
    public void SerializeDeserialize_GeometryWithVertexColors()
    {
        // Arrange
        var vertices = new[]
        {
            new Vertex(new Vector3(0, 0, 0)),
            new Vertex(new Vector3(1, 0, 0)),
            new Vertex(new Vector3(0, 1, 0)),
        };
        var colors = new[]
        {
            new Vector4(1, 0, 0, 0),
            new Vector4(0, 1, 0, 0),
            new Vector4(0, 0, 1, 0),
        };
        var indices = new uint[] { 0, 1, 2 };
        var original = new Geometry(vertices, indices, colors, Topology.Triangle);

        // Act
        string json = JsonSerializer.Serialize(original, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<Geometry>(json);

        // Assert
        Assert.IsNotNull(deserialized);
        Assert.AreEqual(3, deserialized.VertexColors.Count);

        for (int i = 0; i < colors.Length; i++)
        {
            Assert.AreEqual(colors[i], deserialized.VertexColors[i]);
        }
    }

    [TestMethod]
    public void SerializeDeserialize_CompleteGeometry()
    {
        // Arrange
        var vertices = new[]
        {
            new Vertex(
                new Vector3(0, 0, 0),
                new Vector3(0, 1, 0),
                new Vector2(0, 0),
                new Vector3(1, 0, 0)
            ),
            new Vertex(
                new Vector3(1, 0, 0),
                new Vector3(0, 1, 0),
                new Vector2(1, 0),
                new Vector3(0, 1, 0)
            ),
            new Vertex(
                new Vector3(0, 1, 0),
                new Vector3(0, 1, 0),
                new Vector2(0, 1),
                new Vector3(0, 0, 1)
            ),
        };
        var indices = new uint[] { 0, 1, 2 };
        var colors = new[]
        {
            new Vector4(1, 0, 0, 0),
            new Vector4(0, 1, 0, 0),
            new Vector4(0, 0, 1, 0),
        };

        var original = new Geometry(vertices, indices, colors, Topology.Triangle)
        {
            IsDynamic = true,
        };
        var originalId = original.Id;

        // Act
        string json = JsonSerializer.Serialize(original, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<Geometry>(json);

        // Assert
        Assert.IsNotNull(deserialized);
        Assert.AreEqual(originalId, deserialized.Id);
        Assert.AreEqual(Topology.Triangle, deserialized.Topology);
        Assert.AreEqual(3, deserialized.Vertices.Count);
        Assert.AreEqual(3, deserialized.Indices.Count);
        Assert.AreEqual(3, deserialized.VertexColors.Count);
        Assert.IsTrue(deserialized.IsDynamic);

        for (int i = 0; i < vertices.Length; i++)
        {
            AssertVertexEqual(vertices[i], deserialized.Vertices[i]);
        }

        CollectionAssert.AreEqual(indices, deserialized.Indices.ToArray());

        for (int i = 0; i < colors.Length; i++)
        {
            Assert.AreEqual(colors[i], deserialized.VertexColors[i]);
        }
    }

    [TestMethod]
    [DataRow(Topology.Point)]
    [DataRow(Topology.Line)]
    [DataRow(Topology.Triangle)]
    [DataRow(Topology.TriangleStrip)]
    [DataRow(Topology.LineStrip)]
    public void SerializeDeserialize_DifferentTopologies(Topology topology)
    {
        // Arrange
        var vertices = new[]
        {
            new Vertex(new Vector3(0, 0, 0)),
            new Vertex(new Vector3(1, 0, 0)),
            new Vertex(new Vector3(0, 1, 0)),
        };
        var original = new Geometry(vertices, topology);

        // Act
        string json = JsonSerializer.Serialize(original, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<Geometry>(json);

        // Assert
        Assert.IsNotNull(deserialized);
        Assert.AreEqual(topology, deserialized.Topology);
    }

    [TestMethod]
    public void SerializeDeserialize_VertexWithAllProperties()
    {
        // Arrange
        var vertex = new Vertex(
            new Vector3(1.5f, 2.5f, 3.5f),
            new Vector3(0, 1, 0),
            new Vector2(0.5f, 0.75f),
            new Vector3(0.8f, 0.6f, 0.4f)
        );

        // Act
        string json = JsonSerializer.Serialize(vertex, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<Vertex>(json);

        // Assert
        AssertVertexEqual(vertex, deserialized);
    }

    [TestMethod]
    public void SerializeDeserialize_FastList()
    {
        // Arrange
        var list = new FastList<int> { 1, 2, 3, 4, 5 };

        // Act
        string json = JsonSerializer.Serialize(list, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<FastList<int>>(json);

        // Assert
        Assert.IsNotNull(deserialized);
        Assert.AreEqual(5, deserialized.Count);
        CollectionAssert.AreEqual(list.ToArray(), deserialized.ToArray());
    }

    [TestMethod]
    public void SerializeDeserialize_LargeGeometry()
    {
        // Arrange - Create a geometry with many vertices
        const int vertexCount = 1000;
        var vertices = Enumerable
            .Range(0, vertexCount)
            .Select(i => new Vertex(
                new Vector3(i, i * 2, i * 3),
                new Vector3(0, 1, 0),
                new Vector2(i / 1000f, i / 1000f),
                new Vector3(1, 1, 1)
            ))
            .ToArray();

        var indices = Enumerable.Range(0, vertexCount).Select(i => (uint)i).ToArray();
        var original = new Geometry(vertices, indices, topology: Topology.Triangle);

        // Act
        string json = JsonSerializer.Serialize(original, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<Geometry>(json);

        // Assert
        Assert.IsNotNull(deserialized);
        Assert.AreEqual(vertexCount, deserialized.Vertices.Count);
        Assert.AreEqual(vertexCount, deserialized.Indices.Count);
    }

    [TestMethod]
    public void SerializeDeserialize_PreservesGuid()
    {
        // Arrange
        var original = new Geometry();
        var originalId = original.Id;

        // Act
        string json = JsonSerializer.Serialize(original, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<Geometry>(json);

        // Assert
        Assert.IsNotNull(deserialized);
        Assert.AreEqual(originalId, deserialized.Id);
        Assert.AreNotEqual(Guid.Empty, deserialized.Id);
    }

    [TestMethod]
    public void SerializeDeserialize_IsDynamicProperty()
    {
        // Arrange
        var original = new Geometry { IsDynamic = true };

        // Act
        string json = JsonSerializer.Serialize(original, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<Geometry>(json);

        // Assert
        Assert.IsNotNull(deserialized);
        Assert.IsTrue(deserialized.IsDynamic);
    }

    [TestMethod]
    public void Serialize_ProducesValidJson()
    {
        // Arrange
        var geometry = new Geometry(
            new[] { new Vertex(new Vector3(0, 0, 0)) },
            new uint[] { 0 },
            topology: Topology.Triangle
        );

        // Act
        string json = JsonSerializer.Serialize(geometry, JsonOptions);

        // Assert
        Assert.IsTrue(json.Contains("\"Id\""));
        Assert.IsTrue(json.Contains("\"Topology\""));
        Assert.IsTrue(json.Contains("\"Vertices\""));
        Assert.IsTrue(json.Contains("\"Indices\""));
        Assert.IsTrue(json.Contains("\"IsDynamic\""));
    }

    [TestMethod]
    public void Deserialize_HandlesEmptyCollections()
    {
        // Arrange
        var guid = Guid.NewGuid();
        string json =
            @"{
            ""Id"": """
            + guid
            + @""",
            ""Topology"": "
            + (int)Topology.Triangle
            + @",
            ""Vertices"": [],
            ""Indices"": [],
            ""VertexColors"": [],
            ""IsDynamic"": false
        }";

        // Act
        var deserialized = JsonSerializer.Deserialize<Geometry>(json);

        // Assert
        Assert.IsNotNull(deserialized);
        Assert.AreEqual(0, deserialized.Vertices.Count);
        Assert.AreEqual(0, deserialized.Indices.Count);
        Assert.AreEqual(0, deserialized.VertexColors.Count);
    }

    [TestMethod]
    public void Deserialize_HandlesNullBiNormals()
    {
        // Arrange
        var guid = Guid.NewGuid();
        string json =
            @"{
            ""Id"": """
            + guid
            + @""",
            ""Topology"": "
            + (int)Topology.Triangle
            + @",
            ""Vertices"": [],
            ""Indices"": [],
            ""IsDynamic"": false
        }";

        // Act
        var deserialized = JsonSerializer.Deserialize<Geometry>(json);

        // Assert
        Assert.IsNotNull(deserialized);
        Assert.AreEqual(0, deserialized.VertexColors.Count);
    }

    #region Helper Methods

    private static void AssertVertexEqual(Vertex expected, Vertex actual)
    {
        Assert.AreEqual(expected.Position, actual.Position, $"Position mismatch");
        Assert.AreEqual(expected.Normal, actual.Normal, $"Normal mismatch");
        Assert.AreEqual(expected.TexCoord, actual.TexCoord, $"TexCoord mismatch");
        Assert.AreEqual(expected.Tangent, actual.Tangent, $"Tangent mismatch");
    }

    #endregion
}
