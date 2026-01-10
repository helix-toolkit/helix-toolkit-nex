# HelixToolkit.Nex.Shaders.Tests

Unit tests for the HelixToolkit.Nex.Shaders project, including shader building system and complete compile pipeline tests.

## Test Files

### ShaderBuildingTests.cs
Unit tests for shader preprocessing and building (no GPU required).
- **18 test methods** covering preprocessing, caching, and builder API
- Can run without GPU context

### ShaderCompilePipelineTests.cs
Integration tests for the complete shader compile pipeline (requires GPU).
- **25+ test methods** covering end-to-end shader compilation
- Tests preprocessing → SPIR-V compilation workflow
- Requires Vulkan GPU context

## Running Tests

### Visual Studio
1. Open Test Explorer (Test → Test Explorer)
2. Click "Run All" to run all tests
3. Use the filter box to run specific categories

### Command Line
```bash
# Run all tests
dotnet test

# Run only unit tests (no GPU required)
dotnet test --filter "TestCategory!=GPURequired"

# Run only integration tests (GPU required)
dotnet test --filter "TestCategory=GPURequired"

# Run with detailed output
dotnet test --logger "console;verbosity=detailed"

# Run specific category
dotnet test --filter "TestCategory=PBR"
dotnet test --filter "TestCategory=Caching"
dotnet test --filter "TestCategory=FluentAPI"
dotnet test --filter "TestCategory=Integration"
```

## Test Structure

### ShaderBuildingTests.cs (Unit Tests)
Comprehensive unit tests for the shader building system with 18 test methods.

#### Test Categories

**ShaderBuilding** - Core building functionality
- `TestBasicCompilation` - Verify basic shader compilation works
- `TestMultipleStages` - Test all shader stages (vertex, fragment, compute, etc.)
- `TestAllShaderStages` - Comprehensive test of all supported stages
- `TestEmptyShaderSource` - Handle empty shader input

**PBR** - PBR-specific tests
- `TestPBRInclusion` - Verify PBR functions are included correctly
- `TestFluentBuilderWithPBR` - Test fluent builder with PBR
- `TestPBRMaterialStructure` - Verify PBR material struct works

**Preprocessor** - Preprocessing features
- `TestDefines` - Test custom define injection
- `TestDefineWithValue` - Test defines with values
- `TestCommentStripping` - Verify comment removal
- `TestVersionDirectiveHandling` - Test version directive processing
- `TestDefaultVersionDirective` - Test default version behavior

**Caching** - Cache functionality
- `TestCaching` - Basic cache hit/miss behavior
- `TestCacheEviction` - LRU eviction strategy
- `TestCacheStatistics` - Cache statistics tracking

**FluentAPI** - Fluent builder tests
- `TestFluentBuilder` - Basic fluent builder API
- `TestFluentBuilderWithPBR` - Fluent builder with PBR functions

**Factory** - Factory methods
- `TestFactoryMethods` - Test factory method creation

**Headers** - Header access
- `TestGetHeaderDirectly` - Direct header file access

### ShaderCompilePipelineTests.cs (Integration Tests)
Complete pipeline tests requiring GPU context with 25+ test methods.

#### Test Categories

**Integration** - Full pipeline tests (all tests in this file)
**GPURequired** - Tests requiring Vulkan GPU context (all tests in this file)

#### Test Sections

**Basic Pipeline Tests**
- `TestBasicShaderCompilePipeline` - Basic shader compilation end-to-end
- `TestPBRShaderCompilePipeline` - PBR shader with full pipeline
- `TestVertexShaderCompilePipeline` - Vertex shader compilation

**Fluent Builder Pipeline Tests**
- `TestFluentBuilderPipeline` - Fluent API with SPIR-V compilation
- `TestFluentBuilderWithPBRPipeline` - Fluent API + PBR + SPIR-V

**Multiple Shader Stages Pipeline**
- `TestMultipleShaderStagesPipeline` - Compile vertex + fragment together
- `TestComputeShaderPipeline` - Compute shader compilation

**Advanced Features Pipeline Tests**
- `TestShaderWithMultipleDefinesPipeline` - Multiple preprocessor defines
- `TestShaderWithPushConstantsPipeline` - Push constants support
- `TestShaderWithTexturesPipeline` - Bindless texture support

