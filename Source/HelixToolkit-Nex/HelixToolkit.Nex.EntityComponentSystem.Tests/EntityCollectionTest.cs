namespace HelixToolkit.Nex.ECS.Tests;

[TestClass]
public class EntityCollectionTest
{
    [TestMethod]
    [DataRow(10)]
    [DataRow(100)]
    [DataRow(1000)]
    public void BuildBasicTest1(int total)
    {
        using var world = World.CreateWorld();
        int count = total;

        {
            // Single component
            while (count-- > 0)
            {
                var entity = world.CreateEntity();
                entity.Set(new Speed() { Acceleration = count, Velocity = count - 1 });
            }
            using var collection = world.CreateCollection().Has<Speed>().Build();
            Assert.AreEqual(total, collection.Count);

            foreach (var entity in collection)
            {
                Assert.IsTrue(entity.Has<Speed>());
            }
        }

        {
            // Different component
            count = total;
            while (count-- > 0)
            {
                var entity = world.CreateEntity();
                entity.Set(new Health() { Value = count });
                entity.Set(new Mileage() { Value = count * 2 });
            }

            using var collection = world.CreateCollection().Has<Health>().Has<Mileage>().Build();
            Assert.AreEqual(total, collection.Count);
            foreach (var entity in collection)
            {
                Assert.IsFalse(entity.Has<Speed>());
                Assert.IsTrue(entity.Has<Health>());
                Assert.IsTrue(entity.Has<Mileage>());
            }
        }

        {
            // Mixing component
            count = total;
            while (count-- > 0)
            {
                var entity = world.CreateEntity();
                entity.Set(new Speed() { Acceleration = count, Velocity = count - 1 });
                entity.Set(new Health() { Value = count });
                entity.Set(new Mileage() { Value = count * 2 });
            }
            using var collection = world.CreateCollection().Has<Health>().NotHas<Speed>().Build();
            Assert.AreEqual(total, collection.Count);
            foreach (var entity in collection)
            {
                Assert.IsFalse(entity.Has<Speed>());
                Assert.IsTrue(entity.Has<Health>());
            }
        }

        {
            // Mixing 2
            count = total;
            while (count-- > 0)
            {
                var entity = world.CreateEntity();
                entity.Set(new Health() { Value = count });
                entity.Set(new Mileage() { Value = count * 2 });
            }
            using var collection = world.CreateCollection().Has<Health>().NotHas<Speed>().Build();
            Assert.AreEqual(total * 2, collection.Count);
            foreach (var entity in collection)
            {
                Assert.IsFalse(entity.Has<Speed>());
                Assert.IsTrue(entity.Has<Health>());
            }
        }

        {
            // Mixing 3
            using var collection = world.CreateCollection().Has<Health>().NotHas<Speed>().Build();
            Assert.AreEqual(total * 2, collection.Count);
            foreach (var entity in world)
            {
                entity.Remove<Speed>();
            }
            Assert.AreEqual(world.Count - total, collection.Count);
            foreach (var entity in collection)
            {
                Assert.IsFalse(entity.Has<Speed>());
                Assert.IsTrue(entity.Has<Health>());
            }
            foreach (var entity in world)
            {
                entity.Remove<Health>();
            }
            Assert.AreEqual(0, collection.Count);
        }
    }

    [TestMethod]
    [DataRow(10)]
    [DataRow(100)]
    [DataRow(1000)]
    public void CollectionAddTest1(int total)
    {
        using var world = World.CreateWorld();
        int count = total;
        while (count-- > 0)
        {
            var entity = world.CreateEntity();
            entity.Set(new Speed() { Acceleration = count, Velocity = count - 1 });
        }

        var collection = world.CreateCollection().Has<Speed>().Build();
        Assert.AreEqual(total, collection.Count);

        count = total;
        while (count-- > 0)
        {
            var entity = world.CreateEntity();
            entity.Set(new Speed() { Acceleration = count, Velocity = count - 1 });
        }

        Assert.AreEqual(total * 2, collection.Count);
        foreach (var entity in collection)
        {
            Assert.IsTrue(entity.Has<Speed>());
        }
    }

    [TestMethod]
    [DataRow(10)]
    [DataRow(100)]
    [DataRow(1000)]
    public void CollectionRemoveTest1(int total)
    {
        using var world = World.CreateWorld();
        int count = total;
        while (count-- > 0)
        {
            var entity = world.CreateEntity();
            entity.Set(new Speed() { Acceleration = count, Velocity = count - 1 });
        }

        var collection = world.CreateCollection().Has<Speed>().Build();
        Assert.AreEqual(total, collection.Count);

        foreach (var entity in collection.ToArray())
        {
            entity.Remove<Speed>();
        }

        Assert.AreEqual(0, collection.Count);
    }

