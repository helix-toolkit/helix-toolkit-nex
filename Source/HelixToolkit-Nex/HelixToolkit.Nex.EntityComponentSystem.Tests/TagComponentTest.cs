namespace HelixToolkit.Nex.ECS.Tests;

[TestClass]
public class TagComponentTest
{
    public required World? World;

    [TestInitialize]
    public void Setup()
    {
        World = World.CreateWorld();
    }

    [TestCleanup]
    public void Shutdown()
    {
        World?.Dispose();
    }

    [TestMethod]
    public void TagSetAndHasTest()
    {
        var entity = World!.CreateEntity();
        Assert.IsFalse(entity.Has<TagA>());

        var result = entity.Tag<TagA>();
        Assert.AreEqual(ResultCode.Ok, result);
        Assert.IsTrue(entity.Has<TagA>());
        Assert.IsFalse(entity.Has<TagB>());
    }

    [TestMethod]
    public void TagSetViaSetMethodTest()
    {
        var entity = World!.CreateEntity();
        var result = entity.Set(new TagA());
        Assert.AreEqual(ResultCode.Ok, result);
        Assert.IsTrue(entity.Has<TagA>());
    }

    [TestMethod]
    public void TagSetIdempotentTest()
    {
        var entity = World!.CreateEntity();
        entity.Tag<TagA>();
        entity.Tag<TagA>(); // Setting again should be idempotent
        Assert.IsTrue(entity.Has<TagA>());
        Assert.AreEqual(1, World!.GetComponentManager<TagA>()!.Count);
    }

    [TestMethod]
    public void TagRemoveTest()
    {
        var entity = World!.CreateEntity();
        entity.Tag<TagA>();
        Assert.IsTrue(entity.Has<TagA>());

        var result = entity.Remove<TagA>();
        Assert.AreEqual(ResultCode.Ok, result);
        Assert.IsFalse(entity.Has<TagA>());
    }

    [TestMethod]
    public void TagRemoveNotFoundTest()
    {
        var entity = World!.CreateEntity();
        var result = entity.Remove<TagA>();
        Assert.AreEqual(ResultCode.NotFound, result);
    }

    [TestMethod]
    [DataRow(10)]
    [DataRow(100)]
    [DataRow(1000)]
    public void TagMultiEntityTest(int count)
    {
        var entities = new List<Entity>();
        for (int i = 0; i < count; ++i)
        {
            var entity = World!.CreateEntity();
            entity.Tag<TagA>();
            entities.Add(entity);
        }

        Assert.AreEqual(count, World!.GetComponentManager<TagA>()!.Count);

        for (int i = 0; i < count; ++i)
        {
            Assert.IsTrue(entities[i].Has<TagA>());
        }

        // Remove from the front
        for (int i = 0; i < count; ++i)
        {
            entities[i].Remove<TagA>();
            Assert.IsFalse(entities[i].Has<TagA>());
            Assert.AreEqual(count - i - 1, World!.GetComponentManager<TagA>()!.Count);

            // Verify remaining entities still have the tag
            for (int j = i + 1; j < count; ++j)
            {
                Assert.IsTrue(entities[j].Has<TagA>());
            }
        }
    }

    [TestMethod]
    [DataRow(10)]
    [DataRow(100)]
    [DataRow(1000)]
    public void TagDisposeEntityCleansUpTest(int count)
    {
        var entities = new List<Entity>();
        for (int i = 0; i < count; ++i)
        {
            var entity = World!.CreateEntity();
            entity.Tag<TagA>();
            entity.Tag<TagB>();
            entities.Add(entity);
        }

        Assert.AreEqual(count, World!.GetComponentManager<TagA>()!.Count);
        Assert.AreEqual(count, World!.GetComponentManager<TagB>()!.Count);

        for (int i = 0; i < count; ++i)
        {
            entities[i].Dispose();
        }

        Assert.AreEqual(0, World!.GetComponentManager<TagA>()!.Count);
        Assert.AreEqual(0, World!.GetComponentManager<TagB>()!.Count);
    }

    [TestMethod]
    public void TagMixedWithDataComponentTest()
    {
        var entity = World!.CreateEntity();
        entity.Tag<TagA>();
        entity.Set(new Speed { Velocity = 10, Acceleration = 5 });

        Assert.IsTrue(entity.Has<TagA>());
        Assert.IsTrue(entity.Has<Speed>());
        Assert.AreEqual(10, entity.Get<Speed>().Velocity);
        Assert.AreEqual(5, entity.Get<Speed>().Acceleration);

        entity.Remove<TagA>();
        Assert.IsFalse(entity.Has<TagA>());
        Assert.IsTrue(entity.Has<Speed>());
        Assert.AreEqual(10, entity.Get<Speed>().Velocity);
    }

    [TestMethod]
    public void TagNoStorageAllocationTest()
    {
        // Verify that the manager for tag components is a TagComponentManager
        // (no underlying T[] storage used)
        var entity = World!.CreateEntity();
        entity.Tag<TagA>();

        var manager = World!.GetComponentManager<TagA>()!;
        // Storage should remain empty for tag components
        Assert.AreEqual(0, manager.Storage.Count);
        Assert.AreEqual(1, manager.Count);
    }

    [TestMethod]
    public void TagGetReturnsDefaultTest()
    {
        var entity = World!.CreateEntity();
        entity.Tag<TagA>();

        // Get on a tag component should return a default value (not throw)
        var tag = entity.Get<TagA>();
        Assert.AreEqual(default(TagA), tag);
    }

    [TestMethod]
    public void TagHasAnyComponentTest()
    {
        Assert.IsFalse(World!.HasAnyComponent<TagA>());

        var entity = World!.CreateEntity();
        entity.Tag<TagA>();

        Assert.IsTrue(World.HasAnyComponent<TagA>());

        entity.Remove<TagA>();
        Assert.IsFalse(World.HasAnyComponent<TagA>());
    }

    [TestMethod]
    [DataRow(10)]
    [DataRow(100)]
    public void TagEntityCollectionTest(int count)
    {
        var taggedEntities = new List<Entity>();
        var untaggedEntities = new List<Entity>();

        for (int i = 0; i < count; ++i)
        {
            var entity = World!.CreateEntity();
            if (i % 2 == 0)
            {
                entity.Tag<TagA>();
                taggedEntities.Add(entity);
            }
            else
            {
                untaggedEntities.Add(entity);
            }
        }

        // Use RuleBuilder to query entities with TagA
        using var collection = World!.CreateCollection().Has<TagA>().Build();

        Assert.AreEqual(taggedEntities.Count, collection.Count);
        foreach (var entity in taggedEntities)
        {
            Assert.IsTrue(collection.Contains(entity));
        }
        foreach (var entity in untaggedEntities)
        {
            Assert.IsFalse(collection.Contains(entity));
        }
    }
}