**Error Handling Pipeline Tests**
- `TestInvalidShaderSyntaxPipeline` - Handle GLSL syntax errors
- `TestMissingPBRInclusionPipeline` - Detect missing PBR inclusion
- `TestEmptyShaderPipeline` - Handle empty shader input

**Performance and Stress Tests**
- `TestBatchShaderCompilationPipeline` - Compile multiple shaders
- `TestLargeShaderPipeline` - Large shader with many functions

**Real-World Scenario Tests**
- `TestShaderToyStyleShaderPipeline` - ShaderToy-style shader
- `TestPBRLightingShaderPipeline` - Full PBR lighting shader

**Comparison Tests**
- `TestComparisonTwoStepVsOneStep` - Compare traditional vs integrated approach

## Test Pattern

Each test follows the **Arrange-Act-Assert** pattern:

```csharp
[TestMethod]
[TestCategory("CategoryName")]
public void TestMethodName()
{
    // Arrange - Set up test data
    string shader = "...";
    var options = new ShaderBuildOptions { ... };

    // Act - Execute the code under test
    var result = _compiler.Compile(ShaderStage.Fragment, shader, options);

    // Assert - Verify the results
    Assert.IsTrue(result.Success, "Should compile successfully");
    Assert.IsNotNull(result.Source);
}
```

## Test Initialization

### Unit Tests (ShaderBuildingTests)
```csharp
[TestInitialize]
public void Initialize()
{
    _compiler = new ShaderCompiler();
}

[TestCleanup]
public void Cleanup()
{
    _compiler?.ClearCache();
    _compiler = null;
}
```

### Integration Tests (ShaderCompilePipelineTests)
```csharp
[ClassInitialize]
public static void ClassInit(TestContext context)
{
    // Create headless Vulkan context once for all tests
    _context = VulkanBuilder.CreateHeadless(new VulkanContextConfig());
}

[ClassCleanup]
public static void ClassCleanup()
{
    _context?.Dispose();
}
```

## Running Specific Tests

### By Category
```bash
# PBR-related tests
dotnet test --filter "TestCategory=PBR"

# Caching tests
dotnet test --filter "TestCategory=Caching"

# Preprocessor tests
dotnet test --filter "TestCategory=Preprocessor"

# Integration tests (GPU required)
dotnet test --filter "TestCategory=Integration"

# All tests except GPU tests
dotnet test --filter "TestCategory!=GPURequired"
```

### By Name
```bash
# Run single test
dotnet test --filter "FullyQualifiedName~TestBasicCompilation"

# Run tests matching pattern
dotnet test --filter "FullyQualifiedName~Pipeline"

# Run all pipeline tests
dotnet test --filter "FullyQualifiedName~ShaderCompilePipeline"
```

### Multiple Filters
```bash
# PBR or Caching tests
dotnet test --filter "(TestCategory=PBR)|(TestCategory=Caching)"

# All tests except GPU and Integration
dotnet test --filter "(TestCategory!=GPURequired)&(TestCategory!=Integration)"
```

## Test Coverage

### ShaderBuildingTests (Unit Tests)
Target coverage for core components:
- ShaderBuilder: 95%+
- ShaderCache: 95%+
- ShaderCompiler: 90%+
- ShaderBuildOptions: 100%

### ShaderCompilePipelineTests (Integration Tests)
Coverage areas:
- Extension methods: 100%
- One-step compilation: 100%
- Fluent builder with context: 100%
- Error handling: 90%+
- Real-world scenarios: Representative samples

## Test Statistics

- **Total Test Methods**: 43+ (18 unit + 25+ integration)
- **Test Categories**: 9 (ShaderBuilding, PBR, Preprocessor, Caching, FluentAPI, ErrorHandling, Factory, Headers, Integration)
- **GPU Required Tests**: 25+ (all in ShaderCompilePipelineTests)
- **No GPU Required Tests**: 18 (all in ShaderBuildingTests)

## Code Coverage

To generate code coverage reports:

```bash
# Install coverage tool
dotnet tool install --global dotnet-coverage

# Run tests with coverage
dotnet test --collect:"XPlat Code Coverage"

# Generate report
dotnet coverage report coverage.cobertura.xml
```

