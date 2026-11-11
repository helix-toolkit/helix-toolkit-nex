using HelixToolkit.Nex.Rendering.Components;
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
    [DataRow("AnimatedMorphCube", 1, false)]
    [DataRow("SimpleMorph", 1, false)]
    [DataRow("MorphStressTest", 3, false)]
    public void Import_AnimatedSample_PlaysAllClips(string model, int clipCount, bool skinned)
    {
        var path = GetSamplePath(model);
        RegisterMaterialNamesFromFile(path);
        using var worldData = CreateTestWorldDataProvider();

        var result = new Importer().Import(path, worldData);

        Assert.IsTrue(result.Success, FormatDiagnostics(result));
        Assert.AreEqual(clipCount, result.Animations.Count, FormatDiagnostics(result));

        var meshNodes = CollectMeshNodes(result.RootNode).ToList();
        var skinnings = meshNodes.Select(m => m.Skinning).OfType<Rendering.Skinning>().ToList();
        Assert.AreEqual(skinned, skinnings.Count > 0);
        var deformedCount = meshNodes.Count(m => m.Skinning is not null || m.Morphing is not null);

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
                    if (channel.Path == AnimationPath.Weights)
                    {
                        Assert.IsNotNull(channel.Weights, $"{clip.Name}: unbound weight channel");
                        continue;
                    }
                    var world = channel.Target!.WorldTransform.Value;
                    Assert.IsTrue(
                        float.IsFinite(world.M11 + world.M22 + world.M33 + world.M41 + world.M42 + world.M43),
                        $"{clip.Name}: non-finite transform on {channel.Target}"
                    );
                }
            }
            Assert.AreEqual(
                deformedCount,
                worldData.SkinningData!.Dispatches.Length,
                "Every skinned or morphed mesh is dispatched while animating."
            );
            player.Stop();
        }
    }

    /// <summary>
    /// MorphStressTest: 8 morph targets (position and normal deltas) on two primitives sharing the
    /// mesh weights, animated by three weight clips.
    /// </summary>
    [TestMethod]
    public void Import_MorphStressTest_BindsSharedWeightsAndAnimatesThem()
    {
        var path = GetSamplePath("MorphStressTest");
        RegisterMaterialNamesFromFile(path);
        using var worldData = CreateTestWorldDataProvider();

        var result = new Importer().Import(path, worldData);

        Assert.IsTrue(result.Success, FormatDiagnostics(result));
        var morphed = CollectMeshNodes(result.RootNode).Where(m => m.Morphing is not null).ToList();
        Assert.AreEqual(2, morphed.Count, "Both primitives are morphed.");
        var weights = morphed[0].Morphing!.Weights;
        Assert.AreSame(weights, morphed[1].Morphing!.Weights, "Primitives share the mesh weights.");
        Assert.AreEqual(8, weights.Count);
        foreach (var node in morphed)
        {
            var geometry = node.Geometry!;
            Assert.AreEqual(8, geometry.MorphTargets.Count);
            Assert.IsTrue(geometry.MorphTargets.All(t => t.PositionDeltas.Count == geometry.Vertices.Count));
            Assert.IsTrue(geometry.MorphTargets.All(t => t.NormalDeltas.Count == geometry.Vertices.Count));
            Assert.IsFalse(node.Entity.Get<MeshDrawInfo>().IsCullable, "Morphed meshes are not culled.");
            Assert.IsTrue(geometry.HasGeneratedTangents, "No TANGENT attribute: tangents are generated.");
        }
        Assert.AreEqual(2, result.Resources.Morphings.Count);

        foreach (var clip in result.Animations)
        {
            // 8 weights are animated by two channels of four weights each.
            Assert.AreEqual(2, clip.Channels.Count, clip.ToString());
            Assert.IsTrue(clip.Channels.All(c => ReferenceEquals(c.Weights, weights)), clip.ToString());
        }

        Assert.IsTrue(worldData.Update());
        Assert.AreEqual(2, worldData.SkinningData!.Dispatches.Length, "Morph-only meshes are dispatched.");
        Assert.IsTrue(morphed.All(m => m.Morphing!.MorphedVertexBuffer.Valid));

        // Playing a clip changes the weights; stopping restores the initial weights.
        var rest = weights.Values.ToArray();
        var player = new AnimationPlayer(result.Animations[0]);
        player.Play();
        var changed = false;
        for (int frame = 0; frame < 10 && !changed; frame++)
        {
            player.Update(result.Animations[0].Duration / 10f);
            changed = !weights.Values.SequenceEqual(rest);
        }
        Assert.IsTrue(changed, "The clip animates the weights.");
        var version = weights.Version;
        Assert.IsTrue(worldData.Update());
        Assert.IsTrue(morphed[0].Morphing!.ActiveWeightCount > 0);
        Assert.AreEqual(version, weights.Version);

        player.Stop();
        CollectionAssert.AreEqual(rest, weights.Values.ToArray());
    }

    /// <summary>
    /// SimpleMorph has no NORMAL attribute: flat normals unweld the vertices, which must expand the
    /// morph target deltas per triangle corner as well.
    /// </summary>
    [TestMethod]
    public void Import_SimpleMorph_UnweldsMorphTargetsWithFlatNormals()
    {
        var path = GetSamplePath("SimpleMorph");
        RegisterMaterialNamesFromFile(path);
        using var worldData = CreateTestWorldDataProvider();

        var result = new Importer().Import(path, worldData);

        Assert.IsTrue(result.Success, FormatDiagnostics(result));
        var node = CollectMeshNodes(result.RootNode).Single(m => m.Morphing is not null);
        var geometry = node.Geometry!;
        Assert.AreEqual(2, geometry.MorphTargets.Count);
        Assert.IsTrue(geometry.MorphTargets.All(t => t.PositionDeltas.Count == geometry.Vertices.Count));
        CollectionAssert.AreEqual(new[] { 0.5f, 0.5f }, node.Morphing!.Weights.Values.ToArray());
    }

    private static string GetSamplePath(string model)
    {
        // A rooted model name would make Path.Combine silently drop the assets root.
        if (string.IsNullOrWhiteSpace(model) || Path.IsPathRooted(model))
        {
            Assert.Inconclusive($"Invalid sample model name: '{model}'.");
        }
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
        return path;
    }
}
