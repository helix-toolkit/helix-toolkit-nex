namespace HelixToolkit.Nex.Rendering;

/// <summary>
/// Declares the broad execution phase a render/compute pass belongs to.
/// The graph compiler automatically orders passes so that all passes in an
/// earlier stage complete before any pass in a later stage begins, without
/// requiring every node to name its predecessors explicitly.
/// <para>
/// Within a single stage, fine-grained ordering is still expressed through
/// resource edges (inputs/outputs) or the explicit <c>after</c> list.
/// </para>
/// </summary>
public enum RenderStage
{
    /// <summary>CPU/GPU data preparation: frustum culling etc.</summary>
    Prepare = 0,

    /// <summary>Opaque geometry: depth pre-pass, light culling, opaque meshes, point clouds, etc.</summary>
    Opaque = 10,

    /// <summary>Transparent geometry: WBOIT render + composite, alpha-blended passes, etc.</summary>
    Transparent = 20,

    /// <summary>Full-screen HDR post-processing effects (FXAA, bloom, …). Runs before tone mapping.</summary>
    PostProcess = 30,

    /// <summary>
    /// HDR-to-LDR conversion. Separating this from <see cref="PostProcess"/> ensures that
    /// all HDR effects complete before the scene is linearised, and that all
    /// <see cref="Overlay"/> passes receive an LDR surface to draw onto.
    /// </summary>
    ToneMap = 35,

    /// <summary>
    /// LDR overlays rendered on top of the tone-mapped image: gizmos, debug geometry,
    /// editor widgets, etc. Depth buffer from the opaque pass is still available here.
    /// </summary>
    Overlay = 40,

    /// <summary>Final blit to the swap-chain / output texture.</summary>
    Output = 50,
}

public readonly record struct RenderResource(string Name, ResourceType Type);

public sealed record ResourceBuildParams(RenderContext Context);

public sealed class RenderGraph(IServiceProvider serviceProvider) : Initializable
{
    private static readonly ITracer _tracer = TracerFactory.GetTracer(nameof(RenderGraph));
    private static readonly ILogger _logger = LogManager.Create<RenderGraph>();

    public sealed class GraphNode(
        string passName,
        IList<RenderResource> inputs,
        IList<RenderResource> outputs,
        IList<string> after,
        string? pingPongGroup = null,
        RenderStage stage = RenderStage.Opaque
    )
    {
        public readonly RenderPass Pass = new();
        public readonly Framebuffer Framebuffer = new();
        public readonly Dependencies Dependencies = new();
        public readonly string PassName = passName;

        // Stored as List so ResolvePingPongSlots can patch them before the dependency graph is built.
        public readonly List<RenderResource> Inputs = [.. inputs];
        public readonly List<RenderResource> Outputs = [.. outputs];

        /// <summary>
        /// Names of passes that must execute before this one, regardless of resource dependencies.
        /// </summary>
        public readonly IList<string> After = after;

        /// <summary>
        /// The broad execution phase this pass belongs to.
        /// The graph compiler adds automatic edges so that every pass in a lower-numbered
        /// stage finishes before any pass in a higher-numbered stage begins.
        /// </summary>
        public readonly RenderStage Stage = stage;

        /// <summary>
        /// The ping-pong group this pass belongs to, or <see langword="null"/> if not a ping-pong pass.
        /// </summary>
        public readonly string? PingPongGroup = pingPongGroup;

        /// <summary>
        /// The name of the resource slot this pass reads from (resolved by <see cref="RenderGraph.Compile"/>
        /// for ping-pong passes; <see langword="null"/> for regular passes).
        /// </summary>
        public string? PingPongReadSlot { get; internal set; }

        /// <summary>
        /// The name of the resource slot this pass writes to (resolved by <see cref="RenderGraph.Compile"/>
        /// for ping-pong passes; <see langword="null"/> for regular passes).
        /// </summary>
        public string? PingPongWriteSlot { get; internal set; }
    }

    // -----------------------------------------------------------------------
    // Ping-pong group storage
    // -----------------------------------------------------------------------

