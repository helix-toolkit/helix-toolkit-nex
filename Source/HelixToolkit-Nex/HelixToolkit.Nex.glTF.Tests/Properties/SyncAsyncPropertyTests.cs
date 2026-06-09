using System.Numerics;
using System.Text.Json;
using FsCheck;
using HelixToolkit.Nex.DependencyInjection;
using HelixToolkit.Nex.Engine;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Graphics.Mock;
using HelixToolkit.Nex.Rendering;
using HelixToolkit.Nex.Scene;

namespace HelixToolkit.Nex.glTF.Tests.Properties;

// Feature: gltf-importer, Property 15: Sync/Async equivalence

/// <summary>
/// Property-based tests for sync/async equivalence (Property 15).
/// Verifies that for any valid glTF model, Import(model) and ImportAsync(model).Result
/// produce structurally equivalent ImportResult objects (same scene graph, same materials,
/// same diagnostics).
/// **Validates: Requirements 8.4**
/// </summary>
[TestClass]
public class SyncAsyncPropertyTests
{
    private static readonly Config FsCheckConfig = Config.Default.WithMaxTest(100);

    private readonly List<string> _tempFiles = [];

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
                { /* best effort cleanup */
                }
            }
        }
    }

    /// <summary>
    /// Creates a WorldDataProvider backed by a MockContext for testing.
    /// </summary>
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

    /// <summary>
    /// Writes a glTF JSON file with embedded base64 buffer data to a temp file.
    /// Returns the file path.
    /// </summary>
    private string WriteGltfToTempFile(GltfDocument doc)
    {
        var path = Path.Combine(Path.GetTempPath(), $"sync_async_test_{Guid.NewGuid()}.gltf");

        var json = doc.ToJson();
        File.WriteAllText(path, json);
        _tempFiles.Add(path);
        return path;
    }

    /// <summary>
    /// Recursively counts all nodes in the scene graph.
    /// </summary>
    private static int CountNodes(Node? node)
    {
        if (node == null)
            return 0;
        int count = 1;
        if (node.Children != null)
        {
            foreach (var child in node.Children)
            {
                count += CountNodes(child);
            }
        }
        return count;
    }

    /// <summary>
    /// Collects all node names in depth-first order for structural comparison.
    /// </summary>
    private static List<string?> CollectNodeNames(Node? node)
    {
        var names = new List<string?>();
        CollectNodeNamesRecursive(node, names);
        return names;
    }

    private static void CollectNodeNamesRecursive(Node? node, List<string?> names)
    {
        if (node == null)
            return;
        names.Add(node.Name);
        if (node.Children != null)
        {
            foreach (var child in node.Children)
            {
                CollectNodeNamesRecursive(child, names);
            }
        }
    }

    /// <summary>
    /// Collects all MeshNode instances from the scene graph.
    /// </summary>
    private static List<MeshNode> CollectMeshNodes(Node? node)
    {
        var meshNodes = new List<MeshNode>();
        CollectMeshNodesRecursive(node, meshNodes);
        return meshNodes;
    }

    private static void CollectMeshNodesRecursive(Node? node, List<MeshNode> meshNodes)
    {
        if (node == null)
            return;
        if (node is MeshNode meshNode)
        {
            meshNodes.Add(meshNode);
        }
        if (node.Children != null)
        {
            foreach (var child in node.Children)
            {
                CollectMeshNodesRecursive(child, meshNodes);
            }
        }
    }

    /// <summary>
    /// Compares two ImportResults for structural equivalence.
    /// Returns true if they have the same structure.
    /// </summary>
    private static bool AreStructurallyEquivalent(ImportResult syncResult, ImportResult asyncResult)
    {
        // Both should have the same Success status
        if (syncResult.Success != asyncResult.Success)
            return false;

        // If both failed, compare diagnostics
        if (!syncResult.Success && !asyncResult.Success)
        {
            return syncResult.Diagnostics.Count == asyncResult.Diagnostics.Count;
        }

        // Compare node counts
        int syncNodeCount = CountNodes(syncResult.RootNode);
        int asyncNodeCount = CountNodes(asyncResult.RootNode);
        if (syncNodeCount != asyncNodeCount)
            return false;

        // Compare node names (topology + naming)
        var syncNames = CollectNodeNames(syncResult.RootNode);
        var asyncNames = CollectNodeNames(asyncResult.RootNode);
        if (syncNames.Count != asyncNames.Count)
            return false;
        for (int i = 0; i < syncNames.Count; i++)
        {
            if (syncNames[i] != asyncNames[i])
                return false;
        }

        // Compare MeshNode counts and vertex counts
        var syncMeshNodes = CollectMeshNodes(syncResult.RootNode);
        var asyncMeshNodes = CollectMeshNodes(asyncResult.RootNode);
        if (syncMeshNodes.Count != asyncMeshNodes.Count)
            return false;

        for (int i = 0; i < syncMeshNodes.Count; i++)
        {
            var syncMesh = syncMeshNodes[i];
            var asyncMesh = asyncMeshNodes[i];

            // Compare geometry vertex counts
            int syncVertexCount = syncMesh.Geometry?.Vertices.Count ?? -1;
            int asyncVertexCount = asyncMesh.Geometry?.Vertices.Count ?? -1;
            if (syncVertexCount != asyncVertexCount)
                return false;

            // Compare material property values
            if (
                (syncMesh.MaterialProperties?.Valid ?? false)
                != (asyncMesh.MaterialProperties?.Valid ?? false)
            )
                return false;

            if (
                syncMesh.MaterialProperties is { Valid: true } syncMat
                && asyncMesh.MaterialProperties is { Valid: true } asyncMat
            )
            {
                ref var syncProps = ref syncMat.Properties;
                ref var asyncProps = ref asyncMat.Properties;

                if (syncProps.Metallic != asyncProps.Metallic)
                    return false;
                if (syncProps.Roughness != asyncProps.Roughness)
                    return false;
            }

            // Compare IsRenderable
            if (syncMesh.IsRenderable != asyncMesh.IsRenderable)
                return false;
        }

        // Compare diagnostic counts (warnings should be the same)
        if (syncResult.Diagnostics.Count != asyncResult.Diagnostics.Count)
            return false;

        return true;
    }

    /// <summary>
    /// A minimal glTF document builder that produces valid JSON with embedded base64 buffers.
    /// </summary>
    private sealed class GltfDocument
    {
        public int NodeCount { get; init; }
        public int MeshCount { get; init; }
        public int PrimitivesPerMesh { get; init; }
        public int VerticesPerPrimitive { get; init; }
        public float Metallic { get; init; }
        public float Roughness { get; init; }

        public string ToJson()
        {
            // Build vertex position data for all primitives across all meshes
            int totalPrimitives = MeshCount * PrimitivesPerMesh;
            int floatsPerPrimitive = VerticesPerPrimitive * 3; // VEC3
            int totalFloats = totalPrimitives * floatsPerPrimitive;
            var positionData = new float[totalFloats];

            for (int p = 0; p < totalPrimitives; p++)
            {
                for (int v = 0; v < VerticesPerPrimitive; v++)
                {
                    int baseIdx = (p * VerticesPerPrimitive + v) * 3;
                    positionData[baseIdx] = p * 2.0f + v * 0.5f; // x
                    positionData[baseIdx + 1] = v * 1.0f; // y
                    positionData[baseIdx + 2] = 0.0f; // z
                }
            }

            var byteBuffer = new byte[totalFloats * sizeof(float)];
            System.Buffer.BlockCopy(positionData, 0, byteBuffer, 0, byteBuffer.Length);
            var base64Data = Convert.ToBase64String(byteBuffer);

            // Build JSON structure
            var buffers = new List<object>
            {
                new Dictionary<string, object>
                {
                    ["uri"] = $"data:application/octet-stream;base64,{base64Data}",
                    ["byteLength"] = byteBuffer.Length,
                },
            };

            // Create buffer views and accessors for each primitive
            var bufferViews = new List<object>();
            var accessors = new List<object>();
            int bytesPerPrimitive = VerticesPerPrimitive * 3 * sizeof(float);

            for (int p = 0; p < totalPrimitives; p++)
            {
                bufferViews.Add(
                    new Dictionary<string, object>
                    {
                        ["buffer"] = 0,
                        ["byteOffset"] = p * bytesPerPrimitive,
                        ["byteLength"] = bytesPerPrimitive,
                    }
                );

                accessors.Add(
                    new Dictionary<string, object>
                    {
                        ["bufferView"] = p,
                        ["byteOffset"] = 0,
                        ["componentType"] = 5126, // FLOAT
                        ["type"] = "VEC3",
                        ["count"] = VerticesPerPrimitive,
                    }
                );
            }

            // Create materials
            var materials = new List<object>
            {
                new Dictionary<string, object>
                {
                    ["name"] = "TestMaterial",
                    ["pbrMetallicRoughness"] = new Dictionary<string, object>
                    {
                        ["baseColorFactor"] = new[] { 1.0f, 1.0f, 1.0f, 1.0f },
                        ["metallicFactor"] = Metallic,
                        ["roughnessFactor"] = Roughness,
                    },
                },
            };

            // Create meshes
            var meshes = new List<object>();
            int accessorIdx = 0;
            for (int m = 0; m < MeshCount; m++)
            {
                var primitives = new List<object>();
                for (int p = 0; p < PrimitivesPerMesh; p++)
                {
                    primitives.Add(
                        new Dictionary<string, object>
                        {
                            ["attributes"] = new Dictionary<string, int>
                            {
                                ["POSITION"] = accessorIdx,
                            },
                            ["material"] = 0,
                            ["mode"] = 4, // TRIANGLES
                        }
                    );
                    accessorIdx++;
                }

                meshes.Add(
                    new Dictionary<string, object>
                    {
                        ["name"] = $"Mesh_{m}",
                        ["primitives"] = primitives,
                    }
                );
            }

            // Create nodes: first NodeCount nodes are hierarchy nodes, some reference meshes
            var nodes = new List<object>();
            var sceneNodeIndices = new List<int>();

            for (int n = 0; n < NodeCount; n++)
            {
                var node = new Dictionary<string, object> { ["name"] = $"Node_{n}" };

                // Assign meshes to the first MeshCount nodes
                if (n < MeshCount)
                {
                    node["mesh"] = n;
                }

                nodes.Add(node);
                sceneNodeIndices.Add(n);
            }

            // Build the full glTF document
            var gltf = new Dictionary<string, object>
            {
                ["asset"] = new Dictionary<string, object> { ["version"] = "2.0" },
                ["scene"] = 0,
                ["scenes"] = new List<object>
                {
                    new Dictionary<string, object>
                    {
                        ["name"] = "TestScene",
                        ["nodes"] = sceneNodeIndices,
                    },
                },
                ["nodes"] = nodes,
                ["meshes"] = meshes,
                ["materials"] = materials,
                ["accessors"] = accessors,
                ["bufferViews"] = bufferViews,
                ["buffers"] = buffers,
            };

            return JsonSerializer.Serialize(
                gltf,
                new JsonSerializerOptions { WriteIndented = true }
            );
        }
    }

    /// <summary>
    /// Property 15: For any valid glTF file, the synchronous Import method and the asynchronous
    /// ImportAsync method (with a non-cancelled CancellationToken) SHALL produce ImportResult values
    /// with structurally equivalent scene graphs (same node count, same topology, same geometry
    /// vertex counts, same material property values).
    /// **Validates: Requirements 8.4**
    /// </summary>
    [TestMethod]
    public void Import_And_ImportAsync_ProduceStructurallyEquivalentResults()
    {
        // Generator: produce valid glTF configurations with varying complexity
        var configGen =
            from nodeCount in Gen.Choose(1, 5)
            from meshCount in Gen.Choose(1, Math.Min(nodeCount, 3))
            from primitivesPerMesh in Gen.Choose(1, 3)
            from verticesPerPrimitive in Gen.Choose(3, 12)
            from metallic in Gen.Choose(0, 100).Select(v => v / 100.0f)
            from roughness in Gen.Choose(0, 100).Select(v => v / 100.0f)
            select new GltfDocument
            {
                NodeCount = nodeCount,
                MeshCount = meshCount,
                PrimitivesPerMesh = primitivesPerMesh,
                VerticesPerPrimitive = verticesPerPrimitive,
                Metallic = metallic,
                Roughness = roughness,
            };

        Prop.ForAll(
                Arb.From(configGen),
                (GltfDocument doc) =>
                {
                    // Arrange: Write glTF to temp file
                    var filePath = WriteGltfToTempFile(doc);

                    // Create two separate WorldDataProviders (each with their own world)
                    using var syncWorldData = CreateTestWorldDataProvider();
                    using var asyncWorldData = CreateTestWorldDataProvider();

                    var importer = new Importer();

                    // Act: Import synchronously
                    var syncResult = importer.Import(filePath, syncWorldData);

                    // Act: Import asynchronously
                    var asyncResult = importer
                        .ImportAsync(filePath, asyncWorldData, cancellationToken: CancellationToken.None)
                        .GetAwaiter()
                        .GetResult();

                    // Assert: Both results should be structurally equivalent
                    return AreStructurallyEquivalent(syncResult, asyncResult);
                }
            )
            .Check(FsCheckConfig);
    }
}