    [TestMethod]
    [DataRow(10)]
    [DataRow(100)]
    [DataRow(1000)]
    public void CombinationTest(int total)
    {
        using var world = World.CreateWorld();
        using var collection1 = world
            .CreateCollection()
            .NotHas<Health>()
            .Has<Speed>()
            .Has<Mileage>()
            .NotHas<Position>()
            .Build();
        using var collection2 = world
            .CreateCollection()
            .Has<Health>()
            .Has<Position>()
            .NotHas<Speed>()
            .NotHas<Mileage>()
            .Build();
        using var collection3 = world.CreateCollection().Has<Mileage>().Build();
        int count1 = 0;
        int count2 = 0;
        int count3 = 0;
        for (int i = 0; i < total; ++i)
        {
            var entity = world.CreateEntity();
            if (i % 2 == 0)
            {
                entity.Set(new Speed() { Acceleration = i });
                entity.Set(new Mileage() { Value = i * 2 });
                ++count1;
            }
            else if (i % 3 == 0)
            {
                entity.Set(new Health() { Value = i });
                entity.Set(new Position() { X = i, Y = i * 2 });
                ++count2;
            }
            else
            {
                entity.Set(new Health() { Value = i });
                entity.Set(new Position() { X = i, Y = i * 2 });
                entity.Set(new Speed() { Acceleration = i });
                entity.Set(new Mileage() { Value = i * 2 });
                ++count3;
            }
        }
        Assert.AreEqual(count1, collection1.Count);
        Assert.AreEqual(count2, collection2.Count);
        Assert.AreEqual(count1 + count3, collection3.Count);
        foreach (var entity in collection1)
        {
            Assert.IsTrue(entity.Has<Speed>());
            Assert.IsTrue(entity.Has<Mileage>());
            Assert.IsFalse(entity.Has<Health>());
            Assert.IsFalse(entity.Has<Position>());
        }
        foreach (var entity in collection2)
        {
            Assert.IsFalse(entity.Has<Speed>());
            Assert.IsFalse(entity.Has<Mileage>());
            Assert.IsTrue(entity.Has<Health>());
            Assert.IsTrue(entity.Has<Position>());
        }
        foreach (var entity in collection3)
        {
            Assert.IsTrue(entity.Has<Mileage>());
        }

        foreach (var entity in world)
        {
            entity.Remove<Health>();
        }

        Assert.AreEqual(count1, collection1.Count);
        Assert.AreEqual(0, collection2.Count);
        Assert.AreEqual(count1 + count3, collection3.Count);

        foreach (var entity in world)
        {
            entity.Remove<Position>();
        }

        Assert.AreEqual(count1 + count3, collection1.Count);
        Assert.AreEqual(0, collection2.Count);
        Assert.AreEqual(count1 + count3, collection3.Count);

        foreach (var entity in world)
        {
            entity.Remove<Speed>();
        }

        Assert.AreEqual(0, collection1.Count);
        Assert.AreEqual(0, collection2.Count);
        Assert.AreEqual(count1 + count3, collection3.Count);

        foreach (var entity in world)
        {
            entity.Set(new Position());
            entity.Remove<Mileage>();
        }

        Assert.AreEqual(0, collection1.Count);
        Assert.AreEqual(0, collection2.Count);
        Assert.AreEqual(0, collection3.Count);

        foreach (var entity in world)
        {
            entity.Set(new Speed());
        }

        Assert.AreEqual(0, collection1.Count);
        Assert.AreEqual(0, collection2.Count);
        Assert.AreEqual(0, collection3.Count);

        foreach (var entity in world)
        {
            entity.Set(new Mileage());
            entity.Set(new Health());
        }

        Assert.AreEqual(0, collection1.Count);
        Assert.AreEqual(0, collection2.Count);
        Assert.AreEqual(total, collection3.Count);
    }

    [TestMethod]
    [DataRow(5, 1000)]
    [DataRow(10, 1000)]
    public void MultiWorldTest(int worldCount, int total)
    {
        Parallel.For(
            0,
            worldCount,
            (worldIdx) =>
            {
                CombinationTest(total);
            }
        );
    }

    [TestMethod]
    [DataRow(10)]
    [DataRow(100)]
    [DataRow(1000)]
    public void TypeConflictTest(int total)
    {
        using var world = World.CreateWorld();
        using var collection = world.CreateCollection().Has<S1>().Has<S2>().NotHas<S1>().Build();
        for (int i = 0; i < total; ++i)
        {
            var entity = world.CreateEntity();
            entity.Set<S2>();
        }

        Assert.AreEqual(0, collection.Count);

        foreach (var entity in world)
        {
            entity.Set<S1>();
        }

        Assert.AreEqual(0, collection.Count);

        foreach (var entity in world)
        {
            entity.Remove<S1>();
        }

        Assert.AreEqual(0, collection.Count);
    }