    /// <summary>
    /// Describes a ping-pong texture pair registered with the graph.
    /// </summary>
    private sealed class PingPongGroupEntry(string slotA, string slotB)
    {
        /// <summary>First physical texture slot name (e.g. TextureColorF16Target).</summary>
        public readonly string SlotA = slotA;

        /// <summary>Second physical texture slot name (e.g. TextureColorF16Sample).</summary>
        public readonly string SlotB = slotB;
    }

    private readonly Dictionary<string, PingPongGroupEntry> _pingPongGroups = [];

    /// <summary>
    /// Registers a ping-pong texture pair under a logical group name.
    /// The two slots alternate as read/write targets across consecutive ping-pong passes in the
    /// same group, eliminating the need for any runtime buffer swap.
    /// </summary>
    /// <param name="groupName">A unique logical name for this ping-pong group.</param>
    /// <param name="slotA">Resource name of the first physical texture (e.g. <see cref="SystemBufferNames.TextureColorF16A"/>).</param>
    /// <param name="slotB">Resource name of the second physical texture (e.g. <see cref="SystemBufferNames.TextureColorF16B"/>).</param>
    /// <returns>The current instance for method chaining.</returns>
    public RenderGraph AddPingPongGroup(string groupName, string slotA, string slotB)
    {
        _pingPongGroups[groupName] = new PingPongGroupEntry(slotA, slotB);
        IsDirty = true;
        return this;
    }

    /// <summary>
    /// Adds a pass that participates in a ping-pong buffer group. The graph compiler
    /// statically assigns which slot is read and which is written for this pass based on
    /// the position of the pass in the group's execution chain — no runtime buffer swap is needed.
    /// </summary>
    /// <param name="passName">The unique name of the pass.</param>
    /// <param name="pingPongGroup">The name of the ping-pong group registered via <see cref="AddPingPongGroup"/>.</param>
    /// <param name="extraInputs">Additional non-ping-pong input resources required by the pass.</param>
    /// <param name="extraOutputs">Additional non-ping-pong output resources produced by the pass.</param>
    /// <param name="onSetup">
    /// Setup action for the pass. Use <see cref="GraphNode.PingPongReadSlot"/> and
    /// <see cref="GraphNode.PingPongWriteSlot"/> (accessible via the compiled
    /// <see cref="SortedPasses"/>) to bind the correct textures — or use the
    /// <see cref="RenderResources"/> overload that resolves them automatically.
    /// </param>
    /// <param name="after">Optional explicit ordering constraints (pass names that must precede this one).</param>
    /// <param name="stage">
    /// The broad execution phase this pass belongs to. Defaults to <see cref="RenderStage.PostProcess"/>.
    /// </param>
    /// <returns>The current instance for method chaining.</returns>
    public RenderGraph AddPingPongPass(
        string passName,
        string pingPongGroup,
        IList<RenderResource> extraInputs,
        IList<RenderResource> extraOutputs,
        IList<string>? after = null,
        RenderStage stage = RenderStage.PostProcess
    )
    {
        if (_passes.ContainsKey(passName))
        {
            throw new InvalidOperationException(
                $"A pass with the name '{passName}' already exists in the render graph."
            );
        }
        if (!_pingPongGroups.ContainsKey(pingPongGroup))
        {
            throw new InvalidOperationException(
                $"Ping-pong group '{pingPongGroup}' has not been registered. Call AddPingPongGroup first."
            );
        }

        // Inputs/outputs for the dependency graph are resolved at Compile() time once slot
        // assignments are known. For now we store a sentinel — Compile() will patch them.
        var node = new GraphNode(
            passName,
            extraInputs,
            extraOutputs,
            after ?? [],
            pingPongGroup,
            stage
        );

        _passes.Add(passName, node);
        IsDirty = true;
        return this;
    }

    private readonly IServiceProvider _services = serviceProvider;
    private readonly Dictionary<string, GraphNode> _passes = [];
    private readonly Dictionary<string, List<GraphNode>> _resourceProducers = [];
    private readonly List<GraphNode> _sortedPasses = [];
    private long _lastUpdatedTimeStamp = 0;

    public IReadOnlyList<GraphNode> SortedPasses => _sortedPasses;

    public bool IsDirty { private set; get; } = true;

    public override string Name => nameof(RenderGraph);

