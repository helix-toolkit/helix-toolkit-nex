using FsCheck;
using FsCheck.Fluent;

namespace HelixToolkit.Nex.ECS.Tests;

/// <summary>
/// Example-based tests for the tag-component path of <see cref="World.GetComponents{T}"/> and
/// <see cref="World.GetComponentEntities{T}"/> (feature: ecs-world-component-query-tests).
///
/// <para>
/// Tag types (e.g. <see cref="TagA"/>) are empty structs whose presence is tracked by a
/// <c>TagManager&lt;T&gt;</c> rather than value storage. When at least one entity carries a tag,
/// <see cref="World.GetComponents{T}"/> returns that tag manager (not
/// <see cref="EmptyComponents{T}"/>), and its <c>Count</c> and enumerated entities reflect exactly
/// the tagged entities. These tests cover requirements R5.1-R5.4.
/// </para>
/// </summary>
[TestClass]
public class WorldComponentQueryTagTests
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
    /// R5.1: A world with exactly one tagged entity returns a tag manager (not
    /// <see cref="EmptyComponents{T}"/>) reporting a Count of 1.
    /// </summary>
    [TestMethod]
    public void SingleTaggedEntity_GetComponents_NotEmptyComponents_CountIsOne()
    {
        var entity = World!.CreateEntity();
        entity.Tag<TagA>();

        var components = World.GetComponents<TagA>();

        // Not the EmptyComponents sentinel: a real tag manager must back the query.
        Assert.IsFalse(
            components is EmptyComponents<TagA>,
            "Expected a tag manager, not EmptyComponents, when an entity carries the tag."
        );
        Assert.AreEqual(1, components.Count);
    }

    /// <summary>
    /// R5.2 / R5.4: Five tagged entities are yielded exactly once each by
    /// <see cref="World.GetComponentEntities{T}"/>, and <see cref="World.GetComponents{T}"/> reports
    /// a Count of 5.
    /// </summary>
    [TestMethod]
    public void FiveTaggedEntities_YieldsExactlyThoseFive_NoDuplicates_CountIsFive()
    {
        var tagged = new List<Entity>();
        for (var i = 0; i < 5; ++i)
        {
            var entity = World!.CreateEntity();
            entity.Tag<TagA>();
            tagged.Add(entity);
        }

        // R5.4: Count matches the number of tagged entities.
        Assert.AreEqual(5, World!.GetComponents<TagA>().Count);

        // R5.2: enumeration yields exactly those five, with no duplicates.
        var yielded = new List<Entity>();
        foreach (var entity in World.GetComponentEntities<TagA>())
        {
            yielded.Add(entity);
        }

        Assert.AreEqual(5, yielded.Count, "Expected exactly five yielded entities.");
        var yieldedSet = new HashSet<Entity>(yielded);
        Assert.AreEqual(yielded.Count, yieldedSet.Count, "Yielded entities must contain no duplicates.");
        CollectionAssert.AreEquivalent(tagged, yieldedSet.ToList());
    }

    /// <summary>
    /// R5.3: With 3 of 5 entities tagged, <see cref="World.GetComponentEntities{T}"/> yields exactly
    /// the 3 tagged entities and none of the 2 untagged entities.
    /// </summary>
    [TestMethod]
    public void ThreeOfFiveTagged_YieldsOnlyTagged_ExcludesUntagged()
    {
        var tagged = new List<Entity>();
        var untagged = new List<Entity>();
        for (var i = 0; i < 5; ++i)
        {
            var entity = World!.CreateEntity();
            if (i < 3)
            {
                entity.Tag<TagA>();
                tagged.Add(entity);
            }
            else
            {
                untagged.Add(entity);
            }
        }

        var yielded = new HashSet<Entity>();
        foreach (var entity in World!.GetComponentEntities<TagA>())
        {
            yielded.Add(entity);
        }

        Assert.AreEqual(3, yielded.Count, "Expected exactly the three tagged entities.");
        foreach (var entity in tagged)
        {
            Assert.IsTrue(yielded.Contains(entity), "Every tagged entity must be yielded.");
        }
        foreach (var entity in untagged)
        {
            Assert.IsFalse(yielded.Contains(entity), "No untagged entity may be yielded.");
        }
    }

    /// <summary>
    /// For any generated tag population, <see cref="World.GetComponents{T}"/> for a tag type reports
    /// a Count equal to the number of tagged entities, and whenever at least one entity is tagged the
    /// returned <see cref="IComponents{T}"/> is a real tag manager rather than the
    /// <see cref="EmptyComponents{T}"/> sentinel.
    ///
    /// <para>
    /// <see cref="EmptyComponents{T}"/> is a value type returned boxed as <see cref="IComponents{T}"/>,
    /// so identity is checked with an <c>is EmptyComponents&lt;TagA&gt;</c> type test rather than
    /// <see cref="object.ReferenceEquals"/>.
    /// </para>
    ///
    /// <para><b>Validates: Requirements 5.1, 5.4</b></para>
    /// </summary>
    [TestMethod]
    public void Property8_TagCount_EqualsTaggedCount_WithNonEmptyIdentity()
    {
        // Feature: ecs-world-component-query-tests, Property 8: Tag-component count equals tagged count with non-empty identity
        Prop.ForAll(
                ComponentQueryGenerators.TagPopulation(),
                ((int Total, IReadOnlyList<int> TaggedIndices) population) =>
                {
                    var (world, entities) = ComponentQueryGenerators.BuildTags<TagA>(
                        population.Total,
                        population.TaggedIndices
                    );
                    try
                    {
                        var expectedTagged = ComponentQueryGenerators.ExpectedTaggedSet(
                            population.TaggedIndices,
                            entities
                        );
                        var expectedCount = expectedTagged.Count;

                        var components = world.GetComponents<TagA>();

                        // Count equals the number of tagged entities.
                        if (components.Count != expectedCount)
                        {
                            return false;
                        }

                        // Whenever at least one entity is tagged, the query must be backed by a real
                        // tag manager, not the EmptyComponents sentinel (boxed struct -> use `is`).
                        if (expectedCount > 0 && components is EmptyComponents<TagA>)
                        {
                            return false;
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
    /// For any generated tag population, the set of entities yielded by
    /// <see cref="World.GetComponentEntities{T}"/> for a tag type equals exactly the set of tagged
    /// entities: every tagged entity appears once, no untagged entity appears, and there are no
    /// duplicates.
    ///
    /// <para><b>Validates: Requirements 5.2, 5.3</b></para>
    /// </summary>
    [TestMethod]
    public void Property9_TagMembership_EqualsTaggedSet_NoDuplicates_ExcludesUntagged()
    {
        // Feature: ecs-world-component-query-tests, Property 9: Tag-component membership equals the tagged set
        Prop.ForAll(
                ComponentQueryGenerators.TagPopulation(),
                ((int Total, IReadOnlyList<int> TaggedIndices) population) =>
                {
                    var (world, entities) = ComponentQueryGenerators.BuildTags<TagA>(
                        population.Total,
                        population.TaggedIndices
                    );
                    try
                    {
                        var expectedTagged = ComponentQueryGenerators.ExpectedTaggedSet(
                            population.TaggedIndices,
                            entities
                        );

                        // Collect the yielded entities.
                        var yielded = new List<Entity>();
                        foreach (var entity in world.GetComponentEntities<TagA>())
                        {
                            yielded.Add(entity);
                        }

                        // No duplicate entities are yielded.
                        var yieldedSet = new HashSet<Entity>(yielded);
                        if (yieldedSet.Count != yielded.Count)
                        {
                            return false;
                        }

                        // The yielded set equals exactly the expected tagged set (this also
                        // guarantees every tagged entity is present and no untagged entity is
                        // yielded).
                        return yieldedSet.SetEquals(expectedTagged);
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
