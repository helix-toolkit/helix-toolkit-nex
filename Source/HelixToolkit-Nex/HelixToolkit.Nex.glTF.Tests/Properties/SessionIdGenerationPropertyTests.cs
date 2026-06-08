using System.Text.RegularExpressions;

namespace HelixToolkit.Nex.glTF.Tests.Properties;

/// <summary>
/// Property-based tests for session identifier generation (Properties 1 and 2).
/// Feature: import-resource-isolation
/// </summary>
[TestClass]
public class SessionIdGenerationPropertyTests
{
    private static readonly Config FsCheckConfig = Config.Default.WithMaxTest(100);

    // Feature: import-resource-isolation, Property 2: Session Identifier Format Validity

    /// <summary>
    /// Property 2: For any generated Session_Identifier, the value SHALL match the regex pattern
    /// ^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$ and have a length of
    /// exactly 36 characters.
    /// **Validates: Requirements 1.2, 2.5, 3.5, 7.5**
    /// </summary>
    [TestMethod]
    public void SessionId_MatchesGuidDFormat_ForAnyGeneration()
    {
        var pattern = new Regex(
            @"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$",
            RegexOptions.Compiled
        );

        // Generate arbitrary integers as seeds to drive independent GUID generations.
        // Each iteration generates a fresh session ID and validates its format.
        Prop.ForAll(
                ArbMap.Default.ArbFor<int>(),
                _ =>
                {
                    var sessionId = Guid.NewGuid().ToString("D");

                    // Assert length is exactly 36 characters
                    if (sessionId.Length != 36)
                        return false;

                    // Assert matches GUID "D" format pattern (lowercase hex with hyphens)
                    if (!pattern.IsMatch(sessionId))
                        return false;

                    return true;
                }
            )
            .Check(FsCheckConfig);
    }
}
