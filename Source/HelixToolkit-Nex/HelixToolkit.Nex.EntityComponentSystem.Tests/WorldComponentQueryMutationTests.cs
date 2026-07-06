using FsCheck;
using FsCheck.Fluent;

namespace HelixToolkit.Nex.ECS.Tests;

/// <summary>
/// Example-based tests for additive and subtractive component-query mutation behavior
/// (feature: ecs-world-component-query-tests). This class hosts the observational example tests
/// for Requirement 7; the universal mutation properties (Properties 10-15) live alongside them as
/// separate property-based tests.
/// </summary>
[TestClass]
public class WorldComponentQueryMutationTests
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
    /// Collects the entities yielded by <see cref="World.GetComponentEntities{T}"/> into a set.
    /// </summary>
    private static HashSet<Entity> EntitySet<T>(World world)
    {
        var set = new HashSet<Entity>();
        foreach (var entity in world.GetComponentEntities<T>())
        {
            set.Add(entity);
        }
        return set;
    }

    /// <summary>
    /// R7.7: Removing a data component from an entity that does not carry it reports
    /// <see cref="ResultCode.NotFound"/> and leaves both <see cref="World.GetComponents{T}"/> and
    /// <see cref="World.GetComponentEntities{T}"/> results identical to those observed beforehand.
    /// </summary>
    [TestMethod]
    public void NoOpRemovalOfAbsentComponentReturnsNotFoundAndLeavesQueriesUnchanged()
    {
        // Two carriers of Speed and one entity that never carries Speed.
        var carrierA = World!.CreateEntity();
        carrierA.Set(new Speed { Velocity = 1, Acceleration = 2 });
        var carrierB = World!.CreateEntity();
        carrierB.Set(new Speed { Velocity = 3, Acceleration = 4 });
        var nonCarrier = World!.CreateEntity();

        // Observe the queries before the no-op removal.
        var countBefore = World!.GetComponents<Speed>().Count;
        var entitiesBefore = EntitySet<Speed>(World!);

        // Requesting removal of a component the entity does not carry is a no-op / failure.
        var result = nonCarrier.Remove<Speed>();
        Assert.AreEqual(ResultCode.NotFound, result);

        // Queries are unchanged by the failed removal.
        var countAfter = World!.GetComponents<Speed>().Count;
        var entitiesAfter = EntitySet<Speed>(World!);

        Assert.AreEqual(countBefore, countAfter);
        Assert.AreEqual(2, countAfter);
        Assert.IsTrue(entitiesBefore.SetEquals(entitiesAfter));
        Assert.IsTrue(entitiesAfter.Contains(carrierA));
        Assert.IsTrue(entitiesAfter.Contains(carrierB));
        Assert.IsFalse(entitiesAfter.Contains(nonCarrier));
    }

    /// <summary>
    /// R7.6: Removing a data component from one of several carriers leaves the other carriers
    /// unchanged in both <see cref="World.GetComponents{T}"/> and
    /// <see cref="World.GetComponentEntities{T}"/> (membership and stored values).
    /// </summary>
    [TestMethod]
    public void RemovingComponentFromOneCarrierLeavesOtherCarriersUnchanged()
    {
        var entities = new List<Entity>();
        for (var i = 0; i < 4; ++i)
        {
            var entity = World!.CreateEntity();
            entity.Set(new Speed { Velocity = i, Acceleration = i * 10 });
            entities.Add(entity);
        }

        // The entity we will remove the component from, and the ones that must stay unchanged.
        var removed = entities[1];
        var survivors = new[] { entities[0], entities[2], entities[3] };

        // Record the stored value of each survivor before the removal.
        var valuesBefore = new Dictionary<Entity, Speed>();
        foreach (var survivor in survivors)
        {
            valuesBefore[survivor] = survivor.Get<Speed>();
        }

        var countBefore = World!.GetComponents<Speed>().Count;
        Assert.AreEqual(4, countBefore);

        // Remove the component from a single carrier.
        var result = removed.Remove<Speed>();
        Assert.AreEqual(ResultCode.Ok, result);

        // Count drops by exactly one and the removed entity is gone from the entity query.
        Assert.AreEqual(countBefore - 1, World!.GetComponents<Speed>().Count);
        var entitiesAfter = EntitySet<Speed>(World!);
        Assert.IsFalse(entitiesAfter.Contains(removed));

        // Every survivor remains present with its original stored value intact.
        foreach (var survivor in survivors)
        {
            Assert.IsTrue(entitiesAfter.Contains(survivor));
            Assert.IsTrue(survivor.Has<Speed>());

            var expected = valuesBefore[survivor];
            var actual = survivor.Get<Speed>();
            Assert.AreEqual(expected.Velocity, actual.Velocity);
            Assert.AreEqual(expected.Acceleration, actual.Acceleration);
        }

        Assert.AreEqual(survivors.Length, entitiesAfter.Count);
    }

    /// <summary>
    /// Collects the entities yielded by <see cref="World.GetComponentEntities{T}"/> into a list,
    /// preserving order and any duplicates so callers can assert each entity appears exactly once.
    /// </summary>
    private static List<Entity> EntityList<T>(World world)
    {
        var list = new List<Entity>();
        foreach (var entity in world.GetComponentEntities<T>())
        {
            list.Add(entity);
        }
        return list;
    }

    // Feature: ecs-world-component-query-tests, Property 10: Adding a data component is reflected additively
    //
    // For any generated world population with a data-component count of N, assigning that component
    // to exactly one additional entity that did not previously carry it results in a subsequent
    // GetComponents<T>().Count of N+1 and a GetComponentEntities<T>() set equal to the previous set
    // plus the newly added entity, with each entity appearing exactly once.
    //
    // Validates: Requirements 6.1, 6.2
    [TestMethod]
    public void Property10_AddingDataComponent_IsReflectedAdditively()
    {
        var property = Prop.ForAll(
            ComponentQueryGenerators.DataPopulation(),
            plan =>
            {
                var (world, entities) = ComponentQueryGenerators.Build(plan);
                try
                {
                    // Record N (the count before) and the yielded carrier set before the addition.
                    var countBefore = world.GetComponents<Speed>().Count;
                    var setBefore = new HashSet<Entity>(EntityList<Speed>(world));

                    // Identify an entity that does not currently carry Speed. Carrier ordinals are
                    // known from the plan; any other created entity is a non-carrier. If the plan has
                    // no non-carrier (every entity carries Speed, or the world is empty), create one.
                    var carrierOrdinals = new HashSet<int>(plan.CarrierIndices);
                    Entity target = default;
                    var haveTarget = false;
                    for (var i = 0; i < entities.Count; ++i)
                    {
                        if (!carrierOrdinals.Contains(i))
                        {
                            target = entities[i];
                            haveTarget = true;
                            break;
                        }
                    }

                    if (!haveTarget)
                    {
                        target = world.CreateEntity();
                    }

                    // The target must genuinely not carry the component beforehand.
                    Assert.IsFalse(target.Has<Speed>());
                    Assert.IsFalse(setBefore.Contains(target));

                    // Assign the data component to exactly this one additional entity.
                    target.Set(new Speed { Velocity = 42f, Acceleration = -7f });

                    // Count increases by exactly one.
                    var countAfter = world.GetComponents<Speed>().Count;
                    Assert.AreEqual(countBefore + 1, countAfter);

                    // The yielded set equals the previous set plus the newly added entity, with each
                    // entity appearing exactly once (no duplicates in the enumeration).
                    var listAfter = EntityList<Speed>(world);
                    var setAfter = new HashSet<Entity>(listAfter);
                    Assert.AreEqual(listAfter.Count, setAfter.Count);

                    var expected = new HashSet<Entity>(setBefore) { target };
                    Assert.IsTrue(setAfter.SetEquals(expected));
                }
                finally
                {
                    world.Dispose();
                }
            }
        );

        Check.One(Config.QuickThrowOnFailure.WithMaxTest(100), property);
    }

    // Feature: ecs-world-component-query-tests, Property 11: Adding a tag component is reflected additively
    //
    // For any world containing zero components of tag type T, tagging exactly one entity results in a
    // subsequent GetComponents<T>() that is not EmptyComponents<T>.Empty and reports a Count of 1, and
    // a GetComponentEntities<T>() that yields exactly that one entity and no other.
    //
    // Validates: Requirements 6.3, 6.4
    [TestMethod]
    public void Property11_AddingTagComponent_IsReflectedAdditively()
    {
        var property = Prop.ForAll(
            ComponentQueryGenerators.TagPopulation(),
            population =>
            {
                var (total, _) = population;

                // Build a world with `total` untagged entities so it contains ZERO of tag type TagA
                // before the mutation (empty tagged-ordinal set).
                var (world, entities) = ComponentQueryGenerators.BuildTags<TagA>(
                    total,
                    Array.Empty<int>()
                );
                try
                {
                    // Precondition: the world contains zero components of tag type TagA.
                    Assert.AreEqual(0, world.GetComponents<TagA>().Count);

                    // We need at least one entity to tag; if the plan produced an empty world, create one.
                    Entity target = entities.Count > 0 ? entities[0] : world.CreateEntity();
                    Assert.IsFalse(target.Has<TagA>());

                    // Tag exactly one entity with the tag component.
                    target.Tag<TagA>();

                    // The query is now backed by a real tag manager, not the EmptyComponents sentinel.
                    var components = world.GetComponents<TagA>();
                    Assert.IsFalse(components is EmptyComponents<TagA>);
                    Assert.AreEqual(1, components.Count);

                    // GetComponentEntities<TagA>() yields exactly that one entity and no other.
                    var list = EntityList<TagA>(world);
                    Assert.AreEqual(1, list.Count);
                    Assert.AreEqual(target, list[0]);
                }
                finally
                {
                    world.Dispose();
                }
            }
        );

        Check.One(Config.QuickThrowOnFailure.WithMaxTest(100), property);
    }

    // Feature: ecs-world-component-query-tests, Property 12: Removing a data component is reflected subtractively
    //
    // For any generated world population with at least one carrier of data component T, removing T
    // from one carrier results in a subsequent GetComponents<T>().Count equal to the prior count
    // minus one, a GetComponentEntities<T>() set equal to the prior set minus the removed entity
    // (which is absent), and every remaining carrier still present; removing T from every carrier
    // yields a Count of 0 and an empty enumeration.
    //
    // Validates: Requirements 7.1, 7.2, 7.3, 7.6
    [TestMethod]
    public void Property12_RemovingDataComponent_IsReflectedSubtractively()
    {
        var property = Prop.ForAll(
            ComponentQueryGenerators.DataPopulation(),
            plan =>
            {
                // Skip zero-carrier cases gracefully: the property requires at least one carrier.
                if (plan.CarrierIndices.Count == 0)
                {
                    return true;
                }

                var (world, entities) = ComponentQueryGenerators.Build(plan);
                try
                {
                    // The full carrier set, in plan order, so we can remove one then the rest.
                    var carriers = plan.CarrierIndices.Select(idx => entities[idx]).ToList();

                    // Record the prior count and yielded carrier set before any removal.
                    var countBefore = world.GetComponents<Speed>().Count;
                    var setBefore = new HashSet<Entity>(EntityList<Speed>(world));
                    Assert.AreEqual(carriers.Count, countBefore);
                    Assert.IsTrue(setBefore.SetEquals(carriers));

                    // Remove the component from exactly one carrier.
                    var removed = carriers[0];
                    var removeResult = removed.Remove<Speed>();
                    Assert.AreEqual(ResultCode.Ok, removeResult);

                    // Count is exactly the prior count minus one.
                    var countAfterOne = world.GetComponents<Speed>().Count;
                    Assert.AreEqual(countBefore - 1, countAfterOne);

                    // The yielded set equals the prior set minus the removed entity: the removed
                    // entity is absent and every remaining carrier is still present.
                    var listAfterOne = EntityList<Speed>(world);
                    var setAfterOne = new HashSet<Entity>(listAfterOne);
                    Assert.AreEqual(listAfterOne.Count, setAfterOne.Count);
                    Assert.IsFalse(setAfterOne.Contains(removed));

                    var expectedAfterOne = new HashSet<Entity>(setBefore);
                    expectedAfterOne.Remove(removed);
                    Assert.IsTrue(setAfterOne.SetEquals(expectedAfterOne));
                    foreach (var survivor in carriers.Skip(1))
                    {
                        Assert.IsTrue(setAfterOne.Contains(survivor));
                    }

                    // Remove the component from every remaining carrier.
                    foreach (var survivor in carriers.Skip(1))
                    {
                        var survivorResult = survivor.Remove<Speed>();
                        Assert.AreEqual(ResultCode.Ok, survivorResult);
                    }

                    // With no carriers left, Count is 0 and the enumeration is empty.
                    Assert.AreEqual(0, world.GetComponents<Speed>().Count);
                    Assert.AreEqual(0, EntityList<Speed>(world).Count);
                }
                finally
                {
                    world.Dispose();
                }

                return true;
            }
        );

        Check.One(Config.QuickThrowOnFailure.WithMaxTest(100), property);
    }

    // Feature: ecs-world-component-query-tests, Property 13: Removing a tag component is reflected subtractively
    //
    // For any generated tag population with at least one tagged entity, removing the tag from one
    // tagged entity results in a GetComponentEntities<T>() set equal to the prior tagged set minus
    // the removed entity, with every remaining tagged entity still present.
    //
    // Validates: Requirements 7.4
    [TestMethod]
    public void Property13_RemovingTagComponent_IsReflectedSubtractively()
    {
        var property = Prop.ForAll(
            ComponentQueryGenerators.TagPopulation(),
            population =>
            {
                var (total, taggedIndices) = population;

                // Skip zero-tagged cases gracefully: the property requires at least one tagged entity.
                if (taggedIndices.Count == 0)
                {
                    return true;
                }

                var (world, entities) = ComponentQueryGenerators.BuildTags<TagA>(total, taggedIndices);
                try
                {
                    // The full tagged set, in ordinal order, so we can remove one and check the rest.
                    var tagged = taggedIndices.Select(idx => entities[idx]).ToList();

                    // Record the prior tagged set before the removal and confirm it matches the model.
                    var expectedBefore = ComponentQueryGenerators.ExpectedTaggedSet(taggedIndices, entities);
                    var setBefore = new HashSet<Entity>(EntityList<TagA>(world));
                    Assert.IsTrue(setBefore.SetEquals(expectedBefore));

                    // Remove the tag from exactly one tagged entity.
                    var removed = tagged[0];
                    var removeResult = removed.Remove<TagA>();
                    Assert.AreEqual(ResultCode.Ok, removeResult);

                    // The yielded set equals the prior tagged set minus the removed entity: the removed
                    // entity is absent and every remaining tagged entity is still present, with no
                    // duplicate entities in the enumeration.
                    var listAfter = EntityList<TagA>(world);
                    var setAfter = new HashSet<Entity>(listAfter);
                    Assert.AreEqual(listAfter.Count, setAfter.Count);
                    Assert.IsFalse(setAfter.Contains(removed));

                    var expectedAfter = new HashSet<Entity>(setBefore);
                    expectedAfter.Remove(removed);
                    Assert.IsTrue(setAfter.SetEquals(expectedAfter));
                    foreach (var survivor in tagged.Skip(1))
                    {
                        Assert.IsTrue(setAfter.Contains(survivor));
                    }
                }
                finally
                {
                    world.Dispose();
                }

                return true;
            }
        );

        Check.One(Config.QuickThrowOnFailure.WithMaxTest(100), property);
    }

    // Feature: ecs-world-component-query-tests, Property 14: Component removal does not affect other component types
    //
    // For any entity carrying two or more distinct data-component types, removing one of those types
    // leaves the entity still yielded by GetComponentEntities for every other type it continues to
    // carry.
    //
    // Validates: Requirements 7.5
    [TestMethod]
    public void Property14_ComponentRemoval_DoesNotAffectOtherComponentTypes()
    {
        // Data-component type codes: 0 = Speed, 1 = Health, 2 = Position (from Common.cs).
        // The valid multi-type combinations carry two or more distinct types.
        int[][] typeCombos =
        [
            [0, 1],
            [0, 2],
            [1, 2],
            [0, 1, 2],
        ];

        // Sets the data component identified by <paramref name="code"/> on the entity.
        static void SetType(Entity entity, int code)
        {
            switch (code)
            {
                case 0:
                    entity.Set(new Speed { Velocity = 1f, Acceleration = 2f });
                    break;
                case 1:
                    entity.Set(new Health { Value = 3 });
                    break;
                default:
                    entity.Set(new Position { X = 4f, Y = 5f });
                    break;
            }
        }

        static ResultCode RemoveType(Entity entity, int code) =>
            code switch
            {
                0 => entity.Remove<Speed>(),
                1 => entity.Remove<Health>(),
                _ => entity.Remove<Position>(),
            };

        static bool HasType(Entity entity, int code) =>
            code switch
            {
                0 => entity.Has<Speed>(),
                1 => entity.Has<Health>(),
                _ => entity.Has<Position>(),
            };

        static bool YieldedFor<T>(World world, Entity entity)
        {
            foreach (var yielded in world.GetComponentEntities<T>())
            {
                if (yielded == entity)
                {
                    return true;
                }
            }
            return false;
        }

        static bool Yielded(World world, int code, Entity entity) =>
            code switch
            {
                0 => YieldedFor<Speed>(world, entity),
                1 => YieldedFor<Health>(world, entity),
                _ => YieldedFor<Position>(world, entity),
            };

        var generator =
            from comboIdx in Gen.Choose(0, typeCombos.Length - 1)
            let carried = typeCombos[comboIdx]
            from removePos in Gen.Choose(0, carried.Length - 1)
            from extraEntities in Gen.Choose(0, 5)
            select (Carried: carried, RemoveCode: carried[removePos], Extra: extraEntities);

        var property = Prop.ForAll(
            generator.ToArbitrary(),
            testCase =>
            {
                var (carried, removeCode, extra) = testCase;

                var world = World.CreateWorld();
                try
                {
                    // The entity under test carries two or more distinct data-component types.
                    var entity = world.CreateEntity();
                    foreach (var code in carried)
                    {
                        SetType(entity, code);
                    }

                    // Optionally create other entities carrying an assortment of the same types so
                    // the queries have a non-trivial population beyond the entity under test.
                    for (var i = 0; i < extra; ++i)
                    {
                        var other = world.CreateEntity();
                        SetType(other, i % 3);
                    }

                    // Precondition: the entity is yielded for every type it carries.
                    foreach (var code in carried)
                    {
                        Assert.IsTrue(Yielded(world, code, entity));
                    }

                    // Remove exactly one of the carried types from the entity.
                    var removeResult = RemoveType(entity, removeCode);
                    Assert.AreEqual(ResultCode.Ok, removeResult);

                    // The entity is absent from the removed type's query.
                    Assert.IsFalse(HasType(entity, removeCode));
                    Assert.IsFalse(Yielded(world, removeCode, entity));

                    // The entity is still yielded for every OTHER type it continues to carry.
                    foreach (var code in carried)
                    {
                        if (code == removeCode)
                        {
                            continue;
                        }

                        Assert.IsTrue(HasType(entity, code));
                        Assert.IsTrue(Yielded(world, code, entity));
                    }
                }
                finally
                {
                    world.Dispose();
                }

                return true;
            }
        );

        Check.One(Config.QuickThrowOnFailure.WithMaxTest(100), property);
    }

    // Feature: ecs-world-component-query-tests, Property 15: Removing an absent component is a no-op
    //
    // For any generated world population and any entity that does not carry data component T,
    // requesting removal of T from that entity returns a failure/no-op result code, and
    // GetComponents and GetComponentEntities for every affected type yield results identical to
    // those observed before the request.
    //
    // Validates: Requirements 7.7
    [TestMethod]
    public void Property15_RemovingAbsentComponent_IsNoOp()
    {
        var property = Prop.ForAll(
            ComponentQueryGenerators.DataPopulation(),
            plan =>
            {
                var (world, entities) = ComponentQueryGenerators.Build(plan);
                try
                {
                    // Identify an entity that does NOT carry Speed. Carrier ordinals are known from
                    // the plan; any other created entity is a non-carrier. If every entity carries
                    // Speed (or the world is empty), create a fresh entity that carries nothing.
                    var carrierOrdinals = new HashSet<int>(plan.CarrierIndices);
                    Entity target = default;
                    var haveTarget = false;
                    for (var i = 0; i < entities.Count; ++i)
                    {
                        if (!carrierOrdinals.Contains(i))
                        {
                            target = entities[i];
                            haveTarget = true;
                            break;
                        }
                    }

                    if (!haveTarget)
                    {
                        target = world.CreateEntity();
                    }

                    // Precondition: the target genuinely does not carry the component.
                    Assert.IsFalse(target.Has<Speed>());

                    // Observe GetComponents<Speed>().Count and the GetComponentEntities<Speed>() set
                    // before the no-op removal request.
                    var countBefore = world.GetComponents<Speed>().Count;
                    var setBefore = EntitySet<Speed>(world);

                    // Requesting removal of a component the entity does not carry is a no-op/failure.
                    var result = target.Remove<Speed>();
                    Assert.AreEqual(ResultCode.NotFound, result);

                    // GetComponents and GetComponentEntities yield results identical to before.
                    var countAfter = world.GetComponents<Speed>().Count;
                    var setAfter = EntitySet<Speed>(world);

                    Assert.AreEqual(countBefore, countAfter);
                    Assert.IsTrue(setBefore.SetEquals(setAfter));
                    Assert.IsFalse(setAfter.Contains(target));
                }
                finally
                {
                    world.Dispose();
                }

                return true;
            }
        );

        Check.One(Config.QuickThrowOnFailure.WithMaxTest(100), property);
    }
}
