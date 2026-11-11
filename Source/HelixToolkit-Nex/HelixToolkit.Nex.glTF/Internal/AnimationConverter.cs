using System.Numerics;
using glTFLoader.Schema;
using HelixToolkit.Nex.Scene;
using GltfAnimationSampler = glTFLoader.Schema.AnimationSampler;
using GltfChannelPath = glTFLoader.Schema.AnimationChannelTarget.PathEnum;
using GltfInterpolation = glTFLoader.Schema.AnimationSampler.InterpolationEnum;
using SceneAnimationChannel = HelixToolkit.Nex.Scene.AnimationChannel;
using SceneAnimationSampler = HelixToolkit.Nex.Scene.AnimationSampler;

namespace HelixToolkit.Nex.glTF.Internal;

/// <summary>
/// An imported animation clip whose channel targets are bound once the scene is materialized.
/// </summary>
/// <param name="Clip">The clip. Its channels have no target until bound.</param>
/// <param name="TargetNodes">
/// The glTF node index of each channel, index-aligned with the clip channels. Weight channels bind
/// to the morph target weights of that node.
/// </param>
internal sealed record ImportedAnimation(AnimationClip Clip, int[] TargetNodes);

/// <summary>
/// Converts glTF animations into <see cref="AnimationClip"/>s: node transform channels
/// (translation, rotation, scale) and morph target weight channels. A weight channel animating N
/// weights becomes ceil(N / 4) channels of four weights each.
/// </summary>
internal sealed class AnimationConverter(AccessorReader reader, List<ImportDiagnostic> diagnostics)
{
    private const string Category = "Animation";

    /// <summary>
    /// Converts all animations of the model. Animations without any supported channel are skipped.
    /// </summary>
    public List<ImportedAnimation> Convert(Gltf model)
    {
        var result = new List<ImportedAnimation>();
        if (model.Animations is not { Length: > 0 } animations)
        {
            return result;
        }
        for (int i = 0; i < animations.Length; i++)
        {
            var imported = Convert(model, animations[i], i);
            if (imported is not null)
            {
                result.Add(imported);
            }
        }
        return result;
    }

    private ImportedAnimation? Convert(Gltf model, Animation animation, int animationIndex)
    {
        var name = string.IsNullOrEmpty(animation.Name)
            ? $"Animation_{animationIndex}"
            : animation.Name;
        var channels = new List<SceneAnimationChannel>();
        var targets = new List<int>();
        var samplers = new Dictionary<int, SceneAnimationSampler?>();
        var weightSamplers = new Dictionary<int, SceneAnimationSampler[]?>();
        var nodeCount = model.Nodes?.Length ?? 0;

        foreach (var channel in animation.Channels ?? [])
        {
            if (channel.Target.Node is not int nodeIndex || nodeIndex < 0 || nodeIndex >= nodeCount)
            {
                Warn(animationIndex, $"Animation '{name}' has a channel without a valid target node.");
                continue;
            }
            if (channel.Target.Path == GltfChannelPath.weights)
            {
                var weightCount = SceneBuilder.GetMorphTargetCount(model, model.Nodes![nodeIndex]);
                if (weightCount == 0)
                {
                    Warn(
                        animationIndex,
                        $"Animation '{name}' animates the weights of node {nodeIndex}, which has no morph targets. The channel is skipped."
                    );
                    continue;
                }
                if (!weightSamplers.TryGetValue(channel.Sampler, out var groups))
                {
                    groups = ConvertWeightSamplers(animation, channel.Sampler, weightCount, name, animationIndex);
                    weightSamplers[channel.Sampler] = groups;
                }
                for (int g = 0; groups is not null && g < groups.Length; g++)
                {
                    channels.Add(new SceneAnimationChannel(null, g * 4, groups[g]));
                    targets.Add(nodeIndex);
                }
                continue;
            }
            if (!samplers.TryGetValue(channel.Sampler, out var sampler))
            {
                sampler = ConvertSampler(animation, channel.Sampler, channel.Target.Path, name, animationIndex);
                samplers[channel.Sampler] = sampler;
            }
            if (sampler is null)
            {
                continue;
            }
            var path = channel.Target.Path switch
            {
                GltfChannelPath.translation => AnimationPath.Translation,
                GltfChannelPath.rotation => AnimationPath.Rotation,
                _ => AnimationPath.Scale,
            };
            channels.Add(new SceneAnimationChannel(null, path, sampler));
            targets.Add(nodeIndex);
        }

        if (channels.Count == 0)
        {
            Warn(animationIndex, $"Animation '{name}' has no supported channels and is skipped.");
            return null;
        }
        return new ImportedAnimation(new AnimationClip(name, channels), [.. targets]);
    }

