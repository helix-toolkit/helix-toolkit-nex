using System.Numerics;
using System.Reflection;
using HelixToolkit.Nex.ECS;
using HelixToolkit.Nex.Engine.Components;
using HelixToolkit.Nex.Geometries;
using HelixToolkit.Nex.glTF.Internal;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Graphics.Mock;
using HelixToolkit.Nex.Material;
using HelixToolkit.Nex.Repository;
using HelixToolkit.Nex.Shaders;
using Newtonsoft.Json.Linq;
using Gltf = glTFLoader.Schema.Gltf;
using NexImage = HelixToolkit.Nex.Textures.Image;
using Node = HelixToolkit.Nex.Scene.Node;

namespace HelixToolkit.Nex.glTF.Tests.Properties;

// Feature: gltf-light-import-cr-fixes
// Task 4: Supporting exploration checks for the localized (independent) defects that share the
// KHR_lights_punctual import path. These complement Properties 1–3 (the central structural defect,
// cone-angle re-validation, and material-props API). Like the other exploration tests, each check
// encodes the EXPECTED (fixed) behavior and is intended to FAIL on the UNFIXED code — the failure is
// the documented counterexample confirming the defect exists.
//
// Covered here (runtime-observable):
//   * Null config guard (1.2 / 2.2): SceneBuilder constructed with a null config must throw
//     ArgumentNullException("config"). On unfixed code the null is stored, so no exception is thrown.
//   * Unrecognized light kind (1.4 / 2.4): driving AttachLightComponent's switch with a LightKind
//     outside {Directional, Point, Spot} must emit a diagnostic and attach no light. On unfixed code
//     the switch has no default case, so nothing happens and no diagnostic is emitted.
//   * "Infinite range" messaging (1.6 / 2.6): the invalid-range diagnostic text must describe the
//     finite config-default behavior, not "infinite range". On unfixed code the message says
//     "Using infinite range".
//
// The CS1574 compilation defect (1.5 / 2.5) is a compile-time documentation warning, not a runtime
// assertion, so it cannot be expressed as a normally-passing unit test without breaking the build.
// It is documented out-of-band by building the glTF project with documentation generation enabled
// (see the task report). The four <see cref="ParsedLight.InfiniteRange"/> cross-references in
// LightConverter.cs and ParsedLight.cs raise CS1574 on the unfixed code.
//
// DO NOT "fix" these tests or the code when they fail — the failures are the documented counterexamples.

/// <summary>
/// Supporting exploration checks (task 4) for the localized defects of the
/// gltf-light-import-cr-fixes feature. Mirrors the in-memory glTF build harness used by the
/// Properties 1–3 exploration tests (stub managers + an ECS <see cref="World"/>).
/// </summary>
[TestClass]
public class LocalizedDefectExplorationTests
{
    // QuickThrowOnFailure throws (failing the MSTest) when a property is violated, so a surfaced
    // counterexample fails the test instead of only being printed to the console.
    private static readonly Config FsCheckConfig = Config.QuickThrowOnFailure.WithMaxTest(100);

    #region Mock Infrastructure (mirrors LightAttachmentTargetExplorationTests)

    private sealed class StubGeometryManager : IGeometryManager
    {
        public IReadOnlyList<Pool<GeometryResourceType, Geometry>.PoolEntry> Objects =>
            throw new NotImplementedException();
        public int Count => 0;
        public int TotalStaticIndexCount => 0;

        public Handle<GeometryResourceType> Add(Geometry geometry) =>
            Handle<GeometryResourceType>.Null;

        public Task<(bool Success, Handle<GeometryResourceType>)> AddAsync(Geometry geometry) =>
            Task.FromResult((false, Handle<GeometryResourceType>.Null));

        public bool Remove(Geometry geometry) => false;

        public bool UploadStaticMeshIndices(ref SafeWriteContext ctx) => true;

        public void Clear() { }

        public Geometry? GetGeometryById(uint index) => null;

        public Geometry? GetGeometry(Handle<GeometryResourceType> handle) => null;

