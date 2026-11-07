using System.Numerics;
using HelixToolkit.Nex.Scene;

namespace HelixToolkit.Nex.Tests.Scene;

[TestClass]
public sealed class AnimationSamplerTests
{
    private static Vector4 V(float x, float y = 0, float z = 0) => new(x, y, z, 0);

    private static Vector4 Q(Quaternion q) => new(q.X, q.Y, q.Z, q.W);

    [TestMethod]
    public void Linear_InterpolatesAndClamps()
    {
        var sampler = new AnimationSampler([1f, 3f], [V(0), V(10)], AnimationInterpolation.Linear);

        Assert.AreEqual(V(0), sampler.Sample(0f));
        Assert.AreEqual(V(0), sampler.Sample(1f));
        Assert.AreEqual(V(5), sampler.Sample(2f));
        Assert.AreEqual(V(10), sampler.Sample(3f));
        Assert.AreEqual(V(10), sampler.Sample(9f));
    }

    [TestMethod]
    public void Constructor_CopiesArrays()
    {
        float[] times = [1f, 3f];
        Vector4[] values = [V(0), V(10)];
        var sampler = new AnimationSampler(times, values, AnimationInterpolation.Linear);

        times[1] = 0f;
        values[1] = V(100);

        Assert.AreEqual(3f, sampler.EndTime);
        Assert.AreEqual(V(5), sampler.Sample(2f));
    }

    [TestMethod]
    public void Linear_MultipleKeys_FindsSegment()
    {
        var sampler = new AnimationSampler(
            [0f, 1f, 2f, 4f],
            [V(0), V(1), V(3), V(7)],
            AnimationInterpolation.Linear
        );

        Assert.AreEqual(V(0.5f), sampler.Sample(0.5f));
        Assert.AreEqual(V(1), sampler.Sample(1f));
        Assert.AreEqual(V(2), sampler.Sample(1.5f));
        Assert.AreEqual(V(5), sampler.Sample(3f));
    }

    [TestMethod]
    public void Step_HoldsPreviousKey()
    {
        var sampler = new AnimationSampler(
            [0f, 1f, 2f],
            [V(0), V(1), V(2)],
            AnimationInterpolation.Step
        );

        Assert.AreEqual(V(0), sampler.Sample(0.99f));
        Assert.AreEqual(V(1), sampler.Sample(1f));
        Assert.AreEqual(V(1), sampler.Sample(1.99f));
        Assert.AreEqual(V(2), sampler.Sample(2f));
    }

    [TestMethod]
    public void CubicSpline_MatchesHermite()
    {
        // Key 0: value 0, out-tangent 1. Key 1: value 1, in-tangent 1. Over one second this is
        // the straight line p(s) = s.
        var sampler = new AnimationSampler(
            [0f, 1f],
            [V(0), V(0), V(1), V(1), V(1), V(0)],
            AnimationInterpolation.CubicSpline
        );

        Assert.AreEqual(0.25f, sampler.Sample(0.25f).X, 1e-6f);
        Assert.AreEqual(0.5f, sampler.Sample(0.5f).X, 1e-6f);
        Assert.AreEqual(1f, sampler.Sample(1f).X, 1e-6f);
    }

    [TestMethod]
    public void CubicSpline_ZeroTangents_EasesInAndOut()
    {
        var sampler = new AnimationSampler(
            [0f, 2f],
            [V(0), V(0), V(0), V(0), V(4), V(0)],
            AnimationInterpolation.CubicSpline
        );

        // p(s) = 4 * (3s^2 - 2s^3); s = 0.25 -> 0.625.
        Assert.AreEqual(0.625f, sampler.Sample(0.5f).X, 1e-5f);
        Assert.AreEqual(2f, sampler.Sample(1f).X, 1e-5f);
    }

    [TestMethod]
    public void Rotation_Linear_UsesSlerp()
    {
        var q0 = Quaternion.Identity;
        var q1 = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2);
        var sampler = new AnimationSampler([0f, 1f], [Q(q0), Q(q1)], AnimationInterpolation.Linear);

        var q = sampler.SampleRotation(0.5f);

