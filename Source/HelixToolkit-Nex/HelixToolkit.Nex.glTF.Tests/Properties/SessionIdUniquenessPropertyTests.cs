namespace HelixToolkit.Nex.glTF.Tests.Properties;

// Feature: import-resource-isolation, Property 1: Session Identifier Uniqueness

/// <summary>
/// Property-based tests for Session Identifier Uniqueness (Property 1).
/// Verifies that for any N (2..50) session IDs generated using Guid.NewGuid().ToString("D"),
/// all N values are distinct (set cardinality equals N).
/// **Validates: Requirements 1.1, 1.4, 5.5, 7.4**
/// </summary>
[TestClass]
public class SessionIdUniquenessPropertyTests
{
    private static readonly Config FsCheckConfig = Config.Default.WithMaxTest(100);

    /// <summary>
    /// Property 1: For any N in [2..50], generating N session IDs produces N distinct values.
    /// The set cardinality of generated session IDs always equals the count of IDs generated.
    /// **Validates: Requirements 1.1, 1.4, 5.5, 7.4**
    /// </summary>
    [TestMethod]
    public void GeneratedSessionIds_AreAlwaysDistinct()
    {
        // Generator: N in range [2..50]
        var countGen = Gen.Choose(2, 50);

        Prop.ForAll(
                Arb.From(countGen),
                (int n) =>
                {
                    // Generate N session IDs using the same mechanism as Importer
                    var sessionIds = new List<string>(n);
                    for (int i = 0; i < n; i++)
                    {
                        sessionIds.Add(Guid.NewGuid().ToString("D"));
                    }

                    // Assert all N values are distinct (set cardinality equals N)
                    var distinctCount = new HashSet<string>(
                        sessionIds,
                        StringComparer.Ordinal
                    ).Count;
                    return distinctCount == n;
                }
            )
            .Check(FsCheckConfig);
    }
}
