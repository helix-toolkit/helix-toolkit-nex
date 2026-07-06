using FsCheck;
using FsCheck.Fluent;

namespace HelixToolkit.Nex.ECS.Tests;

/// <summary>
/// Empty-world / no-component behavior for the ECS World component-query methods
/// (feature: ecs-world-component-query-tests).
///
/// <para>
/// These example-based tests verify how <see cref="World.GetComponents{T}"/> and
/// <see cref="World.GetComponentEntities{T}"/> behave when no entity carries the queried
/// component type. They document the deliberate data/tag asymmetry: querying an absent
/// <em>data</em> type lazily creates a non-null, empty manager (Count 0), while querying an
/// absent <em>tag</em> type returns <see cref="EmptyComponents{T}.Empty"/>.
/// </para>
/// </summary>
[TestClass]
public class WorldComponentQueryEmptyTests
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

    // R1.1: GetComponents for a data type in a world where no entity carries it returns a
    // non-null IComponents reporting a Count of exactly 0.
    [TestMethod]
    public void GetComponents_DataType_EmptyWorld_NonNull_CountZero()
    {
        var components = World!.GetComponents<Speed>();

        Assert.IsNotNull(components);
        Assert.AreEqual(0, components.Count);
    }

    // R1.2: GetComponentEntities for a data type in an empty world yields exactly 0 entities.
    [TestMethod]
    public void GetComponentEntities_DataType_EmptyWorld_YieldsZero()
    {
        var entities = World!.GetComponentEntities<Speed>();

        Assert.AreEqual(0, CountEntities(entities));
    }

    // R1.3: GetComponents for a tag type in a world where no entity carries that tag returns the
    // EmptyComponents instance and reports a Count of exactly 0.
    [TestMethod]
    public void GetComponents_TagType_NoCarriers_IsEmptyComponents_CountZero()
    {
        var components = World!.GetComponents<TagA>();

        Assert.IsInstanceOfType(components, typeof(EmptyComponents<TagA>));
        Assert.AreEqual(0, components.Count);
    }

    // R1.4: GetComponentEntities for a tag type in a world where no entity carries that tag yields
    // exactly 0 entities.
    [TestMethod]
    public void GetComponentEntities_TagType_NoCarriers_YieldsZero()
    {
        var entities = World!.GetComponentEntities<TagA>();

        Assert.AreEqual(0, CountEntities(entities));
    }

    // R1.5: GetComponentEntities for a data type in a world that contains entities but none carry
    // that type yields exactly 0 entities.
    [TestMethod]
    public void GetComponentEntities_DataType_EntitiesButNoCarriers_YieldsZero()
    {
        // Create entities that carry a different component type, so none carry Speed.
        for (var i = 0; i < 5; ++i)
        {
            var entity = World!.CreateEntity();
            entity.Set(new Health { Value = i });
        }

        var entities = World!.GetComponentEntities<Speed>();

        Assert.AreEqual(0, CountEntities(entities));
    }

    // R9.5: GetComponents for a data type that no entity has ever carried reports a Count of 0 and
    // its GetEntities yields zero entities.
    [TestMethod]
    public void GetComponents_NeverCarriedDataType_CountZero_GetEntitiesYieldsZero()
    {
        var components = World!.GetComponents<Speed>();

        Assert.AreEqual(0, components.Count);
        Assert.AreEqual(0, CountEntities(components.GetEntities()));
    }

    /// <summary>Collects the entities yielded by iterating a <see cref="ComponentEntities{T}"/> into a set.</summary>
    private static HashSet<Entity> CollectEntities<T>(ComponentEntities<T> entities)
        where T : struct
    {
        var set = new HashSet<Entity>();
        foreach (var entity in entities)
        {
            set.Add(entity);
        }
        return set;
    }

    // Feature: ecs-world-component-query-tests, Property 7: Repeated queries are side-effect free
    //
    // For any generated world population and any number of consecutive calls (two or more) to
    // GetComponents<T>() and GetComponentEntities<T>() for a type that no entity carries, every call
    // reports a Count of 0, and the world's total entity count and the set of entities yielded for
    // every populated type remain unchanged across the calls.
    //
    // The generated population assigns the Speed data component to a (possibly empty) subset of
    // entities; the queried absent type is Health, which no entity in the population carries.
    // Speed is therefore the "populated type" whose yielded set must remain stable across repeats.
    //
    // Validates: Requirements 1.6
    [TestMethod]
    public void Property7_RepeatedQueries_AreSideEffectFree()
    {
        Prop.ForAll(
                ComponentQueryGenerators.DataPopulation(),
                (ComponentQueryGenerators.PopulationPlan plan) =>
                {
                    var (world, _) = ComponentQueryGenerators.Build(plan);
                    try
                    {
                        // Baseline captured before any repeated query of the absent type.
                        var expectedTotalEntities = world.Count;
                        var populatedBaseline = CollectEntities(world.GetComponentEntities<Speed>());

                        // Call the query methods for the absent type (Health) several times in a row.
                        const int repetitions = 3;
                        for (var r = 0; r < repetitions; ++r)
                        {
                            // Every GetComponents call for the absent type reports Count 0.
                            if (world.GetComponents<Health>().Count != 0)
                            {
                                return false;
                            }

                            // Every GetComponentEntities call for the absent type yields no entities.
                            if (CountEntities(world.GetComponentEntities<Health>()) != 0)
                            {
                                return false;
                            }

                            // The world's total entity count is unchanged by the queries.
                            if (world.Count != expectedTotalEntities)
                            {
                                return false;
                            }

                            // The populated type's yielded set is unchanged by the queries.
                            var populatedNow = CollectEntities(world.GetComponentEntities<Speed>());
                            if (!populatedNow.SetEquals(populatedBaseline))
                            {
                                return false;
                            }
                        }

                        return true;
                    }
                    finally
                    {
                        world.Dispose();
                    }
                }
            )
            .QuickCheckThrowOnFailure();
    }
}
