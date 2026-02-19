namespace HelixToolkit.Nex.Rendering;

public sealed class RenderGraph
{
    private static readonly ITracer _tracer = TracerFactory.GetTracer(nameof(RenderGraph));

    private sealed class Node
    {
        public Renderer PPass = null!;
        public List<Node> Dependencies = [];
        public List<Node> Dependents = [];
    }

    private readonly List<Renderer> _passes = [];
    private readonly Dictionary<string, Renderer> _resourceProducers = [];
    private readonly List<Renderer> _sortedPasses = [];
    public bool IsDirty { private set; get; } = true;

    public void AddPass(Renderer pass)
    {
        _passes.Add(pass);
        IsDirty = true;
    }

    public void RemovePass(Renderer pass)
    {
        _passes.Remove(pass);
        _resourceProducers.Clear();
        _sortedPasses.Clear();
        IsDirty = true;
    }

    public void Compile()
    {
        using var t = _tracer.BeginScope(nameof(Compile));
        _resourceProducers.Clear();
        var nodes = new Dictionary<Renderer, Node>();

        // 1. Identify producers for each resource
        foreach (var pass in _passes)
        {
            nodes[pass] = new Node { PPass = pass };
            foreach (var output in pass.GetOutputs())
            {
                _resourceProducers[output] = pass;
            }
        }

        // 2. Build dependency graph
        foreach (var pass in _passes)
        {
            var consumerNode = nodes[pass];
            foreach (var input in pass.GetInputs())
            {
                if (_resourceProducers.TryGetValue(input, out var producer))
                {
                    if (producer != pass)
                    {
                        var producerNode = nodes[producer];
                        if (!consumerNode.Dependencies.Contains(producerNode))
                        {
                            consumerNode.Dependencies.Add(producerNode);
                            producerNode.Dependents.Add(consumerNode);
                        }
                    }
                }
            }
        }

        // 3. Topological Sort (Kahn's algorithm)
        _sortedPasses.Clear();
        var queue = new Queue<Node>();
        var inDegree = new Dictionary<Node, int>();

        foreach (var node in nodes.Values)
        {
            inDegree[node] = node.Dependencies.Count;
            if (node.Dependencies.Count == 0)
            {
                queue.Enqueue(node);
            }
        }

        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            _sortedPasses.Add(node.PPass);

            foreach (var dependent in node.Dependents)
            {
                inDegree[dependent]--;
                if (inDegree[dependent] == 0)
                {
                    queue.Enqueue(dependent);
                }
            }
        }

        if (_sortedPasses.Count != _passes.Count)
        {
            // Cycle detected - find the passes involved in the cycle
            var passesInCycle = _passes.Except(_sortedPasses).ToList();
            var cyclePassNames = string.Join(", ", passesInCycle.Select(p => p.Name));
            throw new InvalidOperationException(
                $"Cyclic dependency detected in render graph. Passes involved in cycle: {cyclePassNames}"
            );
        }
        IsDirty = false;
    }

    public void Execute(RenderContext context, ICommandBuffer cmdBuf)
    {
        if (IsDirty)
        {
            Compile();
        }
        // Execute sorted passes
        foreach (var pass in _sortedPasses)
        {
            if (pass.Enabled)
            {
                pass.Render(context, cmdBuf);
            }
        }
    }
}
