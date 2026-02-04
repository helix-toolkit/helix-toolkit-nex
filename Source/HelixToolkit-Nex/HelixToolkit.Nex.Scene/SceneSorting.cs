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
                    transform.UpdateWorldTransform(Matrix4x4.Identity);
                }
                else
                {
                    transform.UpdateWorldTransform(node.Parent!.Transform.WorldTransform);
                }

                var level = node.Info.Level;
                for (int j = i + 1; j < sortedNodes.Count; ++j)
                {
                    if (sortedNodes[j].Info.Level <= level)
                    {
                        break; // Stop updating when we reach a node at the same or higher level
                    }
                    sortedNodes[j].Transform.MarkWorldDirty();
                }
            }
        }
    }
}
