using FsCheck;
using FsCheck.Fluent;

namespace HelixToolkit.Nex.ECS.Tests;

/// <summary>
/// Disabled/disposed entity lifecycle behavior for the ECS World component-query methods
/// (feature: ecs-world-component-query-tests).
///
/// <para>
/// These example-based tests document how <see cref="World.GetComponentEntities{T}"/> treats an
/// entity whose enabled state changes. Per the design research, <c>SetEnabled(false)</c> only flips
/// an enabled flag and does not touch component storage, so the enumerator (which checks storage
/// membership, not the enabled flag) is expected to keep yielding disabled entities. R8.1 is
/// authored to <em>record</em> this observed baseline rather than presume inclusion or exclusion.
/// </para>
/// </summary>
[TestClass]
public class WorldComponentQueryLifecycleTests
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

    // R8.1: a single carrier of a data component in an otherwise empty world (for that type) is set
    // to disabled. Record the set of entities yielded by GetComponentEntities as the observed
    // baseline and assert the yielded set matches that recorded behavior (neither inclusion nor
    // exclusion is presumed). Per design research finding #7, disabling does not touch storage, so
    // the observed baseline is that the disabled carrier remains in the query results.
    [TestMethod]
    public void SingleCarrierDisabled_GetComponentEntities_MatchesRecordedBaseline()
    {
        var entity = World!.CreateEntity();
        entity.Set(new Speed { Velocity = 3.5f, Acceleration = 1.25f });

        entity.SetEnabled(false);
        Assert.IsFalse(entity.Enabled, "Precondition: the carrier should be disabled.");

        // Record the observed baseline: which entities does GetComponentEntities yield for the
        // disabled carrier's component type? SetEnabled(false) does not remove the component from
        // storage, so the disabled entity is observed to remain in the yielded set.
        var observedBaseline = new List<Entity> { entity };

        var yielded = Collect<Speed>(World!);

        // Assert the yielded set matches the recorded observed behavior.
        CollectionAssert.AreEquivalent(observedBaseline, yielded);
    }

    // R8.2: a disabled carrier of a data component is later re-enabled; GetComponentEntities for
    // that type yields that entity.
    [TestMethod]
    public void DisabledCarrierReEnabled_GetComponentEntities_YieldsEntity()
    {
        var entity = World!.CreateEntity();
        entity.Set(new Speed { Velocity = 7.0f, Acceleration = -2.0f });

        entity.SetEnabled(false);
        Assert.IsFalse(entity.Enabled, "Precondition: the carrier should be disabled.");

        entity.SetEnabled(true);
        Assert.IsTrue(entity.Enabled, "Precondition: the carrier should be re-enabled.");

        var yielded = Collect<Speed>(World!);

        Assert.AreEqual(1, yielded.Count);
        Assert.AreEqual(entity, yielded[0]);
    }

    // Feature: ecs-world-component-query-tests, Property 16: Disposing a carrier removes it from queries
    //
    // For any generated world population with at least one carrier of data component T, disposing one
    // carrier results in a subsequent GetComponents<T>().Count equal to the prior count minus one and a
    // GetComponentEntities<T>() set that excludes the disposed entity; disposing the sole carrier yields
    // a Count of 0 and an empty enumeration.
    //
    // Validates: Requirements 8.3, 8.4, 9.6
    [TestMethod]
    public void Property16_DisposingCarrier_RemovesItFromQueries()
    {
        var property = Prop.ForAll(
            ComponentQueryGenerators.DataPopulation(),
            plan =>
            {
                // Only exercise populations with at least one carrier; skip empty-carrier cases.
                if (plan.CarrierIndices.Count == 0)
                {
                    return true;
                }

                var (world, entities) = ComponentQueryGenerators.Build(plan);
                try
                {
                    // Record the prior count and the yielded carrier set before disposal.
                    var countBefore = world.GetComponents<Speed>().Count;
                    var setBefore = Collect<Speed>(world);

                    // Dispose exactly one carrier.
                    var disposed = entities[plan.CarrierIndices[0]];
                    disposed.Dispose();

                    // Count drops by exactly one, and the disposed entity is excluded from the query.
                    var countAfter = world.GetComponents<Speed>().Count;
                    var setAfter = Collect<Speed>(world);

                    if (countAfter != countBefore - 1)
                    {
                        return false;
                    }
                    if (setAfter.Contains(disposed))
                    {
                        return false;
                    }

                    // When the disposed carrier was the sole carrier, the type is now empty.
                    if (countBefore == 1)
                    {
                        if (countAfter != 0 || setAfter.Count != 0)
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
        );

        Check.One(Config.QuickThrowOnFailure.WithMaxTest(100), property);
    }

    // Feature: ecs-world-component-query-tests, Property 17: Disabling entities does not change the component count
    //
    // For any generated world population, disabling one or more carriers of data component T leaves
    // GetComponents<T>().Count equal to the count recorded before the disable operation. SetEnabled(false)
    // only flips an enabled flag and does not touch component storage.
    //
    // Validates: Requirements 8.5
    [TestMethod]
    public void Property17_DisablingEntities_DoesNotChangeComponentCount()
    {
        var property = Prop.ForAll(
            ComponentQueryGenerators.DataPopulation(),
            plan =>
            {
                var (world, entities) = ComponentQueryGenerators.Build(plan);
                try
                {
                    // Record the component count before disabling any carriers.
                    var countBefore = world.GetComponents<Speed>().Count;

                    // Disable one or more carriers (all of them). When there are no carriers, the
                    // count is trivially unchanged and remains zero.
                    foreach (var idx in plan.CarrierIndices)
                    {
                        entities[idx].SetEnabled(false);
                    }

                    // Disabling must not change the component count.
                    var countAfter = world.GetComponents<Speed>().Count;
                    return countAfter == countBefore;
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