    [TestMethod]
    [DataRow(10)]
    [DataRow(100)]
    [DataRow(1000)]
    public void LargeNumberOfStructTypeTests(int total)
    {
        using var world = World.CreateWorld();

        for (int i = 0; i < total; ++i)
        {
            var entity = world.CreateEntity();
            entity.Set<S1>();
            entity.Set<S2>();
            entity.Set<S3>();
            entity.Set<S4>();
            entity.Set<S5>();
            entity.Set<S6>();
            entity.Set<S7>();
            entity.Set<S8>();
            entity.Set<S9>();
            entity.Set<S10>();
            entity.Set<S11>();
            entity.Set<S12>();
            entity.Set<S13>();
            entity.Set<S14>();
            entity.Set<S15>();
            entity.Set<S16>();
            entity.Set<S17>();
            entity.Set<S18>();
            entity.Set<S19>();
            entity.Set<S20>();
            entity.Set<S21>();
            entity.Set<S22>();
            entity.Set<S23>();
            entity.Set<S24>();
            entity.Set<S25>();
            entity.Set<S26>();
            entity.Set<S27>();
            entity.Set<S28>();
            entity.Set<S29>();
            entity.Set<S30>();
            entity.Set<S31>();
            entity.Set<S32>();
            entity.Set<S33>();
            entity.Set<S34>();
            entity.Set<S35>();
            entity.Set<S36>();
            entity.Set<S37>();
            entity.Set<S38>();
            entity.Set<S39>();
            entity.Set<S40>();
            entity.Set<S41>();
            entity.Set<S42>();
            entity.Set<S43>();
            entity.Set<S44>();
        }
        using var collection1 = world
            .CreateCollection()
            .Has<S1>()
            .Has<S2>()
            .Has<S38>()
            .Has<S44>()
            .Build();
        using var collection2 = world
            .CreateCollection()
            .Has<S2>()
            .Has<S38>()
            .Has<S44>()
            .NotHas<S1>()
            .Build();
        Assert.AreEqual(total, collection1.Count);
        Assert.AreEqual(0, collection2.Count);

        for (int i = 0; i < total; ++i)
        {
            var entity = world.CreateEntity();
            entity.Set<S1>();
            entity.Set<S2>();
        }

        Assert.AreEqual(total, collection1.Count);
        Assert.AreEqual(0, collection2.Count);

        for (int i = 0; i < total; ++i)
        {
            var entity = world.CreateEntity();
            entity.Set<S1>();
            entity.Set<S2>();
            entity.Set<S38>();
            entity.Set<S44>();
        }

        Assert.AreEqual(total * 2, collection1.Count);
        Assert.AreEqual(0, collection2.Count);

        var entities = collection1.ToArray();

        foreach (var entity in entities)
        {
            entity.Remove<S1>();
        }

        Assert.AreEqual(0, collection1.Count);
        Assert.AreEqual(total * 2, collection2.Count);

        foreach (var entity in entities)
        {
            entity.Remove<S2>();
        }

        Assert.AreEqual(0, collection1.Count);
        Assert.AreEqual(0, collection2.Count);
    }

    [TestMethod]
    [DataRow(10)]
    [DataRow(100)]
    [DataRow(1000)]
    public void EnableDisableTest(int total)
    {
        using var world = World.CreateWorld();
        var list1 = new List<Entity>();
        for (int i = 0; i < total; ++i)
        {
            var entity = world.CreateEntity();
            entity.Set<S1>();
            entity.Set<S2>();
            list1.Add(entity);
        }

        for (int i = 0; i < total; ++i)
        {
            var entity = world.CreateEntity();
            entity.Set<S2>();
            entity.Set<S3>();
            list1.Add(entity);
        }

        for (int i = 0; i < total; ++i)
        {
            var entity = world.CreateEntity();
            entity.Set<S1>();
            entity.Set<S3>();
            list1.Add(entity);
        }

        using var collection1 = world.CreateCollection().Has<S1>().EnabledOnly().Build();
        using var collection2 = world.CreateCollection().Has<S2>().EnabledOnly().Build();
        using var collection3 = world.CreateCollection().Has<S3>().EnabledOnly().Build();

        Assert.AreEqual(total * 2, collection1.Count);
        Assert.AreEqual(total * 2, collection2.Count);
        Assert.AreEqual(total * 2, collection3.Count);

        for (int i = 0; i < list1.Count; ++i)
        {
            if (list1[i].Has<S1>())
            {
                list1[i].SetEnabled(false);
            }
        }

        Assert.AreEqual(0, collection1.Count);
        Assert.AreEqual(total, collection2.Count);
        Assert.AreEqual(total, collection3.Count);

        for (int i = 0; i < list1.Count; ++i)
        {
            if (list1[i].Has<S2>())
            {
                list1[i].SetEnabled(false);
            }
        }

        Assert.AreEqual(0, collection1.Count);
        Assert.AreEqual(0, collection2.Count);
        Assert.AreEqual(0, collection3.Count);

        for (int i = 0; i < list1.Count; ++i)
        {
            if (list1[i].Has<S3>())
            {
                list1[i].SetEnabled(true);
            }
        }

        Assert.AreEqual(total, collection1.Count);
        Assert.AreEqual(total, collection2.Count);
        Assert.AreEqual(total * 2, collection3.Count);

        for (int i = 0; i < list1.Count; ++i)
        {
            if (list1[i].Has<S2>())
            {
                list1[i].SetEnabled(true);
            }
        }

        Assert.AreEqual(total * 2, collection1.Count);
        Assert.AreEqual(total * 2, collection2.Count);
        Assert.AreEqual(total * 2, collection3.Count);
    }

