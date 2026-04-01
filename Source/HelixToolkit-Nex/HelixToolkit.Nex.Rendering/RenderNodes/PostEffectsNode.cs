namespace HelixToolkit.Nex.Rendering.RenderNodes;

public enum PostEffectPriority : uint
{
    AntiAliasing = 10,
    Bloom = 20,
    Highlight = 30,
    ToneMapping = 40,
    Other = 50,
}

public abstract class PostEffect() : Initializable
{
    public IContext? Context { internal set; get; }
    public bool Enabled { get; set; } = true;

    public Renderer? Renderer { internal set; get; }

    public abstract Color DebugColor { get; }

    /// <summary>
    /// Gets or sets the priority level of the effect. Effects with lower priority values are executed before those with higher values.
    /// </summary>
    public abstract uint Priority { get; }

    /// <summary>
    /// Called from <see cref="PostEffectsNode.AddToGraph"/> before the graph is compiled.
    /// Override to register textures or buffers that the effect owns into the shared
    /// <see cref="RenderGraph"/> so they are allocated and resized by the resource set,
    /// just like resources owned by any other <see cref="RenderNode"/>.
    /// </summary>
    /// <param name="graph">The render graph that is being built.</param>
    public virtual void RegisterResources(RenderGraph graph) { }

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
    /// <returns>
    /// Whether the effect applied successfully.
    /// </returns>
    public abstract bool Apply(in RenderResources res, ref string readSlot, ref string writeSlot);
}

public sealed class PostEffectsNode : RenderNode
{
    private readonly List<PostEffect> _effects = [];
    private readonly Dictionary<string, PostEffect> _effectMap = [];
    private bool _changed = true;

    // Resolved by AddToGraph and stored so OnRender can alternate slots across effects.
    private string _initialReadSlot = SystemBufferNames.TextureColorF16A;
    private string _initialWriteSlot = SystemBufferNames.TextureColorF16B;

    public override string Name => nameof(PostEffectsNode);
    public override Color4 DebugColor => Color.Aqua;

    /// <summary>
    /// Adds a post-processing effect to the collection.
    /// </summary>
    /// <remarks>If the effect is added successfully and the system is currently attached, the effect is
    /// initialized immediately.</remarks>
    /// <param name="effect">The <see cref="PostEffect"/> to add. The effect must have a unique name.</param>
    /// <exception cref="ArgumentException">Thrown if a post-effect with the same name as <paramref name="effect"/> already exists in the collection.</exception>
    public void AddEffect(PostEffect effect)
    {
        if (_effectMap.ContainsKey(effect.Name))
        {
            throw new ArgumentException(
                $"A post-effect with the name '{effect.Name}' has already been added."
            );
        }
        _effectMap[effect.Name] = effect;
        _effects.Add(effect);
        _changed = true;
        if (IsAttached)
        {
            effect.Initialize().CheckResult();
        }
    }

    /// <summary>
    /// Removes the effect with the specified name from the collection.
    /// </summary>
    /// <remarks>If an effect with the specified name exists, it is removed from the collection, and any
    /// associated resources are released.  The method has no effect if the specified name does not exist in the
    /// collection.</remarks>
    /// <param name="name">The name of the effect to remove. This value cannot be <see langword="null"/> or empty.</param>
    public void RemoveEffect(string name)
    {
        if (_effectMap.TryGetValue(name, out var effect))
        {
            effect.Teardown();
            _effectMap.Remove(name);
            _effects.Remove(effect);
            _changed = true;
        }
    }

    /// <summary>
    /// Attempts to retrieve a post-processing effect by its name.
    /// </summary>
    /// <param name="name">The name of the post-processing effect to retrieve. This parameter is case-sensitive.</param>
    /// <param name="effect">When this method returns, contains the <see cref="PostEffect"/> associated with the specified name, if the name
    /// exists in the collection; otherwise, <see langword="null"/>. This parameter is passed uninitialized.</param>
    /// <returns><see langword="true"/> if the effect with the specified name exists; otherwise, <see langword="false"/>.</returns>
    public bool TryGetEffect(string name, out PostEffect? effect)
    {
        return _effectMap.TryGetValue(name, out effect);
    }

    protected override bool BeginRender(in RenderResources res)
    {
        return true;
    }

    protected override void EndRender(in RenderResources res) { }

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
            if (effect.Apply(in res, ref read, ref write))
            {
                // Flip for next effect: what was just written becomes the next read source.
                (read, write) = (write, read);
            }
            res.CmdBuffer.PopDebugGroupLabel();
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
            effect.Renderer = Renderer;
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
        // Give each effect the opportunity to register its own graph-managed resources
        // before the graph is compiled, so allocations and screen-size rebuilds are handled
        // centrally by the resource set — just like resources from any other RenderNode.
        foreach (var effect in _effects)
        {
            effect.RegisterResources(graph);
        }

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
