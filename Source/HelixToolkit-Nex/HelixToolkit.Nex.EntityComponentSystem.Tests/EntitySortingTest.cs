namespace HelixToolkit.Nex.ECS.Tests;

public struct HierarchyInfo : ISortable<HierarchyInfo>
{
    public int Level;

    public bool Compare(ref HierarchyInfo obj)
    {
        return Level < obj.Level;
    }
}

[TestClass]
public class EntitySortingTest
{
    [TestMethod]
    [DataRow(10)]
    [DataRow(100)]
    [DataRow(1000)]
    public void Sorting(int count)
    {
        using var world = World.CreateWorld();
        var entities = new List<Entity>();
        for (var i = 0; i < count; i++)
        {
            entities.Add(world.CreateEntity());
            entities.Last().Set(new HierarchyInfo { Level = count - i - 1 });
        }

        var components = world.GetComponents<HierarchyInfo>();
        Assert.AreEqual(count, components.Count);
        for (var i = 0; i < count; ++i)
        {
            Assert.AreEqual(count - i - 1, components[i].Level);
        }

        world.SortComponent<HierarchyInfo>();

        for (var i = 0; i < count; ++i)
        {
            Assert.AreEqual(i, components[i].Level);
        }
    }
}