        public Pool<GeometryResourceType, Geometry>.Enumerator GetEnumerator() =>
            throw new NotImplementedException();

        public int GetDirtyCount() => 0;

        public ResultCode UploadMeshInfoDynamic(ElementBuffer<MeshInfo> buffer) => ResultCode.Ok;

        public void Dispose() { }
    }

    private sealed class StubMaterialPropertyManager : IPBRMaterialPropertyManager
    {
        private readonly PBRMaterialPropertyManager _inner = new();

        public int Count => _inner.Count;

        public PBRMaterialProperties Create(string materialName) => _inner.Create("PBR");

        public PBRMaterialProperties Create(string materialName, ref PBRProperties properties) =>
            _inner.Create("PBR", ref properties);

        public PBRMaterialProperties Create(MaterialTypeId materialTypeId) =>
            _inner.Create(materialTypeId);

        public PBRMaterialProperties Create(
            MaterialTypeId materialTypeId,
            ref PBRProperties properties
        ) => _inner.Create(materialTypeId, ref properties);

        public void Clear() => _inner.Clear();

        public IReadOnlyList<Pool<MaterialPropertyResource, PBRProperties>.PoolEntry> Objects =>
            _inner.Objects;

        public ref PBRProperties At(int index) => ref _inner.At(index);

        public ResultCode UploadDynamic(ElementBuffer<PBRProperties> buffer) => ResultCode.Ok;

        public ResultCode UploadDynamic(
            ElementBuffer<PBRProperties> buffer,
            IEnumerable<uint> indices
        ) => ResultCode.Ok;

        public void Dispose() => _inner.Dispose();
    }

    private sealed class StubTextureRepository : ITextureRepository
    {
        public int Count => 0;

        public TextureRef GetOrCreateFromStream(
            string name,
            Stream stream,
            bool generateMipmaps = true,
            string? debugName = null
        ) => TextureRef.Null;

        public TextureRef GetOrCreateFromFile(
            string filePath,
            bool generateMipmaps = true,
            string? debugName = null
        ) => TextureRef.Null;

        public TextureRef GetOrCreateFromImage(
            string name,
            NexImage image,
            bool generateMipmaps = true
        ) => TextureRef.Null;

        public Task<TextureRef> GetOrCreateFromStreamAsync(
            string name,
            Stream stream,
            bool generateMipmaps = true,
            string? debugName = null
        ) => Task.FromResult(TextureRef.Null);

        public Task<TextureRef> GetOrCreateFromFileAsync(
            string filePath,
            bool generateMipmaps = true,
            string? debugName = null
        ) => Task.FromResult(TextureRef.Null);

        public Task<TextureRef> GetOrCreateFromImageAsync(
            string name,
            NexImage image,
            bool generateMipmaps = true
        ) => Task.FromResult(TextureRef.Null);

        public bool Remove(string key) => false;

        public bool TryGet(string cacheKey, out TextureCacheEntry? entry)
        {
            entry = null;
            return false;
        }

        public void Clear() { }

        public int CleanupExpired() => 0;

        public RepositoryStatistics GetStatistics() =>
            new()
            {
                TotalEntries = 0,
                MaxEntries = 0,
                TotalHits = 0,
                TotalMisses = 0,
            };

        public void Dispose() { }
    }

    private sealed class StubSamplerRepository : ISamplerRepository
    {
        private readonly MockContext _context = new();
        private readonly SamplerRepository _inner;

        public StubSamplerRepository()
        {
            _context.Initialize();
            _inner = new SamplerRepository(_context);
        }

        public int Count => _inner.Count;

        public SamplerRef GetOrCreate(string key, SamplerStateDesc desc) =>
            _inner.GetOrCreate(key, desc);

        public bool Remove(string key) => _inner.Remove(key);

        public bool TryGet(string cacheKey, out SamplerModuleCacheEntry? entry) =>
            _inner.TryGet(cacheKey, out entry);

        public void Clear() => _inner.Clear();

        public int CleanupExpired() => _inner.CleanupExpired();

