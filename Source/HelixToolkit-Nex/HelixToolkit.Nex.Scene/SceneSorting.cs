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
        var nodeInfos = world.GetComponents<NodeInfo>();
        var transforms = world.GetComponents<Transform>();
        var worldTransforms = world.GetComponents<WorldTransform>();
        var parents = world.GetComponents<Parent>();

        int level = 0;
        for (int i = 0; i < nodeInfos.Count; ++i)
        {
            ref var nodeInfo = ref nodeInfos[i];
            var entity = world.GetEntity(nodeInfo.EntityId);
            if (!entity.Valid)
            {
                continue;
            }

            ref var transform = ref transforms[entity];
            Debug.Assert(level <= nodeInfo.Level);
            level = nodeInfo.Level;

            var parentWorld = Matrix4x4.Identity;
            ref readonly var parent = ref parents[entity];
            if (parent.ParentEntity.Valid)
            {
                // No need for entity.TryGet — every Node always has a Parent component.
                if (transforms[parent.ParentEntity].Timestamp > transform.Timestamp)
                {
                    transform.MarkWorldDirty();
                }
                if (transform.IsWorldDirty)
                {
                    parentWorld = worldTransforms[parent.ParentEntity].Value;
                }
            }

            if (transform.IsWorldDirty)
            {
                if (transform.UpdateWorldTransform(parentWorld, out var worldMatrix))
                {
                    // Write directly to component storage — WorldTransform is derived data;
                    // firing ComponentChangedEvent on every frame update is unnecessary overhead.
                    worldTransforms[entity] = new WorldTransform(worldMatrix);
                    entity.NotifyComponentChanged<WorldTransform>();
                }
            }
        }
    }
}
