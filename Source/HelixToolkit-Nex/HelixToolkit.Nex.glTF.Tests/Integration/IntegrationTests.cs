using System.Numerics;
using System.Text;
using System.Text.Json;
using HelixToolkit.Nex.DependencyInjection;
using HelixToolkit.Nex.Engine;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Graphics.Mock;
using HelixToolkit.Nex.Rendering;
using HelixToolkit.Nex.Scene;

namespace HelixToolkit.Nex.glTF.Tests.Integration;

/// <summary>
/// End-to-end integration tests that exercise the full import pipeline
/// with programmatically created glTF/GLB sample files.
/// Validates: Requirements 1.1, 1.2, 8.4
/// </summary>
[TestClass]
public class IntegrationTests
{
    private Importer _importer = null!;
    private readonly List<string> _tempFiles = [];

    [TestInitialize]
    public void Setup()
    {
        _importer = new Importer();
    }

    [TestCleanup]
    public void Cleanup()
    {
        foreach (var file in _tempFiles)
        {
            if (File.Exists(file))
            {
                try
                {
                    File.Delete(file);
                }
                catch
                { /* best effort */
                }
            }
        }
    }

    #region Helpers

    private static WorldDataProvider CreateTestWorldDataProvider()
    {
        var mockContext = new MockContext();
        mockContext.Initialize();

        var services = new ServiceCollection
        {
            new ServiceDescriptor(typeof(IContext), mockContext),
        };
        services.AddSingleton<IResourceManager, ResourceManager>();

        var serviceProvider = services.BuildServiceProvider();
        var worldData = new WorldDataProvider(serviceProvider);
        worldData.Initialize();
        return worldData;
    }

