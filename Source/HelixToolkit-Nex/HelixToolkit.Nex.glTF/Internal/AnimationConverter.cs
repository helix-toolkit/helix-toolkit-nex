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
/// <param name="TargetNodes">The glTF node index of each channel, index-aligned with the clip channels.</param>
internal sealed record ImportedAnimation(AnimationClip Clip, int[] TargetNodes);

/// <summary>
/// Converts glTF animations into <see cref="AnimationClip"/>s. Only node transform channels
/// (translation, rotation, scale) are supported; morph target weight channels are skipped.
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
        var skippedWeights = false;
        var nodeCount = model.Nodes?.Length ?? 0;

        foreach (var channel in animation.Channels ?? [])
        {
            if (channel.Target.Path == GltfChannelPath.weights)
            {
                skippedWeights = true;
                continue;
            }
            if (channel.Target.Node is not int nodeIndex || nodeIndex < 0 || nodeIndex >= nodeCount)
            {
                Warn(animationIndex, $"Animation '{name}' has a channel without a valid target node.");
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

        if (skippedWeights)
        {
            Warn(
                animationIndex,
                $"Animation '{name}' animates morph target weights, which are not supported. Those channels are skipped."
            );
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

    private void Warn(int animationIndex, string message)
    {
        diagnostics.Add(new ImportDiagnostic(DiagnosticSeverity.Warning, message, Category, animationIndex));
    }
}