        public RepositoryStatistics GetStatistics() => _inner.GetStatistics();

        public void Dispose()
        {
            _inner.Dispose();
            _context.Dispose();
        }
    }

    #endregion

    #region Dependency builders

    /// <summary>
    /// Builds the converter dependencies a <see cref="SceneBuilder"/> requires, sharing the supplied
    /// diagnostics list so emitted diagnostics are observable by the test.
    /// </summary>
    private static (
        MeshConverter Mesh,
        MaterialConverter Material,
        LightConverter Light
    ) BuildConverters(World world, List<ImportDiagnostic> diagnostics)
    {
        var model = new Gltf();
        var manifest = new ResourceManifest();

        var accessorReader = new AccessorReader(model, []);
        var geoManager = new StubGeometryManager();
        var meshConverter = new MeshConverter(geoManager, accessorReader, diagnostics, manifest, MeshConverterTestDefaults.Config, MeshConverterTestDefaults.Decoder, false);

        var textureRepo = new StubTextureRepository();
        var samplerRepo = new StubSamplerRepository();
        var textureLoader = new TextureLoader(
            textureRepo,
            samplerRepo,
            "C:\\test",
            model,
            [],
            diagnostics,
            manifest,
            Guid.NewGuid().ToString("D")
        );
        var materialManager = new StubMaterialPropertyManager();
        var materialConverter = new MaterialConverter(
            materialManager,
            textureLoader,
            diagnostics,
            manifest
        );

        var lightConverter = new LightConverter(diagnostics, ImporterConfig.Default);

        return (meshConverter, materialConverter, lightConverter);
    }

    #endregion

    /// <summary>
    /// Supporting check (1.2 / 2.2): constructing <see cref="SceneBuilder"/> with a <c>null</c>
    /// <c>config</c> SHALL throw <see cref="ArgumentNullException"/> for the <c>config</c> parameter.
    ///
    /// On the UNFIXED code the constructor stores the null reference (<c>_config = config;</c>) and
    /// does not throw, so this test FAILS (no exception is observed) — the documented counterexample.
    /// **Validates: Requirements 1.2, 2.2**
    /// </summary>
    [TestMethod]
    public void NullConfig_Constructor_ThrowsArgumentNullExceptionForConfig()
    {
        using var world = World.CreateWorld();
        var diagnostics = new List<ImportDiagnostic>();
        var (mesh, material, light) = BuildConverters(world, diagnostics);

        var ex = Assert.ThrowsException<ArgumentNullException>(() =>
            _ = new SceneBuilder(world, mesh, material, light, diagnostics, config: null!)
        );

        Assert.AreEqual("config", ex.ParamName);
    }

