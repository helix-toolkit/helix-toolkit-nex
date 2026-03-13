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
    public abstract void Apply(in RenderResources res);
}

public sealed class PostEffectsNode : RenderNode
{
    private readonly List<PostEffect> _effects = [];
    private bool _changed = true;
    public override string Name => nameof(PostEffectsNode);

    public override Color4 DebugColor => Color.Aqua;

    public void AddEffect(PostEffect effect)
    {
        _effects.Add(effect);
        _changed = true;
    }

    protected override bool BeginRender(in RenderResources res)
    {
        // Compute nodes do not begin a render pass.
        res.CmdBuffer.PushDebugGroupLabel(Name, DebugColor);
        return true;
    }

    protected override void EndRender(in RenderResources res)
    {
        res.CmdBuffer.PopDebugGroupLabel();
        // Compute nodes do not end a render pass.
    }

    protected override void OnRender(in RenderResources res)
    {
        if (_changed)
        {
            _effects.Sort((a, b) => a.Priority.CompareTo(b.Priority));
            _changed = false;
        }
        foreach (var effect in _effects)
        {
            if (!effect.Enabled)
            {
                continue;
            }
            res.CmdBuffer.PushDebugGroupLabel(effect.Name, effect.DebugColor);
            effect.Apply(in res);
            res.CmdBuffer.PopDebugGroupLabel();
        }
        res.Context.SwapColorPingPongBuffers(); // Final swap to ensure the last effect's output is in the expected buffer for downstream nodes.
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
        graph.AddPass(
            nameof(PostEffectsNode),
            // Input is TextureColorF16Target: this is what the prior pass (e.g. ForwardPlusOpaqueNode)
            // produces, establishing the correct compile-time ordering edge.
            // Output is TextureColorF16Sample: the other ping-pong buffer, so the declared names
            // are distinct and no self-loop is created.
            // The actual per-effect ping-pong swap happens at runtime in OnRender via
            // SwapColorPingPongBuffers(), so the physical textures behind these names alternate
            // each effect — the graph only needs to express the ordering dependency.
            inputs: [new(SystemBufferNames.TextureColorF16Target, ResourceType.Texture)],
            outputs: [new(SystemBufferNames.TextureColorF16Sample, ResourceType.Texture)],
            onSetup: (res) =>
            {
                res.Pass.Colors[0].ClearColor = Color.Transparent;
                res.Pass.Colors[0].LoadOp = LoadOp.Load;
                res.Pass.Colors[0].StoreOp = StoreOp.Store;
            }
        );
    }
}
