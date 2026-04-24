# Requirements Document

## Introduction

This feature establishes `Limits.cs` as the single source of truth for bit-packing constants used across the engine. During shader building, the `ShaderBuilder` will automatically replace named placeholders in GLSL source files with values derived from `Limits.cs`. On the C# side, `Utils.UnpackMeshInfo` will reference `Limits` constants directly instead of hardcoded magic numbers. This ensures that changing a limit value in one place propagates consistently to both GLSL shaders and C# unpacking logic.

## Glossary

- **Limits**: The static class `HelixToolkit.Nex.Limits` that defines maximum value constants (e.g., `MaxEntityId`, `MaxWorldId`, `MaxInstanceCount`, `MaxIndexCount`).
- **ShaderBuilder**: The internal class `HelixToolkit.Nex.Shaders.ShaderBuilder` responsible for processing GLSL shader source code, resolving `#include` directives, injecting defines, and producing final shader text.
- **HeaderPackEntity**: The GLSL header file `HeaderPackEntity.glsl` containing `packObjectInfo` and `packPrimitiveId` functions that pack world ID, entity ID, instance index, and primitive ID into a `uvec2`.
- **UnpackMeshInfo**: The C# method `Utils.UnpackMeshInfo` that unpacks the same packed `uvec2` data back into individual IDs on the CPU side.
- **Placeholder**: A named token in GLSL source (e.g., `LIMITS_MAX_WORLD_ID`) that the ShaderBuilder replaces with a concrete value derived from `Limits` constants at build time.
- **Bit_Layout**: The packing scheme for entity information into two 32-bit unsigned integers: X channel holds worldId (low 4 bits), entityId (bits 4–19), and instanceIndex low (bits 20–31); Y channel holds instanceIndex high (bits 0–9) and primitiveId (bits 10–31).
- **Derived_Constant**: A value computed from `Limits` constants, such as bit widths (e.g., number of bits for `MaxWorldId` = 4) and bit shift amounts, rather than stored directly in `Limits.cs`.

## Requirements

### Requirement 1: Derive Bit-Packing Constants from Limits

**User Story:** As a developer, I want bit masks, bit widths, and shift amounts to be computed from `Limits` constants, so that changing a max value automatically updates all packing and unpacking logic.

#### Acceptance Criteria

1. THE Limits class SHALL provide or enable derivation of the bit width for each limit constant (WorldId = 4 bits, EntityId = 16 bits, InstanceCount = 22 bits, IndexCount = 22 bits).
2. WHEN a Limits constant value changes, THE Derived_Constants (bit masks, bit widths, and shift amounts) SHALL reflect the new value without requiring manual edits to any other file.
3. THE Derived_Constants SHALL be consistent with the Bit_Layout: worldId occupies the lowest bits of X, entityId is shifted left by the worldId bit width, instanceIndex low is shifted left by the sum of worldId and entityId bit widths, instanceIndex high occupies the lowest bits of Y, and primitiveId is shifted left by the instanceIndex high bit width.

### Requirement 2: GLSL Placeholder Replacement in ShaderBuilder

**User Story:** As a developer, I want the ShaderBuilder to replace named placeholders in GLSL source with values from `Limits`, so that shader code stays in sync with C# constants automatically.

#### Acceptance Criteria

1. WHEN the ShaderBuilder processes GLSL source containing a Placeholder token, THE ShaderBuilder SHALL replace that token with the corresponding value derived from the Limits class.
2. THE ShaderBuilder SHALL support Placeholder tokens for at least the following values: max world ID mask, max entity ID mask, max instance count mask, max index count mask, world ID bit width, entity ID bit width, instance index low bit width, instance index high bit width, entity ID shift amount, instance index low shift amount, and instance index high shift amount.
3. IF a GLSL source file contains an unrecognized Placeholder token that follows the Limits placeholder naming pattern, THEN THE ShaderBuilder SHALL log a warning identifying the unrecognized token.
4. WHEN no Placeholder tokens are present in the GLSL source, THE ShaderBuilder SHALL produce output identical to its current behavior.

### Requirement 3: Update HeaderPackEntity.glsl to Use Placeholders

**User Story:** As a developer, I want `HeaderPackEntity.glsl` to use named placeholders instead of hardcoded hex literals, so that the shader packing logic is driven by `Limits.cs`.

#### Acceptance Criteria

1. THE HeaderPackEntity file SHALL use Placeholder tokens in place of all hardcoded bit mask literals (0xFu, 0xFFFFu, 0xFFFu, 0x3FFu, 0x3FFFFFu).
2. THE HeaderPackEntity file SHALL use Placeholder tokens in place of all hardcoded bit shift literals (4u, 20u, 12u, 10u).
3. WHEN the ShaderBuilder processes HeaderPackEntity after Placeholder replacement, THE resulting GLSL source SHALL be functionally equivalent to the current hardcoded version given the current Limits values.

### Requirement 4: Update UnpackMeshInfo to Use Limits Constants

**User Story:** As a developer, I want `Utils.UnpackMeshInfo` to reference `Limits` constants and derived values instead of hardcoded magic numbers, so that C# unpacking stays in sync with `Limits.cs`.

#### Acceptance Criteria

1. THE UnpackMeshInfo method SHALL use Limits constants or Derived_Constants for all bit mask values currently hardcoded as 0xF, 0x3FF.
2. THE UnpackMeshInfo method SHALL use Limits constants or Derived_Constants for all bit shift values currently hardcoded as 4, 20, 12, 10.
3. WHEN Limits constant values change, THE UnpackMeshInfo method SHALL produce correct unpacking results consistent with the updated Bit_Layout without requiring manual code changes to UnpackMeshInfo.
4. THE UnpackMeshInfo method SHALL produce identical output to the current implementation given the current Limits values.

### Requirement 5: Round-Trip Consistency Between Packing and Unpacking

**User Story:** As a developer, I want the GLSL packing and C# unpacking to remain perfectly inverse operations, so that data survives a pack-then-unpack cycle without loss.

#### Acceptance Criteria

1. FOR ALL valid combinations of worldId (0 to MaxWorldId), entityId (0 to MaxEntityId), instanceIndex (0 to MaxInstanceCount), and primitiveId (0 to MaxIndexCount), packing via the HeaderPackEntity logic then unpacking via UnpackMeshInfo SHALL return the original values.
2. WHEN any Limits constant value is changed, THE round-trip property (pack then unpack returns original values) SHALL continue to hold for all valid input combinations under the new limits.

### Requirement 6: Backward Compatibility of ShaderBuilder

**User Story:** As a developer, I want existing shaders that do not use placeholders to continue building without changes, so that the new feature does not break any current shader code.

#### Acceptance Criteria

1. WHEN the ShaderBuilder processes a GLSL source file that contains no Placeholder tokens, THE ShaderBuilder SHALL produce output identical to the output produced before this feature was added.
2. THE ShaderBuilder SHALL continue to support all existing functionality including `#include` resolution, custom defines, version directive handling, and comment stripping without modification to their behavior.
