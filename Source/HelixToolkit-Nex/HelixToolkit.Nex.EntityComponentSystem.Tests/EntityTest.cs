namespace HelixToolkit.Nex.ECS.Tests;

[TestClass]
public class EntityTests
{
    private World? _world;

    [TestInitialize]
    public void Setup()
    {
        _world = World.CreateWorld();
    }

    [TestCleanup]
    public void Shutdown()
    {
        _world?.Dispose();
    }

    public void CreateEntityTest1()
    {
        var entity = _world!.CreateEntity();
        Assert.AreEqual(_world, entity.World);
        Assert.IsTrue(_world.HasEntity(entity));
        Assert.IsTrue(entity.Enabled);

        entity.Dispose();
        Assert.IsFalse(entity.Valid);
        Assert.IsFalse(_world.HasEntity(entity));
        Assert.IsFalse(entity.Enabled);
    }

    [TestMethod]
    [DataRow(10)]
    [DataRow(100)]
    [DataRow(1000)]
    public void CreateEntityTest2(int loop)
    {
        for (int i = 0; i < loop; ++i)
        {
            var entity = _world!.CreateEntity();
            Assert.AreEqual(_world, entity.World);
            Assert.IsTrue(_world.HasEntity(entity));
            Assert.IsTrue(entity.Enabled);

            entity.Dispose();
            Assert.IsFalse(entity.Valid);
            Assert.IsFalse(_world.HasEntity(entity));
            Assert.IsFalse(entity.Enabled);
        }
    }

    [TestMethod]
    [DataRow(5000, 1)]
    [DataRow(100, 10)]
    [DataRow(1000, 50)]
    public void CreateEntityTest3(int inner, int outer)
    {
        var entities = new List<Entity>();
        for (int j = 0; j < outer; ++j)
        {
            for (int i = 0; i < inner; ++i)
            {
                var entity = _world!.CreateEntity();
                Assert.AreEqual(_world, entity.World);
                Assert.IsTrue(_world.HasEntity(entity));
                Assert.IsTrue(entity.Enabled);
                entities.Add(entity);
            }

            foreach (var entity in entities)
            {
                entity.Dispose();
                Assert.IsFalse(entity.Valid);
                Assert.IsFalse(_world!.HasEntity(entity));
                Assert.IsFalse(entity.Enabled);
            }
            entities.Clear();
        }
    }

    [TestMethod]
    [DataRow(10)]
    [DataRow(100)]
    [DataRow(1000)]
    public void DisposeWorldTest(int loop)
    {
        var entities = new List<Entity>();
        for (int i = 0; i < loop; ++i)
        {
            var entity = _world!.CreateEntity();
            Assert.AreEqual(_world, entity.World);
            Assert.IsTrue(_world.HasEntity(entity));
            Assert.IsTrue(entity.Enabled);
            entities.Add(entity);
        }

        _world!.Dispose();
        foreach (var entity in entities)
        {
            Assert.IsFalse(entity.Valid);
            Assert.IsFalse(_world.HasEntity(entity));
            Assert.IsFalse(entity.Enabled);
        }
    }

    [TestMethod]
    [DataRow(10)]
    [DataRow(100)]
    [DataRow(1000)]
    public void GenerationTest1(int loop)
    {
        Entity entity1 = default;
        while (loop-- > 0)
        {
            Entity entity = _world!.CreateEntity();
            Assert.IsTrue(entity.Valid);
            Assert.IsTrue(entity.Enabled);
            Assert.IsFalse(entity1.Valid);
            Assert.IsFalse(entity1.Enabled);
            var worldGen = entity.Generation.WorldGeneration;
            var entityGen = entity.Generation.EntityGeneration;
            var id = entity.Id;
            entity.Dispose();
            Assert.IsFalse(entity.Valid);
            Assert.IsFalse(entity.Enabled);
            entity1 = _world.CreateEntity();
            Assert.AreEqual(worldGen, entity1.Generation.WorldGeneration);
            Assert.AreNotEqual(entityGen, entity1.Generation.EntityGeneration);
            Assert.AreEqual(id, entity1.Id);
            Assert.IsTrue(entity1.Valid);
            Assert.IsTrue(entity1.Enabled);
            Assert.IsFalse(entity.Valid);
            Assert.IsFalse(entity.Enabled);
            entity1.Dispose();
            Assert.IsFalse(entity1.Valid);
            Assert.IsFalse(entity1.Enabled);
        }
    }