## Continuous Integration

These tests are designed to run in CI/CD pipelines:

```yaml
# Example GitHub Actions
- name: Run Unit Tests (No GPU)
  run: dotnet test --filter "TestCategory!=GPURequired" --logger "trx;LogFileName=unit-test-results.trx"

- name: Run Integration Tests (With GPU)
  run: dotnet test --filter "TestCategory=GPURequired" --logger "trx;LogFileName=integration-test-results.trx"
  
- name: Publish Test Results
  uses: dorny/test-reporter@v1
  if: always()
  with:
    name: Test Results
    path: '**/*-test-results.trx'
    reporter: dotnet-trx
```

## GPU Requirements

Integration tests in `ShaderCompilePipelineTests.cs` require:
- **Vulkan-capable GPU**
- **Vulkan SDK** installed
- **Proper GPU drivers**

If GPU is not available:
- Integration tests will be skipped
- Unit tests will still run successfully

## Test Data

Tests use inline shader code strings for simplicity. Key test shaders:
- **Basic shaders** - Minimal functionality
- **PBR shaders** - Full PBR material system
- **ShaderToy shaders** - Real-world shader toy examples
- **Complex shaders** - Large shaders with many functions

## Debugging Tests

### Visual Studio
1. Set breakpoint in test method
2. Right-click test in Test Explorer
3. Select "Debug Selected Tests"

### Command Line
```bash
# Run with debugger attached (VS Code)
dotnet test --logger "console;verbosity=detailed" --blame
```

## Adding New Tests

When adding new tests:

1. **Unit Tests** (ShaderBuildingTests.cs)
   - For testing preprocessing logic
   - No GPU context needed
   - Fast execution

2. **Integration Tests** (ShaderCompilePipelineTests.cs)
   - For testing complete pipeline
   - Requires GPU context
   - Tests SPIR-V compilation

Follow the existing naming convention: `Test<Feature><Aspect>Pipeline` for integration tests.

Example:
```csharp
[TestMethod]
[TestCategory("GPURequired")]
[TestCategory("Integration")]
public void TestNewFeaturePipeline()
{
    // Arrange
    string shader = "...";
    
    // Act
    var (buildResult, module) = _context!.BuildAndCompileShader(...);
    
    // Assert
    Assert.IsTrue(buildResult.Success);
    Assert.IsTrue(module.Valid);
}
```

## Test Coverage Goals

### Unit Tests
- Preprocessing: 95%+
- Builder API: 95%+
- Cache: 95%+
- Options: 100%

### Integration Tests
- Extension methods: 100%
- One-step compilation: 100%
- Error scenarios: 90%+
- Real-world cases: Representative coverage

## Troubleshooting

### Tests Fail Locally
1. Clean and rebuild: `dotnet clean && dotnet build`
2. Clear global cache: Delete temporary files
3. Check embedded resources are properly configured
4. For GPU tests: Verify Vulkan is properly installed

### Tests Pass Locally but Fail in CI
1. Check for environment-specific dependencies
2. Verify embedded resource paths
3. Ensure deterministic test behavior
4. For GPU tests: CI environment may not have GPU support

### GPU Tests Fail
1. Verify Vulkan SDK is installed
2. Check GPU drivers are up to date
3. Run Vulkan validation: `vulkaninfo`
4. Try running with validation disabled in test

## Related Documentation

- [SHADER_BUILDING_README.md](../HelixToolkit.Nex.Shaders/SHADER_BUILDING_README.md) - Complete API documentation
- [INTEGRATION_GUIDE.md](../HelixToolkit.Nex.Shaders/INTEGRATION_GUIDE.md) - Integration examples
- [CONTEXT_INTEGRATION_ANALYSIS.md](../HelixToolkit.Nex.Shaders/CONTEXT_INTEGRATION_ANALYSIS.md) - Pipeline integration analysis
- [ShaderBuildingExamples.cs](../HelixToolkit.Nex.Shaders/ShaderBuildingExamples.cs) - Usage examples

## Contributing

When contributing tests:
1. Ensure all tests pass
2. Add tests for new features
3. Update test documentation
4. Follow existing patterns and conventions
5. Use descriptive test and assertion messages
6. Categorize tests appropriately (unit vs integration, GPU vs no GPU)