    /// <summary>
    /// Supporting check (1.4 / 2.4): driving <see cref="SceneBuilder"/>'s light-kind switch (in
    /// <c>AttachLightComponent</c>) with a <see cref="LightKind"/> value outside
    /// <c>{Directional, Point, Spot}</c> SHALL emit a diagnostic and attach no light component to the
    /// node's entity.
    ///
    /// The switch is exercised indirectly via reflection because the method is private and the parsed
    /// light kind is never out-of-enum through the public path. On the UNFIXED code the switch has no
    /// <c>default</c> branch, so an unrecognized kind silently no-ops: no diagnostic is emitted, which
    /// fails the "a diagnostic is emitted" expectation below — the documented counterexample.
    /// **Validates: Requirements 1.4, 2.4**
    /// </summary>
    [TestMethod]
    public void UnrecognizedLightKind_EmitsDiagnostic_AndAttachesNoLight()
    {
        using var world = World.CreateWorld();
        var diagnostics = new List<ImportDiagnostic>();
        var (mesh, material, light) = BuildConverters(world, diagnostics);

        var builder = new SceneBuilder(
            world,
            mesh,
            material,
            light,
            diagnostics,
            ImporterConfig.Default
        );

        var attach = typeof(SceneBuilder).GetMethod(
            "AttachLightComponent",
            BindingFlags.NonPublic | BindingFlags.Instance
        );
        Assert.IsNotNull(attach, "AttachLightComponent should exist on SceneBuilder.");

        // A LightKind value outside the defined { Directional=0, Point=1, Spot=2 } set.
        var unrecognizedKind = (LightKind)999;
        var parsedLight = new ParsedLight(
            unrecognizedKind,
            Vector3.One,
            Intensity: 1.0f,
            Range: 10.0f,
            InnerConeAngle: 0.0f,
            OuterConeAngle: MathF.PI / 4.0f
        );

        var node = new Node(world, "UnrecognizedLightNode");
        int diagnosticsBefore = diagnostics.Count;

        // AttachLightComponent(Node node, int nodeIndex, ParsedLight light, int lightIndex).
        // Position/direction are driven by the node transform, so no world matrix is threaded.
        attach!.Invoke(builder, [node, 0, parsedLight, 0]);

        bool diagnosticEmitted = diagnostics.Count > diagnosticsBefore;
        bool noDirectional = !node.Entity.TryGet<DirectionalLightComponent>(out _);
        bool noRange = !node.Entity.TryGet<RangeLightComponent>(out _);

        // Expected (fixed) behavior: a diagnostic is emitted AND no light is attached.
        Assert.IsTrue(
            diagnosticEmitted,
            "An unrecognized LightKind should emit a diagnostic via the switch default case."
        );
        Assert.IsTrue(noDirectional && noRange, "No light component should be attached.");
    }

    #region Infinite-range messaging

    private enum RangedKind
    {
        Point,
        Spot,
    }

    private readonly record struct InvalidRangeCase(RangedKind Kind, float Range);

    private static Gen<InvalidRangeCase> InvalidRangeCaseGen() =>
        from kind in Gen.Elements(RangedKind.Point, RangedKind.Spot)
            // Non-positive range values (<= 0) are invalid per the converter and trigger the
            // invalid-range diagnostic whose wording is under test.
        from milli in Gen.Choose(-100000, 0)
        select new InvalidRangeCase(kind, milli / 1000.0f);

    private static List<ImportDiagnostic> ParseWithInvalidRange(InvalidRangeCase c)
    {
        var lightDefinition = new JObject
        {
            ["type"] = c.Kind == RangedKind.Point ? "point" : "spot",
            ["range"] = c.Range,
        };

        var documentExtension = new JObject { ["lights"] = new JArray { lightDefinition } };
        var model = new Gltf
        {
            Extensions = new Dictionary<string, object>
            {
                [LightConverter.ExtensionName] = documentExtension,
            },
        };

        var diagnostics = new List<ImportDiagnostic>();
        var converter = new LightConverter(diagnostics, ImporterConfig.Default);
        _ = converter.ParseLights(model);
        return diagnostics;
    }

    /// <summary>
    /// Supporting check (1.6 / 2.6): the diagnostic emitted for an invalid <c>range</c> on a
    /// point/spot light SHALL describe the actual finite config-default behavior rather than using
    /// the misleading "infinite range" wording.
    ///
    /// On the UNFIXED code <c>AddInvalidRangeDiagnostic</c> produces "... Using infinite range.",
    /// so the property below is violated and this test FAILS — the documented counterexample.
    /// **Validates: Requirements 1.6, 2.6**
    /// </summary>
    [TestMethod]
    public void InvalidRange_DiagnosticMessage_DoesNotMentionInfiniteRange()
    {
        Prop.ForAll(
                Arb.From(InvalidRangeCaseGen()),
                (InvalidRangeCase c) =>
                {
                    var diagnostics = ParseWithInvalidRange(c);

                    // The invalid range must produce a diagnostic, and its wording must not mention
                    // "infinite range" (the finite config-default behavior is what actually happens).
                    return diagnostics.Count > 0
                        && diagnostics.All(d =>
                            d.Message.IndexOf("infinite range", StringComparison.OrdinalIgnoreCase)
                            < 0
                        );
                }
            )
            .Check(FsCheckConfig);
    }

    #endregion
}
