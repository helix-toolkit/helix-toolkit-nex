using HelixToolkit.Nex.glTF.Internal;
using HelixToolkit.Nex.glTF.Tests.Mocks;
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
// This test exercises the fixed two-argument API:
// CreateMaterialProps(PBRShadingMode shadingMode, string materialName).
// On earlier versions (single-argument API), the material's display Name was forced to equal the
// shading-mode identifier, which violates the property being asserted below.
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
        var samplerRepo = new StubSamplerRepository(StubSamplerRepositoryMode.MockContextBacked);
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
