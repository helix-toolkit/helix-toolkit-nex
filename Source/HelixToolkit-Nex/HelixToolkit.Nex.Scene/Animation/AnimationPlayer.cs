namespace HelixToolkit.Nex.Scene;

/// <summary>
/// Plays an <see cref="AnimationClip"/>: tracks the playback time and poses the clip's target
/// nodes when <see cref="Update"/> is called.
/// </summary>
/// <remarks>
/// <para>
/// Call <see cref="Update"/> once per frame on the world's owning thread, before rendering:
/// <code>
/// var player = new AnimationPlayer(importResult.Animations[0]);
/// player.Play();
/// // per frame:
/// player.Update(deltaTime);
/// engine.Render(viewport, worldData);
/// </code>
/// </para>
/// <para>
/// The player remembers the transforms of the target nodes (and the animated morph target
/// weights) before it first poses them, and restores them on <see cref="Stop"/> or when
/// <see cref="Clip"/> changes, so switching clips does not leave nodes animated only by the
/// previous clip in its last pose.
/// </para>
/// </remarks>
public sealed class AnimationPlayer
{
    private readonly record struct RestPose(Vector3 Translation, Quaternion Rotation, Vector3 Scale);

    private readonly Dictionary<Node, RestPose> _restPoses = [];
    private readonly Dictionary<MorphWeights, float[]> _restWeights = [];
    private AnimationClip? _clip;
    private float _time;
    private bool _dirty;

    /// <summary>
    /// Initializes a new instance of the <see cref="AnimationPlayer"/> class.
    /// </summary>
    /// <param name="clip">The clip to play.</param>
    public AnimationPlayer(AnimationClip? clip = null)
    {
        Clip = clip;
    }

    /// <summary>
    /// Gets or sets the clip to play. Changing the clip restores the rest pose of the previous
    /// clip's targets and rewinds to the start of the new clip, which is posed on the next
    /// <see cref="Update"/>. Playback state is kept.
    /// </summary>
    public AnimationClip? Clip
    {
        get => _clip;
        set
        {
            if (ReferenceEquals(_clip, value))
            {
                return;
            }
            RestorePose();
            _clip = value;
            _time = value?.StartTime ?? 0f;
            _dirty = true;
        }
    }

    /// <summary>
    /// Gets or sets the playback time in clip seconds. Setting the time poses the targets on the
    /// next <see cref="Update"/>, even when paused.
    /// </summary>
    public float Time
    {
        get => _time;
        set
        {
            _time = value;
            _dirty = true;
        }
    }

    /// <summary>
    /// Gets or sets the playback speed multiplier. Negative values play backwards. Default is 1.
    /// </summary>
    public float Speed { get; set; } = 1f;

    /// <summary>
    /// Gets or sets a value indicating whether playback wraps around at the end of the clip.
    /// When <see langword="false"/>, playback stops on the last frame. Default is <see langword="true"/>.
    /// </summary>
    public bool Loop { get; set; } = true;

    /// <summary>
    /// Gets a value indicating whether the clip is playing.
    /// </summary>
    public bool IsPlaying { get; private set; }

    /// <summary>
    /// Starts or resumes playback. A non-looping clip that reached its end restarts.
    /// </summary>
    public void Play()
    {
        if (_clip is null)
        {
            return;
        }
        if (!Loop)
        {
            if (Speed >= 0f && _time >= _clip.EndTime)
            {
                Time = _clip.StartTime;
            }
            else if (Speed < 0f && _time <= _clip.StartTime)
            {
                Time = _clip.EndTime;
            }
        }
        IsPlaying = true;
    }

    /// <summary>
    /// Pauses playback, keeping the current pose.
    /// </summary>
    public void Pause()
    {
        IsPlaying = false;
    }

    /// <summary>
    /// Stops playback, rewinds to the start of the clip and restores the targets' rest pose.
    /// </summary>
    public void Stop()
    {
        IsPlaying = false;
        RestorePose();
        _time = _clip?.StartTime ?? 0f;
        _dirty = false;
    }

    /// <summary>
    /// Advances the playback time while playing and poses the clip's targets. Must be called on
    /// the owning thread of the targets' world, before the frame is rendered.
    /// </summary>
    /// <param name="deltaTime">The elapsed time in seconds since the previous update.</param>
    public void Update(float deltaTime)
    {
        if (_clip is null || !(IsPlaying || _dirty))
        {
            return;
        }
        if (IsPlaying)
        {
            Advance(_clip, deltaTime * Speed);
        }
        _dirty = false;
        CaptureRestPose(_clip);
        _clip.Apply(_time);
    }

    private void Advance(AnimationClip clip, float delta)
    {
        var start = clip.StartTime;
        var duration = clip.Duration;
        if (duration <= 0f)
        {
            _time = start;
            if (!Loop)
            {
                IsPlaying = false;
            }
            return;
        }
        var time = _time + delta;
        if (Loop)
        {
            var offset = (time - start) % duration;
            _time = start + (offset < 0f ? offset + duration : offset);
        }
        else
        {
            _time = Math.Clamp(time, start, clip.EndTime);
            // Stop on the last frame in the direction of playback.
            if (delta >= 0f ? time >= clip.EndTime : time <= start)
            {
                IsPlaying = false;
            }
        }
    }

    private void CaptureRestPose(AnimationClip clip)
    {
        foreach (var channel in clip.Channels)
        {
            if (!channel.IsTransform)
            {
                if (channel.Weights is { } weights && !_restWeights.ContainsKey(weights))
                {
                    _restWeights[weights] = weights.Values.ToArray();
                }
                continue;
            }
            var target = channel.Target;
            if (target is null || !target.Alive || _restPoses.ContainsKey(target))
            {
                continue;
            }
            ref var transform = ref target.Transform;
            _restPoses[target] = new RestPose(
                transform.Translation,
                transform.Rotation,
                transform.Scale
            );
        }
    }

    private void RestorePose()
    {
        foreach (var (node, pose) in _restPoses)
        {
            if (!node.Alive)
            {
                continue;
            }
            ref var transform = ref node.Transform;
            transform.Translation = pose.Translation;
            transform.Rotation = pose.Rotation;
            transform.Scale = pose.Scale;
            node.NotifyTransformChanged();
        }
        _restPoses.Clear();
        foreach (var (weights, values) in _restWeights)
        {
            weights.Set(values);
        }
        _restWeights.Clear();
    }
}
