using HelixToolkit.Nex.Scene;
using static HelixToolkit.Nex.glTF.Tests.Integration.ModelImportTests;

namespace HelixToolkit.Nex.glTF.Tests.Integration;

/// <summary>
/// Imports and plays the animated models of the Khronos glTF-Sample-Assets repository.
/// </summary>
/// <remarks>
/// The sample assets are not part of this repository. Set the <c>HELIX_GLTF_SAMPLE_ASSETS</c>
/// environment variable to the <c>Models</c> folder of a glTF-Sample-Assets checkout to run these
/// tests; otherwise they are reported as inconclusive.
/// </remarks>
[TestClass]
public class SampleAssetAnimationTests
{
    private const string AssetsVariable = "HELIX_GLTF_SAMPLE_ASSETS";

    [TestMethod]
    [DataRow("AnimatedCube", 1, false)]
    [DataRow("AnimatedTriangle", 1, false)]
    [DataRow("BoxAnimated", 1, false)]
    [DataRow("InterpolationTest", 9, false)]
    [DataRow("SimpleSkin", 1, true)]
    [DataRow("RiggedSimple", 1, true)]
    [DataRow("RiggedFigure", 1, true)]
    [DataRow("CesiumMan", 1, true)]
    [DataRow("BrainStem", 1, true)]
    [DataRow("Fox", 3, true)]
    [DataRow("AnimatedMorphCube", 0, false)]
    public void Import_AnimatedSample_PlaysAllClips(string model, int clipCount, bool skinned)
    {
        var root = Environment.GetEnvironmentVariable(AssetsVariable);
        if (string.IsNullOrEmpty(root))
        {
            Assert.Inconclusive($"Set {AssetsVariable} to the glTF-Sample-Assets Models folder.");
        }
        var path = Path.Combine(root, model, "glTF", model + ".gltf");
        if (!File.Exists(path))
        {
            Assert.Inconclusive($"Sample model not found: {path}");
        }
        RegisterMaterialNamesFromFile(path);
        using var worldData = CreateTestWorldDataProvider();

        var result = new Importer().Import(path, worldData);

        Assert.IsTrue(result.Success, FormatDiagnostics(result));
        Assert.AreEqual(clipCount, result.Animations.Count, FormatDiagnostics(result));
        if (clipCount == 0)
        {
            Assert.IsTrue(
                result.Diagnostics.Any(d => d.Message.Contains("morph target weights")),
                FormatDiagnostics(result)
            );
            return;
        }

        var skinnings = CollectMeshNodes(result.RootNode)
            .Select(m => m.Skinning)
            .OfType<Rendering.Skinning>()
            .ToList();
        Assert.AreEqual(skinned, skinnings.Count > 0);

        var player = new AnimationPlayer();
        foreach (var clip in result.Animations)
        {
            Assert.IsTrue(clip.Duration > 0f, clip.ToString());
            Assert.IsTrue(clip.Channels.All(c => c.Target is { Alive: true }), clip.ToString());

            player.Clip = clip;
            player.Play();
            for (int frame = 0; frame < 12; frame++)
            {
                player.Update(clip.Duration / 10f);
                Assert.IsTrue(worldData.Update());
                foreach (var channel in clip.Channels)
                {
                    var world = channel.Target!.WorldTransform.Value;
                    Assert.IsTrue(
                        float.IsFinite(world.M11 + world.M22 + world.M33 + world.M41 + world.M42 + world.M43),
                        $"{clip.Name}: non-finite transform on {channel.Target}"
                    );
                }
            }
            if (skinned)
            {
                Assert.AreEqual(
                    skinnings.Count,
                    worldData.SkinningData!.Dispatches.Length,
                    "Every skinned mesh is dispatched while animating."
                );
            }
            player.Stop();
        }
    }
}
