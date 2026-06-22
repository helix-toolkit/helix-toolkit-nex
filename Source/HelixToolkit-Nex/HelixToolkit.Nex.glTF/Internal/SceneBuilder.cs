using System.Numerics;
using glTFLoader.Schema;
using HelixToolkit.Nex.ECS;
using HelixToolkit.Nex.Engine.Components;
using HelixToolkit.Nex.Lights;
using HelixToolkit.Nex.Maths;
using HelixToolkit.Nex.Scene;
using HelixToolkit.Nex.Shaders.Frag;
using Newtonsoft.Json.Linq;
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
    private readonly LightConverter _lightConverter;
    private readonly List<ImportDiagnostic> _diagnostics;
    private readonly ImporterConfig _config;

    /// <summary>
    /// The lights parsed from the document-level <c>KHR_lights_punctual</c> extension for the
    /// current build, positionally parallel to the glTF lights array. A <c>null</c> entry means
    /// the definition at that index was not convertible and must not be attached. Populated once
    /// per <see cref="BuildScene"/> call.
    /// </summary>
    private IReadOnlyList<ParsedLight?> _parsedLights = [];

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
    /// <param name="lightConverter">The light converter for parsing KHR_lights_punctual lights.</param>
    /// <param name="diagnostics">The diagnostics list to report warnings/errors to.</param>
    /// <param name="config">The importer configuration providing default light ranges and mesh settings.</param>
    public SceneBuilder(
        World world,
        MeshConverter meshConverter,
        MaterialConverter materialConverter,
        LightConverter lightConverter,
        List<ImportDiagnostic> diagnostics,
        ImporterConfig config
    )
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _meshConverter = meshConverter ?? throw new ArgumentNullException(nameof(meshConverter));
        _materialConverter =
            materialConverter ?? throw new ArgumentNullException(nameof(materialConverter));
        _lightConverter = lightConverter ?? throw new ArgumentNullException(nameof(lightConverter));
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _config = config ?? throw new ArgumentNullException(nameof(config));
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
        // Parse the document-level KHR_lights_punctual lights once for the duration of this build.
        // Per-node attachment (later tasks) reads from this parsed list. When the extension is
        // absent or the lights array is empty/absent, this is an empty list (no diagnostics).
        _parsedLights = _lightConverter.ParseLights(model);

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
        if (model.Nodes == null || nodeIndex < 0 || nodeIndex >= model.Nodes.Length)
        {
            _diagnostics.Add(
                new ImportDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"Scene references node index {nodeIndex} which is out of range.",
                    "Node",
                    nodeIndex
                )
            );

            var placeholder = new Node(_world, $"InvalidNode_{nodeIndex}");
            parent?.AddChild(placeholder);
            return placeholder;
        }
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

        // Resolve and (if valid) attach a KHR_lights_punctual light referenced by this node.
        // The light's effective position/direction are driven by the node transform, so no world
        // matrix is threaded here.
        TryAttachLight(gltfNode, nodeIndex, node);

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
    /// The per-node property name (within the <c>KHR_lights_punctual</c> node extension object)
    /// that references a light by its index in the document-level lights array.
    /// </summary>
    private const string LightReferencePropertyName = "light";

    /// <summary>
    /// Resolves and validates the <c>KHR_lights_punctual</c> light reference on a glTF node and,
    /// when the reference is valid and convertible, hands off to <see cref="AttachLightComponent"/>
    /// to materialize and attach the engine light component.
    /// </summary>
    /// <remarks>
    /// Resolution and validation rules:
    /// <list type="bullet">
    /// <item>
    /// The node has no <c>KHR_lights_punctual</c> extension, or the extension lacks a valid integer
    /// <c>light</c> reference: the node output is left unchanged (Requirement 7.4).
    /// </item>
    /// <item>
    /// The reference index is negative or greater than or equal to the length of the lights array:
    /// a Warning diagnostic identifying the node is added and no light is attached (Requirement 6.1).
    /// </item>
    /// <item>
    /// The reference index is in range but the parsed light slot is <c>null</c> (e.g. the definition
    /// had an unknown type and was not convertible): an Error diagnostic identifying the node and the
    /// unresolved reference is added and no light is attached (Requirement 5.7).
    /// </item>
    /// </list>
    /// </remarks>
    /// <param name="gltfNode">The source glTF node.</param>
    /// <param name="nodeIndex">The index of the node in the glTF Nodes array (used in diagnostics).</param>
    /// <param name="node">The engine node created for <paramref name="gltfNode"/>.</param>
    private void TryAttachLight(GltfNode gltfNode, int nodeIndex, Node node)
    {
        // Requirement 7.4: a node without the KHR_lights_punctual extension is unchanged.
        if (
            gltfNode.Extensions is null
            || !gltfNode.Extensions.TryGetValue(LightConverter.ExtensionName, out var extensionRaw)
            || extensionRaw is not JObject extensionObj
        )
        {
            return;
        }

        // Requirement 7.4: the extension object must carry an integer `light` reference. When the
        // property is absent or is not an integer, there is no light to attach and the node output
        // is left unchanged.
        var lightToken = extensionObj[LightReferencePropertyName];
        if (lightToken is not { Type: JTokenType.Integer })
        {
            return;
        }

        var lightIndex = lightToken.Value<int>();

        // Requirement 6.1: a reference that is negative or >= the lights array length is out of
        // range → Warning diagnostic identifying the node, no attach. The parsed list is positionally
        // parallel to the document-level lights array, so its count is that array's length.
        if (lightIndex < 0 || lightIndex >= _parsedLights.Count)
        {
            _diagnostics.Add(
                new ImportDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"Node {nodeIndex} references KHR_lights_punctual light index {lightIndex} which is out of range. No light was attached.",
                    "Node",
                    nodeIndex
                )
            );
            return;
        }

        // Requirement 5.7: a reference that is in range but points at a non-convertible definition
        // (null slot, e.g. unknown light type) → Error diagnostic identifying the node and the
        // unresolved reference, no attach.
        var parsedLight = _parsedLights[lightIndex];
        if (parsedLight is null)
        {
            _diagnostics.Add(
                new ImportDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Node {nodeIndex} references KHR_lights_punctual light index {lightIndex} which could not be converted (unknown or invalid light definition). No light was attached.",
                    "Node",
                    nodeIndex
                )
            );
            return;
        }

        // The reference is valid and the definition is convertible: hand off the non-null parsed
        // light for component construction and attachment.
        AttachLightComponent(node, nodeIndex, parsedLight.Value, lightIndex);
    }

    /// <summary>
    /// Materializes the engine light component for a resolved, convertible <see cref="ParsedLight"/>
    /// and attaches it to the referencing node's own entity. The light's effective position and
    /// direction are driven by the node transform, so the importer does not set per-light
    /// position/direction (directional and range <c>Direction</c> stay at the component default
    /// <c>-Vector3.UnitZ</c>, and range <c>Position</c> stays at the component default).
    /// </summary>
    /// <param name="node">The engine node to attach the light component to.</param>
    /// <param name="nodeIndex">The index of the node in the glTF Nodes array (used in diagnostics).</param>
    /// <param name="light">The resolved, convertible parsed light to materialize.</param>
    /// <param name="lightIndex">
    /// The index of the referenced light in the document-level lights array (used in diagnostics to
    /// identify the offending light).
    /// </param>
    private void AttachLightComponent(Node node, int nodeIndex, ParsedLight light, int lightIndex)
    {
        var color = new Color4(light.Color);

        switch (light.Kind)
        {
            case LightKind.Directional:
                // Attach the directional light component to the referencing node's own entity.
                // Position/direction are driven by the node transform, so Direction is left at the
                // component default (-Vector3.UnitZ).
                node.Entity.Set(
                    new DirectionalLightInfo { Color = color, Intensity = light.Intensity }
                );
                break;

            case LightKind.Point:
                // Attach the point (range) light component to the referencing node's own entity.
                node.Entity.Set(
                    new RangeLightInfo(RangeLightType.Point)
                    {
                        Color = color,
                        Intensity = light.Intensity,
                        Range = light.Range,
                    }
                );
                if (_config.CreatePointLightMeshes)
                {
                    // Create a small sphere mesh to visualize the point light position. The
                    // visualization mesh stays a child of the node (preservation 3.5); only the
                    // light component lives on the node's own entity.
                    var sphereMesh = _meshConverter.GetSphereMesh();
                    var sphereNode = new MeshNode(_world, $"PointLightMesh_{nodeIndex}")
                    {
                        Geometry = sphereMesh,
                        MaterialProperties = _materialConverter.CreateMaterialProps(
                            PBRShadingMode.Unlit,
                            $"PointLightMesh_{nodeIndex}"
                        ),
                    };
                    sphereNode.MaterialProperties.Emissive = color;
                    sphereNode.Transform.Scale = new Vector3(_config.PointLightMeshSize);
                    node.AddChild(sphereNode);
                }
                break;

            case LightKind.Spot:
                // Attach the spot (range) light component to the referencing node's own entity.
                // Position/direction are driven by the node transform, so Direction is left at the
                // component default (-Vector3.UnitZ).
                var spotAngles = new Vector2(light.InnerConeAngle, light.OuterConeAngle);
                node.Entity.Set(
                    new RangeLightInfo(RangeLightType.Spot)
                    {
                        Color = color,
                        Intensity = light.Intensity,
                        Range = light.Range,
                        SpotAngles = spotAngles,
                    }
                );
                break;

            default:
                // An unrecognized LightKind cannot be materialized into an engine light component.
                // Emit a diagnostic identifying the node and the referenced light index, and attach
                // no light.
                _diagnostics.Add(
                    new ImportDiagnostic(
                        DiagnosticSeverity.Warning,
                        $"Node {nodeIndex} references KHR_lights_punctual light index {lightIndex} with an unrecognized light kind '{light.Kind}'. No light was attached.",
                        "Node",
                        nodeIndex
                    )
                );
                break;
        }
    }

    /// <summary>
    /// Builds the mesh node(s) for the specified glTF mesh and attaches them to the parent node.
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
        if (metadata.DoubleSided && !meshNode.IsTransparent)
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