    [TestMethod]
    [DataRow(10)]
    [DataRow(100)]
    [DataRow(1000)]
    public void EntityDisposeTest(int total)
    {
        using var world = World.CreateWorld();
        var list1 = new List<Entity>();
        for (int i = 0; i < total; ++i)
        {
            var entity = world.CreateEntity();
            entity.Set<S1>();
            entity.Set<S2>();
            list1.Add(entity);
        }

        for (int i = 0; i < total; ++i)
        {
            var entity = world.CreateEntity();
            entity.Set<S2>();
            entity.Set<S3>();
            list1.Add(entity);
        }

        for (int i = 0; i < total; ++i)
        {
            var entity = world.CreateEntity();
            entity.Set<S1>();
            entity.Set<S3>();
            list1.Add(entity);
        }

        using var collection1 = world.CreateCollection().Has<S1>().EnabledOnly().Build();
        using var collection2 = world.CreateCollection().Has<S2>().EnabledOnly().Build();
        using var collection3 = world.CreateCollection().Has<S3>().EnabledOnly().Build();

        Assert.AreEqual(total * 2, collection1.Count);
        Assert.AreEqual(total * 2, collection2.Count);
        Assert.AreEqual(total * 2, collection3.Count);

        for (int i = 0; i < list1.Count; ++i)
        {
            if (list1[i].Has<S1>())
            {
                list1[i].Dispose();
            }
        }

        Assert.AreEqual(0, collection1.Count);
        Assert.AreEqual(total, collection2.Count);
        Assert.AreEqual(total, collection3.Count);

        for (int i = 0; i < list1.Count; ++i)
        {
            if (list1[i].Has<S2>())
            {
                list1[i].Dispose();
            }
        }

        Assert.AreEqual(0, collection1.Count);
        Assert.AreEqual(0, collection2.Count);
        Assert.AreEqual(0, collection3.Count);
    }

    [TestMethod]
    [DataRow(10)]
    [DataRow(100)]
    [DataRow(1000)]
    public void WorldDisposeTest(int total)
    {
        using var world = World.CreateWorld();
        var list1 = new List<Entity>();
        for (int i = 0; i < total; ++i)
        {
            var entity = world.CreateEntity();
            entity.Set<S1>();
            entity.Set<S2>();
            list1.Add(entity);
        }

        for (int i = 0; i < total; ++i)
        {
            var entity = world.CreateEntity();
            entity.Set<S2>();
            entity.Set<S3>();
            list1.Add(entity);
        }

        for (int i = 0; i < total; ++i)
        {
            var entity = world.CreateEntity();
            entity.Set<S1>();
            entity.Set<S3>();
            list1.Add(entity);
        }

        using var collection1 = world.CreateCollection().Has<S1>().EnabledOnly().Build();
        using var collection2 = world.CreateCollection().Has<S2>().EnabledOnly().Build();
        using var collection3 = world.CreateCollection().Has<S3>().EnabledOnly().Build();

        Assert.AreEqual(total * 2, collection1.Count);
        Assert.AreEqual(total * 2, collection2.Count);
        Assert.AreEqual(total * 2, collection3.Count);

        world.Dispose();
        Assert.AreEqual(0, collection1.Count);
        Assert.AreEqual(0, collection2.Count);
        Assert.AreEqual(0, collection3.Count);
        Assert.IsTrue(collection1.Disposed);
        Assert.IsTrue(collection2.Disposed);
        Assert.IsTrue(collection3.Disposed);
    }
}
