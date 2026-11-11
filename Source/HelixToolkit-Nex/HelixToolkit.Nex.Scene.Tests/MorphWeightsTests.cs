using System.Numerics;
using HelixToolkit.Nex.Scene;

namespace HelixToolkit.Nex.Tests.Scene;

[TestClass]
public sealed class MorphWeightsTests
{
    [TestMethod]
    public void Version_ChangesOnlyWhenAWeightChanges()
    {
        var weights = new MorphWeights([0.5f, 0f]);
        var version = weights.Version;

        weights[0] = 0.5f;
        Assert.AreEqual(version, weights.Version);

        weights[1] = 1f;
        Assert.AreNotEqual(version, weights.Version);
        CollectionAssert.AreEqual(new[] { 0.5f, 1f }, weights.Values.ToArray());
    }

    [TestMethod]
    public void SetVector_IgnoresComponentsPastTheLastWeight()
    {
        var weights = new MorphWeights(6);

        weights.Set(4, new Vector4(1, 2, 3, 4));

        CollectionAssert.AreEqual(new[] { 0f, 0f, 0f, 0f, 1f, 2f }, weights.Values.ToArray());
    }

    [TestMethod]
    public void WeightChannel_WritesFourWeightsAtItsOffset()
    {
        var weights = new MorphWeights(5);
        var sampler = new AnimationSampler(
            [0f, 1f],
            [Vector4.Zero, new Vector4(1, 2, 3, 4)],
            AnimationInterpolation.Linear
        );
        var channel = new AnimationChannel(weights, 4, sampler);

        Assert.AreEqual(AnimationPath.Weights, channel.Path);
        Assert.IsFalse(channel.IsTransform);
        Assert.IsTrue(channel.Apply(0.5f));

        CollectionAssert.AreEqual(new[] { 0f, 0f, 0f, 0f, 0.5f }, weights.Values.ToArray());
    }

    [TestMethod]
    public void WeightChannel_WithoutWeights_IsSkipped()
    {
        var sampler = new AnimationSampler([0f], [Vector4.One], AnimationInterpolation.Step);
        var channel = new AnimationChannel((MorphWeights?)null, 0, sampler);

        Assert.IsFalse(channel.Apply(0f));
        // A clip with an unbound weight channel must not touch a null target.
        new AnimationClip("Unbound", [channel]).Apply(0f);
    }

    [TestMethod]
    public void Player_RestoresWeightsOnStopAndClipChange()
    {
        var weights = new MorphWeights([0.25f, 0.75f]);
        var sampler = new AnimationSampler(
            [0f, 1f],
            [new Vector4(1, 1, 0, 0), new Vector4(0, 1, 0, 0)],
            AnimationInterpolation.Linear
        );
        var clip = new AnimationClip("Weights", [new AnimationChannel(weights, 0, sampler)]);
        var player = new AnimationPlayer(clip);

        player.Play();
        player.Update(0.5f);
        CollectionAssert.AreEqual(new[] { 0.5f, 1f }, weights.Values.ToArray());

        player.Stop();
        CollectionAssert.AreEqual(new[] { 0.25f, 0.75f }, weights.Values.ToArray());

        player.Play();
        player.Update(0.5f);
        player.Clip = null;
        CollectionAssert.AreEqual(new[] { 0.25f, 0.75f }, weights.Values.ToArray());
    }
}