        var expected = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 4);
        Assert.AreEqual(1f, MathF.Abs(Quaternion.Dot(expected, q)), 1e-5f);
    }

    [TestMethod]
    public void Rotation_IsNormalized()
    {
        var sampler = new AnimationSampler(
            [0f],
            [new Vector4(0, 0, 0, 2)],
            AnimationInterpolation.Linear
        );

        Assert.AreEqual(Quaternion.Identity, sampler.SampleRotation(0f));
    }

    [TestMethod]
    public void DuplicateTimes_DoNotDivideByZero()
    {
        var sampler = new AnimationSampler(
            [0f, 1f, 1f, 2f],
            [V(0), V(1), V(5), V(6)],
            AnimationInterpolation.Linear
        );

        Assert.AreEqual(V(0.5f), sampler.Sample(0.5f));
        Assert.AreEqual(V(5), sampler.Sample(1f));
        Assert.AreEqual(V(5.5f), sampler.Sample(1.5f));
    }

    [TestMethod]
    public void Constructor_ValidatesInput()
    {
        Assert.ThrowsException<ArgumentException>(() =>
            new AnimationSampler([], [], AnimationInterpolation.Linear)
        );
        Assert.ThrowsException<ArgumentException>(() =>
            new AnimationSampler([0f, 1f], [V(0)], AnimationInterpolation.Linear)
        );
        Assert.ThrowsException<ArgumentException>(() =>
            new AnimationSampler([0f, 1f], [V(0), V(1)], AnimationInterpolation.CubicSpline)
        );
        Assert.ThrowsException<ArgumentException>(() =>
            new AnimationSampler([1f, 0f], [V(0), V(1)], AnimationInterpolation.Linear)
        );
    }
}

[TestClass]
public sealed class AnimationPlayerTests
{
    private World _world = null!;

    [TestInitialize]
    public void Setup()
    {
        _world = World.CreateWorld();
    }

    [TestCleanup]
    public void Cleanup()
    {
        _world.Dispose();
    }

    /// <summary>
    /// A clip translating <paramref name="node"/> along X from 0 to 10 over [start, start + 1].
    /// </summary>
    private static AnimationClip CreateClip(Node node, float start = 0f, string name = "Move")
    {
        var sampler = new AnimationSampler(
            [start, start + 1f],
            [Vector4.Zero, new Vector4(10, 0, 0, 0)],
            AnimationInterpolation.Linear
        );
        return new AnimationClip(name, [new AnimationChannel(node, AnimationPath.Translation, sampler)]);
    }

    [TestMethod]
    public void Clip_ComputesTimeRange()
    {
        var node = new Node(_world);
        var late = new AnimationSampler([0.5f, 3f], [Vector4.Zero, Vector4.One], AnimationInterpolation.Linear);
        var clip = new AnimationClip(
            "Clip",
            [
                new AnimationChannel(node, AnimationPath.Translation, CreateClip(node, 1f).Channels[0].Sampler),
                new AnimationChannel(node, AnimationPath.Scale, late),
            ]
        );

        Assert.AreEqual(0.5f, clip.StartTime);
        Assert.AreEqual(3f, clip.EndTime);
        Assert.AreEqual(2.5f, clip.Duration);
    }

    [TestMethod]
    public void Clip_Apply_WritesTransforms()
    {
        var node = new Node(_world);
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 1f);
        var clip = new AnimationClip(
            "Clip",
            [
                new AnimationChannel(
                    node,
                    AnimationPath.Rotation,
                    new AnimationSampler(
                        [0f],
                        [new Vector4(rotation.X, rotation.Y, rotation.Z, rotation.W)],
                        AnimationInterpolation.Step
                    )
                ),
                new AnimationChannel(
                    node,
                    AnimationPath.Scale,
                    new AnimationSampler([0f], [new Vector4(2, 3, 4, 0)], AnimationInterpolation.Step)
                ),
            ]
        );

        clip.Apply(0f);

