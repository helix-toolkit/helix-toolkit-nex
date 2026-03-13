namespace HelixToolkit.Nex.Rendering.RenderNodes;

public abstract class PostEffect() : Initializable
{
    public IContext? Context { internal set; get; }
    public bool Enabled { get; set; } = true;

    public abstract Color DebugColor { get; }

    /// <summary>
    /// Gets or sets the priority level of the effect. Effects with lower priority values are executed before those with higher values.
    /// </summary>
    public int Priority { get; set; } = 0;

    /// <summary>
    /// Applies this post-effect.
    /// </summary>
    /// <param name="res">Render resources for the current frame.</param>
    /// <param name="readSlot">
    /// The resource-set key of the texture to read from (sampled input).
    /// Resolved at compile time by the render graph's ping-pong slot assignment.
    /// </param>
    /// <param name="writeSlot">
    /// The resource-set key of the texture to write to (render target).
    /// Resolved at compile time by the render graph's ping-pong slot assignment.
    /// </param>
    public abstract void Apply(in RenderResources res, ref string readSlot, ref string writeSlot);
}

public sealed class PostEffectsNode : RenderNode
{
    private readonly List<PostEffect> _effects = [];
    private bool _changed = true;

    // Resolved by AddToGraph and stored so OnRender can alternate slots across effects.
    private string _initialReadSlot = SystemBufferNames.TextureColorF16A;
    private string _initialWriteSlot = SystemBufferNames.TextureColorF16B;

    public override string Name => nameof(PostEffectsNode);
    public override Color4 DebugColor => Color.Aqua;

    public void AddEffect(PostEffect effect)
    {
        _effects.Add(effect);
        _changed = true;
    }

    protected override bool BeginRender(in RenderResources res)
    {
        res.CmdBuffer.PushDebugGroupLabel(Name, DebugColor);
        return true;
    }

    protected override void EndRender(in RenderResources res)
    {
        res.CmdBuffer.PopDebugGroupLabel();
    }

    protected override void OnRender(in RenderResources res)
    {
        if (_changed)
        {
            _effects.Sort((a, b) => a.Priority.CompareTo(b.Priority));
            _changed = false;
        }

        // Alternate read/write slots across effects without any runtime resource-dict mutation.
        var read = _initialReadSlot;
        var write = _initialWriteSlot;

        foreach (var effect in _effects)
        {
            if (!effect.Enabled)
            {
                continue;
            }
            res.CmdBuffer.PushDebugGroupLabel(effect.Name, effect.DebugColor);
            effect.Apply(in res, ref read, ref write);
            res.CmdBuffer.PopDebugGroupLabel();

            // Flip for next effect: what was just written becomes the next read source.
            (read, write) = (write, read);
        }

        // `read` now holds the slot that was last written (the flip above moved it there).
        // When zero effects ran, `read` == _initialReadSlot, which is already the correct source.
        // Publish this as the stable TextureColorF16Current alias so all downstream passes
        // (e.g. RenderToFinalNode) always read the correct texture regardless of effect count.
        if (res.Context.ResourceSet is { } resourceSet)
        {
            resourceSet.Textures[SystemBufferNames.TextureColorF16Current] = resourceSet.Textures[
                read
            ];
        }
    }

    protected override bool OnSetup()
    {
        foreach (var effect in _effects)
        {
            effect.Context = Context;
            effect.Initialize().CheckResult();
        }
        return true;
    }

    protected override void OnTeardown()
    {
        foreach (var effect in _effects)
        {
            effect.Teardown();
            effect.Context = null;
        }
        base.OnTeardown();
    }

    public override void AddToGraph(RenderGraph graph)
    {
        // Register the ping-pong group if it hasn't been added yet (PrepareNode owns the textures).
        graph.AddPingPongGroup(
            PingPongGroups.ColorF16,
            SystemBufferNames.TextureColorF16A,
            SystemBufferNames.TextureColorF16B
        );

        // Register the stable current-color alias. It has no build function because its handle
        // is written at runtime by OnRender, not allocated as a separate GPU texture.
        graph.AddTexture(
            SystemBufferNames.TextureColorF16Current,
            null,
            dependsOnScreenSize: false
        );

        graph.AddPingPongPass(
            nameof(PostEffectsNode),
            PingPongGroups.ColorF16,
            extraInputs: [new(SystemBufferNames.TextureColorF16A, ResourceType.Texture)],
            // Declare TextureColorF16Current as an output so downstream passes that consume it
            // are correctly ordered after PostEffectsNode by the topological sort.
            extraOutputs: [new(SystemBufferNames.TextureColorF16Current, ResourceType.Texture)],
            onSetup: (res, readSlot, writeSlot) =>
            {
                _initialReadSlot = readSlot;
                _initialWriteSlot = writeSlot;

                res.Pass.Colors[0].ClearColor = Color.Transparent;
                res.Pass.Colors[0].LoadOp = LoadOp.Load;
                res.Pass.Colors[0].StoreOp = StoreOp.Store;
            },
            after: [nameof(ForwardPlusOpaqueNode)]
        );
    }
}
