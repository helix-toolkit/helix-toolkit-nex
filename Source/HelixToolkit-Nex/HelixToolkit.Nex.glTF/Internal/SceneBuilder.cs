using System.Numerics;
using glTFLoader.Schema;
using HelixToolkit.Nex.ECS;
using HelixToolkit.Nex.Maths;
using HelixToolkit.Nex.Scene;
using GltfNode = glTFLoader.Schema.Node;
using Node = HelixToolkit.Nex.Scene.Node;

namespace HelixToolkit.Nex.glTF.Internal;

/// <summary>
/// Walks the glTF node tree, creates Node/MeshNode instances, applies transforms,
/// and wires parent-child relationships.
/// </summary>
internal sealed class SceneBuilder
{
    private readonly World _world;
    private readonly MeshConverter _meshConverter;
    private readonly MaterialConverter _materialConverter;
    private readonly List<ImportDiagnostic> _diagnostics;

    /// <summary>
    /// The identity matrix in column-major order as stored by glTF2Loader.
    /// </summary>
    private static readonly float[] IdentityMatrixColumnMajor =
    [
        1,
        0,
        0,
        0,
        0,
        1,
        0,
        0,
        0,
        0,
        1,
        0,
        0,
        0,
        0,
        1,
    ];

    /// <summary>
    /// Initializes a new instance of the <see cref="SceneBuilder"/> class.
    /// </summary>
    /// <param name="world">The ECS world to create nodes in.</param>
    /// <param name="meshConverter">The mesh converter for creating geometry.</param>
    /// <param name="materialConverter">The material converter for creating materials.</param>
    /// <param name="diagnostics">The diagnostics list to report warnings/errors to.</param>
    public SceneBuilder(
        World world,
        MeshConverter meshConverter,
        MaterialConverter materialConverter,
        List<ImportDiagnostic> diagnostics
    )
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _meshConverter = meshConverter ?? throw new ArgumentNullException(nameof(meshConverter));
        _materialConverter =
            materialConverter ?? throw new ArgumentNullException(nameof(materialConverter));
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    }

    /// <summary>
    /// Builds the engine scene graph from the specified glTF scene.
    /// Selects the default scene (via the model's Scene property) or the first scene if no default is specified.
    /// </summary>
    /// <param name="model">The deserialized glTF model.</param>
    /// <param name="sceneIndex">
    /// The scene index to import. If -1, uses the model's default scene property or falls back to the first scene.
    /// </param>
    /// <returns>The root Node of the imported scene.</returns>
    public Node BuildScene(Gltf model, int sceneIndex)
    {
        // Determine which scene to import
        int resolvedSceneIndex = sceneIndex;
        if (resolvedSceneIndex < 0)
        {
            // Use the model's default scene property, or fall back to the first scene
            resolvedSceneIndex = model.Scene ?? 0;
        }

        // Get the scene definition
        var scenes = model.Scenes;
        if (scenes == null || resolvedSceneIndex >= scenes.Length)
        {
            // No scenes available — return an empty root node
            return new Node(_world, "Root");
        }

        var scene = scenes[resolvedSceneIndex];

        // Create root node for the scene
        var rootNode = new Node(_world, scene.Name ?? "Root");

        // Iterate over the scene's top-level node indices, preserving order
        if (scene.Nodes != null)
        {
            foreach (int nodeIndex in scene.Nodes)
            {
                BuildNode(model, nodeIndex, rootNode);
            }
        }

        return rootNode;
    }

    /// <summary>
    /// Recursively builds an engine Node from a glTF node, applying transforms
    /// and wiring parent-child relationships.
    /// </summary>
    /// <param name="model">The deserialized glTF model.</param>
    /// <param name="nodeIndex">The index of the node in the glTF Nodes array.</param>
    /// <param name="parent">The parent Node to attach this node to, or null for root-level nodes.</param>
    /// <returns>The created Node.</returns>
    internal Node BuildNode(Gltf model, int nodeIndex, Node? parent)
    {
        var gltfNode = model.Nodes[nodeIndex];

        // Create the engine node
        var node = new Node(_world);

        // Set name from glTF node name
        if (!string.IsNullOrEmpty(gltfNode.Name))
        {
            node.Name = gltfNode.Name;
        }

        // Apply transform
        ApplyTransform(node, gltfNode, nodeIndex);

        // Attach to parent (preserves child ordering)
        parent?.AddChild(node);

        // Handle mesh reference
        if (gltfNode.Mesh.HasValue)
        {
            BuildMeshNodes(model, gltfNode.Mesh.Value, node);
        }

        // Recursively process children, preserving order
        if (gltfNode.Children != null)
        {
            foreach (int childIndex in gltfNode.Children)
            {
                BuildNode(model, childIndex, node);
            }
        }

        return node;
    }

    /// <summary>
    /// Builds MeshNode(s) for a glTF mesh referenced by a node.
    /// Single primitive: creates a MeshNode directly as a child of the parent node.
    /// Multiple primitives: creates a parent Node (named after the mesh) with a child MeshNode per primitive.
    /// Skips primitives with invalid geometry handles. Omits the parent Node if all primitives are skipped.
    /// </summary>
    /// <param name="model">The deserialized glTF model.</param>
    /// <param name="meshIndex">The index of the mesh in the glTF Meshes array.</param>
    /// <param name="parentNode">The node to attach mesh nodes to.</param>
    private void BuildMeshNodes(Gltf model, int meshIndex, Node parentNode)
    {
        if (model.Meshes == null || meshIndex < 0 || meshIndex >= model.Meshes.Length)
        {
            _diagnostics.Add(
                new ImportDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"Node references mesh index {meshIndex} which is out of range.",
                    "Mesh",
                    meshIndex
                )
            );
            return;
        }

        var mesh = model.Meshes[meshIndex];
        var primitives = mesh.Primitives;

        if (primitives == null || primitives.Length == 0)
        {
            return;
        }

        if (primitives.Length == 1)
        {
            // Single primitive: create MeshNode directly on the parent node
            var meshNode = CreateMeshNodeForPrimitive(
                model,
                primitives[0],
                meshIndex,
                0,
                mesh.Name ?? $"Mesh_{meshIndex}"
            );

            if (meshNode != null)
            {
                parentNode.AddChild(meshNode);
            }
        }
        else
        {
            // Multiple primitives: create a parent Node, then child MeshNode per primitive
            var meshNodes = new List<MeshNode>();

            for (int primIndex = 0; primIndex < primitives.Length; primIndex++)
            {
                var meshNode = CreateMeshNodeForPrimitive(
                    model,
                    primitives[primIndex],
                    meshIndex,
                    primIndex,
                    $"{mesh.Name ?? $"Mesh_{meshIndex}"}_Primitive{primIndex}"
                );

                if (meshNode != null)
                {
                    meshNodes.Add(meshNode);
                }
            }

            // Omit parent Node when all primitives are skipped
            if (meshNodes.Count == 0)
            {
                _diagnostics.Add(
                    new ImportDiagnostic(
                        DiagnosticSeverity.Warning,
                        $"Mesh {meshIndex} has all primitives skipped due to errors. Omitting mesh node.",
                        "Mesh",
                        meshIndex
                    )
                );
                return;
            }

            // Create parent Node named after the mesh
            var meshParentNode = new Node(_world, mesh.Name ?? $"Mesh_{meshIndex}");
            parentNode.AddChild(meshParentNode);

            foreach (var meshNode in meshNodes)
            {
                meshParentNode.AddChild(meshNode);
            }
        }
    }

    /// <summary>
    /// Creates a single MeshNode for a glTF mesh primitive.
    /// Returns null if the geometry handle is invalid (primitive is skipped).
    /// </summary>
    private MeshNode? CreateMeshNodeForPrimitive(
        Gltf model,
        MeshPrimitive primitive,
        int meshIndex,
        int primIndex,
        string nodeName
    )
    {
        // Convert geometry
        var (geometry, handle) = _meshConverter.ConvertPrimitive(
            model,
            primitive,
            meshIndex,
            primIndex
        );

        // Skip MeshNode creation when geometry handle is invalid
        if (geometry == null || !handle.Valid)
        {
            return null;
        }

        // Convert material
        MaterialConvertResult materialResult;
        if (primitive.Material.HasValue)
        {
            materialResult = _materialConverter.ConvertMaterialWithMetadata(
                model,
                primitive.Material.Value
            );
        }
        else
        {
            // No material specified — use default material with default metadata
            var defaultMaterial = _materialConverter.GetDefaultMaterial();
            materialResult = new MaterialConvertResult(defaultMaterial, MaterialMetadata.Default);
        }

        // Create MeshNode
        var meshNode = new MeshNode(_world, nodeName);
        meshNode.Geometry = geometry;
        meshNode.MaterialProperties = materialResult.Material;

        // Set IsRenderable based on validity of both geometry and material
        bool materialValid = materialResult.Material != null && materialResult.Material.Valid;
        meshNode.IsRenderable = materialValid;

        // Apply material metadata to MeshNode
        ApplyMaterialMetadata(meshNode, materialResult.Metadata);

        return meshNode;
    }

    /// <summary>
    /// Applies material metadata (alpha mode, double-sided) to the MeshNode.
    /// </summary>
    private static void ApplyMaterialMetadata(MeshNode meshNode, MaterialMetadata metadata)
    {
        // AlphaMode == Blend → set IsTransparent = true
        if (metadata.AlphaMode == AlphaMode.Blend)
        {
            meshNode.IsTransparent = true;
        }
        else if (metadata.AlphaMode == AlphaMode.Mask)
        {
            meshNode.IsAlphaMask = true;
        }

        // DoubleSided == true → disable backface culling
        if (metadata.DoubleSided)
        {
            meshNode.IsAlphaMask = true; // Use alpha mask mode to disable backface culling (since our engine doesn't have a separate culling property)
        }
    }

    /// <summary>
    /// Applies the glTF node's transform to the engine node.
    /// Matrix takes precedence over TRS. If neither is specified, identity is used.
    /// </summary>
    private void ApplyTransform(Node node, GltfNode gltfNode, int nodeIndex)
    {
        ref var transform = ref node.Transform;

        if (HasExplicitMatrix(gltfNode.Matrix))
        {
            // Matrix takes precedence over TRS
            // glTF stores column-major; System.Numerics Matrix4x4 constructor takes row-major
            var matrix = gltfNode.Matrix.ToMatrix();

            if (Matrix4x4.Decompose(matrix, out var scale, out var rotation, out var translation))
            {
                transform.Scale = scale;
                transform.Rotation = rotation;
                transform.Translation = translation;
            }
            else
            {
                // Decomposition failed — use best-effort extraction
                // Extract translation from the last column (already transposed to row-major)
                transform.Translation = matrix.Translation;
                transform.Rotation = Quaternion.Identity;
                transform.Scale = Vector3.One;

                _diagnostics.Add(
                    new ImportDiagnostic(
                        DiagnosticSeverity.Warning,
                        $"Node {nodeIndex} matrix could not be decomposed into valid TRS components (may contain skew or non-uniform negative scale). Using closest valid decomposition.",
                        "Node",
                        nodeIndex
                    )
                );
            }
        }
        else
        {
            // Use TRS properties directly (defaults are identity values)
            transform.Translation = new Vector3(
                gltfNode.Translation[0],
                gltfNode.Translation[1],
                gltfNode.Translation[2]
            );
            transform.Rotation = new Quaternion(
                gltfNode.Rotation[0],
                gltfNode.Rotation[1],
                gltfNode.Rotation[2],
                gltfNode.Rotation[3]
            );
            transform.Scale = new Vector3(gltfNode.Scale[0], gltfNode.Scale[1], gltfNode.Scale[2]);
        }
    }

    /// <summary>
    /// Determines whether the glTF node has an explicitly specified (non-identity) matrix.
    /// </summary>
    private static bool HasExplicitMatrix(float[] matrix)
    {
        if (matrix == null || matrix.Length != 16)
        {
            return false;
        }

        for (int i = 0; i < 16; i++)
        {
            if (MathF.Abs(matrix[i] - IdentityMatrixColumnMajor[i]) > float.Epsilon)
            {
                return true;
            }
        }

        return false;
    }
}
