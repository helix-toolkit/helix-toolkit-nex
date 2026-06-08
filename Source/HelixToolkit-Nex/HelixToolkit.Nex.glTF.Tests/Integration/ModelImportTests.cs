using glTFLoader;
using HelixToolkit.Nex.DependencyInjection;
using HelixToolkit.Nex.Engine;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Graphics.Mock;
using HelixToolkit.Nex.Material;
using HelixToolkit.Nex.Rendering;
using HelixToolkit.Nex.Scene;

namespace HelixToolkit.Nex.glTF.Tests.Integration;

/// <summary>
/// Integration tests that import real glTF/GLB model files bundled in the test project.
/// Validates the full import pipeline against production-quality assets.
/// </summary>
[TestClass]
public class ModelImportTests
{
    private Importer _importer = null!;

    [ClassInitialize]
    public static void ClassSetup(TestContext _)
    {
        // Register all material names from the test model files so the importer can create them.
        // This must happen once before any test runs since PBRMaterialTypeRegistry is static.
        var modelFiles = new[]
        {
            GetAssetPath("BrainStem", "glTF", "BrainStem.gltf"),
            GetAssetPath("DamagedHelmet", "glTF", "DamagedHelmet.gltf"),
            GetAssetPath("Earth", "scene.gltf"),
        };

        foreach (var file in modelFiles)
        {
            if (File.Exists(file))
            {
                RegisterMaterialNamesFromFile(file);
            }
        }
    }

    [TestInitialize]
    public void Setup()
    {
        _importer = new Importer();
    }

    #region Helpers