        Assert.AreEqual(rotation, node.Transform.Rotation);
        Assert.AreEqual(new Vector3(2, 3, 4), node.Transform.Scale);
        Assert.IsTrue(node.Transform.IsLocalDirty);
    }

    [TestMethod]
    public void Clip_Apply_SkipsMissingTargets()
    {
        var node = new Node(_world);
        var clip = CreateClip(node);
        var unbound = new AnimationClip("Unbound", [new AnimationChannel(null, AnimationPath.Translation, clip.Channels[0].Sampler)]);
        node.Dispose();

        clip.Apply(0.5f);
        unbound.Apply(0.5f);
    }

    [TestMethod]
    public void Update_WhilePlaying_AdvancesAndPoses()
    {
        var node = new Node(_world);
        var player = new AnimationPlayer(CreateClip(node));
        player.Play();

        player.Update(0.25f);

        Assert.AreEqual(0.25f, player.Time, 1e-6f);
        Assert.AreEqual(2.5f, node.Transform.Translation.X, 1e-5f);
    }

    [TestMethod]
    public void Update_Loop_WrapsAroundClipRange()
    {
        var node = new Node(_world);
        var player = new AnimationPlayer(CreateClip(node, start: 2f)) { Loop = true };
        Assert.AreEqual(2f, player.Time);
        player.Play();

        player.Update(1.25f);

        Assert.IsTrue(player.IsPlaying);
        Assert.AreEqual(2.25f, player.Time, 1e-5f);
    }

    [TestMethod]
    public void Update_Loop_Backwards_WrapsToEnd()
    {
        var node = new Node(_world);
        var player = new AnimationPlayer(CreateClip(node)) { Speed = -1f };
        player.Play();

        player.Update(0.25f);

        Assert.AreEqual(0.75f, player.Time, 1e-5f);
        Assert.AreEqual(7.5f, node.Transform.Translation.X, 1e-4f);
    }

    [TestMethod]
    public void Update_NoLoop_StopsOnLastFrame_AndPlayRestarts()
    {
        var node = new Node(_world);
        var player = new AnimationPlayer(CreateClip(node)) { Loop = false };
        player.Play();

        player.Update(0f);
        Assert.IsTrue(player.IsPlaying, "A zero step at the start must not end playback.");

        player.Update(5f);
        Assert.IsFalse(player.IsPlaying);
        Assert.AreEqual(1f, player.Time);
        Assert.AreEqual(10f, node.Transform.Translation.X);

        player.Play();
        Assert.AreEqual(0f, player.Time);
        Assert.IsTrue(player.IsPlaying);
    }

    [TestMethod]
    public void Update_WhilePaused_OnlyAppliesTimeChanges()
    {
        var node = new Node(_world);
        var player = new AnimationPlayer(CreateClip(node));

        player.Update(0.5f);
        Assert.AreEqual(0f, player.Time, "Paused players do not advance.");

        node.Transform.Translation = new Vector3(42, 0, 0);
        player.Update(0.5f);
        Assert.AreEqual(42f, node.Transform.Translation.X, "Nothing changed, so the pose is not re-applied.");

        player.Time = 0.5f;
        player.Update(0.5f);
        Assert.AreEqual(5f, node.Transform.Translation.X, 1e-5f);
    }

    [TestMethod]
    public void Stop_RestoresRestPose()
    {
        var node = new Node(_world);
        node.Transform.Translation = new Vector3(-1, 2, 3);
        var player = new AnimationPlayer(CreateClip(node));
        player.Play();
        player.Update(0.5f);
        Assert.AreEqual(5f, node.Transform.Translation.X, 1e-5f);

        player.Stop();

        Assert.IsFalse(player.IsPlaying);
        Assert.AreEqual(0f, player.Time);
        Assert.AreEqual(new Vector3(-1, 2, 3), node.Transform.Translation);
    }

    [TestMethod]
    public void ChangingClip_RestoresPreviousTargets()
    {
        var a = new Node(_world);
        var b = new Node(_world);
        var player = new AnimationPlayer(CreateClip(a, name: "A"));
        player.Play();
        player.Update(0.5f);
        Assert.AreEqual(5f, a.Transform.Translation.X, 1e-5f);

        player.Clip = CreateClip(b, name: "B");
        player.Update(0.25f);

        Assert.AreEqual(0f, a.Transform.Translation.X, "Node A returns to its rest pose.");
        Assert.AreEqual(2.5f, b.Transform.Translation.X, 1e-5f);
        Assert.IsTrue(player.IsPlaying);
    }

    [TestMethod]
    public void AnimatedTransforms_PropagateToChildren()
    {
        var parent = new Node(_world);
        var child = new Node(_world);
        child.Transform.Translation = new Vector3(0, 1, 0);
        parent.AddChild(child);
        _world.SortSceneNodes();
        _world.UpdateTransforms();

        var player = new AnimationPlayer(CreateClip(parent)) { Loop = false };
        player.Play();
        player.Update(1f);
        _world.UpdateTransforms();

        Assert.AreEqual(new Vector3(10, 1, 0), child.WorldTransform.Value.Translation);
    }
}
