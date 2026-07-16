using FsCheck;
using FsCheck.Fluent;

namespace HelixToolkit.Nex.ECS.Tests;

/// <summary>
/// Shared FsCheck generators and reference model for the ECS World component-query test suite
/// (feature: ecs-world-component-query-tests).
///
/// <para>
/// A <see cref="PopulationPlan"/> is the source of truth for a generated world: it describes how
/// many entities to create, which of those entities carry the data component under test, and the
/// <see cref="Speed"/> value assigned to each carrier. <see cref="Build"/> materializes a plan into
/// a fresh <see cref="World"/>; the reference-model helpers derive the expected carrier set, count,
/// and value map directly from the plan so property tests can compare query results against them.
/// </para>
///
/// <para>
/// The generators produce every population shape the requirements describe: empty world
/// (<c>TotalEntities == 0</c>), entities-but-no-carriers (empty carrier set with
/// <c>TotalEntities &gt; 0</c>), a single carrier, all carriers, and arbitrary partial subsets.
/// </para>
/// </summary>
internal static class ComponentQueryGenerators
{
    /// <summary>
    /// A generated plan describing how many entities to create, which of them carry the data
    /// component under test, and the field values to assign to each carrier.
    /// </summary>
    /// <param name="TotalEntities">Total number of entities to create in the world (always &gt;= the number of carriers).</param>
    /// <param name="CarrierIndices">
    /// Distinct entity ordinals (indices into the created-entities list) that carry the component.
    /// Ordered ascending; may be empty.
    /// </param>
    /// <param name="Values">
    /// The <see cref="Speed"/> value assigned to each carrier, parallel to <paramref name="CarrierIndices"/>.
    /// </param>
    internal sealed record PopulationPlan(
        int TotalEntities,
        IReadOnlyList<int> CarrierIndices,
        IReadOnlyList<Speed> Values
    );

    /// <summary>
    /// Generates finite <see cref="float"/> values in the range [-1000, 1000] with three-decimal
    /// resolution. NaN/Infinity are deliberately excluded so that field-for-field value comparisons
    /// in the reference model remain well defined.
    /// </summary>
    private static Gen<float> FiniteFloat() =>
        from n in Gen.Choose(-1_000_000, 1_000_000)
        select n / 1000f;

    /// <summary>
    /// Generates an arbitrary <see cref="Speed"/> data component with finite field values.
    /// </summary>
    private static Gen<Speed> SpeedValue() =>
        from velocity in FiniteFloat()
        from acceleration in FiniteFloat()
        select new Speed { Velocity = velocity, Acceleration = acceleration };

    /// <summary>
    /// FsCheck generator for a data-component population: a total entity count in [0, maxEntities],
    /// a distinct (possibly empty) subset of carrier ordinals, and an arbitrary <see cref="Speed"/>
    /// value for each carrier.
    /// </summary>
    /// <param name="maxEntities">The inclusive upper bound on the number of entities to create.</param>
    internal static Arbitrary<PopulationPlan> DataPopulation(int maxEntities = 30) =>
        (
            from total in Gen.Choose(0, maxEntities)
            from carrierFlags in Gen.ArrayOf(Gen.Elements(true, false), total)
            let carriers = CarrierIndicesFromFlags(carrierFlags)
            from values in Gen.ArrayOf(SpeedValue(), carriers.Count)
            select new PopulationPlan(total, carriers, values)
        ).ToArbitrary();

    /// <summary>
    /// FsCheck generator for a tag-component population: a total entity count in [0, maxEntities]
    /// and a distinct (possibly empty) subset of tagged ordinals.
    /// </summary>
    /// <param name="maxEntities">The inclusive upper bound on the number of entities to create.</param>
    internal static Arbitrary<(int Total, IReadOnlyList<int> TaggedIndices)> TagPopulation(
        int maxEntities = 30
    ) =>
        (
            from total in Gen.Choose(0, maxEntities)
            from tagFlags in Gen.ArrayOf(Gen.Elements(true, false), total)
            let tagged = CarrierIndicesFromFlags(tagFlags)
            select (total, (IReadOnlyList<int>)tagged)
        ).ToArbitrary();

    /// <summary>
    /// Converts a per-ordinal boolean flag array into the ascending list of ordinals whose flag is set.
    /// </summary>
    private static IReadOnlyList<int> CarrierIndicesFromFlags(bool[] flags)
    {
        var indices = new List<int>();
        for (var i = 0; i < flags.Length; ++i)
        {
            if (flags[i])
            {
                indices.Add(i);
            }
        }
        return indices;
    }