    /// <summary>
    /// Adds a texture to the render graph with the specified name and build function.
    /// The texture builder is registered on the <see cref="RenderContext.ResourceSet"/>
    /// during <see cref="Execute"/>.
    /// </summary>
    /// <param name="name">The unique name of the texture to add. Must not already exist in the render graph.</param>
    /// <param name="buildFunc">A function that defines how to build the texture resource. Can be <see langword="null"/> if no custom build is
    /// required.</param>
    /// <param name="dependsOnScreenSize">Indicates whether the texture's size should depend on the screen size. The default is <see langword="true"/>.</param>
    /// <returns>The current instance of the <see cref="RenderGraph"/> to allow for method chaining.</returns>
    /// <exception cref="InvalidOperationException">Thrown if a texture with the specified <paramref name="name"/> and its creation function already exists in the render graph.</exception>
    public RenderGraph AddTexture(
        string name,
        Func<ResourceBuildParams, TextureResource>? buildFunc,
        bool dependsOnScreenSize = true
    )
    {
        if (
            buildFunc is not null
            && _textureRegistrations.TryGetValue(name, out var existing)
            && existing.BuildFunc is not null
        )
        {
            throw new InvalidOperationException(
                $"A texture with the name '{name}' already exists in the render graph."
            );
        }
        _textureRegistrations[name] = new TextureRegistration(buildFunc, dependsOnScreenSize);
        IsDirty = true;
        return this;
    }

    public RenderGraph AddFinalOutputTexture()
    {
        return AddTexture(SystemBufferNames.FinalOutputTexture, null);
    }

    /// <summary>
    /// Adds a buffer to the render graph with the specified name and build function.
    /// The buffer builder is registered on the <see cref="RenderContext.ResourceSet"/>
    /// during <see cref="Execute"/>.
    /// </summary>
    /// <param name="name">The unique name of the buffer to add. Must not already exist in the render graph.</param>
    /// <param name="buildFunc">A function that defines how to build the buffer resource. Can be <see langword="null"/> if no specific build
    /// function is required.</param>
    /// <param name="dependsOnScreenSize">Indicates whether the buffer's size depends on the screen size. The default is <see langword="true"/>.</param>
    /// <returns>The current instance of <see cref="RenderGraph"/> to allow for method chaining.</returns>
    /// <exception cref="InvalidOperationException">Thrown if a buffer with the specified <paramref name="name"/> and its creation function already exists in the render graph.</exception>
    public RenderGraph AddBuffer(
        string name,
        Func<ResourceBuildParams, BufferResource>? buildFunc,
        bool dependsOnScreenSize = true
    )
    {
        if (
            buildFunc is not null
            && _bufferRegistrations.TryGetValue(name, out var existing)
            && existing.BuildFunc is not null
        )
        {
            throw new InvalidOperationException(
                $"A buffer with the name '{name}' already exists in the render graph."
            );
        }
        _bufferRegistrations[name] = new BufferRegistration(buildFunc, dependsOnScreenSize);
        IsDirty = true;
        return this;
    }

    /// <summary>
    /// Adds a regular (non-ping-pong) render pass.
    /// </summary>
    /// <param name="after">
    /// Optional list of pass names that must execute before this pass, regardless of resource dependencies.
    /// </param>
    /// <param name="stage">
    /// The broad execution phase this pass belongs to. The graph compiler automatically orders passes
    /// so all passes in an earlier stage complete before any pass in a later stage. Defaults to <see cref="RenderStage.Opaque"/>.
    /// </param>
    public RenderGraph AddPass(
        string passName,
        IList<RenderResource> inputs,
        IList<RenderResource> outputs,
        IList<string>? after = null,
        RenderStage stage = RenderStage.Opaque
    )
    {
        if (_passes.ContainsKey(passName))
        {
            throw new InvalidOperationException(
                $"A pass with the name '{passName}' already exists in the render graph."
            );
        }
        _passes.Add(passName, new GraphNode(passName, inputs, outputs, after ?? [], stage: stage));
        IsDirty = true;
        return this;
    }

    /// <summary>
    /// Removes a render pass by its name.
    /// </summary>
    public RenderGraph RemovePass(string passName)
    {
        _passes.Remove(passName);
        _resourceProducers.Clear();
        _sortedPasses.Clear();
        IsDirty = true;
        return this;
    }

