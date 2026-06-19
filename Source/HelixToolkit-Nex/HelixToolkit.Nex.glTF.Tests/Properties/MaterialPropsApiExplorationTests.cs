using HelixToolkit.Nex.glTF.Internal;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Graphics.Mock;
using HelixToolkit.Nex.Material;
using HelixToolkit.Nex.Repository;
using HelixToolkit.Nex.Shaders.Frag;
using Gltf = glTFLoader.Schema.Gltf;
using NexImage = HelixToolkit.Nex.Textures.Image;

namespace HelixToolkit.Nex.glTF.Tests.Properties;

// Feature: gltf-light-import-cr-fixes
// Property 3 (Bug Condition): Material props API separates shading mode from display name.
//
// EXPLORATION TEST — this test encodes the EXPECTED (fixed) behavior and is intended to FAIL on the
// UNFIXED code. The current MaterialConverter.CreateMaterialProps takes a SINGLE string parameter
// that is used BOTH as the shading-mode / material-type identifier (passed to
// IPBRMaterialPropertyManager.Create(materialName)) AND as the display Name (material.Name =
// materialName). A caller therefore cannot choose a shading mode and a human-readable display name
// independently: the resulting material's Name is forced to equal the shading-mode identifier.
//
// To keep the whole test PROJECT compiling (the fixed two-arg overload
// CreateMaterialProps(PBRShadingMode, string) does not exist yet, so calling it would be a compile
// error that breaks every other test), this test exercises the CURRENT single-arg API. It asks for
// a material whose shading mode is derived from `shadingMode` (so it passes the shading-mode
// identifier `shadingMode.ToString()`, the only value that yields the correct shading mode) and
// then asserts that the display Name independently equals `materialName`. On the unfixed code the
// Name is conflated with the shading-mode identifier (Name == shadingMode.ToString() != materialName),
// so the property is violated and the test FAILS.
//
// After the fix (tasks 7.8 / 7.13), CreateMaterialProps(PBRShadingMode shadingMode, string
// materialName) sets the shading mode from `shadingMode` and Name from `materialName` independently,
// and this property holds.
//
// DO NOT "fix" this test or the code when it fails — the failure is the documented counterexample.

/// <summary>
/// Property-based exploration test for Property 3 of the gltf-light-import-cr-fixes feature.
/// For any <see cref="PBRShadingMode"/> and any display name, creating material properties SHALL
/// produce a material whose shading mode is derived from the shading mode and whose display
/// <see cref="PBRMaterialProperties.Name"/> equals the display name, independently of one another.
///
/// The current single-arg <see cref="MaterialConverter.CreateMaterialProps(string)"/> conflates the
/// two: the single string is used both as the shading-mode identifier and as the display name, so a
/// distinct display name cannot be expressed and this property is violated.
/// </summary>
[TestClass]
public class MaterialPropsApiExplorationTests
{
    // QuickThrowOnFailure throws (failing the MSTest) when the property is violated, so a surfaced
    // counterexample fails the test instead of only being printed to the console.
    private static readonly Config FsCheckConfig = Config.QuickThrowOnFailure.WithMaxTest(100);

    #region Mock Infrastructure

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

    #region Case model + generators

    private readonly record struct MaterialPropsCase(
        PBRShadingMode ShadingMode,
        string MaterialName
    );

    // Only the shading modes that are registered as built-in material types (PBRShadingMode.None is
    // not registered, so it cannot be created via the manager).
    private static Gen<PBRShadingMode> ShadingModeGen() =>
        Gen.Elements(
            PBRShadingMode.PBR,
            PBRShadingMode.Unlit,
            PBRShadingMode.DebugTileLightCount,
            PBRShadingMode.Normal,
            PBRShadingMode.Flat,
            PBRShadingMode.CAD,
            PBRShadingMode.CADFlat
        );

    // Display names that are deliberately distinct from any shading-mode identifier (e.g. the
    // point-light mesh name the importer wants to use), so the conflation defect is observable.
    private static Gen<string> DisplayNameGen() =>
        from index in Gen.Choose(0, 100000)
        select $"PointLightMesh_{index}";

    private static Gen<MaterialPropsCase> MaterialPropsCaseGen() =>
        from shadingMode in ShadingModeGen()
        from materialName in DisplayNameGen()
        select new MaterialPropsCase(shadingMode, materialName);

    #endregion

    #region Helpers

    private static MaterialConverter CreateConverter()
    {
        var diagnostics = new List<ImportDiagnostic>();
        var manifest = new ResourceManifest();

        // Use the REAL material property manager so the shading mode is genuinely derived from the
        // registered material-type name (the stub used elsewhere always returns "PBR").
        var materialManager = new PBRMaterialPropertyManager();

        var textureRepo = new StubTextureRepository();
        var samplerRepo = new StubSamplerRepository();
        var textureLoader = new TextureLoader(
            textureRepo,
            samplerRepo,
            "C:\\test",
            new Gltf(),
            [],
            diagnostics,
            manifest,
            Guid.NewGuid().ToString("D")
        );

        return new MaterialConverter(materialManager, textureLoader, diagnostics, manifest);
    }

    #endregion

    /// <summary>
    /// Property 3 (Bug Condition): the created material's shading mode is derived from
    /// <c>shadingMode</c> and its display <c>Name</c> equals <c>materialName</c>, independently.
    /// **Validates: Requirements 1.9, 2.9**
    /// </summary>
    [TestMethod]
    public void CreateMaterialProps_SeparatesShadingModeFromDisplayName()
    {
        Prop.ForAll(
                Arb.From(MaterialPropsCaseGen()),
                (MaterialPropsCase c) =>
                {
                    var converter = CreateConverter();

                    // The fixed two-arg API sets the shading mode from c.ShadingMode and the
                    // display name from c.MaterialName independently.
                    var material = converter.CreateMaterialProps(c.ShadingMode, c.MaterialName);

                    // Shading mode must be derived from the requested shading mode.
                    bool shadingModeMatches =
                        material.MaterialTypeId == (MaterialTypeId)c.ShadingMode;

                    // Display name must independently equal the requested display name.
                    bool nameMatches = material.Name == c.MaterialName;

                    return shadingModeMatches && nameMatches;
                }
            )
            .Check(FsCheckConfig);
    }
}
