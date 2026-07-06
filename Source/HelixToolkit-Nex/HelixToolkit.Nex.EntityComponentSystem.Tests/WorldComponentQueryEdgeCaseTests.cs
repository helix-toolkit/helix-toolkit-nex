using FsCheck;
using FsCheck.Fluent;

namespace HelixToolkit.Nex.ECS.Tests;

/// <summary>
/// Edge-case and robustness behavior for the ECS World component-query methods
/// (feature: ecs-world-component-query-tests).
///
/// <para>
/// These tests cover unusual usage patterns for <see cref="World.GetComponents{T}"/> and
/// <see cref="World.GetComponentEntities{T}"/>: sole-carrier disposal, disjoint component types,
/// multi-type entities, and re-enumeration. Property-based tests for disjoint-type isolation and
/// multi-type membership are added in later tasks; this class starts with the sole-carrier
/// disposal example (R9.6).
/// </para>
/// </summary>
[TestClass]
public class WorldComponentQueryEdgeCaseTests
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

    /// <summary>Counts the entities yielded by iterating a <see cref="ComponentEntities{T}"/>.</summary>
    private static int CountEntities<T>(ComponentEntities<T> entities)
        where T : struct
    {
        var count = 0;
        foreach (var _ in entities)
        {
            ++count;
        }
        return count;
    }

    // R9.6: When an entity is disposed while it is the only carrier of a data component, a
    // subsequent GetComponents reports a Count of 0 for that type and GetComponentEntities yields
    // zero entities.
    [TestMethod]
    public void DisposeSoleCarrier_DataType_CountZero_YieldsZero()
    {
        var entity = World!.CreateEntity();
        entity.Set(new Speed { Velocity = 12f, Acceleration = 3f });

        // Precondition: the entity is the sole carrier of Speed.
        Assert.AreEqual(1, World!.GetComponents<Speed>().Count);
        Assert.AreEqual(1, CountEntities(World!.GetComponentEntities<Speed>()));

        entity.Dispose();

        Assert.AreEqual(0, World!.GetComponents<Speed>().Count);
        Assert.AreEqual(0, CountEntities(World!.GetComponentEntities<Speed>()));
    }

    // Feature: ecs-world-component-query-tests, Property 18: Disjoint component types are isolated
    //
    // For any world where a set of entities carries data component type A (Speed) and a disjoint
    // set carries a distinct data component type B (Health), GetComponentEntities<A>() yields
    // exactly the entities carrying A and none of the entities that carry only B.
    //
    // Validates: Requirements 9.2, 9.3
    [TestMethod]
    public void Property_DisjointComponentTypes_AreIsolated()
    {
        // Two independent counts: how many entities carry A (Speed) and how many carry B (Health).
        // The two entity sets are disjoint because each entity is created and assigned exactly once.
        var counts = (
            from a in Gen.Choose(0, 20)
            from b in Gen.Choose(0, 20)
            select (a, b)
        ).ToArbitrary();

        var property = Prop.ForAll(
            counts,
            pair =>
            {
                var (aCount, bCount) = pair;
                var world = World.CreateWorld();
                try
                {
                    // A-set: entities carrying Speed.
                    var aEntities = new HashSet<Entity>();
                    for (var i = 0; i < aCount; ++i)
                    {
                        var entity = world.CreateEntity();
                        entity.Set(new Speed { Velocity = i, Acceleration = i * 2 });
                        aEntities.Add(entity);
                    }

                    // B-set: a disjoint set of entities carrying only Health.
                    var bEntities = new HashSet<Entity>();
                    for (var i = 0; i < bCount; ++i)
                    {
                        var entity = world.CreateEntity();
                        entity.Set(new Health { Value = i });
                        bEntities.Add(entity);
                    }

                    var yielded = new HashSet<Entity>();
                    foreach (var entity in world.GetComponentEntities<Speed>())
                    {
                        yielded.Add(entity);
                    }

                    // R9.2: GetComponentEntities<A>() yields exactly the entities carrying A.
                    Assert.IsTrue(yielded.SetEquals(aEntities));

                    // R9.3: it excludes every entity that carries only B.
                    foreach (var bEntity in bEntities)
                    {
                        Assert.IsFalse(yielded.Contains(bEntity));
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

    // Feature: ecs-world-component-query-tests, Property 19: An entity carrying multiple types appears in each type's query
    //
    // For any single entity that carries two or more distinct data-component types (Speed, Health,
    // Position), GetComponentEntities for each of those types yields that entity. The world may also
    // contain other entities carrying only subsets of those types; the multi-type entity must still
    // appear in every one of its types' queries.
    //
    // Validates: Requirements 9.4
    [TestMethod]
    public void Property_MultiTypeEntity_AppearsInEachTypeQuery()
    {
        // Generate a small population of "other" entities, each carrying an arbitrary subset of the
        // three data types, alongside a single entity that carries all three types.
        var plans = (
            from speedOnly in Gen.Choose(0, 6)
            from healthOnly in Gen.Choose(0, 6)
            from positionOnly in Gen.Choose(0, 6)
            select (speedOnly, healthOnly, positionOnly)
        ).ToArbitrary();

        var property = Prop.ForAll(
            plans,
            plan =>
            {
                var (speedOnly, healthOnly, positionOnly) = plan;
                var world = World.CreateWorld();
                try
                {
                    // The single multi-type entity carrying all three distinct data-component types.
                    var multi = world.CreateEntity();
                    multi.Set(new Speed { Velocity = 1f, Acceleration = 2f });
                    multi.Set(new Health { Value = 42 });
                    multi.Set(new Position { X = 3f, Y = 4f });

                    // Other entities carrying only subsets of those types.
                    for (var i = 0; i < speedOnly; ++i)
                    {
                        world.CreateEntity().Set(new Speed { Velocity = i, Acceleration = i });
                    }
                    for (var i = 0; i < healthOnly; ++i)
                    {
                        world.CreateEntity().Set(new Health { Value = i });
                    }
                    for (var i = 0; i < positionOnly; ++i)
                    {
                        world.CreateEntity().Set(new Position { X = i, Y = i });
                    }

                    // The multi-type entity must appear in each type's query.
                    Assert.IsTrue(Yields(world.GetComponentEntities<Speed>(), multi));
                    Assert.IsTrue(Yields(world.GetComponentEntities<Health>(), multi));
                    Assert.IsTrue(Yields(world.GetComponentEntities<Position>(), multi));
                }
                finally
                {
                    world.Dispose();
                }
            }
        );

        Check.One(Config.QuickThrowOnFailure.WithMaxTest(100), property);
    }

    /// <summary>Returns true if iterating a <see cref="ComponentEntities{T}"/> yields the target entity.</summary>
    private static bool Yields<T>(ComponentEntities<T> entities, Entity target)
        where T : struct
    {
        foreach (var entity in entities)
        {
            if (entity == target)
            {
                return true;
            }
        }
        return false;
    }
}