    [TestMethod]
    [DataRow(10)]
    [DataRow(100)]
    [DataRow(1000)]
    public void GenerationTest2(int loop)
    {
        var entities1 = new List<Entity>();
        var entities1Gens = new List<Generation>();
        for (int i = 0; i < loop; ++i)
        {
            entities1.Add(_world!.CreateEntity());
            Assert.IsTrue(entities1[i].Valid);
            Assert.IsTrue(entities1[i].Enabled);
            entities1Gens.Add(entities1[i].Generation);
        }

        for (int i = 0; i < loop; ++i)
        {
            entities1[i].Dispose();
            Assert.IsFalse(entities1[i].Valid);
            Assert.IsFalse(entities1[i].Enabled);
        }

        var entities2 = new List<Entity>();
        for (int i = 0; i < loop; ++i)
        {
            entities2.Add(_world!.CreateEntity());
            Assert.IsTrue(entities2[i].Valid);
            Assert.IsTrue(entities2[i].Enabled);
        }

        for (int i = 0; i < loop; ++i)
        {
            Assert.AreEqual(
                entities1Gens[i].WorldGeneration,
                entities2[i].Generation.WorldGeneration
            );
            Assert.AreEqual(entities1[i].Id, entities2[loop - i - 1].Id);
            Assert.AreNotEqual(
                entities1Gens[i].EntityGeneration,
                entities2[loop - i - 1].Generation.EntityGeneration
            );
            Assert.IsFalse(entities1[i].Valid);
            Assert.IsFalse(entities1[i].Enabled);
        }
    }

    [TestMethod]
    [DataRow(10, 10)]
    [DataRow(100, 10)]
    [DataRow(1000, 10)]
    public void GenerationTest3(int loop, int iteration)
    {
        var entities1 = new List<Entity>();
        var entities1Gens = new List<Generation>();
        for (int i = 0; i < loop; ++i)
        {
            entities1.Add(_world!.CreateEntity());
            Assert.IsTrue(entities1[i].Valid);
            Assert.IsTrue(entities1[i].Enabled);
            entities1Gens.Add(entities1[i].Generation);
        }
        while (iteration-- > 0)
        {
            for (int i = 0; i < loop; ++i)
            {
                entities1[i].Dispose();
                Assert.IsFalse(entities1[i].Valid);
                Assert.IsFalse(entities1[i].Enabled);
            }

            var entities2 = new List<Entity>();
            for (int i = 0; i < loop; ++i)
            {
                entities2.Add(_world!.CreateEntity());
                Assert.IsTrue(entities2[i].Valid);
                Assert.IsTrue(entities2[i].Enabled);
            }

            for (int i = 0; i < loop; ++i)
            {
                Assert.AreEqual(
                    entities1Gens[i].WorldGeneration,
                    entities2[i].Generation.WorldGeneration
                );
                Assert.AreEqual(entities1[i].Id, entities2[loop - i - 1].Id);
                Assert.AreNotEqual(
                    entities1Gens[i].EntityGeneration,
                    entities2[loop - i - 1].Generation.EntityGeneration
                );
                Assert.IsFalse(entities1[i].Valid);
                Assert.IsFalse(entities1[i].Enabled);
            }
            entities1 = entities2;
            entities1Gens = entities2.Select(x => x.Generation).ToList();
        }
    }

    [TestMethod]
    [DataRow(10, 10)]
    [DataRow(100, 10)]
    [DataRow(1000, 10)]
    public void WorldGenerationTest(int loop, int iteration)
    {
        var entities1 = new List<Entity>();
        var entities1Gens = new List<Generation>();
        for (int i = 0; i < loop; ++i)
        {
            entities1.Add(_world!.CreateEntity());
            Assert.IsTrue(entities1[i].Valid);
            Assert.IsTrue(entities1[i].Enabled);
            entities1Gens.Add(entities1[i].Generation);
        }

        var worldId = _world!.Id;
        var worldGen = _world.Generation;
        _world.Dispose();
        _world = World.CreateWorld();
        Assert.AreEqual(worldId, _world.Id);
        Assert.AreNotEqual(worldGen, _world.Generation);
        var entities2 = new List<Entity>();
        for (int i = 0; i < loop; ++i)
        {
            entities2.Add(_world.CreateEntity());
            Assert.IsTrue(entities2[i].Valid);
            Assert.IsTrue(entities2[i].Enabled);
        }

        for (int i = 0; i < loop; ++i)
        {
            Assert.AreNotEqual(
                entities1Gens[i].WorldGeneration,
                entities2[i].Generation.WorldGeneration
            );
            Assert.AreEqual(entities1[i].Id, entities2[i].Id);
            Assert.AreEqual(
                entities1Gens[i].EntityGeneration,
                entities2[i].Generation.EntityGeneration
            );
            Assert.IsFalse(entities1[i].Valid);
            Assert.IsFalse(entities1[i].Enabled);
        }
    }
}
