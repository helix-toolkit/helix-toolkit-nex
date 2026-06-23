using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

namespace HelixToolkit.Nex.glTF.Tests.Properties;

// Feature: consolidate-gltf-test-mocks
// Task 1: Bug-condition exploration test (duplicate inventory + baseline).
//
// Property 1: Bug Condition - Duplicates Replaced By Shared Mocks With Identical Behavior.
//
// This is a deterministic, STRUCTURAL bug: the duplicated `private sealed` stub definitions
// (StubGeometryManager, StubMaterialPropertyManager, StubTextureRepository, StubSamplerRepository)
// scattered across 20+ test files. Because the input space is the concrete set of source files in
// the test project, the property is scoped to enumerate every `.cs` file under Unit/ and Properties/
// and apply the bug condition from the design:
//
//   isBugCondition(file) :=
//       fileIsInTestProject(file, "HelixToolkit.Nex.glTF.Tests")
//       AND declaresPrivateNestedStub(file, { StubGeometryManager, StubMaterialPropertyManager,
//                                              StubTextureRepository, StubSamplerRepository })
//       AND NOT referencesSharedMock(file)   // i.e. no `using HelixToolkit.Nex.glTF.Tests.Mocks;`
//
// The test asserts NO file satisfies isBugCondition (every Stub* reference resolves to a single
// shared mock in HelixToolkit.Nex.glTF.Tests.Mocks and no private duplicate definitions remain).
//
// CRITICAL: This test is EXPECTED TO FAIL on the UNFIXED code. The failure is the documented
// counterexample proving the bug exists (20+ files declare duplicate private stubs). DO NOT "fix"
// this test or the production/test code when it fails - the failure is the evidence of the bug.
// After the consolidation fix (task 3) this same test will PASS, validating the fix.
//
// **Validates: Requirements 1.1, 1.2, 1.3, 2.1, 2.4**

/// <summary>
/// Structural exploration test that inventories every duplicated <c>private sealed</c> stub
/// definition in the <c>HelixToolkit.Nex.glTF.Tests</c> project and asserts the duplication has
/// been consolidated into a single shared mock set under
/// <c>HelixToolkit.Nex.glTF.Tests.Mocks</c>.
/// </summary>
[TestClass]
public class DuplicateStubInventoryExplorationTests
{
    /// <summary>The four stub interface doubles whose duplication this fix consolidates.</summary>
    private static readonly string[] StubTypeNames =
    [
        "StubGeometryManager",
        "StubMaterialPropertyManager",
        "StubTextureRepository",
        "StubSamplerRepository",
    ];

    /// <summary>Namespace of the shared mock set that consolidated references must resolve to.</summary>
    private const string SharedMockNamespace = "HelixToolkit.Nex.glTF.Tests.Mocks";

    // Matches a private nested duplicate stub definition of one of the four interface doubles.
    private static readonly Regex PrivateStubDeclaration = new(
        @"private\s+sealed\s+class\s+(StubGeometryManager|StubMaterialPropertyManager|StubTextureRepository|StubSamplerRepository)\b",
        RegexOptions.Compiled
    );

    // Matches the actual `using HelixToolkit.Nex.glTF.Tests.Mocks;` directive on a using line so
    // that a mere mention of the namespace in a comment or string does not count as a reference.
    private static readonly Regex SharedMockUsingDirective = new(
        @"^\s*using\s+HelixToolkit\.Nex\.glTF\.Tests\.Mocks\s*;",
        RegexOptions.Multiline | RegexOptions.Compiled
    );

    /// <summary>
    /// Resolves the test project root directory from this source file's compile-time path so the
    /// inventory can scan the on-disk sources regardless of the working directory at run time.
    /// Returns <see langword="null"/> when the source tree is unavailable (e.g. CI runs the compiled
    /// binaries on a machine that does not have the <c>.cs</c> sources on disk), so the caller can
    /// degrade gracefully instead of erroring.
    /// </summary>
    private static string? TryGetTestProjectRoot([CallerFilePath] string thisFilePath = "")
    {
        // thisFilePath = .../HelixToolkit.Nex.glTF.Tests/Properties/DuplicateStubInventoryExplorationTests.cs
        if (string.IsNullOrEmpty(thisFilePath) || !File.Exists(thisFilePath))
        {
            return null;
        }
        var propertiesDir = Path.GetDirectoryName(thisFilePath);
        var projectRoot = propertiesDir is null ? null : Path.GetDirectoryName(propertiesDir);
        if (
            projectRoot is null
            || !File.Exists(Path.Combine(projectRoot, "HelixToolkit.Nex.glTF.Tests.csproj"))
        )
        {
            return null;
        }
        return projectRoot;
    }

    private readonly record struct StubFileFinding(
        string RelativePath,
        IReadOnlyList<string> DuplicateStubTypes,
        IReadOnlyList<string> Variants,
        bool ReferencesSharedMock
    );

    private static bool ReferencesSharedMock(string content) =>
        SharedMockUsingDirective.IsMatch(content);

