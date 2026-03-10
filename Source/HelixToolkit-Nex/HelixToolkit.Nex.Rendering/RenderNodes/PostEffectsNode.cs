namespace HelixToolkit.Nex.Rendering.RenderNodes;

public abstract class PostEffect() : Initializable
{
    public IContext? Context { internal set; get; }
    public bool Enabled { get; set; } = true;
    public abstract void Apply(in RenderResources res);
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

    protected override void OnRender(in RenderResources res)
    {
        foreach (var effect in _effects)
        {
            if (!effect.Enabled)
            {
                continue;
            }
            effect.Apply(in res);
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
        graph.AddPass(
            nameof(PostEffectsNode),
            inputs: [new(SystemBufferNames.TextureColorF16, ResourceType.Texture)],
            outputs: [new(SystemBufferNames.FinalOutputTexture, ResourceType.Texture)],
            onSetup: (res) =>
            {
                res.Framebuf.Colors[0].Texture = res.Textures[SystemBufferNames.FinalOutputTexture];
                res.Pass.Colors[0].ClearColor = Color.Transparent;
                res.Pass.Colors[0].LoadOp = LoadOp.Clear;
                res.Pass.Colors[0].StoreOp = StoreOp.Store;
                res.Deps.Textures[0] = res.Textures[SystemBufferNames.TextureColorF16];
            }
        );
    }
}
