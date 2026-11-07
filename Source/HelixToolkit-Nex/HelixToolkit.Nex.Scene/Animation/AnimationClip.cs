namespace HelixToolkit.Nex.Scene;

/// <summary>
/// A named set of <see cref="AnimationChannel"/>s that animate node transforms over a common
/// time range (a glTF <c>animation</c>).
/// </summary>
/// <remarks>
/// A clip only describes the animation. Use an <see cref="AnimationPlayer"/> to play it, or call
/// <see cref="Apply"/> to pose the target nodes at a specific time. Applying a clip writes the
/// target nodes' <see cref="Transform"/>s; the engine propagates them to world transforms (and to
/// skinned meshes whose joints are the animated nodes) on the next frame. Must be applied on the
/// owning thread of the targets' world.
/// </remarks>
public sealed class AnimationClip
{
    private readonly AnimationChannel[] _channels;

    /// <summary>
    /// Gets the clip name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the channels of the clip.
    /// </summary>
    public IReadOnlyList<AnimationChannel> Channels => _channels;

    /// <summary>
    /// Gets the earliest keyframe time of all channels in seconds.
    /// </summary>
    public float StartTime { get; }

    /// <summary>
    /// Gets the latest keyframe time of all channels in seconds.
    /// </summary>
    public float EndTime { get; }

    /// <summary>
    /// Gets the clip duration in seconds (<see cref="EndTime"/> - <see cref="StartTime"/>).
    /// </summary>
    public float Duration => EndTime - StartTime;

    /// <summary>
    /// Initializes a new instance of the <see cref="AnimationClip"/> class.
    /// </summary>
    /// <param name="name">The clip name.</param>
    /// <param name="channels">The animation channels.</param>
    public AnimationClip(string? name, IEnumerable<AnimationChannel> channels)
    {
        ArgumentNullException.ThrowIfNull(channels);
        _channels = [.. channels];
        Name = name ?? string.Empty;
        if (_channels.Length == 0)
        {
            return;
        }
        StartTime = float.MaxValue;
        EndTime = float.MinValue;
        foreach (var channel in _channels)
        {
            StartTime = MathF.Min(StartTime, channel.Sampler.StartTime);
            EndTime = MathF.Max(EndTime, channel.Sampler.EndTime);
        }
    }

    /// <summary>
    /// Poses every channel target at the specified time and notifies the transform changes.
    /// Channels whose target is missing or disposed are skipped.
    /// </summary>
    /// <param name="time">The clip time in seconds.</param>
    public void Apply(float time)
    {
        foreach (var channel in _channels)
        {
            if (channel.Apply(time))
            {
                channel.Target!.NotifyTransformChanged();
            }
        }
    }

    public override string ToString()
    {
        return $"AnimationClip: [{Name}] {_channels.Length} channels, {Duration:0.###}s";
    }
}
