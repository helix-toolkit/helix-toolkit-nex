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
}

/// <summary>
/// Animates one transform property of a target node with an <see cref="AnimationSampler"/>.
/// </summary>
/// <param name="target">The animated node. Channels without a live target are skipped.</param>
/// <param name="path">The animated transform property.</param>
/// <param name="sampler">The keyframe data.</param>
public sealed class AnimationChannel(Node? target, AnimationPath path, AnimationSampler sampler)
{
    /// <summary>
    /// Gets or sets the animated node. Channels without a live target are skipped.
    /// </summary>
    public Node? Target { get; set; } = target;

    /// <summary>
    /// Gets the animated transform property.
    /// </summary>
    public AnimationPath Path { get; } = path;

    /// <summary>
    /// Gets the keyframe data.
    /// </summary>
    public AnimationSampler Sampler { get; } =
        sampler ?? throw new ArgumentNullException(nameof(sampler));

    /// <summary>
    /// Samples the channel at the specified time and writes the value to the target's
    /// <see cref="Transform"/>. Does not notify the transform change.
    /// </summary>
    /// <param name="time">The time in seconds.</param>
    /// <returns><see langword="true"/> if the target was updated; <see langword="false"/> if it is missing.</returns>
    public bool Apply(float time)
    {
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
