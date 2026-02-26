namespace HelixToolkit.Nex.Rendering;

public abstract class PostEffect() : Initializable
{
    public IContext? Context { internal set; get; }
    public bool Enabled { get; set; } = true;
    public abstract void Apply(RenderContext context, ICommandBuffer cmdBuffer, Dependencies deps);
}

public sealed class PostEffectsNode : RenderNode
{
    private readonly List<PostEffect> _effects = [];
    public override string Name => nameof(PostEffectsNode);

    public override Color4 DebugColor => Color.Aqua;

    public void AddEffect(PostEffect effect)
    {
        _effects.Add(effect);
    }

    protected override void OnRender(
        RenderContext context,
        ICommandBuffer cmdBuffer,
        Dependencies deps
    )
    {
        foreach (var effect in _effects)
        {
            if (!effect.Enabled)
            {
                continue;
            }
            effect.Apply(context, cmdBuffer, deps);
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
}
