namespace HelixToolkit.Nex.ECS.Tests;

[TestClass]
public class ComponentTest
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
    [DataRow(10)]
    [DataRow(100)]
    [DataRow(1000)]
    public void CreateDisposeComponentTest1(int total)
    {
        var entities = new List<Entity>();
        for (int i = 0; i < total; ++i)
        {
            var entity = World!.CreateEntity();
            var car = new Speed { Velocity = i, Acceleration = i * 2 };
            entity.Set(car);
            entities.Add(entity);
        }

        for (int i = 0; i < total; ++i)
        {
            var entity = entities[i];
            Assert.IsTrue(entity.Has<Speed>());
            Assert.AreEqual(i, entity.Get<Speed>().Velocity);
            Assert.AreEqual(i * 2, entity.Get<Speed>().Acceleration);
        }

        for (int i = 0; i < total; ++i)
        {
            var entity = entities[i];
            entity.Dispose();
            Assert.IsFalse(entity.Has<Speed>());
            for (int j = i + 1; j < total; ++j)
            {
                var entity1 = entities[j];
                Assert.IsTrue(entity1.Has<Speed>());
                Assert.AreEqual(j, entity1.Get<Speed>().Velocity);
                Assert.AreEqual(j * 2, entity1.Get<Speed>().Acceleration);
            }
        }

        for (int i = 0; i < total; ++i)
        {
            var entity = entities[i];
            Assert.IsFalse(entity.Has<Speed>());
        }

        Assert.AreEqual(0, World!.GetComponentManager<Speed>()!.Count);
    }

    [TestMethod]
    [DataRow(10)]
    [DataRow(100)]
    [DataRow(1000)]
    public void CreateDisposeComponentTest2(int total)
    {
        var entities = new List<Entity>();
        for (int i = 0; i < total; ++i)
        {
            var entity = World!.CreateEntity();
            var component = new Speed { Velocity = i, Acceleration = i * 2 };
            entity.Set(component);
            entities.Add(entity);
        }

        for (int i = 0; i < total; ++i)
        {
            var entity = entities[i];
            Assert.IsTrue(entity.Has<Speed>());
            Assert.AreEqual(i, entity.Get<Speed>().Velocity);
            Assert.AreEqual(i * 2, entity.Get<Speed>().Acceleration);
        }

        for (int i = 0; i < total; ++i)
        {
            var entity = entities[i];
            Assert.IsTrue(entity.Has<Speed>());
            Assert.AreEqual(i, entity.Get<Speed>().Velocity);
            Assert.AreEqual(i * 2, entity.Get<Speed>().Acceleration);
        }

        World!.Dispose();

        for (int i = 0; i < total; ++i)
        {
            var entity = entities[i];
            Assert.IsFalse(entity.Valid);
            Assert.IsFalse(entity.Has<Speed>());
        }

        Assert.IsNull(World!.GetComponentManager<Speed>());
    }

    [TestMethod]
    [DataRow(5000, 1)]
    [DataRow(100, 10)]
    [DataRow(1000, 5)]
    public void CreateDisposeComponentTest3(int total, int outer)
    {
        var entities = new List<Entity>();
        for (int j = 0; j < outer; ++j)
        {
            for (int i = 0; i < total; ++i)
            {
                var entity = World!.CreateEntity();
                var component = new Speed { Velocity = i, Acceleration = i * 2 };
                entity.Set(component);
                entities.Add(entity);
            }

            for (int i = 0; i < total; ++i)
            {
                var entity = entities[i];
                Assert.IsTrue(entity.Has<Speed>());
                Assert.AreEqual(i, entity.Get<Speed>().Velocity);
                Assert.AreEqual(i * 2, entity.Get<Speed>().Acceleration);
            }

            for (int i = 0; i < total; ++i)
            {
                var entity = entities[i];
                Assert.IsTrue(entity.Has<Speed>());
                Assert.AreEqual(i, entity.Get<Speed>().Velocity);
                Assert.AreEqual(i * 2, entity.Get<Speed>().Acceleration);
            }

            foreach (var e in entities)
            {
                e.Dispose();
            }
            entities.Clear();
            Assert.AreEqual(World!.GetComponentManager<Speed>()!.Count, 0);
        }
    }

    public void CreateDisposeComponentTest4(int total, int outer)
    {
        var entities = new List<Entity>();
        for (int j = 0; j < outer; ++j)
        {
            for (int i = 0; i < total; ++i)
            {
                var entity = World!.CreateEntity();
                var component = new Speed { Velocity = i, Acceleration = i * 2 };
                entity.Set(component);
                entities.Add(entity);
                Assert.IsTrue(entity.Has<Speed>());
                Assert.AreEqual(i, entity.Get<Speed>().Velocity);
                Assert.AreEqual(i * 2, entity.Get<Speed>().Acceleration);
                if (i > 0)
                {
                    entities[i - 1].Dispose();
                    Assert.IsTrue(entity.Has<Speed>());
                    Assert.IsFalse(entities[i - 1].Has<Speed>());
                }
            }
            entities[entities.Count - 1].Dispose();
            entities.Clear();
            Assert.AreEqual(0, World!.GetComponentManager<Speed>()!.Count);
        }
    }

    [TestMethod]
    [DataRow(5000, 1)]
    [DataRow(100, 10)]
    [DataRow(1000, 5)]
    public void CreateDisposeComponentTest5(int total, int outer)
    {
        var entities = new List<Entity>();
        for (int j = 0; j < outer; ++j)
        {
            for (int i = 0; i < total; ++i)
            {
                var entity = World!.CreateEntity();
                var component = new Speed { Velocity = i, Acceleration = i * 2 };
                entity.Set(component);
                entities.Add(entity);
                Assert.IsTrue(entity.Has<Speed>());
                Assert.AreEqual(i, entity.Get<Speed>().Velocity);
                Assert.AreEqual(i * 2, entity.Get<Speed>().Acceleration);
            }
            for (int i = total - 1; i >= 0; --i)
            {
                var entity = entities[i];
                entity.Dispose();
                Assert.IsFalse(entity.Has<Speed>());
                for (int k = 0; k < i; ++k)
                {
                    entity = entities[k];
                    Assert.IsTrue(entity.Has<Speed>());
                    Assert.AreEqual(k, entity.Get<Speed>().Velocity);
                    Assert.AreEqual(k * 2, entity.Get<Speed>().Acceleration);
                }
            }
            entities.Clear();
            Assert.AreEqual(0, World!.GetComponentManager<Speed>()!.Count);
        }
    }

    [TestMethod]
    [DataRow(5000, 1)]
    [DataRow(200, 10)]
    [DataRow(1000, 5)]
    public void CreateDisposeComponentTest6(int total, int outer)
    {
        var entities = new List<Entity>();
        var rnd = new Random();
        for (int j = 0; j < outer; ++j)
        {
            for (int i = 0; i < total; ++i)
            {
                var entity = World!.CreateEntity();
                var component = new Speed { Velocity = i, Acceleration = i * 2 };
                entity.Set(component);
                entities.Add(entity);
                Assert.IsTrue(entity.Has<Speed>());
                Assert.AreEqual(i, entity.Get<Speed>().Velocity);
                Assert.AreEqual(i * 2, entity.Get<Speed>().Acceleration);
            }
            for (int i = 0; i < total * 2; ++i)
            {
                var idx = rnd.Next(0, total - 1);
                var entity = entities[idx];
                entity.Dispose();
                Assert.IsFalse(entity.Has<Speed>());
                for (int k = 0; k < total; ++k)
                {
                    entity = entities[k];
                    if (entity.Valid)
                    {
                        Assert.IsTrue(entity.Has<Speed>());
                        Assert.AreEqual(k, entity.Get<Speed>().Velocity);
                        Assert.AreEqual(k * 2, entity.Get<Speed>().Acceleration);
                    }
                }
            }

            for (int i = 0; i < total; ++i)
            {
                entities[i].Dispose();
            }
            entities.Clear();
            Assert.AreEqual(0, World!.GetComponentManager<Speed>()!.Count);
        }
    }

    [TestMethod]
    [DataRow(10, false)]
    [DataRow(100, false)]
    [DataRow(1000, false)]
    [DataRow(10, true)]
    [DataRow(100, true)]
    [DataRow(1000, true)]
    public void MultiComponentTest1(int total, bool keepOrder)
    {
        var cars = new List<Entity>();
        for (int i = 0; i < total; ++i)
        {
            var car = World!.CreateEntity();
            var speed = new Speed() { Acceleration = i, Velocity = i * 2 };
            var health = new Health() { Value = i * 10 };
            var miles = new Mileage() { Value = i * 3 };
            car.Set(speed);
            car.Set(health);
            car.Set(miles);
            cars.Add(car);

            Assert.IsTrue(car.Has<Speed>());
            Assert.IsTrue(car.Has<Health>());
            Assert.IsTrue(car.Has<Mileage>());
            Assert.AreEqual(i, car.Get<Speed>().Acceleration);
            Assert.AreEqual(i * 2, car.Get<Speed>().Velocity);
            Assert.AreEqual(i * 10, car.Get<Health>().Value);
            Assert.AreEqual(i * 3, car.Get<Mileage>().Value);
        }
        Assert.IsTrue(World!.HasAnyComponent<Speed>());
        Assert.IsTrue(World!.HasAnyComponent<Health>());
        Assert.IsTrue(World!.HasAnyComponent<Mileage>());
        Assert.IsTrue(World!.GetComponentManager<Speed>()!.VerifyStorage());
        Assert.IsTrue(World!.GetComponentManager<Health>()!.VerifyStorage());
        Assert.IsTrue(World!.GetComponentManager<Mileage>()!.VerifyStorage());

        for (int i = 0; i < total; ++i)
        {
            var car = cars[i];
            car.Get<Speed>().Acceleration = i + 1000;
            car.Get<Speed>().Velocity = i * 10 + 200;
            car.Get<Health>().Value = i * 100;
            car.Get<Mileage>().Value = i * 5;
        }

        for (int i = 0; i < total; ++i)
        {
            var car = cars[i];
            Assert.AreEqual(i + 1000, car.Get<Speed>().Acceleration);
            Assert.AreEqual(i * 10 + 200, car.Get<Speed>().Velocity);
            Assert.AreEqual(i * 100, car.Get<Health>().Value);
            Assert.AreEqual(i * 5, car.Get<Mileage>().Value);
            Assert.AreEqual(ResultCode.Ok, car.Remove<Speed>(keepOrder));
            Assert.IsFalse(car.Has<Speed>());
            Assert.AreEqual(ResultCode.Ok, car.Remove<Health>(keepOrder));
            Assert.IsFalse(car.Has<Health>());
            Assert.AreEqual(ResultCode.Ok, car.Remove<Mileage>(keepOrder));
            Assert.IsFalse(car.Has<Mileage>());

            for (int j = i + 1; j < total; ++j)
            {
                car = cars[j];
                Assert.AreEqual(j + 1000, car.Get<Speed>().Acceleration);
                Assert.AreEqual(j * 10 + 200, car.Get<Speed>().Velocity);
                Assert.AreEqual(j * 100, car.Get<Health>().Value);
                Assert.AreEqual(j * 5, car.Get<Mileage>().Value);
                Assert.IsTrue(World!.GetComponentManager<Speed>()!.VerifyStorage());
                Assert.IsTrue(World!.GetComponentManager<Health>()!.VerifyStorage());
                Assert.IsTrue(World!.GetComponentManager<Mileage>()!.VerifyStorage());
            }
        }

        Assert.IsFalse(World!.HasAnyComponent<Speed>());
        Assert.IsFalse(World!.HasAnyComponent<Health>());
        Assert.IsFalse(World!.HasAnyComponent<Mileage>());
    }

    [TestMethod]
    [DataRow(10)]
    [DataRow(100)]
    [DataRow(1000)]
    public void MultiComponentTest2(int total)
    {
        var cars = new List<Entity>();
        for (int i = 0; i < total; ++i)
        {
            var car = World!.CreateEntity();
            var speed = new Speed() { Acceleration = i, Velocity = i * 2 };
            var health = new Health() { Value = i * 10 };
            var miles = new Mileage() { Value = i * 3 };
            car.Set(speed);
            car.Set(health);
            car.Set(miles);
            cars.Add(car);

            Assert.IsTrue(car.Has<Speed>());
            Assert.IsTrue(car.Has<Health>());
            Assert.IsTrue(car.Has<Mileage>());
            Assert.AreEqual(i, car.Get<Speed>().Acceleration);
            Assert.AreEqual(i * 2, car.Get<Speed>().Velocity);
            Assert.AreEqual(i * 10, car.Get<Health>().Value);
            Assert.AreEqual(i * 3, car.Get<Mileage>().Value);
        }

        for (int i = 0; i < total; ++i)
        {
            var car = cars[i];
            car.Get<Speed>().Acceleration = i + 1000;
            car.Get<Speed>().Velocity = i * 10 + 200;
            car.Get<Health>().Value = i * 100;
            car.Get<Mileage>().Value = i * 5;
        }

        for (int i = total - 1; i >= 0; --i)
        {
            var car = cars[i];
            car.Dispose();

            for (int j = 0; j < i; ++j)
            {
                car = cars[j];
                Assert.AreEqual(j + 1000, car.Get<Speed>().Acceleration);
                Assert.AreEqual(j * 10 + 200, car.Get<Speed>().Velocity);
                Assert.AreEqual(j * 100, car.Get<Health>().Value);
                Assert.AreEqual(j * 5, car.Get<Mileage>().Value);
            }
        }

        Assert.IsFalse(World!.HasAnyComponent<Speed>());
        Assert.IsFalse(World!.HasAnyComponent<Health>());
        Assert.IsFalse(World!.HasAnyComponent<Mileage>());
    }

    [TestMethod]
    [DataRow(10, false)]
    [DataRow(100, false)]
    [DataRow(1000, false)]
    [DataRow(10, true)]
    [DataRow(100, true)]
    [DataRow(1000, true)]
    public void MultiComponentTest3(int total, bool keepOrder)
    {
        var cars = new List<Entity>();
        for (int i = 0; i < total; ++i)
        {
            var car = World!.CreateEntity();
            var speed = new Speed() { Acceleration = i, Velocity = i * 2 };
            var health = new Health() { Value = i * 10 };
            var miles = new Mileage() { Value = i * 3 };
            car.Set(speed);
            car.Set(health);
            car.Set(miles);
            cars.Add(car);

            Assert.IsTrue(car.Has<Speed>());
            Assert.IsTrue(car.Has<Health>());
            Assert.IsTrue(car.Has<Mileage>());
            Assert.AreEqual(i, car.Get<Speed>().Acceleration);
            Assert.AreEqual(i * 2, car.Get<Speed>().Velocity);
            Assert.AreEqual(i * 10, car.Get<Health>().Value);
            Assert.AreEqual(i * 3, car.Get<Mileage>().Value);
        }

        for (int i = total - 1; i >= 0; --i)
        {
            var car = cars[i];
            Assert.AreEqual(ResultCode.Ok, car.Remove<Speed>(keepOrder));
            Assert.IsFalse(car.Has<Speed>());
            Assert.IsTrue(car.Has<Health>());
            Assert.IsTrue(car.Has<Mileage>());
        }

        Assert.IsFalse(World!.HasAnyComponent<Speed>());
        Assert.IsTrue(World!.HasAnyComponent<Health>());
        Assert.IsTrue(World!.HasAnyComponent<Mileage>());

        for (int i = 0; i < total; ++i)
        {
            var car = cars[i];
            Assert.AreEqual(ResultCode.Ok, car.Remove<Health>(keepOrder));
            Assert.IsFalse(car.Has<Health>());
            Assert.IsTrue(car.Has<Mileage>());
        }

        Assert.IsFalse(World.HasAnyComponent<Health>());
        Assert.IsTrue(World.HasAnyComponent<Mileage>());

        for (int i = total - 1; i >= 0; --i)
        {
            var car = cars[i];
            Assert.AreEqual(ResultCode.Ok, car.Remove<Mileage>(keepOrder));
            Assert.IsFalse(car.Has<Mileage>());
        }

        Assert.IsFalse(World.HasAnyComponent<Mileage>());
    }
}