    private SceneAnimationSampler? ConvertSampler(
        Animation animation,
        int samplerIndex,
        GltfChannelPath path,
        string name,
        int animationIndex
    )
    {
        if (animation.Samplers is null || samplerIndex < 0 || samplerIndex >= animation.Samplers.Length)
        {
            Warn(animationIndex, $"Animation '{name}' references missing sampler {samplerIndex}.");
            return null;
        }
        GltfAnimationSampler gltfSampler = animation.Samplers[samplerIndex];
        var type = path == GltfChannelPath.rotation ? Accessor.TypeEnum.VEC4 : Accessor.TypeEnum.VEC3;
        if (
            !reader.ReadKeyframeTimes(gltfSampler.Input, out var times, out var error)
            || !reader.ReadKeyframeValues(gltfSampler.Output, type, out var values, out error)
        )
        {
            Warn(animationIndex, $"Animation '{name}' sampler {samplerIndex} is skipped: {error}");
            return null;
        }
        var interpolation = gltfSampler.Interpolation switch
        {
            GltfInterpolation.STEP => AnimationInterpolation.Step,
            GltfInterpolation.CUBICSPLINE => AnimationInterpolation.CubicSpline,
            _ => AnimationInterpolation.Linear,
        };
        try
        {
            return new SceneAnimationSampler(times, values, interpolation);
        }
        catch (ArgumentException ex)
        {
            Warn(animationIndex, $"Animation '{name}' sampler {samplerIndex} is skipped: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Converts a weights sampler (SCALAR output, <paramref name="weightCount"/> values per keyframe,
    /// three times as many for cubic splines) into one sampler per group of four weights.
    /// </summary>
    private SceneAnimationSampler[]? ConvertWeightSamplers(
        Animation animation,
        int samplerIndex,
        int weightCount,
        string name,
        int animationIndex
    )
    {
        if (animation.Samplers is null || samplerIndex < 0 || samplerIndex >= animation.Samplers.Length)
        {
            Warn(animationIndex, $"Animation '{name}' references missing sampler {samplerIndex}.");
            return null;
        }
        GltfAnimationSampler gltfSampler = animation.Samplers[samplerIndex];
        if (
            !reader.ReadKeyframeTimes(gltfSampler.Input, out var times, out var error)
            || !reader.ReadScalars(gltfSampler.Output, out var values, out error)
        )
        {
            Warn(animationIndex, $"Animation '{name}' sampler {samplerIndex} is skipped: {error}");
            return null;
        }
        var interpolation = gltfSampler.Interpolation switch
        {
            GltfInterpolation.STEP => AnimationInterpolation.Step,
            GltfInterpolation.CUBICSPLINE => AnimationInterpolation.CubicSpline,
            _ => AnimationInterpolation.Linear,
        };
        var elementCount = interpolation == AnimationInterpolation.CubicSpline ? times.Length * 3 : times.Length;
        if (values.Length != elementCount * weightCount)
        {
            Warn(
                animationIndex,
                $"Animation '{name}' sampler {samplerIndex} has {values.Length} weights, expected {elementCount * weightCount} for {weightCount} morph targets. The sampler is skipped."
            );
            return null;
        }
        var groups = new SceneAnimationSampler[(weightCount + 3) / 4];
        Span<float> v = stackalloc float[4];
        for (int g = 0; g < groups.Length; g++)
        {
            var groupValues = new Vector4[elementCount];
            for (int e = 0; e < elementCount; e++)
            {
                v.Clear();
                for (int c = 0; c < 4 && g * 4 + c < weightCount; c++)
                {
                    v[c] = values[e * weightCount + g * 4 + c];
                }
                groupValues[e] = new Vector4(v[0], v[1], v[2], v[3]);
            }
            try
            {
                groups[g] = new SceneAnimationSampler(times, groupValues, interpolation);
            }
            catch (ArgumentException ex)
            {
                Warn(animationIndex, $"Animation '{name}' sampler {samplerIndex} is skipped: {ex.Message}");
                return null;
            }
        }
        return groups;
    }

    private void Warn(int animationIndex, string message)
    {
        diagnostics.Add(new ImportDiagnostic(DiagnosticSeverity.Warning, message, Category, animationIndex));
    }
}
