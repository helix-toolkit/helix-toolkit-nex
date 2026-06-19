using glTFLoader.Schema;
using HelixToolkit.Nex.glTF.Internal.Draco;
using Newtonsoft.Json.Linq;
using GltfBuffer = glTFLoader.Schema.Buffer;

namespace HelixToolkit.Nex.glTF.Tests.Properties.Helpers;

/// <summary>
/// Builds in-memory glTF models and <see cref="MeshPrimitive"/>s for Draco converter-level tests.
/// Lets a test assemble the pieces the converter inspects — the
/// <c>KHR_draco_mesh_compression</c> extension <see cref="JObject"/>, the primitive's standard
/// <c>attributes</c> map and <c>indices</c>, buffers/bufferViews holding the compressed slice, and
/// accessor metadata (for count-consistency checks) — without touching the native decoder.
/// </summary>
/// <remarks>
/// The builder is stateful: call <see cref="AddBuffer"/>, <see cref="AddBufferView"/>, and
/// <see cref="AddAccessor"/> to register elements (each returns its index), then
/// <see cref="BuildModel"/> to produce the <see cref="Gltf"/> and the matching <c>byte[][]</c>
/// buffer data (the shape <c>AccessorReader</c> consumes). Static helpers
/// (<see cref="BuildExtensionObject"/>, <see cref="BuildPrimitive"/>) cover the common
/// "one primitive" cases directly.
/// </remarks>
internal sealed class DracoModelBuilder
{
    /// <summary>The glTF extension name the builder targets.</summary>
    public const string ExtensionName = DracoExtensionData.ExtensionName;

    private readonly List<byte[]> _buffers = [];
    private readonly List<BufferView> _bufferViews = [];
    private readonly List<Accessor> _accessors = [];
    private readonly List<Mesh> _meshes = [];
    private readonly List<string> _extensionsUsed = [];
    private readonly List<string> _extensionsRequired = [];

    /// <summary>
    /// Registers a buffer's raw bytes and a matching <see cref="GltfBuffer"/> entry.
    /// </summary>
    /// <param name="data">The buffer bytes.</param>
    /// <returns>The index of the registered buffer.</returns>
    public int AddBuffer(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        _buffers.Add(data);
        return _buffers.Count - 1;
    }

    /// <summary>
    /// Registers a <see cref="BufferView"/> over a previously added buffer.
    /// </summary>
    /// <param name="buffer">The buffer index this view refers to.</param>
    /// <param name="byteOffset">The byte offset within the buffer.</param>
    /// <param name="byteLength">The byte length of the view.</param>
    /// <returns>The index of the registered bufferView.</returns>
    public int AddBufferView(int buffer, int byteOffset, int byteLength)
    {
        _bufferViews.Add(
            new BufferView
            {
                Buffer = buffer,
                ByteOffset = byteOffset,
                ByteLength = byteLength,
            }
        );
        return _bufferViews.Count - 1;
    }

    /// <summary>
    /// Registers an <see cref="Accessor"/> carrying the metadata used by the converter's
    /// count-consistency checks (Requirement 5). Buffer placement is irrelevant for those checks,
    /// so <c>bufferView</c> defaults to 0.
    /// </summary>
    /// <param name="count">The accessor's declared element count.</param>
    /// <param name="type">The accessor element type; defaults to <see cref="Accessor.TypeEnum.VEC3"/>.</param>
    /// <param name="componentType">The component type; defaults to <see cref="Accessor.ComponentTypeEnum.FLOAT"/>.</param>
    /// <param name="bufferView">The bufferView index; defaults to 0.</param>
    /// <returns>The index of the registered accessor.</returns>
    public int AddAccessor(
        int count,
        Accessor.TypeEnum type = Accessor.TypeEnum.VEC3,
        Accessor.ComponentTypeEnum componentType = Accessor.ComponentTypeEnum.FLOAT,
        int bufferView = 0
    )
    {
        _accessors.Add(
            new Accessor
            {
                BufferView = bufferView,
                ByteOffset = 0,
                ComponentType = componentType,
                Type = type,
                Count = count,
            }
        );
        return _accessors.Count - 1;
    }

    /// <summary>
    /// Registers a mesh built from the supplied primitives.
    /// </summary>
    /// <param name="primitives">The primitives that make up the mesh.</param>
    /// <returns>The index of the registered mesh.</returns>
    public int AddMesh(params MeshPrimitive[] primitives)
    {
        _meshes.Add(new Mesh { Primitives = primitives });
        return _meshes.Count - 1;
    }

