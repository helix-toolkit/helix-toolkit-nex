namespace HelixToolkit.Nex.Scene;

/// <summary>
/// The node property an <see cref="AnimationChannel"/> animates.
/// </summary>
public enum AnimationPath
{
    /// <summary>
    /// <see cref="Transform.Translation"/>.
    /// </summary>
    Translation,

    /// <summary>
    /// <see cref="Transform.Rotation"/>.
    /// </summary>
    Rotation,

    /// <summary>
    /// <see cref="Transform.Scale"/>.
    /// </summary>
    Scale,

    /// <summary>
    /// Up to four consecutive <see cref="MorphWeights"/>, starting at <see cref="AnimationChannel.WeightOffset"/>.
    /// </summary>
    Weights,
}

/// <summary>
/// Animates one transform property of a target node, or up to four morph target weights, with an
/// <see cref="AnimationSampler"/>.
/// </summary>
public sealed class AnimationChannel
{
    /// <summary>
    /// Initializes a channel animating a transform property of <paramref name="target"/>.
    /// </summary>
    /// <param name="target">The animated node. Channels without a live target are skipped.</param>
    /// <param name="path">The animated transform property.</param>
    /// <param name="sampler">The keyframe data.</param>
    public AnimationChannel(Node? target, AnimationPath path, AnimationSampler sampler)
    {
        Target = target;
        Path = path;
        Sampler = sampler ?? throw new ArgumentNullException(nameof(sampler));
    }

    /// <summary>
    /// Initializes a channel animating up to four morph target weights: the sampled X, Y, Z and W
    /// values are written to the weights at <paramref name="weightOffset"/> and the following indices.
    /// </summary>
    /// <param name="weights">The animated weights. Channels without weights are skipped.</param>
    /// <param name="weightOffset">The index of the first animated weight.</param>
    /// <param name="sampler">The keyframe data.</param>
    public AnimationChannel(MorphWeights? weights, int weightOffset, AnimationSampler sampler)
        : this(null, AnimationPath.Weights, sampler)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(weightOffset);
        Weights = weights;
        WeightOffset = weightOffset;
    }

    /// <summary>
    /// Gets or sets the animated node. Channels animating a transform without a live target are
    /// skipped. For <see cref="AnimationPath.Weights"/> channels this is the node owning the
    /// weights, for information only.
    /// </summary>
    public Node? Target { get; set; }

    /// <summary>
    /// Gets the animated property.
    /// </summary>
    public AnimationPath Path { get; }

    /// <summary>
    /// Gets or sets the animated morph target weights of a <see cref="AnimationPath.Weights"/> channel.
    /// </summary>
    public MorphWeights? Weights { get; set; }

    /// <summary>
    /// Gets the index of the first weight animated by a <see cref="AnimationPath.Weights"/> channel.
    /// </summary>
    public int WeightOffset { get; }

    /// <summary>
    /// Gets the keyframe data.
    /// </summary>
    public AnimationSampler Sampler { get; }

    /// <summary>
    /// Gets a value indicating whether the channel animates a node transform.
    /// </summary>
    public bool IsTransform => Path != AnimationPath.Weights;

    /// <summary>
    /// Samples the channel at the specified time and writes the value to the target's
    /// <see cref="Transform"/> or to the <see cref="Weights"/>. Does not notify the transform change.
    /// </summary>
    /// <param name="time">The time in seconds.</param>
    /// <returns><see langword="true"/> if the target was updated; <see langword="false"/> if it is missing.</returns>
    public bool Apply(float time)
    {
        if (Path == AnimationPath.Weights)
        {
            if (Weights is null)
            {
                return false;
            }
            Weights.Set(WeightOffset, Sampler.Sample(time));
            return true;
        }
        var target = Target;
        if (target is null || !target.Alive)
        {
            return false;
        }
        ref var transform = ref target.Transform;
        switch (Path)
        {
            case AnimationPath.Translation:
                transform.Translation = ToVector3(Sampler.Sample(time));
                break;
            case AnimationPath.Rotation:
                transform.Rotation = Sampler.SampleRotation(time);
                break;
            case AnimationPath.Scale:
                transform.Scale = ToVector3(Sampler.Sample(time));
                break;
        }
        return true;
    }

    private static Vector3 ToVector3(Vector4 v)
    {
        return new Vector3(v.X, v.Y, v.Z);
    }
}
