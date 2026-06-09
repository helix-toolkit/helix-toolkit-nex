namespace HelixToolkit.Nex.glTF.Tests.Unit;

/// <summary>
/// Unit tests for the Importer class (file loading, error cases).
/// Validates: Requirements 1.5, 1.6, 1.7, 1.8, 8.5
/// </summary>
[TestClass]
public class ImporterTests
{
    private Importer _importer = null!;
    private readonly List<string> _tempFiles = [];

    [TestInitialize]
    public void Setup()
    {
        _importer = new Importer();
    }

    [TestCleanup]
    public void Cleanup()
    {
        foreach (var file in _tempFiles)
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
    }

    private string CreateTempFile(string extension, byte[] content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"importer_test_{Guid.NewGuid()}{extension}");
        File.WriteAllBytes(path, content);
        _tempFiles.Add(path);
        return path;
    }

    private string CreateTempFile(string extension, string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"importer_test_{Guid.NewGuid()}{extension}");
        File.WriteAllText(path, content);
        _tempFiles.Add(path);
        return path;
    }

    // =========================================================================
    // Synchronous Import — Error Handling
    // =========================================================================

    [TestMethod]
    public void Import_FileNotFound_ReturnsErrorWithFilePath()
    {
        // Arrange
        var nonExistentPath = Path.Combine(Path.GetTempPath(), "does_not_exist_12345.gltf");

        // Act
        var result = _importer.Import(nonExistentPath, null!);

        // Assert
        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.HasErrors);
        Assert.IsNull(result.RootNode);
        Assert.IsTrue(
            result.Diagnostics.Any(d =>
                d.Severity == DiagnosticSeverity.Error && d.Message.Contains(nonExistentPath)
            ),
            "Diagnostics should contain the file path"
        );
    }

    [TestMethod]
    public void Import_FileNotFound_DoesNotThrowException()
    {
        // Arrange
        var nonExistentPath = @"C:\nonexistent\path\model.gltf";

        // Act — should not throw, returns error result instead
        var result = _importer.Import(nonExistentPath, null!);

        // Assert
        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.HasErrors);
    }

    [TestMethod]
    public void Import_InvalidJson_ReturnsErrorWithParseFailure()
    {
        // Arrange — write invalid JSON to a .gltf file
        var filePath = CreateTempFile(".gltf", "{ this is not valid json !!! }");

        // Act
        var result = _importer.Import(filePath, null!);

        // Assert
        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.HasErrors);
        Assert.IsNull(result.RootNode);
        Assert.IsTrue(
            result.Diagnostics.Any(d =>
                d.Severity == DiagnosticSeverity.Error
                && d.Message.Contains("parse", StringComparison.OrdinalIgnoreCase)
            ),
            "Diagnostics should mention parse failure"
        );
    }

    [TestMethod]
    public void Import_MalformedJson_TruncatedContent_ReturnsError()
    {
        // Arrange — truncated JSON that starts valid but is incomplete
        var filePath = CreateTempFile(".gltf", """{ "asset": { "version": """);

        // Act
        var result = _importer.Import(filePath, null!);

        // Assert
        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.HasErrors);
        Assert.IsNull(result.RootNode);
    }

    [TestMethod]
    public void Import_EmptyFile_ReturnsError()
    {
        // Arrange — empty .gltf file
        var filePath = CreateTempFile(".gltf", "");

        // Act
        var result = _importer.Import(filePath, null!);

        // Assert
        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.HasErrors);
    }

    [TestMethod]
    public void Import_InvalidGlbMagic_ReturnsError()
    {
        // Arrange — write random bytes to a .glb file (invalid magic number)
        var randomBytes = new byte[]
        {
            0x00,
            0x01,
            0x02,
            0x03,
            0x04,
            0x05,
            0x06,
            0x07,
            0x08,
            0x09,
            0x0A,
            0x0B,
        };
        var filePath = CreateTempFile(".glb", randomBytes);

        // Act
        var result = _importer.Import(filePath, null!);

        // Assert
        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.HasErrors);
        Assert.IsNull(result.RootNode);
    }

    [TestMethod]
    public void Import_TruncatedGlb_ReturnsError()
    {
        // Arrange — GLB with valid magic but truncated (only 4 bytes)
        var truncatedGlb = new byte[] { 0x67, 0x6C, 0x54, 0x46 }; // "glTF" magic only
        var filePath = CreateTempFile(".glb", truncatedGlb);

        // Act
        var result = _importer.Import(filePath, null!);

        // Assert
        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.HasErrors);
    }

    [TestMethod]
    public void Import_MissingBinFile_ReturnsErrorWithRelativePath()
    {
        // Arrange — write valid .gltf JSON that references a non-existent .bin file
        var gltfJson = """
            {
                "asset": { "version": "2.0" },
                "buffers": [
                    {
                        "uri": "missing_data.bin",
                        "byteLength": 1024
                    }
                ],
                "scenes": [{}],
                "scene": 0
            }
            """;
        var filePath = CreateTempFile(".gltf", gltfJson);

        // Act
        var result = _importer.Import(filePath, null!);

        // Assert
        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.HasErrors);
        Assert.IsNull(result.RootNode);
        Assert.IsTrue(
            result.Diagnostics.Any(d =>
                d.Severity == DiagnosticSeverity.Error
                && d.Message.Contains("missing_data.bin", StringComparison.OrdinalIgnoreCase)
            ),
            "Diagnostics should mention the missing .bin file path"
        );
    }

    [TestMethod]
    public void Import_MissingBinFile_DiagnosticHasBufferElementType()
    {
        // Arrange
        var gltfJson = """
            {
                "asset": { "version": "2.0" },
                "buffers": [
                    {
                        "uri": "nonexistent.bin",
                        "byteLength": 512
                    }
                ],
                "scenes": [{}],
                "scene": 0
            }
            """;
        var filePath = CreateTempFile(".gltf", gltfJson);

        // Act
        var result = _importer.Import(filePath, null!);

        // Assert
        Assert.IsTrue(
            result.Diagnostics.Any(d =>
                d.Severity == DiagnosticSeverity.Error
                && d.ElementType == "Buffer"
                && d.ElementIndex == 0
            ),
            "Diagnostics should reference Buffer element type with index 0"
        );
    }

    [TestMethod]
    public void Import_InvalidBase64Buffer_ReturnsErrorWithBufferIndex()
    {
        // Arrange — write .gltf with an invalid base64 data URI
        var gltfJson = """
            {
                "asset": { "version": "2.0" },
                "buffers": [
                    {
                        "uri": "data:application/octet-stream;base64,!!!invalid-base64!!!",
                        "byteLength": 16
                    }
                ],
                "scenes": [{}],
                "scene": 0
            }
            """;
        var filePath = CreateTempFile(".gltf", gltfJson);

        // Act
        var result = _importer.Import(filePath, null!);

        // Assert
        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.HasErrors);
        Assert.IsNull(result.RootNode);
        Assert.IsTrue(
            result.Diagnostics.Any(d =>
                d.Severity == DiagnosticSeverity.Error
                && d.ElementType == "Buffer"
                && d.ElementIndex == 0
            ),
            "Diagnostics should reference buffer index 0"
        );
    }

    [TestMethod]
    public void Import_InvalidBase64Buffer_MissingBase64Marker_ReturnsError()
    {
        // Arrange — data URI without the ";base64," marker
        var gltfJson = """
            {
                "asset": { "version": "2.0" },
                "buffers": [
                    {
                        "uri": "data:application/octet-stream,rawdata",
                        "byteLength": 7
                    }
                ],
                "scenes": [{}],
                "scene": 0
            }
            """;
        var filePath = CreateTempFile(".gltf", gltfJson);

        // Act
        var result = _importer.Import(filePath, null!);

        // Assert
        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.HasErrors);
        Assert.IsTrue(
            result.Diagnostics.Any(d =>
                d.Severity == DiagnosticSeverity.Error
                && d.ElementType == "Buffer"
                && d.ElementIndex == 0
                && d.Message.Contains("base64", StringComparison.OrdinalIgnoreCase)
            ),
            "Diagnostics should mention missing base64 marker"
        );
    }

    [TestMethod]
    public void Import_ErrorResult_DiagnosticsAreNeverNull()
    {
        // Arrange
        var nonExistentPath = Path.Combine(Path.GetTempPath(), "no_such_file.gltf");

        // Act
        var result = _importer.Import(nonExistentPath, null!);

        // Assert — Diagnostics should never be null, even on error
        Assert.IsNotNull(result.Diagnostics);
        Assert.IsTrue(result.Diagnostics.Count > 0);
    }

    [TestMethod]
    public void Import_ErrorResult_DiagnosticEntriesHaveValidFields()
    {
        // Arrange
        var filePath = CreateTempFile(".gltf", "not json at all");

        // Act
        var result = _importer.Import(filePath, null!);

        // Assert — each diagnostic should have non-empty message and valid element type
        foreach (var diagnostic in result.Diagnostics)
        {
            Assert.IsFalse(
                string.IsNullOrWhiteSpace(diagnostic.Message),
                "Diagnostic message should not be empty"
            );
            Assert.IsFalse(
                string.IsNullOrWhiteSpace(diagnostic.ElementType),
                "Diagnostic ElementType should not be empty"
            );
        }
    }

    // =========================================================================
    // Asynchronous Import — Error Handling
    // =========================================================================

    [TestMethod]
    public async Task ImportAsync_CancelledToken_ThrowsOperationCanceledException()
    {
        // Arrange — use a pre-cancelled token
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsExceptionAsync<OperationCanceledException>(() =>
            _importer.ImportAsync("any_path.gltf", null!, cancellationToken: cts.Token)
        );
    }

    [TestMethod]
    public async Task ImportAsync_FileNotFound_ReturnsErrorWithFilePath()
    {
        // Arrange
        var nonExistentPath = Path.Combine(Path.GetTempPath(), "async_not_found.gltf");

        // Act
        var result = await _importer.ImportAsync(nonExistentPath, null!);

        // Assert
        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.HasErrors);
        Assert.IsNull(result.RootNode);
        Assert.IsTrue(
            result.Diagnostics.Any(d =>
                d.Severity == DiagnosticSeverity.Error && d.Message.Contains(nonExistentPath)
            ),
            "Async diagnostics should contain the file path"
        );
    }

    [TestMethod]
    public async Task ImportAsync_InvalidJson_ReturnsErrorWithParseFailure()
    {
        // Arrange
        var filePath = CreateTempFile(".gltf", "{ broken json content %%% }");

        // Act
        var result = await _importer.ImportAsync(filePath, null!);

        // Assert
        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.HasErrors);
        Assert.IsTrue(
            result.Diagnostics.Any(d =>
                d.Severity == DiagnosticSeverity.Error
                && d.Message.Contains("parse", StringComparison.OrdinalIgnoreCase)
            ),
            "Async diagnostics should mention parse failure"
        );
    }

    [TestMethod]
    public async Task ImportAsync_InvalidGlbMagic_ReturnsError()
    {
        // Arrange
        var randomBytes = new byte[]
        {
            0xFF,
            0xFE,
            0xFD,
            0xFC,
            0x00,
            0x00,
            0x00,
            0x00,
            0x00,
            0x00,
            0x00,
            0x00,
        };
        var filePath = CreateTempFile(".glb", randomBytes);

        // Act
        var result = await _importer.ImportAsync(filePath, null!);

        // Assert
        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.HasErrors);
        Assert.IsNull(result.RootNode);
    }

    [TestMethod]
    public async Task ImportAsync_MissingBinFile_ReturnsErrorWithRelativePath()
    {
        // Arrange
        var gltfJson = """
            {
                "asset": { "version": "2.0" },
                "buffers": [
                    {
                        "uri": "async_missing.bin",
                        "byteLength": 256
                    }
                ],
                "scenes": [{}],
                "scene": 0
            }
            """;
        var filePath = CreateTempFile(".gltf", gltfJson);

        // Act
        var result = await _importer.ImportAsync(filePath, null!);

        // Assert
        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.HasErrors);
        Assert.IsTrue(
            result.Diagnostics.Any(d =>
                d.Severity == DiagnosticSeverity.Error
                && d.Message.Contains("async_missing.bin", StringComparison.OrdinalIgnoreCase)
            ),
            "Async diagnostics should mention the missing .bin file path"
        );
    }

    [TestMethod]
    public async Task ImportAsync_InvalidBase64Buffer_ReturnsErrorWithBufferIndex()
    {
        // Arrange
        var gltfJson = """
            {
                "asset": { "version": "2.0" },
                "buffers": [
                    {
                        "uri": "data:application/octet-stream;base64,@@@not-valid@@@",
                        "byteLength": 16
                    }
                ],
                "scenes": [{}],
                "scene": 0
            }
            """;
        var filePath = CreateTempFile(".gltf", gltfJson);

        // Act
        var result = await _importer.ImportAsync(filePath, null!);

        // Assert
        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.HasErrors);
        Assert.IsTrue(
            result.Diagnostics.Any(d =>
                d.Severity == DiagnosticSeverity.Error
                && d.ElementType == "Buffer"
                && d.ElementIndex == 0
            ),
            "Async diagnostics should reference buffer index 0"
        );
    }

    [TestMethod]
    public async Task ImportAsync_FileNotFound_DoesNotThrowException()
    {
        // Arrange
        var nonExistentPath = @"C:\totally\fake\path\model.gltf";

        // Act — should not throw, returns error result instead
        var result = await _importer.ImportAsync(nonExistentPath, null!);

        // Assert
        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.HasErrors);
    }
}