    /// <summary>
    /// Marks an extension name as used and (optionally) required, populating the model's
    /// <c>extensionsUsed</c>/<c>extensionsRequired</c> arrays for requiredness-driven severity tests.
    /// </summary>
    /// <param name="required">When <see langword="true"/>, the name is added to <c>extensionsRequired</c>.</param>
    /// <param name="name">The extension name; defaults to <c>KHR_draco_mesh_compression</c>.</param>
    /// <returns>This builder, for chaining.</returns>
    public DracoModelBuilder DeclareExtension(bool required, string name = ExtensionName)
    {
        if (!_extensionsUsed.Contains(name))
        {
            _extensionsUsed.Add(name);
        }

        if (required && !_extensionsRequired.Contains(name))
        {
            _extensionsRequired.Add(name);
        }

        return this;
    }

    /// <summary>
    /// Produces the assembled <see cref="Gltf"/> model and the matching per-buffer byte arrays.
    /// </summary>
    /// <returns>A tuple of the model and the buffer data arrays (as <c>AccessorReader</c> expects).</returns>
    public (Gltf model, byte[][] buffers) BuildModel()
    {
        var gltfBuffers = new GltfBuffer[_buffers.Count];
        for (int i = 0; i < _buffers.Count; i++)
        {
            gltfBuffers[i] = new GltfBuffer { ByteLength = _buffers[i].Length };
        }

        var model = new Gltf
        {
            Buffers = gltfBuffers.Length > 0 ? gltfBuffers : null,
            BufferViews = _bufferViews.Count > 0 ? _bufferViews.ToArray() : null,
            Accessors = _accessors.Count > 0 ? _accessors.ToArray() : null,
            Meshes = _meshes.Count > 0 ? _meshes.ToArray() : null,
            ExtensionsUsed = _extensionsUsed.Count > 0 ? _extensionsUsed.ToArray() : null,
            ExtensionsRequired =
                _extensionsRequired.Count > 0 ? _extensionsRequired.ToArray() : null,
        };

        return (model, _buffers.ToArray());
    }

    // ──────────────────────────────────────────────────────────────────────────────
    // Static helpers for the common "single extension object / single primitive" cases
    // ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a well-formed <c>KHR_draco_mesh_compression</c> extension <see cref="JObject"/> with
    /// the given <c>bufferView</c> index and semantic → Draco-id <c>attributes</c> map.
    /// </summary>
    /// <param name="bufferView">The compressed bufferView index.</param>
    /// <param name="attributes">The semantic name → Draco attribute id map.</param>
    /// <returns>The extension object, suitable as a primitive extension value.</returns>
    public static JObject BuildExtensionObject(
        int bufferView,
        IReadOnlyDictionary<string, int> attributes
    )
    {
        ArgumentNullException.ThrowIfNull(attributes);

        var attributesObj = new JObject();
        foreach (var pair in attributes)
        {
            attributesObj[pair.Key] = pair.Value;
        }

        return new JObject { ["bufferView"] = bufferView, ["attributes"] = attributesObj };
    }

    /// <summary>
    /// Builds a <see cref="MeshPrimitive"/>, optionally attaching a value under the
    /// <c>KHR_draco_mesh_compression</c> extension key.
    /// </summary>
    /// <param name="dracoExtensionValue">
    /// The value to store under the extension key. Pass a <see cref="JObject"/> for a well-formed
    /// extension, an arbitrary <see cref="JToken"/> (or <see langword="null"/> wrapped via
    /// <see cref="JValue.CreateNull"/>) to exercise malformed-value paths, or <see langword="null"/>
    /// to omit the extension entirely (no key added).
    /// </param>
    /// <param name="standardAttributes">The primitive's standard <c>attributes</c> map, or <see langword="null"/>.</param>
    /// <param name="indices">The primitive's <c>indices</c> accessor index, or <see langword="null"/>.</param>
    /// <param name="mode">The primitive topology mode; defaults to <see cref="MeshPrimitive.ModeEnum.TRIANGLES"/>.</param>
    /// <returns>The constructed primitive.</returns>
    public static MeshPrimitive BuildPrimitive(
        object? dracoExtensionValue = null,
        IReadOnlyDictionary<string, int>? standardAttributes = null,
        int? indices = null,
        MeshPrimitive.ModeEnum mode = MeshPrimitive.ModeEnum.TRIANGLES
    )
    {
        var primitive = new MeshPrimitive { Mode = mode };

        if (standardAttributes is not null)
        {
            primitive.Attributes = new Dictionary<string, int>(standardAttributes);
        }

        if (indices.HasValue)
        {
            primitive.Indices = indices.Value;
        }

        if (dracoExtensionValue is not null)
        {
            primitive.Extensions = new Dictionary<string, object>
            {
                [ExtensionName] = dracoExtensionValue,
            };
        }

        return primitive;
    }
}
