namespace HelixToolkit.Nex.ECS.Tests;

[TestClass]
public class WorldTest
{
    [TestMethod]
    [DataRow(2, 5000)]
    [DataRow(5, 1000)]
    [DataRow(10, 500)]
    public void Test1(int worldCount, int entityCount)
    {
        var worlds = new List<World>();
        var cars = new List<Entity>();

        for (int i = 0; i < worldCount; ++i)
        {
            var world = World.CreateWorld();
            worlds.Add(world);
        }
        for (int i = 0; i < entityCount; ++i)
        {
            foreach (var world in worlds)
            {
                var car = world.CreateEntity();
                var speed = new Speed()
                {
                    Acceleration = i + world.Id,
                    Velocity = i * 10 + world.Id,
                };
                var health = new Health() { Value = i * 5 + world.Id };
                var mile = new Mileage() { Value = i * 3 + world.Id };
                car.Set(speed);
                car.Set(health);
                car.Set(mile);
                cars.Add(car);
            }
        }

        for (int i = 0; i < entityCount; ++i)
        {
            for (int j = 0; j < worldCount; ++j)
            {
                var car = cars[i * worldCount + j];
                Assert.IsTrue(car.Has<Speed>());
                Assert.IsTrue(car.Has<Health>());
                Assert.IsTrue(car.Has<Mileage>());

                Assert.AreEqual(i + car.WorldId, car.Get<Speed>().Acceleration);
                Assert.AreEqual(i * 10 + car.WorldId, car.Get<Speed>().Velocity);
                Assert.AreEqual(i * 5 + car.WorldId, car.Get<Health>().Value);
                Assert.AreEqual(i * 3 + car.WorldId, car.Get<Mileage>().Value);

                car.Get<Speed>().Acceleration *= 2;
                car.Get<Speed>().Velocity *= 2;
                car.Get<Health>().Value *= 2;
                car.Get<Mileage>().Value *= 2;

                Assert.AreEqual(i + car.WorldId, car.Get<Speed>().Acceleration / 2);
                Assert.AreEqual(i * 10 + car.WorldId, car.Get<Speed>().Velocity / 2);
                Assert.AreEqual(i * 5 + car.WorldId, car.Get<Health>().Value / 2);
                Assert.AreEqual(i * 3 + car.WorldId, car.Get<Mileage>().Value / 2);
            }
        }

        foreach (var world in worlds)
        {
            world.Dispose();
        }

        foreach (var car in cars)
        {
            Assert.IsFalse(car.Valid);
            Assert.IsFalse(car.Has<Speed>());
            Assert.IsFalse(car.Has<Health>());
            Assert.IsFalse(car.Has<Mileage>());
        }
    }

    [TestMethod]
    [DataRow(2, 5000, 10)]
    [DataRow(5, 1000, 10)]
    [DataRow(10, 1000, 10)]
    public void ParallelTest1(int worldCount, int entityCount, int iteration)
    {
        Parallel.For(
            0,
            worldCount,
            (idx) =>
            {
                for (int iter = 0; iter < iteration; ++iter)
                {
                    var world = World.CreateWorld();
                    var cars = new List<Entity>();
                    for (int i = 0; i < entityCount; ++i)
                    {
                        var car = world.CreateEntity();
                        var speed = new Speed()
                        {
                            Acceleration = i + world.Id,
                            Velocity = i * 10 + world.Id,
                        };
                        var health = new Health() { Value = i * 5 + world.Id };
                        var mile = new Mileage() { Value = i * 3 + world.Id };
                        car.Set(speed);
                        car.Set(health);
                        car.Set(mile);
                        cars.Add(car);
                    }

                    for (int i = 0; i < entityCount; ++i)
                    {
                        var car = cars[i];
                        Assert.IsTrue(car.Has<Speed>());
                        Assert.IsTrue(car.Has<Health>());
                        Assert.IsTrue(car.Has<Mileage>());

                        Assert.AreEqual(i + car.WorldId, car.Get<Speed>().Acceleration);
                        Assert.AreEqual(i * 10 + car.WorldId, car.Get<Speed>().Velocity);
                        Assert.AreEqual(i * 5 + car.WorldId, car.Get<Health>().Value);
                        Assert.AreEqual(i * 3 + car.WorldId, car.Get<Mileage>().Value);

                        car.Get<Speed>().Acceleration *= 2;
                        car.Get<Speed>().Velocity *= 2;
                        car.Get<Health>().Value *= 2;
                        car.Get<Mileage>().Value *= 2;

                        Assert.AreEqual(i + car.WorldId, car.Get<Speed>().Acceleration / 2);
                        Assert.AreEqual(i * 10 + car.WorldId, car.Get<Speed>().Velocity / 2);
                        Assert.AreEqual(i * 5 + car.WorldId, car.Get<Health>().Value / 2);
                        Assert.AreEqual(i * 3 + car.WorldId, car.Get<Mileage>().Value / 2);
                    }

                    foreach (var car in cars)
                    {
                        car.Get<Speed>().Acceleration = 100;
                        car.Get<Speed>().Velocity = 200;
                        Assert.AreEqual(100, car.Get<Speed>().Acceleration);
                        Assert.AreEqual(200, car.Get<Speed>().Velocity);
                        car.Dispose();
                    }

                    Assert.IsFalse(world.HasAnyComponent<Speed>());
                    Assert.IsFalse(world.HasAnyComponent<Health>());
                    Assert.IsFalse(world.HasAnyComponent<Mileage>());
                    world.Dispose();
                }
            }
        );
    }

    [TestMethod]
    [DataRow(2, 5000, 10)]
    [DataRow(5, 1000, 10)]
    [DataRow(10, 1000, 10)]
    public void ParallelTest2(int worldCount, int entityCount, int iteration)
    {
        Parallel.For(
            0,
            worldCount,
            (idx) =>
            {
                for (int iter = 0; iter < iteration; ++iter)
                {
                    var world = World.CreateWorld();
                    var cars = new List<Entity>();
                    for (int i = 0; i < entityCount; ++i)
                    {
                        var car = world.CreateEntity();
                        var speed = new Speed()
                        {
                            Acceleration = i + world.Id,
                            Velocity = i * 10 + world.Id,
                        };
                        var health = new Health() { Value = i * 5 + world.Id };
                        var mile = new Mileage() { Value = i * 3 + world.Id };
                        car.Set(speed);
                        car.Set(health);
                        car.Set(mile);
                        cars.Add(car);
                    }

                    world.Dispose();
                    Assert.IsFalse(
                        world.HasAnyComponent<Speed>(),
                        $"Must not have {nameof(Speed)} component."
                    );
                    Assert.IsFalse(
                        world.HasAnyComponent<Health>(),
                        $"Must not have {nameof(Speed)} component."
                    );
                    Assert.IsFalse(
                        world.HasAnyComponent<Mileage>(),
                        $"Must not have {nameof(Speed)} component."
                    );

                    foreach (var car in cars)
                    {
                        Assert.IsFalse(car.Has<Speed>(), $"Must not have {nameof(Speed)}.");
                        Assert.IsFalse(car.Has<Health>(), $"Must not have {nameof(Health)}");
                        Assert.IsFalse(car.Has<Mileage>(), $"Must not have {nameof(Mileage)}");
                        Assert.IsFalse(car.Valid, $"Must not valid.");
                    }
                }
            }
        );
    }
}