    private static string GetAssetPath(params string[] segments)
    {
        var parts = new[] { AppContext.BaseDirectory, "Assets", "Models" };
        return Path.Combine(parts.Concat(segments).ToArray());
    }

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
    /// Registers all material names found in a glTF/GLB file so the importer can create them.
    /// Uses the default PBR shader implementation for all registered types.
    /// Skips names that are already registered.
    /// </summary>
    private static void RegisterMaterialNamesFromFile(string filePath)
    {
        var model = Interface.LoadModel(filePath);
        if (model.Materials == null)
            return;

        const string defaultPbrImpl = """
            PBRMaterial material = createPBRMaterial();
            vec4 color = forwardPlusLighting(material);
            color.rgb += material.emissive;
            return color;
            """;

        for (int i = 0; i < model.Materials.Length; i++)
        {
            var name = model.Materials[i].Name ?? $"Material_{i}";
            if (!PBRMaterialTypeRegistry.TryGetByName(name, out _))
            {
                PBRMaterialTypeRegistry.Register(name, defaultPbrImpl);
            }
        }
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

    private static void AssertImportSuccessWithMeshes(ImportResult result, string modelName)
    {
        Assert.IsTrue(
            result.Success,
            $"{modelName}: Import should succeed. Diagnostics: {FormatDiagnostics(result)}"
        );
        Assert.IsNotNull(result.RootNode, $"{modelName}: RootNode should not be null");

        // No error-level diagnostics (warnings are acceptable for texture loading with mock context)
        var errors = result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.AreEqual(
            0,
            errors.Count,
            $"{modelName}: Expected no error diagnostics but got: {string.Join("; ", errors.Select(e => e.Message))}"
        );

        // At least 1 mesh node
        var meshNodes = CollectMeshNodes(result.RootNode);
        Assert.IsTrue(
            meshNodes.Count >= 1,
            $"{modelName}: Expected at least 1 mesh node but found {meshNodes.Count}"
        );
    }

    private static string FormatDiagnostics(ImportResult result)
    {
        return string.Join(
            "; ",
            result.Diagnostics.Select(d =>
                $"[{d.Severity}] {d.ElementType}[{d.ElementIndex}]: {d.Message}"
            )
        );
    }

    #endregion

    #region BrainStem glTF (external .bin)

    [TestMethod]
    public void Import_BrainStem_GlTF_Succeeds()
    {
        // Arrange
        var path = GetAssetPath("BrainStem", "glTF", "BrainStem.gltf");
        Assert.IsTrue(File.Exists(path), $"Model file not found: {path}");
        using var worldData = CreateTestWorldDataProvider();

        // Act
        var result = _importer.Import(path, worldData);

        // Assert
        AssertImportSuccessWithMeshes(result, "BrainStem glTF");
    }

    #endregion

    #region BrainStem glTF-Embedded (base64)

    [TestMethod]
    public void Import_BrainStem_GlTFEmbedded_Succeeds()
    {
        // Arrange
        var path = GetAssetPath("BrainStem", "glTF-Embedded", "BrainStem.gltf");
        Assert.IsTrue(File.Exists(path), $"Model file not found: {path}");
        using var worldData = CreateTestWorldDataProvider();

        // Act
        var result = _importer.Import(path, worldData);

        // Assert
        AssertImportSuccessWithMeshes(result, "BrainStem glTF-Embedded");
    }

    #endregion

    #region BrainStem GLB (binary)

    [TestMethod]
    public void Import_BrainStem_GLB_Succeeds()
    {
        // Arrange
        var path = GetAssetPath("BrainStem", "glTF-Binary", "BrainStem.glb");
        Assert.IsTrue(File.Exists(path), $"Model file not found: {path}");
        using var worldData = CreateTestWorldDataProvider();

        // Act
        var result = _importer.Import(path, worldData);

        // Assert
        AssertImportSuccessWithMeshes(result, "BrainStem GLB");
    }

    [TestMethod]
    public async Task ImportAsync_BrainStem_GLB_Succeeds()
    {
        // Arrange
        var path = GetAssetPath("BrainStem", "glTF-Binary", "BrainStem.glb");
        Assert.IsTrue(File.Exists(path), $"Model file not found: {path}");
        using var worldData = CreateTestWorldDataProvider();

        // Act
        var result = await _importer.ImportAsync(path, worldData);

        // Assert
        AssertImportSuccessWithMeshes(result, "BrainStem GLB (async)");
    }

    #endregion

    #region DamagedHelmet glTF (external .bin + textures)

    [TestMethod]
    public void Import_DamagedHelmet_GlTF_Succeeds()
    {
        // Arrange
        var path = GetAssetPath("DamagedHelmet", "glTF", "DamagedHelmet.gltf");
        Assert.IsTrue(File.Exists(path), $"Model file not found: {path}");
        using var worldData = CreateTestWorldDataProvider();

        // Act
        var result = _importer.Import(path, worldData);

        // Assert
        AssertImportSuccessWithMeshes(result, "DamagedHelmet glTF");
    }

    #endregion

    #region DamagedHelmet GLB (binary)

    [TestMethod]
    public void Import_DamagedHelmet_GLB_Succeeds()
    {
        // Arrange
        var path = GetAssetPath("DamagedHelmet", "glTF-Binary", "DamagedHelmet.glb");
        Assert.IsTrue(File.Exists(path), $"Model file not found: {path}");
        using var worldData = CreateTestWorldDataProvider();

        // Act
        var result = _importer.Import(path, worldData);

        // Assert
        AssertImportSuccessWithMeshes(result, "DamagedHelmet GLB");
    }

    [TestMethod]
    public async Task ImportAsync_DamagedHelmet_GLB_Succeeds()
    {
        // Arrange
        var path = GetAssetPath("DamagedHelmet", "glTF-Binary", "DamagedHelmet.glb");
        Assert.IsTrue(File.Exists(path), $"Model file not found: {path}");
        using var worldData = CreateTestWorldDataProvider();

        // Act
        var result = await _importer.ImportAsync(path, worldData);

        // Assert
        AssertImportSuccessWithMeshes(result, "DamagedHelmet GLB (async)");
    }

    #endregion

    #region Earth scene (external .bin + textures in subfolder)

    [TestMethod]
    public void Import_Earth_GlTF_Succeeds()
    {
        // Arrange
        var path = GetAssetPath("Earth", "scene.gltf");
        Assert.IsTrue(File.Exists(path), $"Model file not found: {path}");
        using var worldData = CreateTestWorldDataProvider();

        // Act
        var result = _importer.Import(path, worldData);

        // Assert
        AssertImportSuccessWithMeshes(result, "Earth scene");
    }

    #endregion

    #region Format Equivalence: BrainStem glTF vs GLB produce same node count

    [TestMethod]
    public void Import_BrainStem_GlTFAndGLB_ProduceSameNodeCount()
    {
        // Arrange
        var gltfPath = GetAssetPath("BrainStem", "glTF", "BrainStem.gltf");
        var glbPath = GetAssetPath("BrainStem", "glTF-Binary", "BrainStem.glb");
        Assert.IsTrue(File.Exists(gltfPath), $"glTF file not found: {gltfPath}");
        Assert.IsTrue(File.Exists(glbPath), $"GLB file not found: {glbPath}");

        using var gltfWorldData = CreateTestWorldDataProvider();
        using var glbWorldData = CreateTestWorldDataProvider();

        // Act
        var gltfResult = _importer.Import(gltfPath, gltfWorldData);
        var glbResult = _importer.Import(glbPath, glbWorldData);

        // Assert: both succeed
        Assert.IsTrue(gltfResult.Success, $"glTF import failed: {FormatDiagnostics(gltfResult)}");
        Assert.IsTrue(glbResult.Success, $"GLB import failed: {FormatDiagnostics(glbResult)}");

        // Assert: same total node count
        int gltfNodeCount = CountAllNodes(gltfResult.RootNode);
        int glbNodeCount = CountAllNodes(glbResult.RootNode);
        Assert.AreEqual(
            gltfNodeCount,
            glbNodeCount,
            $"BrainStem glTF ({gltfNodeCount} nodes) and GLB ({glbNodeCount} nodes) should produce the same node count"
        );

        // Assert: same mesh node count
        var gltfMeshNodes = CollectMeshNodes(gltfResult.RootNode);
        var glbMeshNodes = CollectMeshNodes(glbResult.RootNode);
        Assert.AreEqual(
            gltfMeshNodes.Count,
            glbMeshNodes.Count,
            $"BrainStem glTF ({gltfMeshNodes.Count} meshes) and GLB ({glbMeshNodes.Count} meshes) should produce the same mesh count"
        );
    }

    #endregion
}
