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
/// The outcome of recording a glTF scene into a <see cref="SceneCommandBuffer"/>: the buffer
/// holding the deferred node-creation/hierarchy/property commands, and the deferred handle of the
/// scene root. The buffer is materialized by calling <see cref="SceneCommandBuffer.Flush(World)"/>
/// on the target world's owning thread; the root node is then available from
/// <see cref="SceneCommandBuffer.MaterializedNodes"/> keyed by <see cref="Root"/>.
/// </summary>
internal readonly struct SceneRecording(SceneCommandBuffer buffer, DeferredNode root)
{
    /// <summary>The command buffer holding the recorded scene.</summary>
    public SceneCommandBuffer Buffer { get; } = buffer;

    /// <summary>The deferred handle of the recorded scene root.</summary>
    public DeferredNode Root { get; } = root;
}

/// <summary>
/// Walks the glTF node tree and <em>records</em> deferred creation of Node/MeshNode instances,
/// transforms, light components, and parent-child relationships into a
/// <see cref="SceneCommandBuffer"/>.
/// </summary>
/// <remarks>
/// Recording never touches the target <see cref="World"/>: node construction and every
/// <c>World</c>/<c>Entity</c> mutation is captured in command-buffer factories and applied only
/// during <see cref="SceneCommandBuffer.Flush(World)"/> on the world's owning thread. Mesh,
/// material, texture, and light <em>conversion</em> (which operates on resource managers, not the
/// ECS world) and all diagnostics are produced at record time, so the walk can run on a background
/// thread while the flush runs on the owning thread.
/// </remarks>
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
    /// per <see cref="RecordScene"/> call.
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
    /// <param name="world">The ECS world the recorded scene is materialized into during flush.</param>
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
    /// Builds the engine scene graph from the specified glTF scene by recording it into a command
    /// buffer and immediately flushing it onto this builder's world. This is a convenience that
    /// records and materializes in one call on the calling (owning) thread.
    /// </summary>
    /// <param name="model">The deserialized glTF model.</param>
    /// <param name="sceneIndex">
    /// The scene index to import. If -1, uses the model's default scene property or falls back to the first scene.
    /// </param>
    /// <returns>The materialized root Node of the imported scene.</returns>
    public Node BuildScene(Gltf model, int sceneIndex)
    {
        var recording = RecordScene(model, sceneIndex);
        recording.Buffer.Flush(_world);
        return recording.Buffer.MaterializedNodes[recording.Root];
    }

    /// <summary>
    /// Walks the glTF scene and records deferred creation of the engine scene graph into a fresh
    /// <see cref="SceneCommandBuffer"/>, without touching any <see cref="World"/>. All conversion
    /// and diagnostics are produced here; node materialization happens when the returned buffer is
    /// flushed on the world's owning thread.
    /// </summary>
    /// <param name="model">The deserialized glTF model.</param>
    /// <param name="sceneIndex">
    /// The scene index to import. If -1, uses the model's default scene property or falls back to the first scene.
    /// </param>
    /// <returns>The recorded command buffer and the deferred handle of the scene root.</returns>
    public SceneRecording RecordScene(Gltf model, int sceneIndex)
    {
        var buffer = new SceneCommandBuffer();

        // Parse the document-level KHR_lights_punctual lights once for the duration of this build.
        // When the extension is absent or the lights array is empty/absent, this is an empty list.
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
            // No scenes available — record an empty root node
            var emptyRoot = buffer.RecordCreateNode("Root");
            return new SceneRecording(buffer, emptyRoot);
        }

        var scene = scenes[resolvedSceneIndex];

        // Record root node for the scene
        var rootHandle = buffer.RecordCreateNode(scene.Name ?? "Root");

        // Iterate over the scene's top-level node indices, preserving order
        if (scene.Nodes != null)
        {
            foreach (int nodeIndex in scene.Nodes)
            {
                RecordNode(buffer, model, nodeIndex, rootHandle);
            }
        }

        return new SceneRecording(buffer, rootHandle);
    }

    /// <summary>
    /// Asynchronously walks the glTF scene and records deferred creation of the engine scene graph
    /// into a fresh <see cref="SceneCommandBuffer"/>. Mesh/material/texture conversion uses the
    /// async resource-upload path (<c>AddAsync</c>/<c>LoadTextureAsync</c>), so GPU transfers run
    /// off the render thread. No <see cref="World"/> is touched; node materialization happens when
    /// the returned buffer is flushed on the world's owning thread.
    /// </summary>
    /// <param name="model">The deserialized glTF model.</param>
    /// <param name="sceneIndex">
    /// The scene index to import. If -1, uses the model's default scene property or falls back to the first scene.
    /// </param>
    /// <param name="ct">A token used to cancel the asynchronous conversion/recording.</param>
    /// <returns>The recorded command buffer and the deferred handle of the scene root.</returns>
    public async Task<SceneRecording> RecordSceneAsync(
        Gltf model,
        int sceneIndex,
        CancellationToken ct = default
    )
    {
        var buffer = new SceneCommandBuffer();

        _parsedLights = _lightConverter.ParseLights(model);

        int resolvedSceneIndex = sceneIndex;
        if (resolvedSceneIndex < 0)
        {
            resolvedSceneIndex = model.Scene ?? 0;
        }

        var scenes = model.Scenes;
        if (scenes == null || resolvedSceneIndex >= scenes.Length)
        {
            var emptyRoot = buffer.RecordCreateNode("Root");
            return new SceneRecording(buffer, emptyRoot);
        }

        var scene = scenes[resolvedSceneIndex];
        var rootHandle = buffer.RecordCreateNode(scene.Name ?? "Root");

        if (scene.Nodes != null)
        {
            foreach (int nodeIndex in scene.Nodes)
            {
                await RecordNodeAsync(buffer, model, nodeIndex, rootHandle, ct).ConfigureAwait(false);
            }
        }

        return new SceneRecording(buffer, rootHandle);
    }

    /// <summary>
    /// Asynchronous counterpart of <see cref="RecordNode"/>: records a glTF node subtree, awaiting
    /// the async mesh/material conversion so GPU uploads happen off the render thread.
    /// </summary>
    internal async Task<DeferredNode> RecordNodeAsync(
        SceneCommandBuffer buffer,
        Gltf model,
        int nodeIndex,
        DeferredNode parent,
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();

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

            var placeholder = buffer.RecordCreateNode($"InvalidNode_{nodeIndex}");
            buffer.RecordAddChild(parent, placeholder);
            return placeholder;
        }
        var gltfNode = model.Nodes[nodeIndex];

        var resolvedLight = ResolveNodeLight(gltfNode, nodeIndex, out var lightIndex);

        DeferredNode handle;
        if (resolvedLight is ParsedLight light)
        {
            var capturedIndex = nodeIndex;
            handle = buffer.RecordCreateNode(world =>
            {
                var node = new Node(world);
                AttachLightComponent(node, capturedIndex, light, lightIndex);
                return node;
            });
        }
        else
        {
            handle = buffer.RecordCreateNode();
        }

        if (!string.IsNullOrEmpty(gltfNode.Name))
        {
            buffer.RecordName(handle, gltfNode.Name);
        }

        var transform = ComputeTransform(gltfNode, nodeIndex);
        buffer.RecordLocalTransform(handle, in transform);

        buffer.RecordAddChild(parent, handle);

        if (gltfNode.Mesh.HasValue)
        {
            await RecordMeshNodesAsync(buffer, model, gltfNode.Mesh.Value, handle, ct)
                .ConfigureAwait(false);
        }

        if (
            resolvedLight is ParsedLight pointLight
            && pointLight.Kind == LightKind.Point
            && _config.CreatePointLightMeshes
        )
        {
            await RecordPointLightSphereAsync(
                    buffer,
                    handle,
                    nodeIndex,
                    new Color4(pointLight.Color),
                    ct
                )
                .ConfigureAwait(false);
        }

        if (gltfNode.Children != null)
        {
            foreach (int childIndex in gltfNode.Children)
            {
                await RecordNodeAsync(buffer, model, childIndex, handle, ct).ConfigureAwait(false);
            }
        }

        return handle;
    }
    /// This is a convenience that records and materializes in one call on the calling (owning)
    /// thread; it does not parse the document-level lights (use <see cref="RecordScene"/> or
    /// <see cref="BuildScene"/> for full-scene light resolution).
    /// </summary>
    /// <param name="model">The deserialized glTF model.</param>
    /// <param name="nodeIndex">The index of the node in the glTF Nodes array.</param>
    /// <param name="parent">An existing parent node to attach the materialized node to, or null.</param>
    /// <returns>The materialized node.</returns>
    internal Node BuildNode(Gltf model, int nodeIndex, Node? parent)
    {
        var buffer = new SceneCommandBuffer();
        var handle = RecordNode(buffer, model, nodeIndex, parent: default);
        buffer.Flush(_world);
        var node = buffer.MaterializedNodes[handle];
        parent?.AddChild(node);
        return node;
    }

    /// <summary>
    /// Recursively records an engine Node for a glTF node — including its transform, optional light
    /// component, mesh children, and parent-child wiring — into the command buffer.
    /// </summary>
    /// <param name="buffer">The command buffer to record into.</param>
    /// <param name="model">The deserialized glTF model.</param>
    /// <param name="nodeIndex">The index of the node in the glTF Nodes array.</param>
    /// <param name="parent">The deferred handle of the parent node to attach this node to.</param>
    /// <returns>The deferred handle of the recorded node.</returns>
    internal DeferredNode RecordNode(
        SceneCommandBuffer buffer,
        Gltf model,
        int nodeIndex,
        DeferredNode parent
    )
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

            var placeholder = buffer.RecordCreateNode($"InvalidNode_{nodeIndex}");
            buffer.RecordAddChild(parent, placeholder);
            return placeholder;
        }
        var gltfNode = model.Nodes[nodeIndex];

        // Resolve a KHR_lights_punctual light reference (if any) at record time. This validates the
        // reference and emits range/unresolved diagnostics now; the actual light component is set on
        // the node's own entity during flush via the recorded factory.
        var resolvedLight = ResolveNodeLight(gltfNode, nodeIndex, out var lightIndex);

        // Record the engine node. A node that carries a light component is recorded through a
        // factory that materializes the base Node and sets the light component on the owning
        // thread; a node without a light uses the plain base-node creation path.
        DeferredNode handle;
        if (resolvedLight is ParsedLight light)
        {
            var capturedIndex = nodeIndex;
            handle = buffer.RecordCreateNode(world =>
            {
                var node = new Node(world);
                AttachLightComponent(node, capturedIndex, light, lightIndex);
                return node;
            });
        }
        else
        {
            handle = buffer.RecordCreateNode();
        }

        // Set name from glTF node name
        if (!string.IsNullOrEmpty(gltfNode.Name))
        {
            buffer.RecordName(handle, gltfNode.Name);
        }

        // Compute and record the local transform (emits a diagnostic on undecomposable matrices).
        var transform = ComputeTransform(gltfNode, nodeIndex);
        buffer.RecordLocalTransform(handle, in transform);

        // Attach to parent (preserves child ordering: self is attached before its own children).
        buffer.RecordAddChild(parent, handle);

        // Handle mesh reference
        if (gltfNode.Mesh.HasValue)
        {
            RecordMeshNodes(buffer, model, gltfNode.Mesh.Value, handle);
        }

        // For point lights, optionally record the visualization sphere mesh as a child of the node.
        // The light's effective position is driven by the node transform; only the visualization
        // mesh is a child, the light component lives on the node's own entity.
        if (
            resolvedLight is ParsedLight pointLight
            && pointLight.Kind == LightKind.Point
            && _config.CreatePointLightMeshes
        )
        {
            RecordPointLightSphere(buffer, handle, nodeIndex, new Color4(pointLight.Color));
        }

        // Recursively process children, preserving order
        if (gltfNode.Children != null)
        {
            foreach (int childIndex in gltfNode.Children)
            {
                RecordNode(buffer, model, childIndex, handle);
            }
        }

        return handle;
    }

    /// <summary>
    /// The per-node property name (within the <c>KHR_lights_punctual</c> node extension object)
    /// that references a light by its index in the document-level lights array.
    /// </summary>
    private const string LightReferencePropertyName = "light";

    /// <summary>
    /// Resolves and validates the <c>KHR_lights_punctual</c> light reference on a glTF node,
    /// emitting the appropriate diagnostics at record time. Returns the resolved, convertible
    /// <see cref="ParsedLight"/> when the reference is valid, or <c>null</c> when there is no light
    /// to attach.
    /// </summary>
    /// <remarks>
    /// Resolution and validation rules:
    /// <list type="bullet">
    /// <item>
    /// The node has no <c>KHR_lights_punctual</c> extension, or the extension lacks a valid integer
    /// <c>light</c> reference: returns <c>null</c>, no diagnostic (Requirement 7.4).
    /// </item>
    /// <item>
    /// The reference index is negative or greater than or equal to the length of the lights array:
    /// a Warning diagnostic identifying the node is added and <c>null</c> is returned (Requirement 6.1).
    /// </item>
    /// <item>
    /// The reference index is in range but the parsed light slot is <c>null</c> (e.g. the definition
    /// had an unknown type and was not convertible): an Error diagnostic identifying the node and the
    /// unresolved reference is added and <c>null</c> is returned (Requirement 5.7).
    /// </item>
    /// </list>
    /// </remarks>
    /// <param name="gltfNode">The source glTF node.</param>
    /// <param name="nodeIndex">The index of the node in the glTF Nodes array (used in diagnostics).</param>
    /// <param name="lightIndex">The resolved light index, or -1 when no light is attached.</param>
    private ParsedLight? ResolveNodeLight(GltfNode gltfNode, int nodeIndex, out int lightIndex)
    {
        lightIndex = -1;

        // Requirement 7.4: a node without the KHR_lights_punctual extension is unchanged.
        if (
            gltfNode.Extensions is null
            || !gltfNode.Extensions.TryGetValue(LightConverter.ExtensionName, out var extensionRaw)
            || extensionRaw is not JObject extensionObj
        )
        {
            return null;
        }

        // Requirement 7.4: the extension object must carry an integer `light` reference.
        var lightToken = extensionObj[LightReferencePropertyName];
        if (lightToken is not { Type: JTokenType.Integer })
        {
            return null;
        }

        var resolvedIndex = lightToken.Value<int>();

        // Requirement 6.1: out-of-range reference → Warning diagnostic, no attach.
        if (resolvedIndex < 0 || resolvedIndex >= _parsedLights.Count)
        {
            _diagnostics.Add(
                new ImportDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"Node {nodeIndex} references KHR_lights_punctual light index {resolvedIndex} which is out of range. No light was attached.",
                    "Node",
                    nodeIndex
                )
            );
            return null;
        }

        // Requirement 5.7: in-range reference to a non-convertible definition → Error diagnostic, no attach.
        var parsedLight = _parsedLights[resolvedIndex];
        if (parsedLight is null)
        {
            _diagnostics.Add(
                new ImportDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Node {nodeIndex} references KHR_lights_punctual light index {resolvedIndex} which could not be converted (unknown or invalid light definition). No light was attached.",
                    "Node",
                    nodeIndex
                )
            );
            return null;
        }

        lightIndex = resolvedIndex;
        return parsedLight.Value;
    }

    /// <summary>
    /// Sets the engine light component for a resolved, convertible <see cref="ParsedLight"/> on the
    /// referencing node's own entity. Invoked on the owning thread during flush. The light's
    /// effective position and direction are driven by the node transform, so the importer does not
    /// set per-light position/direction (directional and range <c>Direction</c> stay at the
    /// component default <c>-Vector3.UnitZ</c>, and range <c>Position</c> stays at the component
    /// default).
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
                // The optional visualization sphere mesh is recorded separately as a child node.
                node.Entity.Set(
                    new RangeLightInfo(RangeLightType.Point)
                    {
                        Color = color,
                        Intensity = light.Intensity,
                        Range = light.Range,
                    }
                );
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
    /// Records the point-light visualization sphere mesh as a child of the referencing node. The
    /// sphere geometry and unlit emissive material are created at record time; the MeshNode itself
    /// is materialized during flush.
    /// </summary>
    private void RecordPointLightSphere(
        SceneCommandBuffer buffer,
        DeferredNode parent,
        int nodeIndex,
        Color4 color
    )
    {
        var sphereMesh = _meshConverter.GetSphereMesh();
        var material = _materialConverter.CreateMaterialProps(
            PBRShadingMode.Unlit,
            $"PointLightMesh_{nodeIndex}"
        );
        material.Emissive = color;
        var sphereName = $"PointLightMesh_{nodeIndex}";
        var meshSize = _config.PointLightMeshSize;

        var sphereHandle = buffer.RecordCreateNode(world =>
        {
            var sphereNode = new MeshNode(world, sphereName)
            {
                Geometry = sphereMesh,
                MaterialProperties = material,
            };
            return sphereNode;
        });

        var scaleTransform = new Transform { Scale = new Vector3(meshSize) };
        buffer.RecordLocalTransform(sphereHandle, in scaleTransform);
        buffer.RecordAddChild(parent, sphereHandle);
    }

    /// <summary>
    /// Asynchronous counterpart of <see cref="RecordPointLightSphere"/>: registers the sphere
    /// geometry via the async upload path so the GPU transfer runs off the render thread. The
    /// material is created inside the factory on the owning thread during flush.
    /// </summary>
    private async Task RecordPointLightSphereAsync(
        SceneCommandBuffer buffer,
        DeferredNode parent,
        int nodeIndex,
        Color4 color,
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();

        var sphereMesh = await _meshConverter.GetSphereMeshAsync().ConfigureAwait(false);
        var sphereName = $"PointLightMesh_{nodeIndex}";
        var meshSize = _config.PointLightMeshSize;

        var sphereHandle = buffer.RecordCreateNode(world =>
        {
            // Material creation happens on the owning thread during flush.
            var material = _materialConverter.CreateMaterialProps(PBRShadingMode.Unlit, sphereName);
            material.Emissive = color;

            return new MeshNode(world, sphereName)
            {
                Geometry = sphereMesh,
                MaterialProperties = material,
            };
        });

        var scaleTransform = new Transform { Scale = new Vector3(meshSize) };
        buffer.RecordLocalTransform(sphereHandle, in scaleTransform);
        buffer.RecordAddChild(parent, sphereHandle);
    }

    /// <summary>
    /// Records the mesh node(s) for the specified glTF mesh and wires them under the parent handle.
    /// Single primitive: records a MeshNode directly as a child of the parent node.
    /// Multiple primitives: records a parent Node (named after the mesh) with a child MeshNode per primitive.
    /// Skips primitives with invalid geometry handles. Omits the parent Node if all primitives are skipped.
    /// </summary>
    /// <param name="buffer">The command buffer to record into.</param>
    /// <param name="model">The deserialized glTF model.</param>
    /// <param name="meshIndex">The index of the mesh in the glTF Meshes array.</param>
    /// <param name="parent">The deferred handle of the node to attach mesh nodes to.</param>
    private void RecordMeshNodes(
        SceneCommandBuffer buffer,
        Gltf model,
        int meshIndex,
        DeferredNode parent
    )
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
            // Single primitive: record MeshNode directly on the parent node
            if (
                TryRecordMeshNodeForPrimitive(
                    buffer,
                    model,
                    primitives[0],
                    meshIndex,
                    0,
                    mesh.Name ?? $"Mesh_{meshIndex}",
                    out var meshHandle
                )
            )
            {
                buffer.RecordAddChild(parent, meshHandle);
            }
        }
        else
        {
            // Multiple primitives: record a parent Node, then child MeshNode per primitive
            var meshHandles = new List<DeferredNode>();

            for (int primIndex = 0; primIndex < primitives.Length; primIndex++)
            {
                if (
                    TryRecordMeshNodeForPrimitive(
                        buffer,
                        model,
                        primitives[primIndex],
                        meshIndex,
                        primIndex,
                        $"{mesh.Name ?? $"Mesh_{meshIndex}"}_Primitive{primIndex}",
                        out var meshHandle
                    )
                )
                {
                    meshHandles.Add(meshHandle);
                }
            }

            // Omit parent Node when all primitives are skipped
            if (meshHandles.Count == 0)
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

            // Record parent Node named after the mesh
            var meshParentHandle = buffer.RecordCreateNode(mesh.Name ?? $"Mesh_{meshIndex}");
            buffer.RecordAddChild(parent, meshParentHandle);

            foreach (var meshHandle in meshHandles)
            {
                buffer.RecordAddChild(meshParentHandle, meshHandle);
            }
        }
    }

    /// <summary>
    /// Records a single MeshNode for a glTF mesh primitive. Geometry and material conversion happen
    /// here (record time); the MeshNode is materialized during flush. Returns <c>false</c> when the
    /// geometry handle is invalid (primitive is skipped).
    /// </summary>
    private bool TryRecordMeshNodeForPrimitive(
        SceneCommandBuffer buffer,
        Gltf model,
        MeshPrimitive primitive,
        int meshIndex,
        int primIndex,
        string nodeName,
        out DeferredNode handle
    )
    {
        handle = default;

        // Convert geometry
        var (geometry, geometryHandle) = _meshConverter.ConvertPrimitive(
            model,
            primitive,
            meshIndex,
            primIndex
        );

        // Skip MeshNode creation when geometry handle is invalid
        if (geometry == null || !geometryHandle.Valid)
        {
            return false;
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

        var material = materialResult.Material;
        var metadata = materialResult.Metadata;

        // Set IsRenderable based on validity of both geometry and material
        bool materialValid = material != null && material.Valid;

        // Record the MeshNode: construction and entity mutation are deferred to flush.
        handle = buffer.RecordCreateNode(world =>
        {
            var meshNode = new MeshNode(world, nodeName)
            {
                Geometry = geometry,
                MaterialProperties = material,
                IsRenderable = materialValid,
            };

            // Apply material metadata (alpha mode, double-sided) to the MeshNode.
            ApplyMaterialMetadata(meshNode, metadata);

            return meshNode;
        });

        return true;
    }

    /// <summary>
    /// Asynchronous counterpart of <see cref="RecordMeshNodes"/>: awaits async primitive conversion
    /// so geometry/texture GPU uploads run off the render thread.
    /// </summary>
    private async Task RecordMeshNodesAsync(
        SceneCommandBuffer buffer,
        Gltf model,
        int meshIndex,
        DeferredNode parent,
        CancellationToken ct
    )
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
            var (recorded, meshHandle) = await TryRecordMeshNodeForPrimitiveAsync(
                    buffer,
                    model,
                    primitives[0],
                    meshIndex,
                    0,
                    mesh.Name ?? $"Mesh_{meshIndex}",
                    ct
                )
                .ConfigureAwait(false);
            if (recorded)
            {
                buffer.RecordAddChild(parent, meshHandle);
            }
        }
        else
        {
            var meshHandles = new List<DeferredNode>();

            for (int primIndex = 0; primIndex < primitives.Length; primIndex++)
            {
                var (recorded, meshHandle) = await TryRecordMeshNodeForPrimitiveAsync(
                        buffer,
                        model,
                        primitives[primIndex],
                        meshIndex,
                        primIndex,
                        $"{mesh.Name ?? $"Mesh_{meshIndex}"}_Primitive{primIndex}",
                        ct
                    )
                    .ConfigureAwait(false);
                if (recorded)
                {
                    meshHandles.Add(meshHandle);
                }
            }

            if (meshHandles.Count == 0)
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

            var meshParentHandle = buffer.RecordCreateNode(mesh.Name ?? $"Mesh_{meshIndex}");
            buffer.RecordAddChild(parent, meshParentHandle);

            foreach (var meshHandle in meshHandles)
            {
                buffer.RecordAddChild(meshParentHandle, meshHandle);
            }
        }
    }

    /// <summary>
    /// Asynchronous counterpart of <see cref="TryRecordMeshNodeForPrimitive"/>: converts geometry
    /// via the async transfer-queue upload path (off the render thread), but defers material/texture
    /// conversion into the recorded factory so the texture GPU upload (which needs the graphics
    /// queue, including mip generation) runs on the owning thread during flush. Returns whether a
    /// mesh node was recorded and its handle.
    /// </summary>
    private async Task<(bool Recorded, DeferredNode Handle)> TryRecordMeshNodeForPrimitiveAsync(
        SceneCommandBuffer buffer,
        Gltf model,
        MeshPrimitive primitive,
        int meshIndex,
        int primIndex,
        string nodeName,
        CancellationToken ct
    )
    {
        // Convert geometry via the async upload path (dedicated transfer queue — safe off-thread).
        var (geometry, geometryHandle) = await _meshConverter
            .ConvertPrimitiveAsync(model, primitive, meshIndex, primIndex, ct)
            .ConfigureAwait(false);

        // Skip MeshNode creation when geometry handle is invalid
        if (geometry == null || !geometryHandle.Valid)
        {
            return (false, default);
        }

        // Material conversion (which uploads textures on the graphics queue, including mip blits)
        // is deferred into the factory, which runs on the owning thread during flush.
        var capturedPrimitive = primitive;
        var handle = buffer.RecordCreateNode(world =>
        {
            MaterialConvertResult materialResult = capturedPrimitive.Material.HasValue
                ? _materialConverter.ConvertMaterialWithMetadata(
                    model,
                    capturedPrimitive.Material.Value
                )
                : new MaterialConvertResult(
                    _materialConverter.GetDefaultMaterial(),
                    MaterialMetadata.Default
                );

            var material = materialResult.Material;
            bool materialValid = material != null && material.Valid;

            var meshNode = new MeshNode(world, nodeName)
            {
                Geometry = geometry,
                MaterialProperties = material,
                IsRenderable = materialValid,
            };

            ApplyMaterialMetadata(meshNode, materialResult.Metadata);

            return meshNode;
        });

        return (true, handle);
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
    /// Computes the local transform for a glTF node. Matrix takes precedence over TRS. If neither
    /// is specified, identity is used. Emits a diagnostic when an explicit matrix cannot be
    /// decomposed into valid TRS components.
    /// </summary>
    private Transform ComputeTransform(GltfNode gltfNode, int nodeIndex)
    {
        var transform = new Transform();

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

        return transform;
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
