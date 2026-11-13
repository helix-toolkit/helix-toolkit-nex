using System.Numerics;
using glTFLoader.Schema;
using HelixToolkit.Nex.Geometries;
using HelixToolkit.Nex.glTF.Internal;
using HelixToolkit.Nex.glTF.Internal.Draco;
using HelixToolkit.Nex.glTF.Tests.Properties.Helpers;
using HelixToolkit.Nex.Graphics.Mock;

namespace HelixToolkit.Nex.glTF.Tests;

/// <summary>
/// Regression tests for Draco-decoded texture coordinates. The decoder returns integer attributes
/// as raw values; normalized UNSIGNED_BYTE/UNSIGNED_SHORT source accessors must be scaled to [0, 1].
/// </summary>
[TestClass]
public class MeshConverterDracoTexCoordTests
{
    private const float Tolerance = 1e-5f;

    private static void AssertUv(Vector2 expected, Vector2 actual)
    {
        Assert.IsTrue(
            Vector2.Distance(expected, actual) < Tolerance,
            $"Expected {expected}, got {actual}."
        );
    }

    /// <summary>
    /// Converts a single Draco primitive with POSITION, TEXCOORD_0 and TEXCOORD_1, using the given
    /// source accessor component types and the given raw decoded UV values.
    /// </summary>
    private static Geometry ConvertDracoPrimitive(
        Accessor.ComponentTypeEnum uv0Type,
        Accessor.ComponentTypeEnum uv1Type,
        bool normalized,
        float[] uv0Values,
        float[] uv1Values,
        List<ImportDiagnostic> diagnostics
    )
    {
        var builder = new DracoModelBuilder();
        var buffer = builder.AddBuffer(new byte[16]);
        var view = builder.AddBufferView(buffer, 0, 16);
        var position = builder.AddAccessor(3, Accessor.TypeEnum.VEC3);
        var uv0 = builder.AddAccessor(3, Accessor.TypeEnum.VEC2, uv0Type);
        var uv1 = builder.AddAccessor(3, Accessor.TypeEnum.VEC2, uv1Type);
        var extension = DracoModelBuilder.BuildExtensionObject(
            view,
            new Dictionary<string, int>
            {
                ["POSITION"] = 0,
                ["TEXCOORD_0"] = 1,
                ["TEXCOORD_1"] = 2,
            }
        );
        var primitive = DracoModelBuilder.BuildPrimitive(
            extension,
            new Dictionary<string, int>
            {
                ["POSITION"] = position,
                ["TEXCOORD_0"] = uv0,
                ["TEXCOORD_1"] = uv1,
            }
        );
        builder.AddMesh(primitive);
        builder.DeclareExtension(required: true);
        var (model, buffers) = builder.BuildModel();
        model.Accessors[uv0].Normalized = normalized;
        model.Accessors[uv1].Normalized = normalized;

        var decoded = new DecodedMesh(
            3,
            new Dictionary<string, DecodedAttribute>
            {
                ["POSITION"] = new([0, 0, 0, 1, 0, 0, 0, 1, 0], 3),
                ["TEXCOORD_0"] = new(uv0Values, 2),
                ["TEXCOORD_1"] = new(uv1Values, 2),
            },
            [0, 1, 2]
        );

        // A real manager over the headless mock context, so the geometry registers successfully.
        using var context = new MockContext();
        context.Initialize();
        using var geometryManager = new GeometryManager(context);
        var converter = new MeshConverter(
            geometryManager,
            new AccessorReader(model, buffers),
            diagnostics,
            new ResourceManifest(),
            ImporterConfig.Default,
            new FakeDracoDecoder(DracoDecodeOutcome.Ok(decoded)),
            true
        );
        var (geometry, _) = converter.ConvertPrimitive(model, primitive, 0, 0);
        Assert.IsNotNull(geometry, string.Join("; ", diagnostics.Select(d => d.Message)));
        return geometry;
    }

    /// <summary>Finds the vertex at the given position (vertex order may change on import).</summary>
    private static VertexProperties PropsAt(Geometry geometry, Vector3 position)
    {
        for (int i = 0; i < geometry.Vertices.Count; i++)
        {
            var v = geometry.Vertices[i];
            if (new Vector3(v.X, v.Y, v.Z) == position)
            {
                return geometry.VertexProps[i];
            }
        }
        Assert.Fail($"No vertex at {position}.");
        return default;
    }

    [TestMethod]
    public void ConvertPrimitive_DracoNormalizedIntegerTexCoords_AreNormalized()
    {
        var diagnostics = new List<ImportDiagnostic>();
        var geometry = ConvertDracoPrimitive(
            Accessor.ComponentTypeEnum.UNSIGNED_SHORT,
            Accessor.ComponentTypeEnum.UNSIGNED_BYTE,
            normalized: true,
            uv0Values: [0, 0, 65535, 32768, 0, 65535],
            uv1Values: [0, 0, 255, 51, 102, 255],
            diagnostics
        );

        var second = PropsAt(geometry, Vector3.UnitX);
        AssertUv(new Vector2(1f, 32768f / 65535f), second.TexCoord);
        AssertUv(new Vector2(1f, 0.2f), second.TexCoord1);

        var third = PropsAt(geometry, Vector3.UnitY);
        AssertUv(new Vector2(0f, 1f), third.TexCoord);
        AssertUv(new Vector2(0.4f, 1f), third.TexCoord1);
    }

    [TestMethod]
    public void ConvertPrimitive_DracoFloatTexCoords_AreUnchanged()
    {
        var diagnostics = new List<ImportDiagnostic>();
        var geometry = ConvertDracoPrimitive(
            Accessor.ComponentTypeEnum.FLOAT,
            Accessor.ComponentTypeEnum.FLOAT,
            normalized: false,
            uv0Values: [0, 0, 0.25f, 0.5f, 2f, -1f],
            uv1Values: [0, 0, 0.75f, 0.125f, 3f, 4f],
            diagnostics
        );

        var second = PropsAt(geometry, Vector3.UnitX);
        AssertUv(new Vector2(0.25f, 0.5f), second.TexCoord);
        AssertUv(new Vector2(0.75f, 0.125f), second.TexCoord1);

        var third = PropsAt(geometry, Vector3.UnitY);
        AssertUv(new Vector2(2f, -1f), third.TexCoord);
        AssertUv(new Vector2(3f, 4f), third.TexCoord1);
    }
}
