using FsCheck;
using FsCheck.Fluent;

namespace HelixToolkit.Nex.ECS.Tests;

/// <summary>
/// Verifies storage-order consistency between <see cref="World.GetComponents{T}"/> and
/// <see cref="World.GetComponentEntities{T}"/> for data components
/// (feature: ecs-world-component-query-tests, Requirement 4, Requirement 9.1).
///
/// <para>
/// Order/<c>GetInternalArray</c> properties apply to DATA components only; tag managers do not
/// have value storage and their <c>GetInternalArray()</c> throws <see cref="NotSupportedException"/>.
/// </para>
/// </summary>
[TestClass]
public class WorldComponentQueryOrderTests
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
    /// R4.1 / R4.3: <c>GetComponents&lt;Speed&gt;()</c> and <c>GetComponentEntities&lt;Speed&gt;()</c>
    /// yield the same element count, and the entity at each zero-based ordinal position of
    /// <c>GetComponentEntities</c> carries the component value found at the same ordinal position of
    /// <c>GetComponents().GetInternalArray()</c>.
    /// </summary>
    [TestMethod]
    public void GetComponentsAndGetComponentEntitiesShareCountAndAlignedOrdinals()
    {
        const int total = 8;
        var created = new List<Entity>(total);
        for (var i = 0; i < total; ++i)
        {
            var entity = World!.CreateEntity();
            entity.Set(new Speed { Velocity = i * 3f, Acceleration = i * 7f });
            created.Add(entity);
        }

        var components = World!.GetComponents<Speed>();
        Assert.IsNotNull(components);

        // Collect the entities in the order GetComponentEntities yields them.
        var orderedEntities = new List<Entity>();
        foreach (var entity in World!.GetComponentEntities<Speed>())
        {
            orderedEntities.Add(entity);
        }

        // R4.1: identical element count.
        Assert.AreEqual(total, components.Count);
        Assert.AreEqual(components.Count, orderedEntities.Count);

        // R4.3: the entity at ordinal i corresponds to the component at GetInternalArray()[i].
        var backingArray = components.GetInternalArray();
        for (var i = 0; i < components.Count; ++i)
        {
            var arrayValue = backingArray[i];
            var entityValue = orderedEntities[i].Get<Speed>();
            Assert.AreEqual(
                arrayValue.Velocity,
                entityValue.Velocity,
                $"Velocity mismatch at ordinal {i}."
            );
            Assert.AreEqual(
                arrayValue.Acceleration,
                entityValue.Acceleration,
                $"Acceleration mismatch at ordinal {i}."
            );
        }
    }

    /// <summary>
    /// R4.4: for a data component type that no entity in the world carries, both
    /// <c>GetComponents&lt;Speed&gt;()</c> and <c>GetComponentEntities&lt;Speed&gt;()</c> yield zero elements.
    /// </summary>
    [TestMethod]
    public void BothMethodsYieldZeroElementsForAbsentDataType()
    {
        // Create entities that carry a different, disjoint data type so the world is non-empty
        // but no entity carries Speed.
        for (var i = 0; i < 5; ++i)
        {
            var entity = World!.CreateEntity();
            entity.Set(new Health { Value = i });
        }

        var components = World!.GetComponents<Speed>();
        Assert.IsNotNull(components);
        Assert.AreEqual(0, components.Count);

        var count = 0;
        foreach (var _ in World!.GetComponentEntities<Speed>())
        {
            ++count;
        }
        Assert.AreEqual(0, count);
    }

    /// <summary>
    /// Property 4: for any generated world population, <c>GetComponents&lt;Speed&gt;()</c> and
    /// <c>GetComponentEntities&lt;Speed&gt;()</c> yield the same number of elements, and the entity at each
    /// zero-based ordinal position of <c>GetComponentEntities</c> carries the component value found at
    /// the same ordinal position of <c>GetComponents().GetInternalArray()</c>.
    ///
    /// <para>Applies to DATA components (<see cref="Speed"/>) only; tag managers have no backing array.</para>
    /// </summary>
    [TestMethod]
    public void Property4_OrderConsistencyBetweenMethodsAndBackingArray()
    {
        // Feature: ecs-world-component-query-tests, Property 4: Order consistency between the two methods and the backing array
        Prop.ForAll(
                ComponentQueryGenerators.DataPopulation(),
                (ComponentQueryGenerators.PopulationPlan plan) =>
                {
                    var (world, _) = ComponentQueryGenerators.Build(plan);
                    try
                    {
                        var components = world.GetComponents<Speed>();
                        if (components is null)
                        {
                            return false;
                        }

                        // The entities yielded by GetComponentEntities, in order.
                        var orderedEntities = new List<Entity>();
                        foreach (var entity in world.GetComponentEntities<Speed>())
                        {
                            orderedEntities.Add(entity);
                        }

                        // Same number of elements between the two methods (and the backing array).
                        var backingArray = components.GetInternalArray();
                        if (components.Count != orderedEntities.Count)
                        {
                            return false;
                        }

                        // The entity at ordinal i carries the Speed value at GetInternalArray()[i].
                        for (var i = 0; i < components.Count; ++i)
                        {
                            var arrayValue = backingArray[i];
                            var entityValue = orderedEntities[i].Get<Speed>();
                            if (
                                arrayValue.Velocity != entityValue.Velocity
                                || arrayValue.Acceleration != entityValue.Acceleration
                            )
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
            .Check(Config.QuickThrowOnFailure.WithMaxTest(100));
    }

    /// <summary>
    /// Property 5: for any generated world population and unchanged world state, enumerating the
    /// <see cref="ComponentEntities{T}"/> returned by <c>GetComponentEntities&lt;Speed&gt;()</c> two or more
    /// times yields the same entities in the same order, compared element-by-element at each
    /// zero-based ordinal position.
    ///
    /// <para>Applies to DATA components (<see cref="Speed"/>) only.</para>
    /// </summary>
    [TestMethod]
    public void Property5_EnumerationDeterminism()
    {
        // Feature: ecs-world-component-query-tests, Property 5: Enumeration determinism
        Prop.ForAll(
                ComponentQueryGenerators.DataPopulation(),
                (ComponentQueryGenerators.PopulationPlan plan) =>
                {
                    var (world, _) = ComponentQueryGenerators.Build(plan);
                    try
                    {
                        // Enumerate the component entities three times on unchanged world state.
                        var first = new List<Entity>();
                        foreach (var entity in world.GetComponentEntities<Speed>())
                        {
                            first.Add(entity);
                        }

                        var second = new List<Entity>();
                        foreach (var entity in world.GetComponentEntities<Speed>())
                        {
                            second.Add(entity);
                        }

                        var third = new List<Entity>();
                        foreach (var entity in world.GetComponentEntities<Speed>())
                        {
                            third.Add(entity);
                        }

                        // Every enumeration must yield the same number of entities.
                        if (first.Count != second.Count || first.Count != third.Count)
                        {
                            return false;
                        }

                        // Every enumeration must yield the same entity at each ordinal position.
                        for (var i = 0; i < first.Count; ++i)
                        {
                            if (first[i] != second[i] || first[i] != third[i])
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
            .Check(Config.QuickThrowOnFailure.WithMaxTest(100));
    }
}