    /// <summary>
    /// Materializes a <see cref="PopulationPlan"/> into a fresh <see cref="World"/>: creates
    /// <see cref="PopulationPlan.TotalEntities"/> entities and assigns the generated <see cref="Speed"/>
    /// value to each carrier. The returned entity list is indexed the same as the plan
    /// (index <c>i</c> is the entity created for ordinal <c>i</c>).
    /// </summary>
    /// <remarks>
    /// The caller owns the returned <see cref="World"/> and is responsible for disposing it (for
    /// example via <c>using</c> or <c>try/finally</c>) to keep the static, world-id-keyed manager
    /// storage clean and to avoid exhausting the world-id limit under many property iterations.
    /// </remarks>
    /// <param name="plan">The plan to materialize.</param>
    /// <returns>The created world and the list of created entities (parallel to the plan ordinals).</returns>
    internal static (World World, IReadOnlyList<Entity> Entities) Build(PopulationPlan plan)
    {
        var world = World.CreateWorld();
        var entities = new Entity[plan.TotalEntities];
        for (var i = 0; i < plan.TotalEntities; ++i)
        {
            entities[i] = world.CreateEntity();
        }

        for (var c = 0; c < plan.CarrierIndices.Count; ++c)
        {
            var value = plan.Values[c];
            entities[plan.CarrierIndices[c]].Set(value);
        }

        return (world, entities);
    }

    /// <summary>
    /// Materializes a tag population into a fresh <see cref="World"/>: creates <paramref name="total"/>
    /// entities and tags each entity at a tagged ordinal with tag type <typeparamref name="T"/>.
    /// The returned entity list is indexed the same as the ordinals. The caller owns the world and
    /// must dispose it.
    /// </summary>
    /// <typeparam name="T">The tag component type (an empty struct such as <see cref="TagA"/>).</typeparam>
    /// <param name="total">The number of entities to create.</param>
    /// <param name="taggedIndices">The ordinals to tag.</param>
    internal static (World World, IReadOnlyList<Entity> Entities) BuildTags<T>(
        int total,
        IReadOnlyList<int> taggedIndices
    )
        where T : struct
    {
        var world = World.CreateWorld();
        var entities = new Entity[total];
        for (var i = 0; i < total; ++i)
        {
            entities[i] = world.CreateEntity();
        }

        foreach (var idx in taggedIndices)
        {
            entities[idx].Tag<T>();
        }

        return (world, entities);
    }

    #region Reference model

    /// <summary>
    /// Derives the expected set of carrier entities from a plan and its materialized entities.
    /// This is the reference-model "membership" against which query results are compared.
    /// </summary>
    internal static HashSet<Entity> ExpectedCarrierSet(
        PopulationPlan plan,
        IReadOnlyList<Entity> entities
    )
    {
        var set = new HashSet<Entity>();
        foreach (var idx in plan.CarrierIndices)
        {
            set.Add(entities[idx]);
        }
        return set;
    }

    /// <summary>
    /// Derives the expected component count from a plan (the number of carriers).
    /// </summary>
    internal static int ExpectedCount(PopulationPlan plan) => plan.CarrierIndices.Count;

    /// <summary>
    /// Derives the expected <see cref="Entity"/> to <see cref="Speed"/> value map from a plan and its
    /// materialized entities. This is the reference-model "value fidelity" source of truth.
    /// </summary>
    internal static Dictionary<Entity, Speed> ExpectedValueMap(
        PopulationPlan plan,
        IReadOnlyList<Entity> entities
    )
    {
        var map = new Dictionary<Entity, Speed>();
        for (var c = 0; c < plan.CarrierIndices.Count; ++c)
        {
            map[entities[plan.CarrierIndices[c]]] = plan.Values[c];
        }
        return map;
    }

    /// <summary>
    /// Derives the expected set of tagged entities from a tag population and its materialized entities.
    /// </summary>
    internal static HashSet<Entity> ExpectedTaggedSet(
        IReadOnlyList<int> taggedIndices,
        IReadOnlyList<Entity> entities
    )
    {
        var set = new HashSet<Entity>();
        foreach (var idx in taggedIndices)
        {
            set.Add(entities[idx]);
        }
        return set;
    }

    #endregion
}
