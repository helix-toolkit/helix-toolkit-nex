namespace HelixToolkit.Nex.ECS;

[StructLayout(LayoutKind.Sequential)]
internal struct EntityState
{
    public static EntityState InvalidState = new() { _state = 0 };

    /// <summary>
    /// Gets or sets the generation.
    /// This generation includes worlds generation + self generation
    /// </summary>
    /// <value>
    /// The generation.
    /// </value>
    internal Generation Generation;
    private State _state;
    internal ComponentTypeSet ComponentTypes;

    public void Reset(in byte worldId, in byte worldGeneration)
    {
        if (worldId == 0 || worldGeneration == 0)
        {
            _state = 0;
            Generation = new Generation(0, 0, Generation.EntityGeneration);
        }
        else
        {
            _state = State.All;
            Generation = new Generation(
                worldId,
                worldGeneration,
                (ushort)(Generation.EntityGeneration + 1u)
            );
        }
        ComponentTypes.Reset();
    }

    public bool Enabled
    {
        readonly get => (_state & State.Enabled) != 0;
        set => _state = value ? (_state | State.Enabled) : (_state & ~State.Enabled);
    }

    public bool Valid
    {
        readonly get => (_state & State.Valid) != 0 && Generation.HasWorld;
        set => _state = value ? (_state | State.Valid) : (_state & ~State.Valid);
    }

    public override readonly string ToString()
    {
        return $"EntityState {Generation} : {_state}";
    }
}