    /// <summary>
    /// Compiles the render graph: resolves ping-pong slot assignments, identifies resource
    /// producers, builds the dependency graph, and performs a topological sort.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when a circular dependency is detected.</exception>
    public void Compile()
    {
        using var t = _tracer.BeginScope(nameof(Compile));
        _resourceProducers.Clear();
        _sortedPasses.Clear();

        // ----------------------------------------------------------------
        // Step 0: Resolve ping-pong slot assignments.
        //
        // For each ping-pong group, walk all passes that belong to it in
        // registration order (stabilised by `after` chains), alternating
        // which slot is read and which is written.  The first pass in a
        // group reads SlotA and writes SlotB; the second reads SlotB and
        // writes SlotA; and so on.  Slot assignments are stored on the
        // GraphNode and the real inputs/outputs lists are patched so the
        // dependency edges below are built correctly.
        // ----------------------------------------------------------------
        ResolvePingPongSlots();

        // ----------------------------------------------------------------
        // Step 1: Identify producers for each resource.
        // ----------------------------------------------------------------
        foreach (var pass in _passes.Values)
        {
            foreach (var output in pass.Outputs)
            {
                if (!_resourceProducers.ContainsKey(output.Name))
                {
                    _resourceProducers[output.Name] = [];
                }
                _resourceProducers[output.Name].Add(pass);
            }
        }

        // ----------------------------------------------------------------
        // Step 2: Build dependency graph and calculate in-degrees.
        // ----------------------------------------------------------------
        var inDegree = new Dictionary<GraphNode, int>();
        var adjacencyList = new Dictionary<GraphNode, List<GraphNode>>();

        foreach (var pass in _passes.Values)
        {
            inDegree[pass] = 0;
            adjacencyList[pass] = [];
        }

        var addedEdges = new HashSet<(GraphNode, GraphNode)>();
        foreach (var pass in _passes.Values)
        {
            // Resource-based edges
            foreach (var input in pass.Inputs)
            {
                if (!_resourceProducers.TryGetValue(input.Name, out var producers))
                {
                    continue; // external resource — no internal producer
                }
                foreach (var producer in producers)
                {
                    if (addedEdges.Add((producer, pass)))
                    {
                        adjacencyList[producer].Add(pass);
                        inDegree[pass]++;
                    }
                }
            }

            // Explicit ordering edges (`after`)
            foreach (var afterName in pass.After)
            {
                if (!_passes.TryGetValue(afterName, out var predecessor))
                {
                    _logger.LogWarning(
                        "Pass '{PASS}' declares an 'after' dependency on '{AFTER}', but that pass does not exist in the graph. Ignoring.",
                        pass.PassName,
                        afterName
                    );
                    continue;
                }
                if (addedEdges.Add((predecessor, pass)))
                {
                    adjacencyList[predecessor].Add(pass);
                    inDegree[pass]++;
                }
            }

            // Stage-based ordering edges:
            // Every pass in an earlier stage is a predecessor of every pass in a later stage.
            foreach (var other in _passes.Values)
            {
                if (other.Stage < pass.Stage && addedEdges.Add((other, pass)))
                {
                    adjacencyList[other].Add(pass);
                    inDegree[pass]++;
                }
            }
        }

        // ----------------------------------------------------------------
        // Step 3: Kahn's algorithm for topological sort.
        // ----------------------------------------------------------------
        var queue = new Queue<GraphNode>();
        foreach (var kvp in inDegree)
        {
            if (kvp.Value == 0)
            {
                queue.Enqueue(kvp.Key);
            }
        }

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            _sortedPasses.Add(current);
            foreach (var dependent in adjacencyList[current])
            {
                inDegree[dependent]--;
                if (inDegree[dependent] == 0)
                {
                    queue.Enqueue(dependent);
                }
            }
        }

        // ----------------------------------------------------------------
        // Step 4: Cycle check.
        // ----------------------------------------------------------------
        if (_sortedPasses.Count != _passes.Count)
        {
            var unsortedPasses = _passes
                .Values.Where(p => !_sortedPasses.Contains(p))
                .Select(p => p.PassName);
            throw new InvalidOperationException(
                $"Circular dependency detected in render graph. Affected passes: {string.Join(", ", unsortedPasses)}"
            );
        }

