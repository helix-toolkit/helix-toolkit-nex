# Implementation Plan: Limits-Shader-Sync

## Overview

This plan implements automatic synchronization of bit-packing constants between `Limits.cs`, GLSL shaders, and C# unpacking code. A new `LimitsShaderConstants` class derives all masks, bit widths, and shift amounts from `Limits` constants. The `ShaderBuilder` gains a placeholder replacement pass, `HeaderPackEntity.glsl` switches to named placeholders, and `Utils.UnpackMeshInfo` references derived constants instead of hardcoded values.

## Tasks

- [x] 1. Create LimitsShaderConstants class with derived bit-packing constants
  - [x] 1.1 Create `LimitsShaderConstants.cs` in `Source/HelixToolkit-Nex/HelixTookit.Nex/`
    - Define static class `LimitsShaderConstants` in namespace `HelixToolkit.Nex`
    - Compute bit widths from `Limits` constants using `System.Numerics.BitOperations.Log2` (e.g., `WorldIdBits = BitOperations.Log2(Limits.MaxWorldId + 1)`)
    - Compute derived values: `InstanceLowBits = 32 - WorldIdBits - EntityIdBits`, `InstanceHighBits = InstanceCountBits - InstanceLowBits`
    - Compute shift amounts: `EntityIdShift = WorldIdBits`, `InstanceLowShift = WorldIdBits + EntityIdBits`, `PrimitiveIdShift = InstanceHighBits`
    - Compute masks: `WorldIdMask = Limits.MaxWorldId`, `EntityIdMask = Limits.MaxEntityId`, `InstanceLowMask = (1u << InstanceLowBits) - 1`, `InstanceHighMask = (1u << InstanceHighBits) - 1`, `InstanceCountMask = Limits.MaxInstanceCount`, `IndexCountMask = Limits.MaxIndexCount`
    - Add static constructor validation: each Limits constant must be of the form `(1 << n) - 1`, and channel constraints must hold (`WorldIdBits + EntityIdBits + InstanceLowBits == 32`, `InstanceHighBits + IndexCountBits == 32`)
    - Implement `GetGlslPlaceholders()` returning `IReadOnlyDictionary<string, string>` mapping placeholder tokens (e.g., `LIMITS_WORLD_ID_MASK`) to GLSL-formatted values (e.g., `0xFu`)
    - _Requirements: 1.1, 1.2, 1.3_

  - [ ]* 1.2 Write property test: Bit width derivation correctness (Property 1)
    - **Property 1: Bit width derivation correctness**
    - For any unsigned integer max value of the form `(1 << n) - 1` where `1 <= n <= 31`, `BitOperations.Log2(maxValue + 1)` returns exactly `n`
    - Add FsCheck 3.3.2 package reference to `HelixToolkit.Nex.Shaders.Tests` project
    - Create `BitWidthDerivationPropertyTests.cs` in `HelixToolkit.Nex.Shaders.Tests`
    - **Validates: Requirements 1.1**

  - [ ]* 1.3 Write property test: Bit layout channel invariants (Property 2)
    - **Property 2: Bit layout channel invariants**
    - Verify `WorldIdBits + EntityIdBits + InstanceLowBits == 32`, `InstanceLowBits + InstanceHighBits == InstanceCountBits`, and `InstanceHighBits + IndexCountBits == 32`
    - Create `BitLayoutInvariantsPropertyTests.cs` in `HelixToolkit.Nex.Shaders.Tests`
    - **Validates: Requirements 1.3**

- [x] 2. Add placeholder replacement to ShaderBuilder
  - [x] 2.1 Implement `ReplaceLimitsPlaceholders` method in `ShaderBuilder`
    - Add a new private method `ReplaceLimitsPlaceholders(string source)` that reads the placeholder dictionary from `LimitsShaderConstants.GetGlslPlaceholders()` and performs string replacement for each token
    - Add `WarnUnrecognizedPlaceholders(string source)` method that scans for remaining `LIMITS_[A-Z_]+` tokens via regex and adds warnings to the `_warnings` list
    - Integrate the replacement call in `ProcessShader` after version directive extraction but before `ProcessIncludes`
    - Also apply replacement inside `ProcessIncludes` on loaded include content so placeholders in header files are replaced
    - Add a project reference from `HelixToolkit.Nex.Shaders` to `HelixTookit.Nex` if not already present
    - _Requirements: 2.1, 2.2, 2.3, 2.4_

  - [ ]* 2.2 Write property test: Placeholder replacement completeness (Property 3)
    - **Property 3: Placeholder replacement completeness**
    - For any GLSL source string containing known `LIMITS_` placeholder tokens at arbitrary positions, after replacement, zero known placeholders remain and corresponding numeric values are present
    - Create `PlaceholderReplacementPropertyTests.cs` in `HelixToolkit.Nex.Shaders.Tests`
    - **Validates: Requirements 2.1**

  - [ ]* 2.3 Write property test: Unrecognized placeholder warning (Property 4)
    - **Property 4: Unrecognized placeholder warning**
    - For any GLSL source containing a token matching `LIMITS_[A-Z_]+` that is NOT a recognized placeholder, the ShaderBuilder produces a warning identifying that token
    - Create `UnrecognizedPlaceholderPropertyTests.cs` in `HelixToolkit.Nex.Shaders.Tests`
    - **Validates: Requirements 2.3**

  - [ ]* 2.4 Write property test: Backward compatibility (Property 5)
    - **Property 5: Backward compatibility — no placeholders means identical output**
    - For any GLSL source that does not contain `LIMITS_` tokens, ShaderBuilder output with placeholder replacement is identical to output without it
    - Create `BackwardCompatibilityPropertyTests.cs` in `HelixToolkit.Nex.Shaders.Tests`
    - **Validates: Requirements 2.4, 6.1**