    private static IReadOnlyList<string> DetectVariants(string content)
    {
        var variants = new List<string>();
        if (Regex.IsMatch(content, @"\bRemovedKeys\b"))
        {
            variants.Add("remove-tracking (RemovedKeys)");
        }
        if (Regex.IsMatch(content, @"new\s+SamplerRepository\(\s*_context\s*\)"))
        {
            variants.Add("MockContext-backed (new SamplerRepository(_context))");
        }
        if (Regex.IsMatch(content, @"\bstatic\s+readonly\s+Stub\w+\s+Instance\b"))
        {
            variants.Add("static Instance");
        }
        if (variants.Count == 0)
        {
            variants.Add("minimal sentinel");
        }
        return variants;
    }

    /// <summary>
    /// Enumerates every <c>.cs</c> file in the test project and collects the files that satisfy the
    /// bug condition (declare a private nested duplicate stub and do not reference the shared mock).
    /// </summary>
    private static List<StubFileFinding> CollectBugConditionFiles(string projectRoot)
    {
        var findings = new List<StubFileFinding>();

        var sourceFiles = Directory
            .EnumerateFiles(projectRoot, "*.cs", SearchOption.AllDirectories)
            .Where(p =>
            {
                var normalized = p.Replace('\\', '/');
                // Exclude build output directories.
                if (normalized.Contains("/bin/") || normalized.Contains("/obj/"))
                {
                    return false;
                }
                // Exclude the canonical shared mock definitions themselves: they legitimately
                // declare the Stub* types in the HelixToolkit.Nex.glTF.Tests.Mocks namespace and
                // are the consolidation target, not duplicates. (Their doc comments also mention
                // the former `private sealed class Stub*` declarations, which would otherwise be
                // false-positive matches.) The bug condition is scoped to Unit/ and Properties/.
                return !normalized.Contains("/Mocks/");
            })
            .OrderBy(p => p, StringComparer.Ordinal);

        foreach (var file in sourceFiles)
        {
            var content = File.ReadAllText(file);
            var matches = PrivateStubDeclaration.Matches(content);
            if (matches.Count == 0)
            {
                continue; // Not a stub-declaring file: bug condition does not apply.
            }

            var duplicateTypes = matches
                .Select(m => m.Groups[1].Value)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToList();

            var referencesShared = ReferencesSharedMock(content);

            // isBugCondition := declaresPrivateNestedStub AND NOT referencesSharedMock
            if (!referencesShared)
            {
                findings.Add(
                    new StubFileFinding(
                        Path.GetRelativePath(projectRoot, file).Replace('\\', '/'),
                        duplicateTypes,
                        DetectVariants(content),
                        referencesShared
                    )
                );
            }
        }

        return findings;
    }

    /// <summary>
    /// Property 1 (Bug Condition): for the concrete input space of every <c>.cs</c> file in the test
    /// project, NO file SHALL satisfy <c>isBugCondition</c> - i.e. no <c>private sealed class Stub*</c>
    /// duplicate definitions remain and every stub reference resolves to the shared mock set in
    /// <c>HelixToolkit.Nex.glTF.Tests.Mocks</c>.
    ///
    /// EXPECTED OUTCOME on UNFIXED code: FAIL. The failure message lists each offending file and the
    /// stub variant(s) it inlines - the documented counterexamples that prove the bug exists. After
    /// the consolidation fix this assertion PASSES, validating the fix.
    /// **Validates: Requirements 1.1, 1.2, 1.3, 2.1, 2.4**
    /// </summary>
    [TestMethod]
    public void NoTestFileDeclaresDuplicatePrivateStub()
    {
        var projectRoot = TryGetTestProjectRoot();
        if (projectRoot is null)
        {
            Assert.Inconclusive(
                "Test project sources are not available on disk (the .cs files are not copied to "
                    + "the test output), so the structural duplicate-stub inventory cannot run in this "
                    + "environment. Run this test from a source checkout to validate the consolidation."
            );
            return;
        }

        var offenders = CollectBugConditionFiles(projectRoot);

        if (offenders.Count > 0)
        {
            var sb = new StringBuilder();
            sb.AppendLine(
                $"Bug condition holds for {offenders.Count} file(s): each declares a private nested "
                    + "duplicate of a Stub* interface double instead of referencing the shared mocks in "
                    + $"'{SharedMockNamespace}'. These are the counterexamples proving the duplication bug:"
            );
            sb.AppendLine();
            foreach (var f in offenders)
            {
                sb.AppendLine(
                    $"  - {f.RelativePath}\n"
                        + $"      duplicate stubs : {string.Join(", ", f.DuplicateStubTypes)}\n"
                        + $"      variant(s)      : {string.Join("; ", f.Variants)}"
                );
            }
            sb.AppendLine();
            sb.AppendLine(
                "Notable divergences to preserve when consolidating: minimal stubs return Remove=>false "
                    + "while remove-tracking stubs (ResourceManifestTests.cs, ImportResultDisposalTests.cs) "
                    + "append to RemovedKeys and return true; SessionConsistency/ResourceManifestRegistration/"
                    + "ResourceManifestDeduplication use a static Instance creating real TextureRef/SamplerRef; "
                    + "the MockContext-backed sampler wraps a real SamplerRepository and disposes inner-then-context; "
                    + "and DisposalIsolationPropertyTests.cs additionally has a KeyTrackingTextureRepository "
                    + "(AddKey/ContainsKey/IReadOnlySet RemovedKeys) edge case."
            );

            Assert.Fail(sb.ToString());
        }
    }
}
