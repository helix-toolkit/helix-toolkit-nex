namespace HelixToolkit.Nex.Scene;

public static class SceneSorting
{
    private static readonly ThreadLocal<Stack<KeyValuePair<int, IReadOnlyList<Node>>>> _stackLocal =
        new(() => new());

    public static void Flatten(
        this IReadOnlyList<Node> nodes,
        Func<Node, bool>? condition,
        IList<Node> sortedNodes
    )
    {
        var stack = _stackLocal.Value;
        Debug.Assert(stack != null, "Stack should not be null");
        var i = -1;
        var level = 0;
        var currNodes = nodes;
        while (true)
        {
            var count = currNodes.Count;
            while (++i < count)
            {
                var node = currNodes[i];
                if (condition != null && !condition(node))
                {
                    continue;
                }
                sortedNodes.Add(node);
                if (!node.HasChildren)
                {
                    continue;
                }
                stack.Push(new KeyValuePair<int, IReadOnlyList<Node>>(i, currNodes));
                i = -1;
                ++level;
                currNodes = node.Children!;
                count = currNodes.Count;
            }
            if (stack.Count == 0)
            {
                break;
            }
            var prev = stack.Pop();
            i = prev.Key;
            --level;
            currNodes = prev.Value;
        }
    }

    public static void Flatten(this Node root, Func<Node, bool>? condition, IList<Node> sortedNodes)
    {
        Flatten([root], condition, sortedNodes);
    }

    public static void UpdateTransforms(this IReadOnlyList<Node> sortedNodes)
    {
        for (int i = 0; i < sortedNodes.Count; ++i)
        {
            var node = sortedNodes[i];
            ref var transform = ref node.Transform;
            if (transform.IsWorldDirty)
            {
                if (!node.HasParent)
                {
                    if (transform.UpdateWorldTransform(Matrix4x4.Identity, out var world))
                    {
                        node.SetWorldTransform(new WorldTransform(world));
                    }
                }
                else
                {
                    if (
                        transform.UpdateWorldTransform(
                            node.Parent!.WorldTransform.Value,
                            out var world
                        )
                    )
                    {
                        node.SetWorldTransform(new WorldTransform(world));
                    }
                }

                var level = node.Level;
                for (int j = i + 1; j < sortedNodes.Count; ++j)
                {
                    if (sortedNodes[j].Level <= level)
                    {
                        break; // Stop updating when we reach a node at the same or higher level
                    }
                    sortedNodes[j].Transform.MarkWorldDirty();
                }
            }
        }
    }

    public static void SortSceneNodes(this World world)
    {
        world.SortComponent<NodeInfo>();
    }

    public static void UpdateTransforms(this World world)
    {
        var infos = world.GetComponents<NodeInfo>();
        var level = 0;
        foreach (var info in infos)
        {
            var node = info.Node;
            if (node == null)
            {
                continue;
            }
            Debug.Assert(level <= node.Level);
            level = node.Level;
            ref var transform = ref node.Transform;
            if (transform.IsLocalDirty || transform.IsWorldDirty)
            {
                if (!node.HasParent)
                {
                    if (transform.UpdateWorldTransform(Matrix4x4.Identity, out var worldTransform))
                    {
                        node.SetWorldTransform(new WorldTransform(worldTransform));
                    }
                }
                else
                {
                    if (
                        transform.UpdateWorldTransform(
                            node.Parent!.WorldTransform.Value,
                            out var worldTransform
                        )
                    )
                    {
                        node.SetWorldTransform(new WorldTransform(worldTransform));
                    }
                }
            }
        }
    }
}
