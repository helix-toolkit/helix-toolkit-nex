namespace HelixToolkit.Nex.glTF.Tests.Unit;

/// <summary>
/// Unit tests for the ResourceManifest.SessionId property.
/// Validates: Requirements 7.1, 7.2, 7.3, 7.4, 7.5
/// </summary>
[TestClass]
public class ResourceManifestSessionTests
{
    // =========================================================================
    // Empty sentinel SessionId — Validates: Requirement 7.2
    // =========================================================================

    [TestMethod]
    public void EmptySentinel_SessionId_ReturnsEmptyString()
    {
        var empty = ResourceManifest.Empty;

        Assert.AreEqual(string.Empty, empty.SessionId);
    }

    // =========================================================================
    // Constructor with valid sessionId — Validates: Requirements 7.1, 7.5
    // =========================================================================

    [TestMethod]
    public void Constructor_WithValidSessionId_StoresValueInSessionIdProperty()
    {
        var sessionId = Guid.NewGuid().ToString("D");

        var manifest = new ResourceManifest(sessionId);

        Assert.AreEqual(sessionId, manifest.SessionId);
    }

    // =========================================================================
    // Constructor with null — Validates: Requirement 7.1
    // =========================================================================

    [TestMethod]
    public void Constructor_WithNullSessionId_ThrowsArgumentNullException()
    {
        Assert.ThrowsException<ArgumentNullException>(() => new ResourceManifest(null!));
    }

    // =========================================================================
    // Immutability — Validates: Requirement 7.1 (constant for lifetime)
    // =========================================================================

    [TestMethod]
    public void SessionId_IsImmutable_ReturnsSameValueAcrossMultipleReads()
    {
        var manifest = new ResourceManifest();

        var first = manifest.SessionId;
        var second = manifest.SessionId;
        var third = manifest.SessionId;

        Assert.AreEqual(first, second);
        Assert.AreEqual(second, third);
    }

    // =========================================================================
    // Uniqueness across parameterless constructor — Validates: Requirement 7.4
    // =========================================================================

    [TestMethod]
    public void ParameterlessConstructor_TwoInstances_HaveDistinctSessionIds()
    {
        var manifestA = new ResourceManifest();
        var manifestB = new ResourceManifest();

        Assert.AreNotEqual(manifestA.SessionId, manifestB.SessionId);
    }
}
