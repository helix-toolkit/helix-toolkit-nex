using FsCheck;
using FsCheck.Fluent;

namespace HelixToolkit.Nex.ECS.Tests;

/// <summary>
/// Example-based tests for single- and multi-component retrieval via
/// <see cref="World.GetComponents{T}"/> and <see cref="World.GetComponentEntities{T}"/>
/// (feature: ecs-world-component-query-tests).
/// Covers Requirements 2.1, 2.2, 2.3, 2.4, 3.1, 3.4.
/// </summary>
[TestClass]
public class WorldComponentQueryRetrievalTests
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

    /// <summary>
    /// Collects the entities yielded by <see cref="World.GetComponentEntities{T}"/> into a list.
    /// </summary>
    private static List<Entity> Collect<T>(World world)
        where T : struct
    {
        var yielded = new List<Entity>();
        foreach (var entity in world.GetComponentEntities<T>())
        {
            yielded.Add(entity);
        }
        return yielded;
    }

    // R2.1 / R2.2 / R3.1: a fixed count N of entities each carry Speed.
    // GetComponents<Speed>().Count == N and GetComponentEntities<Speed>() yields exactly
    // the assigned entities.
    [TestMethod]
    [DataRow(1)]
    [DataRow(3)]
    [DataRow(5)]
    public void FixedCountRetrievalTest(int n)
    {
        var assigned = new List<Entity>();
        for (var i = 0; i < n; ++i)
        {
            var entity = World!.CreateEntity();
            entity.Set(new Speed { Velocity = i, Acceleration = i * 2f });
            assigned.Add(entity);
        }

        // R2.1 / R3.1: Count equals the number of carriers.
        Assert.AreEqual(n, World!.GetComponents<Speed>().Count);

        // R2.2 / R3.1: enumeration yields exactly the assigned entities (same set, no extras).
        var yielded = Collect<Speed>(World!);
        Assert.AreEqual(n, yielded.Count);
        CollectionAssert.AreEquivalent(assigned, yielded);
    }

    // R2.2 / R2.3: single carrier — the yielded element's entity equals the assigned entity
    // and its Speed value matches field-for-field.
    [TestMethod]
    public void SingleCarrierEntityAndValueTest()
    {
        var expectedValue = new Speed { Velocity = 12.5f, Acceleration = -3.25f };
        var entity = World!.CreateEntity();
        entity.Set(expectedValue);

        var yielded = Collect<Speed>(World!);

        // R2.2: exactly one element whose entity equals the assigned entity.
        Assert.AreEqual(1, yielded.Count);
        Assert.AreEqual(entity, yielded[0]);

        // R2.3: the component value exposed for that entity matches field-for-field.
        var components = World!.GetComponents<Speed>();
        ref var actual = ref components[yielded[0]];
        Assert.AreEqual(expectedValue.Velocity, actual.Velocity);
        Assert.AreEqual(expectedValue.Acceleration, actual.Acceleration);
    }

    // R2.4 / R3.4: querying an absent data type returns an empty result with Count == 0 and
    // yields zero elements, without raising an error.
    [TestMethod]
    public void AbsentDataTypeReturnsEmptyResultTest()
    {
        // World has entities, but none carry Health.
        for (var i = 0; i < 4; ++i)
        {
            var entity = World!.CreateEntity();
            entity.Set(new Speed { Velocity = i, Acceleration = i });
        }

        var components = World!.GetComponents<Health>();
        Assert.IsNotNull(components);
        Assert.AreEqual(0, components.Count);

        var yielded = Collect<Health>(World!);
        Assert.AreEqual(0, yielded.Count);
    }

    // Feature: ecs-world-component-query-tests, Property 1: Data-component count equals carrier count
    //
    // For any generated world population, GetComponents<Speed>().Count for a data component equals
    // the number of entities that carry it (0 when none carry it), and the returned IComponents<T>
    // is never null.
    //
    // Validates: Requirements 1.1, 2.1, 2.4, 3.1, 3.4, 9.5
    [TestMethod]
    public void Property_DataComponentCount_EqualsCarrierCount()
    {
        var property = Prop.ForAll(
            ComponentQueryGenerators.DataPopulation(),
            plan =>
            {
                var (world, _) = ComponentQueryGenerators.Build(plan);
                try
                {
                    var components = world.GetComponents<Speed>();

                    // The returned IComponents<T> is never null.
                    Assert.IsNotNull(components);

                    // Count equals the number of carriers (0 when none carry it).
                    var expected = ComponentQueryGenerators.ExpectedCount(plan);
                    Assert.AreEqual(expected, components.Count);
                }
                finally
                {
                    world.Dispose();
                }
            }
        );

        Check.One(Config.QuickThrowOnFailure.WithMaxTest(100), property);
    }

    // Feature: ecs-world-component-query-tests, Property 2: Data-component membership equals the carrier set
    //
    // For any generated world population, the set of entities yielded by GetComponentEntities<T>()
    // for a data component T equals exactly the carrier set of T, contains no duplicate entities,
    // and includes no entity outside the carrier set (yielding zero when the carrier set is empty,
    // even when other non-carrying entities exist).
    //
    // Validates: Requirements 1.2, 1.5, 2.2, 3.2, 3.3, 3.4, 4.4, 9.5
    [TestMethod]
    public void Property_DataComponentMembership_EqualsCarrierSet()
    {
        var property = Prop.ForAll(
            ComponentQueryGenerators.DataPopulation(),
            plan =>
            {
                var (world, entities) = ComponentQueryGenerators.Build(plan);
                try
                {
                    var yielded = Collect<Speed>(world);

                    // No duplicate entities: list count equals distinct set count.
                    var yieldedSet = new HashSet<Entity>(yielded);
                    Assert.AreEqual(yielded.Count, yieldedSet.Count);

                    // Membership equals exactly the carrier set (no extras, no omissions;
                    // zero when the carrier set is empty even with non-carrying entities present).
                    var expected = ComponentQueryGenerators.ExpectedCarrierSet(plan, entities);
                    Assert.IsTrue(yieldedSet.SetEquals(expected));
                }
                finally
                {
                    world.Dispose();
                }
            }
        );

        Check.One(Config.QuickThrowOnFailure.WithMaxTest(100), property);
    }

    // Feature: ecs-world-component-query-tests, Property 3: Data-component value fidelity
    //
    // For any generated world population, for every carrier entity of data component T, the
    // component value exposed for that entity via GetComponents<T>() (its backing array slot /
    // indexer) equals, field-for-field, the value that was assigned to that entity.
    //
    // Validates: Requirements 2.3
    [TestMethod]
    public void Property_DataComponentValue_Fidelity()
    {
        var property = Prop.ForAll(
            ComponentQueryGenerators.DataPopulation(),
            plan =>
            {
                var (world, entities) = ComponentQueryGenerators.Build(plan);
                try
                {
                    var expectedValues = ComponentQueryGenerators.ExpectedValueMap(plan, entities);
                    var components = world.GetComponents<Speed>();

                    // For every carrier entity, the value exposed via the indexer matches the
                    // assigned value field-for-field.
                    foreach (var (entity, expected) in expectedValues)
                    {
                        ref var actual = ref components[entity];
                        Assert.AreEqual(expected.Velocity, actual.Velocity);
                        Assert.AreEqual(expected.Acceleration, actual.Acceleration);
                    }
                }
                finally
                {
                    world.Dispose();
                }
            }
        );

        Check.One(Config.QuickThrowOnFailure.WithMaxTest(100), property);
    }

    // Feature: ecs-world-component-query-tests, Property 6: Count equals distinct enumerated entity count
    //
    // For any generated world population, GetComponents<Speed>().Count for a data component T equals
    // the number of distinct entities yielded by GetComponentEntities<Speed>().
    //
    // Validates: Requirements 3.5
    [TestMethod]
    public void Property_Count_EqualsDistinctEnumeratedEntityCount()
    {
        var property = Prop.ForAll(
            ComponentQueryGenerators.DataPopulation(),
            plan =>
            {
                var (world, _) = ComponentQueryGenerators.Build(plan);
                try
                {
                    // Distinct entities yielded by GetComponentEntities<Speed>().
                    var distinct = new HashSet<Entity>(Collect<Speed>(world));

                    // GetComponents<Speed>().Count equals that distinct count.
                    Assert.AreEqual(distinct.Count, world.GetComponents<Speed>().Count);
                }
                finally
                {
                    world.Dispose();
                }
            }
        );

        Check.One(Config.QuickThrowOnFailure.WithMaxTest(100), property);
    }
}
