namespace HelixToolkit.Nex.ECS.Tests;

public struct HierarchyInfo : ISortable<HierarchyInfo>
{
    public int Level;

    public bool Compare(ref HierarchyInfo obj)
    {
        return Level < obj.Level;
    }
}

public struct HierarchyInfoDescending : ISortable<HierarchyInfoDescending>
{
    public int Level;

    public bool Compare(ref HierarchyInfoDescending obj)
    {
        return Level > obj.Level;
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

    /// <summary>
    /// Sorting an already-sorted list should be a no-op and produce the same order.
    /// </summary>
    [TestMethod]
    [DataRow(10)]
    [DataRow(100)]
    [DataRow(1000)]
    public void SortingAlreadySorted(int count)
    {
        using var world = World.CreateWorld();
        var entities = new List<Entity>();
        for (var i = 0; i < count; i++)
        {
            entities.Add(world.CreateEntity());
            entities.Last().Set(new HierarchyInfo { Level = i });
        }

        world.SortComponent<HierarchyInfo>();

        var components = world.GetComponents<HierarchyInfo>();
        Assert.AreEqual(count, components.Count);
        for (var i = 0; i < count; ++i)
        {
            Assert.AreEqual(i, components[i].Level);
        }
    }

    /// <summary>
    /// All components share the same level; sort should not alter storage integrity.
    /// </summary>
    [TestMethod]
    [DataRow(10)]
    [DataRow(100)]
    public void SortingAllSameLevel(int count)
    {
        using var world = World.CreateWorld();
        var entities = new List<Entity>();
        for (var i = 0; i < count; i++)
        {
            entities.Add(world.CreateEntity());
            entities.Last().Set(new HierarchyInfo { Level = 42 });
        }

        world.SortComponent<HierarchyInfo>();

        var components = world.GetComponents<HierarchyInfo>();
        Assert.AreEqual(count, components.Count);
        for (var i = 0; i < count; ++i)
        {
            Assert.AreEqual(42, components[i].Level);
        }
        Assert.IsTrue(world.GetComponentManager<HierarchyInfo>()!.VerifyStorage());
    }

    /// <summary>
    /// A single-element manager should sort without any error.
    /// </summary>
    [TestMethod]
    public void SortingSingleElement()
    {
        using var world = World.CreateWorld();
        var entity = world.CreateEntity();
        entity.Set(new HierarchyInfo { Level = 7 });

        world.SortComponent<HierarchyInfo>();

        var components = world.GetComponents<HierarchyInfo>();
        Assert.AreEqual(1, components.Count);
        Assert.AreEqual(7, components[0].Level);
        Assert.IsTrue(world.GetComponentManager<HierarchyInfo>()!.VerifyStorage());
    }

    /// <summary>
    /// After sorting, each entity must still return the same level value it was assigned,
    /// validating that CompMapping and EntityMapping remain consistent.
    /// </summary>
    [TestMethod]
    [DataRow(10)]
    [DataRow(100)]
    [DataRow(1000)]
    public void SortingEntityMappingConsistency(int count)
    {
        using var world = World.CreateWorld();
        var entities = new List<Entity>();
        for (var i = 0; i < count; i++)
        {
            entities.Add(world.CreateEntity());
            entities.Last().Set(new HierarchyInfo { Level = count - i - 1 });
        }

        world.SortComponent<HierarchyInfo>();

        // Each entity must still resolve to its originally assigned level.
        for (var i = 0; i < count; i++)
        {
            Assert.IsTrue(entities[i].Has<HierarchyInfo>(),
                $"Entity {i} should still have HierarchyInfo after sort.");
            Assert.AreEqual(count - i - 1, entities[i].Get<HierarchyInfo>().Level,
                $"Entity {i} returned wrong level after sort.");
        }

        // Internal storage must be coherent.
        Assert.IsTrue(world.GetComponentManager<HierarchyInfo>()!.VerifyStorage());
    }

    /// <summary>
    /// Applying sort twice should leave the result identical to applying it once.
    /// </summary>
    [TestMethod]
    [DataRow(10)]
    [DataRow(100)]
    public void SortingIdempotent(int count)
    {
        using var world = World.CreateWorld();
        var entities = new List<Entity>();
        for (var i = 0; i < count; i++)
        {
            entities.Add(world.CreateEntity());
            entities.Last().Set(new HierarchyInfo { Level = count - i - 1 });
        }

        world.SortComponent<HierarchyInfo>();
        world.SortComponent<HierarchyInfo>();

        var components = world.GetComponents<HierarchyInfo>();
        Assert.AreEqual(count, components.Count);
        for (var i = 0; i < count; ++i)
        {
            Assert.AreEqual(i, components[i].Level);
        }
        Assert.IsTrue(world.GetComponentManager<HierarchyInfo>()!.VerifyStorage());
    }

    /// <summary>
    /// Removing some entities before sorting should produce a consistent result
    /// on the remaining entities only.
    /// </summary>
    [TestMethod]
    [DataRow(10)]
    [DataRow(100)]
    public void SortingAfterRemovingSomeEntities(int count)
    {
        using var world = World.CreateWorld();
        var entities = new List<Entity>();
        for (var i = 0; i < count; i++)
        {
            entities.Add(world.CreateEntity());
            entities.Last().Set(new HierarchyInfo { Level = count - i - 1 });
        }

        // Remove every other entity before sorting.
        var removed = new HashSet<int>();
        for (var i = 0; i < count; i += 2)
        {
            entities[i].Dispose();
            removed.Add(i);
        }

        world.SortComponent<HierarchyInfo>();

        // The remaining entities must still map to the correct level.
        for (var i = 0; i < count; i++)
        {
            if (removed.Contains(i))
            {
                Assert.IsFalse(entities[i].Valid,
                    $"Entity {i} should have been disposed.");
            }
            else
            {
                Assert.IsTrue(entities[i].Has<HierarchyInfo>(),
                    $"Entity {i} should still have HierarchyInfo.");
                Assert.AreEqual(count - i - 1, entities[i].Get<HierarchyInfo>().Level,
                    $"Entity {i} returned wrong level after remove+sort.");
            }
        }

        // Storage must be coherent.
        Assert.IsTrue(world.GetComponentManager<HierarchyInfo>()!.VerifyStorage());

        // Components in storage must be in ascending order.
        var components = world.GetComponents<HierarchyInfo>();
        for (var i = 1; i < components.Count; ++i)
        {
            Assert.IsTrue(components[i - 1].Level <= components[i].Level,
                $"Component storage not sorted at index {i}.");
        }
    }

    /// <summary>
    /// Sorting, then removing some entities, must leave the remaining mappings intact.
    /// </summary>
    [TestMethod]
    [DataRow(10)]
    [DataRow(100)]
    public void RemovingAfterSorting(int count)
    {
        using var world = World.CreateWorld();
        var entities = new List<Entity>();
        for (var i = 0; i < count; i++)
        {
            entities.Add(world.CreateEntity());
            entities.Last().Set(new HierarchyInfo { Level = count - i - 1 });
        }

        world.SortComponent<HierarchyInfo>();

        // Remove every other entity after sorting.
        var removed = new HashSet<int>();
        for (var i = 0; i < count; i += 2)
        {
            entities[i].Dispose();
            removed.Add(i);
        }

        Assert.IsTrue(world.GetComponentManager<HierarchyInfo>()!.VerifyStorage());

        for (var i = 0; i < count; i++)
        {
            if (removed.Contains(i))
            {
                Assert.IsFalse(entities[i].Valid,
                    $"Entity {i} should have been disposed.");
            }
            else
            {
                Assert.IsTrue(entities[i].Has<HierarchyInfo>(),
                    $"Entity {i} should still have HierarchyInfo after sort+remove.");
                Assert.AreEqual(count - i - 1, entities[i].Get<HierarchyInfo>().Level,
                    $"Entity {i} returned wrong level after sort+remove.");
            }
        }
    }

    /// <summary>
    /// Components whose levels are grouped into a small number of distinct values should
    /// sort into ascending level groups.
    /// </summary>
    [TestMethod]
    [DataRow(30)]
    [DataRow(90)]
    public void SortingWithDuplicateLevels(int count)
    {
        using var world = World.CreateWorld();
        var entities = new List<Entity>();
        // Three depth levels, distributed across all entities.
        for (var i = 0; i < count; i++)
        {
            entities.Add(world.CreateEntity());
            entities.Last().Set(new HierarchyInfo { Level = (count - i - 1) % 3 });
        }

        world.SortComponent<HierarchyInfo>();

        // After sort every component must be >= the previous one (ascending).
        var components = world.GetComponents<HierarchyInfo>();
        Assert.AreEqual(count, components.Count);
        for (var i = 1; i < count; ++i)
        {
            Assert.IsTrue(components[i - 1].Level <= components[i].Level,
                $"Components not in ascending order at index {i}.");
        }

        // Per-entity mappings must still be correct.
        for (var i = 0; i < count; i++)
        {
            Assert.AreEqual((count - i - 1) % 3, entities[i].Get<HierarchyInfo>().Level,
                $"Entity {i} returned wrong level after sort with duplicates.");
        }

        Assert.IsTrue(world.GetComponentManager<HierarchyInfo>()!.VerifyStorage());
    }

    /// <summary>
    /// Descending sort should produce components in descending order of level.
    /// </summary>
    [TestMethod]
    [DataRow(10)]
    [DataRow(100)]
    [DataRow(1000)]
    public void SortingDescending(int count)
    {
        using var world = World.CreateWorld();
        var entities = new List<Entity>();
        for (var i = 0; i < count; i++)
        {
            entities.Add(world.CreateEntity());
            entities.Last().Set(new HierarchyInfoDescending { Level = i });
        }

        world.SortComponent<HierarchyInfoDescending>();

        var components = world.GetComponents<HierarchyInfoDescending>();
        Assert.AreEqual(count, components.Count);
        for (var i = 0; i < count; ++i)
        {
            Assert.AreEqual(count - i - 1, components[i].Level,
                $"Component at index {i} has wrong level for descending sort.");
        }

        // Per-entity mappings must still round-trip correctly.
        for (var i = 0; i < count; i++)
        {
            Assert.AreEqual(i, entities[i].Get<HierarchyInfoDescending>().Level,
                $"Entity {i} returned wrong level after descending sort.");
        }

        Assert.IsTrue(world.GetComponentManager<HierarchyInfoDescending>()!.VerifyStorage());
    }
}
