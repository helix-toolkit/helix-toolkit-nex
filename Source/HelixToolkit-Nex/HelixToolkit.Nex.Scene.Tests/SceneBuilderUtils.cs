using Arch.Core;
using HelixToolkit.Nex.Scene;

namespace HelixToolkit.Nex.Tests.Scene;

internal class SceneBuilderUtils
{
    public static int AddChildRecursively(Node parent, int level, int maxLevel, int childCount, World world)
    {
        if (level >= maxLevel)
        {
            return 0;
        }
        int count = 0;
        for (int i = 0; i < childCount; ++i)
        {
            var child = new Node(world)
            {
                Name = $"Child Node {level}.{i}"
            };
            parent.AddChild(child);
            ++count;
            count += AddChildRecursively(child, level + 1, maxLevel, childCount, world);
        }
        return count;
    }
}