- [x] 3. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 4. Update HeaderPackEntity.glsl to use placeholders
  - [x] 4.1 Replace hardcoded values in `HeaderPackEntity.glsl` with placeholder tokens
    - Replace `0xFu` with `LIMITS_WORLD_ID_MASK`
    - Replace `0xFFFFu` with `LIMITS_ENTITY_ID_MASK`
    - Replace `0xFFFu` with `LIMITS_INSTANCE_LOW_MASK`
    - Replace `0x3FFu` with `LIMITS_INSTANCE_HIGH_MASK`
    - Replace `0x3FFFFFu` with `LIMITS_INDEX_COUNT_MASK`
    - Replace shift literal `4u` with `LIMITS_ENTITY_ID_SHIFT`
    - Replace shift literal `20u` with `LIMITS_INSTANCE_LOW_SHIFT`
    - Replace shift literal `12u` with `LIMITS_INSTANCE_LOW_BITS`
    - Replace shift literal `10u` with `LIMITS_INSTANCE_HIGH_SHIFT`
    - _Requirements: 3.1, 3.2, 3.3_

  - [ ]* 4.2 Write unit tests for HeaderPackEntity placeholder replacement
    - Verify the `.glsl` file contains `LIMITS_` tokens and no hardcoded hex mask literals
    - Verify that after ShaderBuilder processes the file, the output contains the expected numeric values (functionally equivalent to the original)
    - _Requirements: 3.1, 3.2, 3.3_

- [x] 5. Update UnpackMeshInfo to use LimitsShaderConstants
  - [x] 5.1 Refactor `Utils.UnpackMeshInfo` to reference `LimitsShaderConstants`
    - Replace `0xF` with `LimitsShaderConstants.WorldIdMask`
    - Replace `(r >> 4)` shift with `LimitsShaderConstants.EntityIdShift`
    - Replace `Limits.MaxEntityId` mask with `LimitsShaderConstants.EntityIdMask`
    - Replace `(r >> 20)` shift with `LimitsShaderConstants.InstanceLowShift`
    - Replace `0x3FF` with `LimitsShaderConstants.InstanceHighMask`
    - Replace `<< 12` with `LimitsShaderConstants.InstanceLowBits`
    - Replace `>> 10` shift with `LimitsShaderConstants.PrimitiveIdShift`
    - _Requirements: 4.1, 4.2, 4.3, 4.4_

  - [ ]* 5.2 Write property test: Pack-then-unpack round trip (Property 6)
    - **Property 6: Pack-then-unpack round trip**
    - For any valid combination of `worldId` (0 to `MaxWorldId`), `entityId` (0 to `MaxEntityId`), `instanceIndex` (0 to `MaxInstanceCount`), and `primitiveId` (0 to `MaxIndexCount`), implement a C# equivalent of the GLSL pack logic, then unpack via `UnpackMeshInfo`, and verify all four original values are recovered
    - Create `PackUnpackRoundTripPropertyTests.cs` in `HelixToolkit.Nex.Shaders.Tests`
    - Add project reference to `HelixToolkit.Nex.Engine` from the test project
    - **Validates: Requirements 4.4, 5.1**

- [x] 6. Final checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document
- Unit tests validate specific examples and edge cases
- The foundation project folder has a typo: `HelixTookit.Nex` (missing 'l') — file paths must use this exact spelling
- FsCheck 3.3.2 with MSTest is the established PBT pattern in this project (see `LuidComparisonPropertyTests` for reference)
- All new test files go in `HelixToolkit.Nex.Shaders.Tests` using MSTest + FsCheck fluent API with `Config.Default.WithMaxTest(100)`