        IsDirty = false;
        _lastUpdatedTimeStamp = Stopwatch.GetTimestamp();
    }

    /// <summary>
    /// Walks each ping-pong group's passes (ordered by their <c>after</c> chains) and assigns
    /// alternating read/write slot names. Also patches each node's <see cref="GraphNode.Inputs"/>
    /// and <see cref="GraphNode.Outputs"/> lists so the dependency graph is built correctly, and
    /// replaces the placeholder <see cref="GraphNode.OnSetupRenderParams"/> with a closure that
    /// forwards the resolved slot names to the original setup delegate.
    /// </summary>
    private void ResolvePingPongSlots()
    {
        // Group passes by their ping-pong group name.
        var groupedPasses = new Dictionary<string, List<GraphNode>>();
        foreach (var pass in _passes.Values)
        {
            if (pass.PingPongGroup is null)
            {
                continue;
            }
            if (!groupedPasses.TryGetValue(pass.PingPongGroup, out var list))
            {
                list = [];
                groupedPasses[pass.PingPongGroup] = list;
            }
            list.Add(pass);
        }

        foreach (var (groupName, passes) in groupedPasses)
        {
            if (!_pingPongGroups.TryGetValue(groupName, out var group))
            {
                _logger.LogWarning(
                    "Ping-pong group '{GROUP}' used by passes but not registered. Skipping slot assignment.",
                    groupName
                );
                continue;
            }

            // Sort passes within the group by their `after` chain so that a pass declared
            // `after: ["X"]` where X is in the same group always gets the next slot.
            var orderedPasses = TopologicalSortGroup(passes);

            bool readA = true; // first pass reads SlotA, writes SlotB
            foreach (var pass in orderedPasses)
            {
                var readSlot = readA ? group.SlotA : group.SlotB;
                var writeSlot = readA ? group.SlotB : group.SlotA;
                readA = !readA;

                pass.PingPongReadSlot = readSlot;
                pass.PingPongWriteSlot = writeSlot;

                // Patch inputs/outputs so the main Compile() dependency pass sees real edges.
                var patchedInputs = new List<RenderResource>(pass.Inputs)
                {
                    new(readSlot, ResourceType.Texture),
                };
                // Write slot is captured in the setup closure only — do NOT add it as a graph
                // output edge, or it creates a backward resource dependency that conflicts with
                // the forward stage edge (e.g. ToneMappingNode writes A → PostEffectsNode reads A).
                var patchedOutputs = new List<RenderResource>(pass.Outputs);

                // Replace inputs/outputs via reflection-free helper fields.
                // GraphNode.Inputs/Outputs are readonly IList, so we replace the node entirely.
                // Instead, we use a small trick: wrap the existing node's lists via mutation
                // since IList<T> allows Add/Clear.
                pass.Inputs.Clear();
                pass.Inputs.AddRange(patchedInputs);
                pass.Outputs.Clear();
                pass.Outputs.AddRange(patchedOutputs);
            }
        }
    }

    /// <summary>
    /// Topologically sorts a subset of passes (within a single ping-pong group) using
    /// intra-group <c>after</c> edges and <see cref="GraphNode.Stage"/> ordering.
    /// Stage ordering is treated as an implicit edge: every pass in a lower stage
    /// precedes every pass in a higher stage, exactly like the main <see cref="Compile"/> sort.
    /// </summary>
    private static List<GraphNode> TopologicalSortGroup(List<GraphNode> passes)
    {
        var nameMap = passes.ToDictionary(p => p.PassName);
        var inDegree = passes.ToDictionary(p => p, _ => 0);
        var adj = passes.ToDictionary(p => p, _ => new List<GraphNode>());
        var addedEdges = new HashSet<(GraphNode, GraphNode)>();

        foreach (var pass in passes)
        {
            // Intra-group explicit after edges
            foreach (var afterName in pass.After)
            {
                if (
                    nameMap.TryGetValue(afterName, out var predecessor)
                    && addedEdges.Add((predecessor, pass))
                )
                {
                    adj[predecessor].Add(pass);
                    inDegree[pass]++;
                }
            }

            // Intra-group stage edges
            foreach (var other in passes)
            {
                if (other.Stage < pass.Stage && addedEdges.Add((other, pass)))
                {
                    adj[other].Add(pass);
                    inDegree[pass]++;
                }
            }
        }

        // Kahn's algorithm — seed with stage-stable order so slot assignment is deterministic.
        var queue = new Queue<GraphNode>(
            passes.Where(p => inDegree[p] == 0).OrderBy(p => (int)p.Stage)
        );
        var result = new List<GraphNode>(passes.Count);
        while (queue.Count > 0)
        {
            var n = queue.Dequeue();
            result.Add(n);
            foreach (var dep in adj[n].OrderBy(d => (int)d.Stage))
            {
                if (--inDegree[dep] == 0)
                {
                    queue.Enqueue(dep);
                }
            }
        }

        // Any remaining passes (no intra-group constraint) — append sorted by stage.
        foreach (var pass in passes.OrderBy(p => (int)p.Stage))
        {
            if (!result.Contains(pass))
            {
                result.Add(pass);
            }
        }
        return result;
    }

    public bool EnsureResources(RenderContext context)
    {
        var resourceSet = context.ResourceSet;
        if (IsDirty)
        {
            Compile();
        }

        if (context.WindowSize.Width <= 0 || context.WindowSize.Height <= 0)
        {
            _logger.LogWarning(
                "RenderContext has invalid window size {SIZE}. Skipping render graph execution.",
                context.WindowSize
            );
            return false;
        }

        resourceSet.CurrentGraph = this;
        if (resourceSet.LastUpdatedTimeStamp != _lastUpdatedTimeStamp)
        {
            ApplyRegistrations(resourceSet);
            resourceSet.LastUpdatedTimeStamp = _lastUpdatedTimeStamp;
            resourceSet.EnsureResources(context, true);
        }
        else
        {
            resourceSet.EnsureResources(context, false);
        }

        return true;
    }

    public bool Execute(
        RenderContext context,
        ICommandBuffer cmdBuf,
        IReadOnlyDictionary<string, RenderNode> nodes
    )
    {
        if (context.Data is null)
        {
            return false;
        }

        context.ResourceSet.SetupSystemResources(context);
        foreach (var pass in _sortedPasses)
        {
            if (!nodes.TryGetValue(pass.PassName, out var node))
            {
                _logger.LogTrace(
                    "No render node found for pass '{PASS}'. Skipping this pass.",
                    pass.PassName
                );
                continue;
            }
            if (!node.Enabled)
            {
                continue;
            }
            node.Render(
                new RenderResources(
                    context,
                    cmdBuf,
                    pass.Pass,
                    pass.Framebuffer,
                    pass.Dependencies,
                    context.ResourceSet.Textures,
                    context.ResourceSet.Buffers
                )
            );
        }
        return true;
    }

    protected override ResultCode OnInitializing()
    {
        Compile();
        return ResultCode.Ok;
    }

    protected override ResultCode OnTearingDown()
    {
        return ResultCode.Ok;
    }

    // -----------------------------------------------------------------------
    // Resource registration storage (deferred until Execute)
    // -----------------------------------------------------------------------

    private readonly record struct TextureRegistration(
        Func<ResourceBuildParams, TextureResource>? BuildFunc,
        bool DependsOnScreenSize
    );

    private readonly record struct BufferRegistration(
        Func<ResourceBuildParams, BufferResource>? BuildFunc,
        bool DependsOnScreenSize
    );

    private readonly Dictionary<string, TextureRegistration> _textureRegistrations = [];
    private readonly Dictionary<string, BufferRegistration> _bufferRegistrations = [];

    private void ApplyRegistrations(RenderGraphResourceSet resourceSet)
    {
        foreach (var kvp in _textureRegistrations)
        {
            resourceSet.AddTexture(kvp.Key, kvp.Value.BuildFunc, kvp.Value.DependsOnScreenSize);
        }
        foreach (var kvp in _bufferRegistrations)
        {
            resourceSet.AddBuffer(kvp.Key, kvp.Value.BuildFunc, kvp.Value.DependsOnScreenSize);
        }
    }
}