    private string WriteTempFile(string extension, string content)
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"integration_test_{Guid.NewGuid()}{extension}"
        );
        File.WriteAllText(path, content);
        _tempFiles.Add(path);
        return path;
    }

    private string WriteTempFile(string extension, byte[] content)
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"integration_test_{Guid.NewGuid()}{extension}"
        );
        File.WriteAllBytes(path, content);
        _tempFiles.Add(path);
        return path;
    }

    private static string EncodeBufferBase64(float[] data)
    {
        var bytes = new byte[data.Length * sizeof(float)];
        System.Buffer.BlockCopy(data, 0, bytes, 0, bytes.Length);
        return Convert.ToBase64String(bytes);
    }

    private static int CountAllNodes(Node? node)
    {
        if (node == null)
            return 0;
        int count = 1;
        if (node.Children != null)
            foreach (var child in node.Children)
                count += CountAllNodes(child);
        return count;
    }

    private static List<MeshNode> CollectMeshNodes(Node? node)
    {
        var result = new List<MeshNode>();
        CollectMeshNodesRecursive(node, result);
        return result;
    }

    private static void CollectMeshNodesRecursive(Node? node, List<MeshNode> list)
    {
        if (node == null)
            return;
        if (node is MeshNode mn)
            list.Add(mn);
        if (node.Children != null)
            foreach (var child in node.Children)
                CollectMeshNodesRecursive(child, list);
    }

    #endregion

    #region Test 1: Simple Triangle Mesh (minimal valid glTF)

    /// <summary>
    /// Imports a minimal valid glTF file containing a single triangle mesh with a PBR material.
    /// Verifies: Success, RootNode not null, correct vertex count (3),
    /// triangle topology, no diagnostics.
    /// </summary>
    [TestMethod]
    public void Import_SimpleTriangle_ProducesCorrectResult()
    {
        // Arrange: A triangle with 3 vertices
        float[] positions = [0f, 0f, 0f, 1f, 0f, 0f, 0.5f, 1f, 0f];
        var base64 = EncodeBufferBase64(positions);
        int byteLength = positions.Length * sizeof(float); // 36

        var gltfJson = $$"""
            {
                "asset": { "version": "2.0" },
                "scene": 0,
                "scenes": [{ "name": "Scene", "nodes": [0] }],
                "nodes": [{ "name": "TriangleNode", "mesh": 0 }],
                "meshes": [{
                    "name": "TriangleMesh",
                    "primitives": [{
                        "attributes": { "POSITION": 0 },
                        "material": 0,
                        "mode": 4
                    }]
                }],
                "materials": [{
                    "name": "PBR",
                    "pbrMetallicRoughness": {
                        "baseColorFactor": [1.0, 1.0, 1.0, 1.0],
                        "metallicFactor": 0.0,
                        "roughnessFactor": 0.5
                    }
                }],
                "accessors": [{
                    "bufferView": 0,
                    "byteOffset": 0,
                    "componentType": 5126,
                    "type": "VEC3",
                    "count": 3
                }],
                "bufferViews": [{
                    "buffer": 0,
                    "byteOffset": 0,
                    "byteLength": {{byteLength}}
                }],
                "buffers": [{
                    "uri": "data:application/octet-stream;base64,{{base64}}",
                    "byteLength": {{byteLength}}
                }]
            }
            """;

        var filePath = WriteTempFile(".gltf", gltfJson);
        using var worldData = CreateTestWorldDataProvider();

        // Act
        var result = _importer.Import(filePath, worldData);

        // Assert
        Assert.IsTrue(
            result.Success,
            $"Import should succeed. Diagnostics: {string.Join("; ", result.Diagnostics.Select(d => $"[{d.Severity}] {d.ElementType}[{d.ElementIndex}]: {d.Message}"))}"
        );
        Assert.IsNotNull(result.RootNode);
        Assert.AreEqual(
            0,
            result.Diagnostics.Count,
            $"Expected no diagnostics but got: {string.Join("; ", result.Diagnostics.Select(d => d.Message))}"
        );

        // Verify mesh nodes
        var meshNodes = CollectMeshNodes(result.RootNode);
        Assert.AreEqual(1, meshNodes.Count, "Should have exactly 1 mesh node");

        // Verify geometry
        var geometry = meshNodes[0].Geometry;
        Assert.IsNotNull(geometry, "Geometry should not be null");
        Assert.AreEqual(3, geometry.Vertices.Count, "Should have 3 vertices");
        Assert.AreEqual(Topology.Triangle, geometry.Topology);
    }

    #endregion

    #region Test 2: Multi-mesh scene with parent-child hierarchy

    /// <summary>
    /// Imports a glTF file with a parent node containing two child nodes,
    /// each with their own mesh. Verifies hierarchy structure is preserved.
    /// </summary>
    [TestMethod]
    public void Import_MultiMeshHierarchy_PreservesNodeStructure()
    {
        // Arrange: 2 meshes, each with 3 vertices (triangle)
        float[] positions =
        [
            0f,
            0f,
            0f,
            1f,
            0f,
            0f,
            0.5f,
            1f,
            0f,
            2f,
            0f,
            0f,
            3f,
            0f,
            0f,
            2.5f,
            1f,
            0f,
        ];
        var base64 = EncodeBufferBase64(positions);
        int totalBytes = positions.Length * sizeof(float);
        int meshBytes = 3 * 3 * sizeof(float);

        var gltfJson = $$"""
            {
                "asset": { "version": "2.0" },
                "scene": 0,
                "scenes": [{ "name": "Scene", "nodes": [0] }],
                "nodes": [
                    { "name": "Parent", "children": [1, 2] },
                    { "name": "ChildA", "mesh": 0 },
                    { "name": "ChildB", "mesh": 1 }
                ],
                "meshes": [
                    { "name": "MeshA", "primitives": [{ "attributes": { "POSITION": 0 }, "material": 0, "mode": 4 }] },
                    { "name": "MeshB", "primitives": [{ "attributes": { "POSITION": 1 }, "material": 0, "mode": 4 }] }
                ],
                "materials": [{
                    "name": "PBR",
                    "pbrMetallicRoughness": {
                        "baseColorFactor": [1.0, 1.0, 1.0, 1.0],
                        "metallicFactor": 0.5,
                        "roughnessFactor": 0.5
                    }
                }],
                "accessors": [
                    { "bufferView": 0, "byteOffset": 0, "componentType": 5126, "type": "VEC3", "count": 3 },
                    { "bufferView": 1, "byteOffset": 0, "componentType": 5126, "type": "VEC3", "count": 3 }
                ],
                "bufferViews": [
                    { "buffer": 0, "byteOffset": 0, "byteLength": {{meshBytes}} },
                    { "buffer": 0, "byteOffset": {{meshBytes}}, "byteLength": {{meshBytes}} }
                ],
                "buffers": [{
                    "uri": "data:application/octet-stream;base64,{{base64}}",
                    "byteLength": {{totalBytes}}
                }]
            }
            """;

        var filePath = WriteTempFile(".gltf", gltfJson);
        using var worldData = CreateTestWorldDataProvider();

        // Act
        var result = _importer.Import(filePath, worldData);

        // Assert
        Assert.IsTrue(
            result.Success,
            $"Import should succeed. Diagnostics: {string.Join("; ", result.Diagnostics.Select(d => d.Message))}"
        );
        Assert.IsNotNull(result.RootNode);
        Assert.AreEqual(
            0,
            result.Diagnostics.Count,
            $"Expected no diagnostics but got: {string.Join("; ", result.Diagnostics.Select(d => d.Message))}"
        );

        // Root node is the scene root, which should contain the "Parent" node
        var rootChildren = result.RootNode.Children;
        Assert.IsNotNull(rootChildren);
        Assert.AreEqual(1, rootChildren.Count, "Scene root should have 1 child (Parent)");

        var parentNode = rootChildren[0];
        Assert.AreEqual("Parent", parentNode.Name);

        // Parent should have 2 children
        var parentChildren = parentNode.Children;
        Assert.IsNotNull(parentChildren);
        Assert.AreEqual(2, parentChildren.Count, "Parent should have 2 children");

        // Verify mesh nodes exist
        var meshNodes = CollectMeshNodes(result.RootNode);
        Assert.AreEqual(2, meshNodes.Count, "Should have 2 mesh nodes total");

        // Each mesh should have 3 vertices
        foreach (var mn in meshNodes)
        {
            Assert.IsNotNull(mn.Geometry);
            Assert.AreEqual(3, mn.Geometry.Vertices.Count);
        }
    }

    #endregion

    #region Test 3: Model with PBR materials

    /// <summary>
    /// Imports a glTF file with a mesh that has PBR material properties
    /// (metallic/roughness/baseColor). Verifies material values are correct.
    /// </summary>
    [TestMethod]
    public void Import_PbrMaterial_HasCorrectProperties()
    {
        // Arrange: single triangle with PBR material
        float[] positions = [0f, 0f, 0f, 1f, 0f, 0f, 0.5f, 1f, 0f];
        var base64 = EncodeBufferBase64(positions);
        int byteLength = positions.Length * sizeof(float);

        var gltfJson = $$"""
            {
                "asset": { "version": "2.0" },
                "scene": 0,
                "scenes": [{ "name": "Scene", "nodes": [0] }],
                "nodes": [{ "name": "PbrNode", "mesh": 0 }],
                "meshes": [{
                    "name": "PbrMesh",
                    "primitives": [{
                        "attributes": { "POSITION": 0 },
                        "material": 0,
                        "mode": 4
                    }]
                }],
                "materials": [{
                    "name": "PBR",
                    "pbrMetallicRoughness": {
                        "baseColorFactor": [0.8, 0.1, 0.1, 1.0],
                        "metallicFactor": 0.9,
                        "roughnessFactor": 0.3
                    },
                    "emissiveFactor": [0.2, 0.0, 0.0]
                }],
                "accessors": [{
                    "bufferView": 0,
                    "byteOffset": 0,
                    "componentType": 5126,
                    "type": "VEC3",
                    "count": 3
                }],
                "bufferViews": [{
                    "buffer": 0,
                    "byteOffset": 0,
                    "byteLength": {{byteLength}}
                }],
                "buffers": [{
                    "uri": "data:application/octet-stream;base64,{{base64}}",
                    "byteLength": {{byteLength}}
                }]
            }
            """;

        var filePath = WriteTempFile(".gltf", gltfJson);
        using var worldData = CreateTestWorldDataProvider();

        // Act
        var result = _importer.Import(filePath, worldData);

        // Assert
        Assert.IsTrue(
            result.Success,
            $"Import should succeed. Diagnostics: {string.Join("; ", result.Diagnostics.Select(d => d.Message))}"
        );
        Assert.AreEqual(
            0,
            result.Diagnostics.Count,
            $"Expected no diagnostics but got: {string.Join("; ", result.Diagnostics.Select(d => d.Message))}"
        );

        var meshNodes = CollectMeshNodes(result.RootNode);
        Assert.AreEqual(1, meshNodes.Count);

        var mat = meshNodes[0].MaterialProperties;
        Assert.IsNotNull(mat, "Material should not be null");
        Assert.IsTrue(mat.Valid, "Material should be valid");

        // Verify PBR properties
        Assert.AreEqual(0.9f, mat.Metallic, 1e-5f, "Metallic should be 0.9");
        Assert.AreEqual(0.3f, mat.Roughness, 1e-5f, "Roughness should be 0.3");
        Assert.AreEqual(1.0f, mat.Opacity, 1e-5f, "Opacity should be 1.0");

        // Verify albedo (RGB)
        ref var props = ref mat.Properties;
        Assert.AreEqual(0.8f, props.Albedo.X, 1e-5f, "Albedo R should be 0.8");
        Assert.AreEqual(0.1f, props.Albedo.Y, 1e-5f, "Albedo G should be 0.1");
        Assert.AreEqual(0.1f, props.Albedo.Z, 1e-5f, "Albedo B should be 0.1");

        // Verify emissive
        Assert.AreEqual(0.2f, props.Emissive.X, 1e-5f, "Emissive R should be 0.2");
        Assert.AreEqual(0.0f, props.Emissive.Y, 1e-5f, "Emissive G should be 0.0");
        Assert.AreEqual(0.0f, props.Emissive.Z, 1e-5f, "Emissive B should be 0.0");
    }

    #endregion

    #region Test 4: GLB binary file

    /// <summary>
    /// Creates a valid GLB file (header + JSON chunk + BIN chunk) with a triangle
    /// and verifies the full import pipeline works with binary format.
    /// </summary>
    [TestMethod]
    public void Import_GlbBinaryFile_ProducesCorrectResult()
    {
        // Arrange: Build a valid GLB file programmatically
        float[] positions = [0f, 0f, 0f, 1f, 0f, 0f, 0.5f, 1f, 0f];
        var binData = new byte[positions.Length * sizeof(float)];
        System.Buffer.BlockCopy(positions, 0, binData, 0, binData.Length);

        // Pad BIN chunk to 4-byte alignment
        int binPadding = (4 - (binData.Length % 4)) % 4;
        var paddedBin = new byte[binData.Length + binPadding];
        Array.Copy(binData, paddedBin, binData.Length);

        int binByteLength = binData.Length;

        var jsonObj = new Dictionary<string, object>
        {
            ["asset"] = new Dictionary<string, object> { ["version"] = "2.0" },
            ["scene"] = 0,
            ["scenes"] = new[]
            {
                new Dictionary<string, object> { ["name"] = "Scene", ["nodes"] = new[] { 0 } },
            },
            ["nodes"] = new[]
            {
                new Dictionary<string, object> { ["name"] = "GlbTriangle", ["mesh"] = 0 },
            },
            ["meshes"] = new[]
            {
                new Dictionary<string, object>
                {
                    ["name"] = "Mesh",
                    ["primitives"] = new[]
                    {
                        new Dictionary<string, object>
                        {
                            ["attributes"] = new Dictionary<string, int> { ["POSITION"] = 0 },
                            ["material"] = 0,
                            ["mode"] = 4,
                        },
                    },
                },
            },
            ["materials"] = new[]
            {
                new Dictionary<string, object>
                {
                    ["name"] = "PBR",
                    ["pbrMetallicRoughness"] = new Dictionary<string, object>
                    {
                        ["baseColorFactor"] = new[] { 1.0f, 1.0f, 1.0f, 1.0f },
                        ["metallicFactor"] = 0.0f,
                        ["roughnessFactor"] = 0.5f,
                    },
                },
            },
            ["accessors"] = new[]
            {
                new Dictionary<string, object>
                {
                    ["bufferView"] = 0,
                    ["byteOffset"] = 0,
                    ["componentType"] = 5126,
                    ["type"] = "VEC3",
                    ["count"] = 3,
                },
            },
            ["bufferViews"] = new[]
            {
                new Dictionary<string, object>
                {
                    ["buffer"] = 0,
                    ["byteOffset"] = 0,
                    ["byteLength"] = binByteLength,
                },
            },
            ["buffers"] = new[]
            {
                new Dictionary<string, object> { ["byteLength"] = binByteLength },
            },
        };

        var jsonString = JsonSerializer.Serialize(jsonObj);
        var jsonBytes = Encoding.UTF8.GetBytes(jsonString);

        // Pad JSON chunk to 4-byte alignment with spaces (0x20)
        int jsonPadding = (4 - (jsonBytes.Length % 4)) % 4;
        var paddedJson = new byte[jsonBytes.Length + jsonPadding];
        Array.Copy(jsonBytes, paddedJson, jsonBytes.Length);
        for (int i = jsonBytes.Length; i < paddedJson.Length; i++)
            paddedJson[i] = 0x20;

        // Build GLB binary
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        uint totalLength = (uint)(12 + 8 + paddedJson.Length + 8 + paddedBin.Length);
        writer.Write(0x46546C67u); // magic: "glTF"
        writer.Write(2u); // version: 2
        writer.Write(totalLength); // total length

        // JSON chunk
        writer.Write((uint)paddedJson.Length);
        writer.Write(0x4E4F534Au); // "JSON"
        writer.Write(paddedJson);

        // BIN chunk
        writer.Write((uint)paddedBin.Length);
        writer.Write(0x004E4942u); // "BIN\0"
        writer.Write(paddedBin);

        var glbBytes = ms.ToArray();
        var filePath = WriteTempFile(".glb", glbBytes);
        using var worldData = CreateTestWorldDataProvider();

        // Act
        var result = _importer.Import(filePath, worldData);

        // Assert
        Assert.IsTrue(
            result.Success,
            $"GLB import should succeed. Diagnostics: {string.Join("; ", result.Diagnostics.Select(d => d.Message))}"
        );
        Assert.IsNotNull(result.RootNode);
        Assert.AreEqual(
            0,
            result.Diagnostics.Count,
            $"Expected no diagnostics but got: {string.Join("; ", result.Diagnostics.Select(d => d.Message))}"
        );

        var meshNodes = CollectMeshNodes(result.RootNode);
        Assert.AreEqual(1, meshNodes.Count);
        var glbGeometry = meshNodes[0].Geometry;
        Assert.IsNotNull(glbGeometry);
        Assert.AreEqual(3, glbGeometry.Vertices.Count);
    }

    #endregion

    #region Test 5: Multiple materials assigned to different meshes

    /// <summary>
    /// Imports a glTF file with two meshes, each assigned a different material.
    /// Both materials use the registered "PBR" type but with different property values.
    /// Verifies that each mesh node has the correct material properties.
    /// </summary>
    [TestMethod]
    public void Import_MultipleMaterials_AssignedCorrectly()
    {
        // Arrange: 2 meshes with different materials (both named "PBR" to match registry)
        // We use two separate material entries both named "PBR" - the registry allows
        // creating multiple instances from the same type.
        float[] positions =
        [
            0f,
            0f,
            0f,
            1f,
            0f,
            0f,
            0.5f,
            1f,
            0f,
            2f,
            0f,
            0f,
            3f,
            0f,
            0f,
            2.5f,
            1f,
            0f,
        ];
        var base64 = EncodeBufferBase64(positions);
        int totalBytes = positions.Length * sizeof(float);
        int meshBytes = 3 * 3 * sizeof(float);

        var gltfJson = $$"""
            {
                "asset": { "version": "2.0" },
                "scene": 0,
                "scenes": [{ "name": "Scene", "nodes": [0, 1] }],
                "nodes": [
                    { "name": "MetalNode", "mesh": 0 },
                    { "name": "RoughNode", "mesh": 1 }
                ],
                "meshes": [
                    { "name": "MetalMesh", "primitives": [{
                        "attributes": { "POSITION": 0 }, "material": 0, "mode": 4
                    }]},
                    { "name": "RoughMesh", "primitives": [{
                        "attributes": { "POSITION": 1 }, "material": 1, "mode": 4
                    }]}
                ],
                "materials": [
                    {
                        "name": "PBR",
                        "pbrMetallicRoughness": {
                            "baseColorFactor": [1.0, 0.8, 0.0, 1.0],
                            "metallicFactor": 1.0,
                            "roughnessFactor": 0.1
                        }
                    },
                    {
                        "name": "PBR",
                        "pbrMetallicRoughness": {
                            "baseColorFactor": [0.2, 0.2, 0.8, 1.0],
                            "metallicFactor": 0.0,
                            "roughnessFactor": 0.95
                        }
                    }
                ],
                "accessors": [
                    { "bufferView": 0, "byteOffset": 0, "componentType": 5126, "type": "VEC3", "count": 3 },
                    { "bufferView": 1, "byteOffset": 0, "componentType": 5126, "type": "VEC3", "count": 3 }
                ],
                "bufferViews": [
                    { "buffer": 0, "byteOffset": 0, "byteLength": {{meshBytes}} },
                    { "buffer": 0, "byteOffset": {{meshBytes}}, "byteLength": {{meshBytes}} }
                ],
                "buffers": [{
                    "uri": "data:application/octet-stream;base64,{{base64}}",
                    "byteLength": {{totalBytes}}
                }]
            }
            """;

        var filePath = WriteTempFile(".gltf", gltfJson);
        using var worldData = CreateTestWorldDataProvider();

        // Act
        var result = _importer.Import(filePath, worldData);

        // Assert
        Assert.IsTrue(
            result.Success,
            $"Import should succeed. Diagnostics: {string.Join("; ", result.Diagnostics.Select(d => d.Message))}"
        );
        Assert.AreEqual(
            0,
            result.Diagnostics.Count,
            $"Expected no diagnostics but got: {string.Join("; ", result.Diagnostics.Select(d => d.Message))}"
        );

        var meshNodes = CollectMeshNodes(result.RootNode);
        Assert.AreEqual(2, meshNodes.Count, "Should have 2 mesh nodes");

        // Find mesh nodes by metallic value
        var metalNode = meshNodes.First(mn =>
            mn.MaterialProperties != null && mn.MaterialProperties.Metallic > 0.5f
        );
        var roughNode = meshNodes.First(mn =>
            mn.MaterialProperties != null && mn.MaterialProperties.Metallic < 0.5f
        );

        Assert.AreEqual(1.0f, metalNode.MaterialProperties!.Metallic, 1e-5f);
        Assert.AreEqual(0.1f, metalNode.MaterialProperties!.Roughness, 1e-5f);

        Assert.AreEqual(0.0f, roughNode.MaterialProperties!.Metallic, 1e-5f);
        Assert.AreEqual(0.95f, roughNode.MaterialProperties!.Roughness, 1e-5f);

        // Verify albedo colors
        ref var metalProps = ref metalNode.MaterialProperties!.Properties;
        Assert.AreEqual(1.0f, metalProps.Albedo.X, 1e-5f);
        Assert.AreEqual(0.8f, metalProps.Albedo.Y, 1e-5f);
        Assert.AreEqual(0.0f, metalProps.Albedo.Z, 1e-5f);

        ref var roughProps = ref roughNode.MaterialProperties!.Properties;
        Assert.AreEqual(0.2f, roughProps.Albedo.X, 1e-5f);
        Assert.AreEqual(0.2f, roughProps.Albedo.Y, 1e-5f);
        Assert.AreEqual(0.8f, roughProps.Albedo.Z, 1e-5f);
    }

    #endregion

    #region Test 6: Async import produces same results as sync

    /// <summary>
    /// Verifies that ImportAsync produces the same structural result as Import
    /// for a valid glTF file with a mesh and material.
    /// </summary>
    [TestMethod]
    public async Task ImportAsync_ProducesSameResultAsSync()
    {
        // Arrange
        float[] positions = [0f, 0f, 0f, 1f, 0f, 0f, 0.5f, 1f, 0f];
        var base64 = EncodeBufferBase64(positions);
        int byteLength = positions.Length * sizeof(float);

        var gltfJson = $$"""
            {
                "asset": { "version": "2.0" },
                "scene": 0,
                "scenes": [{ "name": "Scene", "nodes": [0] }],
                "nodes": [{ "name": "TestNode", "mesh": 0 }],
                "meshes": [{
                    "name": "TestMesh",
                    "primitives": [{
                        "attributes": { "POSITION": 0 },
                        "material": 0,
                        "mode": 4
                    }]
                }],
                "materials": [{
                    "name": "PBR",
                    "pbrMetallicRoughness": {
                        "baseColorFactor": [0.5, 0.6, 0.7, 0.9],
                        "metallicFactor": 0.4,
                        "roughnessFactor": 0.6
                    }
                }],
                "accessors": [{
                    "bufferView": 0,
                    "byteOffset": 0,
                    "componentType": 5126,
                    "type": "VEC3",
                    "count": 3
                }],
                "bufferViews": [{
                    "buffer": 0,
                    "byteOffset": 0,
                    "byteLength": {{byteLength}}
                }],
                "buffers": [{
                    "uri": "data:application/octet-stream;base64,{{base64}}",
                    "byteLength": {{byteLength}}
                }]
            }
            """;

        var filePath = WriteTempFile(".gltf", gltfJson);
        using var syncWorld = CreateTestWorldDataProvider();
        using var asyncWorld = CreateTestWorldDataProvider();

        // Act
        var syncResult = _importer.Import(filePath, syncWorld);
        var asyncResult = await _importer.ImportAsync(filePath, asyncWorld);

        // Assert: both should have the same outcome (both succeed or both fail identically)
        Assert.AreEqual(
            syncResult.Success,
            asyncResult.Success,
            "Sync and async should have same success status"
        );

        // Same node count
        int syncNodeCount = CountAllNodes(syncResult.RootNode);
        int asyncNodeCount = CountAllNodes(asyncResult.RootNode);
        Assert.AreEqual(syncNodeCount, asyncNodeCount, "Node counts should match");

        // Same mesh node count and vertex counts
        var syncMeshNodes = CollectMeshNodes(syncResult.RootNode);
        var asyncMeshNodes = CollectMeshNodes(asyncResult.RootNode);
        Assert.AreEqual(syncMeshNodes.Count, asyncMeshNodes.Count);

        for (int i = 0; i < syncMeshNodes.Count; i++)
        {
            Assert.AreEqual(
                syncMeshNodes[i].Geometry?.Vertices.Count,
                asyncMeshNodes[i].Geometry?.Vertices.Count
            );
            Assert.AreEqual(
                syncMeshNodes[i].MaterialProperties?.Metallic,
                asyncMeshNodes[i].MaterialProperties?.Metallic
            );
            Assert.AreEqual(
                syncMeshNodes[i].MaterialProperties?.Roughness,
                asyncMeshNodes[i].MaterialProperties?.Roughness
            );
        }

        // Same diagnostic count
        Assert.AreEqual(syncResult.Diagnostics.Count, asyncResult.Diagnostics.Count);
    }

    #endregion
}
