using System.Numerics;
using glTFLoader.Schema;
using HelixToolkit.Nex.glTF.Internal;
using HelixToolkit.Nex.Scene;
using static HelixToolkit.Nex.glTF.Tests.AccessorReaderSkinTests;
using GltfChannel = glTFLoader.Schema.AnimationChannel;
using GltfPath = glTFLoader.Schema.AnimationChannelTarget.PathEnum;
using GltfSampler = glTFLoader.Schema.AnimationSampler;

namespace HelixToolkit.Nex.glTF.Tests;

/// <summary>
/// Tests for reading animation keyframes through <see cref="AccessorReader"/> and converting glTF
/// animations with <see cref="AnimationConverter"/>.
/// </summary>
[TestClass]
public class AnimationConverterTests
{
    private static Accessor Scalar(int count) =>
        new()
        {
            ComponentType = Accessor.ComponentTypeEnum.FLOAT,
            Type = Accessor.TypeEnum.SCALAR,
            Count = count,
        };

    private static Accessor Vec3(int count) =>
        new()
        {
            ComponentType = Accessor.ComponentTypeEnum.FLOAT,
            Type = Accessor.TypeEnum.VEC3,
            Count = count,
        };

    private static byte[] Shorts(params short[] values)
    {
        var bytes = new byte[values.Length * sizeof(short)];
        System.Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static GltfChannel Channel(int sampler, int? node, GltfPath path) =>
        new()
        {
            Sampler = sampler,
            Target = new AnimationChannelTarget { Node = node, Path = path },
        };

    private static GltfSampler Sampler(
        int input,
        int output,
        GltfSampler.InterpolationEnum interpolation = GltfSampler.InterpolationEnum.LINEAR
    ) =>
        new()
        {
            Input = input,
            Output = output,
            Interpolation = interpolation,
        };

    [TestMethod]
    public void ReadKeyframeTimes_ReadsScalarFloats()
    {
        var reader = CreateReader((Floats(0f, 0.5f, 2f), Scalar(3)));

        Assert.IsTrue(reader.ReadKeyframeTimes(0, out var times, out var error), error);

        CollectionAssert.AreEqual(new[] { 0f, 0.5f, 2f }, times);
    }

    [TestMethod]
    public void ReadKeyframeTimes_WrongType_Fails()
    {
        var reader = CreateReader((Floats(0, 0, 0), Vec3(1)));

        Assert.IsFalse(reader.ReadKeyframeTimes(0, out _, out var error));
        Assert.IsNotNull(error);
    }

    [TestMethod]
    public void ReadKeyframeValues_Vec3Float_LeavesWZero()
    {
        var reader = CreateReader((Floats(1, 2, 3, 4, 5, 6), Vec3(2)));

        Assert.IsTrue(
            reader.ReadKeyframeValues(0, Accessor.TypeEnum.VEC3, out var values, out var error),
            error
        );

        CollectionAssert.AreEqual(new[] { new Vector4(1, 2, 3, 0), new Vector4(4, 5, 6, 0) }, values);
    }

    [TestMethod]
    public void ReadKeyframeValues_NormalizedShortRotation_Dequantizes()
    {
        var reader = CreateReader(
            (
                Shorts(0, 0, -32768, 32767),
                Vec4(Accessor.ComponentTypeEnum.SHORT, 1, normalized: true)
            )
        );

        Assert.IsTrue(
            reader.ReadKeyframeValues(0, Accessor.TypeEnum.VEC4, out var values, out var error),
            error
        );

        Assert.AreEqual(new Vector4(0, 0, -1, 1), values[0]);
    }

    [TestMethod]
    public void ReadKeyframeValues_TypeMismatchOrUnnormalizedIntegers_Fail()
    {
        var reader = CreateReader(
            (Floats(1, 2, 3), Vec3(1)),
            ([1, 2, 3, 4], Vec4(Accessor.ComponentTypeEnum.UNSIGNED_BYTE, 1))
        );

        Assert.IsFalse(reader.ReadKeyframeValues(0, Accessor.TypeEnum.VEC4, out _, out _));
        Assert.IsFalse(reader.ReadKeyframeValues(1, Accessor.TypeEnum.VEC4, out _, out _));
    }

    [TestMethod]
    public void Convert_MapsChannelsAndSharesSamplers()
    {
        var (model, reader) = CreateModel(
            (Floats(0f, 1f), Scalar(2)),
            (Floats(0, 0, 0, 1, 2, 3), Vec3(2)),
            (Floats(0, 0, 0, 1, 0, 0, 0, 1), Vec4(Accessor.ComponentTypeEnum.FLOAT, 2)),
            (Floats(0, 1, 2, 3, 4, 5, 6, 7, 8), Vec3(3)),
            (Floats(0f), Scalar(1))
        );
        model.Nodes = [new(), new()];
        model.Animations =
        [
            new Animation
            {
                Name = "Walk",
                Samplers =
                [
                    Sampler(0, 1),
                    Sampler(0, 2, GltfSampler.InterpolationEnum.STEP),
                    Sampler(4, 3, GltfSampler.InterpolationEnum.CUBICSPLINE),
                ],
                Channels =
                [
                    Channel(0, 0, GltfPath.translation),
                    Channel(1, 1, GltfPath.rotation),
                    Channel(0, 1, GltfPath.translation),
                    Channel(2, 0, GltfPath.scale),
                ],
            },
        ];
        var diagnostics = new List<ImportDiagnostic>();

        var animations = new AnimationConverter(reader, diagnostics).Convert(model);

        Assert.AreEqual(0, diagnostics.Count, string.Join("; ", diagnostics.Select(d => d.Message)));
        Assert.AreEqual(1, animations.Count);
        var clip = animations[0].Clip;
        Assert.AreEqual("Walk", clip.Name);
        CollectionAssert.AreEqual(new[] { 0, 1, 1, 0 }, animations[0].TargetNodes);
        Assert.AreEqual(AnimationPath.Translation, clip.Channels[0].Path);
        Assert.AreEqual(AnimationPath.Rotation, clip.Channels[1].Path);
        Assert.AreEqual(AnimationPath.Scale, clip.Channels[3].Path);
        Assert.AreEqual(AnimationInterpolation.Step, clip.Channels[1].Sampler.Interpolation);
        Assert.AreEqual(AnimationInterpolation.CubicSpline, clip.Channels[3].Sampler.Interpolation);
        Assert.AreSame(clip.Channels[0].Sampler, clip.Channels[2].Sampler);
        Assert.IsTrue(clip.Channels.All(c => c.Target is null), "Targets are bound after flush.");
        Assert.AreEqual(1f, clip.Duration);
        Assert.AreEqual(new Vector4(0.5f, 1f, 1.5f, 0), clip.Channels[0].Sampler.Sample(0.5f));
    }

    [TestMethod]
    public void Convert_SkipsWeightsAndInvalidChannels_WithWarnings()
    {
        var (model, reader) = CreateModel(
            (Floats(0f, 1f), Scalar(2)),
            (Floats(0, 0, 0, 1, 1, 1), Vec3(2)),
            (Floats(0, 1), Scalar(2))
        );
        model.Nodes = [new()];
        model.Animations =
        [
            new Animation
            {
                Name = "Mixed",
                Samplers = [Sampler(0, 1), Sampler(0, 2)],
                Channels =
                [
                    Channel(0, 0, GltfPath.scale),
                    Channel(1, 0, GltfPath.weights),
                    Channel(0, null, GltfPath.translation),
                    Channel(0, 7, GltfPath.translation),
                    Channel(5, 0, GltfPath.translation),
                ],
            },
            new Animation
            {
                Name = "MorphOnly",
                Samplers = [Sampler(0, 2)],
                Channels = [Channel(0, 0, GltfPath.weights)],
            },
        ];
        var diagnostics = new List<ImportDiagnostic>();

        var animations = new AnimationConverter(reader, diagnostics).Convert(model);

        Assert.AreEqual(1, animations.Count, "The morph-only animation has no supported channels.");
        Assert.AreEqual(1, animations[0].Clip.Channels.Count);
        Assert.AreEqual(AnimationPath.Scale, animations[0].Clip.Channels[0].Path);
        Assert.IsTrue(diagnostics.All(d => d.Severity == DiagnosticSeverity.Warning));
        Assert.IsTrue(diagnostics.Any(d => d.Message.Contains("morph target weights")));
        Assert.IsTrue(diagnostics.Any(d => d.Message.Contains("missing sampler 5")));
        Assert.IsTrue(diagnostics.Any(d => d.Message.Contains("'MorphOnly' has no supported channels")));
    }

    [TestMethod]
    public void Convert_InvalidSamplerData_IsSkipped()
    {
        // Two keyframe times but only one translation value.
        var (model, reader) = CreateModel((Floats(0f, 1f), Scalar(2)), (Floats(0, 0, 0), Vec3(1)));
        model.Nodes = [new()];
        model.Animations =
        [
            new Animation
            {
                Samplers = [Sampler(0, 1)],
                Channels = [Channel(0, 0, GltfPath.translation)],
            },
        ];
        var diagnostics = new List<ImportDiagnostic>();

        var animations = new AnimationConverter(reader, diagnostics).Convert(model);

        Assert.AreEqual(0, animations.Count);
        Assert.IsTrue(diagnostics.Any(d => d.Message.Contains("sampler 0 is skipped")));
    }
}
