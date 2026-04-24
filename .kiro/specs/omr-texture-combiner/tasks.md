# Implementation Plan: OMR Texture Combiner

## Overview

Implement the `OmrTextureCombiner` fluent builder and its supporting types inside
`HelixToolkit.Nex.Textures`. The work is split into four groups: domain types,
the internal pixel sampler, the combiner itself, and the test file. Each group
builds on the previous one; the final task wires everything together and verifies
the full pipeline.

All production files go in
`helix-toolkit-nex/Source/HelixToolkit-Nex/HelixToolkit.Nex.Textures/`.
The test file goes in
`helix-toolkit-nex/Source/HelixToolkit-Nex/HelixToolkit.Nex.Textures.Tests/`.

---

## Tasks

- [x] 1. Create domain types: `ChannelComponent` enum and `ChannelSource` hierarchy
  - [x] 1.1 Create `ChannelComponent.cs`
    - Declare `public enum ChannelComponent : byte` with members `R = 0`, `G = 1`,
      `B = 2`, `A = 3` in namespace `HelixToolkit.Nex.Textures`
    - Add XML doc comment on the enum and each member
    - _Requirements: 1.2, 5.1_

  - [x] 1.2 Create `ChannelSource.cs`
    - Declare `public abstract class ChannelSource` with a `private ChannelSource()`
      constructor to prevent external subclassing
    - Add nested `public sealed class ImageChannel : ChannelSource` with
      `Image Source` and `ChannelComponent Channel` properties; constructor must
      call `ArgumentNullException.ThrowIfNull(source)`
    - Add nested `public sealed class Constant : ChannelSource` with a `byte Value`
      property; constructor accepts a `byte value` parameter
    - Add XML doc comments on the base class and both subtypes
    - _Requirements: 1.1, 1.2, 1.3, 6.1, 6.2, 6.3_

- [x] 2. Create `PixelSampler.cs` — internal static sampling helper
  - [x] 2.1 Implement `PixelSampler` with the `Sample` method
    - Declare `internal static class PixelSampler` in namespace
      `HelixToolkit.Nex.Textures`
    - Implement `internal static byte Sample(PixelBuffer pixelBuffer, Format format,
      ChannelComponent channel, int x, int y)`
    - Add two private `[StructLayout(LayoutKind.Sequential, Pack = 1)]` structs
      inside the file: `Rgba8Pixel { byte R, G, B, A }` and
      `Bgra8Pixel { byte B, G, R, A }` (field order matches memory layout)
    - `Format.RGBA_UN8` branch: call `pixelBuffer.GetPixel<Rgba8Pixel>(x, y)` and
      return the field matching `channel`
    - `Format.R_UN8` branch: call `pixelBuffer.GetPixel<byte>(x, y)` for channel R;
      return `0` for G and B; return `255` for A
    - `Format.BGRA_UN8` branch: call `pixelBuffer.GetPixel<Bgra8Pixel>(x, y)` and
      return the field matching `channel` (struct field names already map logical
      channels to the correct byte offsets)
    - Default branch: throw `ArgumentException` with message
      `$"Source image format '{format}' is not supported by OmrTextureCombiner. Supported formats: {Format.RGBA_UN8}, {Format.R_UN8}, {Format.BGRA_UN8}."`
      using `nameof(format)` as the parameter name
    - _Requirements: 4.1, 4.2, 4.4, 4.5, 5.1, 5.2, 5.3, 5.4_

  - [x] 2.2 Write property test for `PixelSampler` — Property 3: Channel round-trip
    - `// Feature: omr-texture-combiner, Property 3: ChannelRoundTripPreservesSourceBytes`
    - Generator: pick a supported format from `{RGBA_UN8, R_UN8, BGRA_UN8}`, create a
      small `Image` (4×4 to 16×16) with random pixel data written via
      `SetPixel<byte>` on the raw buffer, pick a random `ChannelComponent`, and a
      random `(x, y)` within bounds
    - Property: `PixelSampler.Sample(pb, format, channel, x, y)` returns the same
      byte that a direct struct read of the pixel at `(x, y)` would return for that
      channel (use `GetPixel<Rgba8Pixel>` / `GetPixel<byte>` / `GetPixel<Bgra8Pixel>`
      as the oracle)
    - Run with 200 iterations via `Prop.ForAll(...).QuickCheckThrowOnFailure()`
    - **Validates: Requirements 2.3, 2.4, 2.5, 2.7, 5.1, 5.2, 5.3, 5.4, 5.5**

- [x] 3. Create `OmrTextureCombiner.cs` — fluent builder
  - [x] 3.1 Declare the class skeleton and private state
    - Declare `public sealed class OmrTextureCombiner` in namespace
      `HelixToolkit.Nex.Textures`
    - Add three private fields: `_occlusion`, `_metallic`, `_roughness`, each of
      type `ChannelSource`, initialized to `new ChannelSource.Constant(0)`
    - Add the private `Rgba8Pixel` output struct (same definition as in
      `PixelSampler.cs`; keep it `internal` and file-scoped to avoid duplication —
      or reference the one from `PixelSampler` if it is accessible)
    - _Requirements: 6.4_

  - [x] 3.2 Implement the `With*` fluent methods
    - `WithOcclusion(Image source, ChannelComponent channel)`: validate `source` is
      not null (`ArgumentNullException`), assign
      `_occlusion = new ChannelSource.ImageChannel(source, channel)`, return `this`
    - `WithOcclusion(byte constantValue)`: assign
      `_occlusion = new ChannelSource.Constant(constantValue)`, return `this`
    - Repeat the same pattern for `WithMetallic` and `WithRoughness`
    - Note: `byte` parameters are already range-safe; the design does not require
      range validation on `byte` constants (the type enforces [0, 255])
    - _Requirements: 6.1, 6.2, 6.3, 7.1_

  - [x] 3.3 Implement `Combine(int width, int height)` — core combine logic
    - **Validation phase** (before any allocation):
      1. Null check: for each `ChannelSource.ImageChannel` source, verify
         `source.Source` is not null; throw `ArgumentNullException` naming the
         channel (e.g., `"Source image for Occlusion channel must not be null."`)
      2. Disposed check: for each `ChannelSource.ImageChannel`, verify
         `source.Source.DataPointer != IntPtr.Zero`; throw
         `ObjectDisposedException` if disposed
      3. Format check: for each `ChannelSource.ImageChannel`, verify
         `source.Source.Description.Format` is in
         `{RGBA_UN8, R_UN8, BGRA_UN8}`; throw `ArgumentException` naming the
         channel and the unsupported format
      4. Dimension consistency: collect `(Width, Height)` from all
         `ChannelSource.ImageChannel` entries; if any two differ, throw
         `ArgumentException` with message
         `$"Source image dimensions are inconsistent: {ch1} is {w1}×{h1} but {ch2} is {w2}×{h2}."`
      5. Dimension resolution: if at least one `ImageChannel` source exists, use
         its dimensions (overriding the caller-supplied `width`/`height`); otherwise
         use the caller-supplied values
    - **Pixel loop** (inside a try/catch for exception safety):
      - Allocate `output = Image.New2D(width, height, MipMapCount.One, Format.RGBA_UN8)`
      - Pre-fetch `PixelBuffer` for each `ImageChannel` source via
        `source.Source.GetPixelBuffer(0, 0)` (mip 0, array 0)
      - Iterate `y` in `[0, height)`, `x` in `[0, width)`:
        - Compute `r` by dispatching on `_occlusion` type:
          `Constant` → `source.Value`;
          `ImageChannel` → `PixelSampler.Sample(pb, format, channel, x, y)`
        - Compute `g` and `b` the same way for `_metallic` and `_roughness`
        - Write `outPb.SetPixel<Rgba8Pixel>(x, y, new Rgba8Pixel { R=r, G=g, B=b, A=255 })`
      - On any exception: `output?.Dispose(); throw;`
    - Return `output`
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 2.7, 2.8, 3.1, 3.2, 4.1, 4.2,
      4.4, 4.5, 4.6, 7.2, 7.4, 8.3_

  - [x] 3.4 Implement `Combine()` — no-args overload
    - If all three sources are `ChannelSource.Constant`, throw `ArgumentException`
      with message
      `"Output dimensions must be supplied explicitly when all channel mappings are constant values."`
    - Otherwise delegate to `Combine(0, 0)` (dimension resolution in 3.3 will
      override the zeros with the source image dimensions)
    - _Requirements: 3.3, 3.4, 6.5_

- [x] 4. Checkpoint — build and verify
  - Ensure the solution builds without errors or warnings under
    `helix-toolkit-nex/Source/HelixToolkit-Nex/HelixToolkit.Nex.sln`
  - Ensure all existing tests in `HelixToolkit.Nex.Textures.Tests` still pass
  - Ask the user if any questions arise before proceeding to the test file

- [x] 5. Create `OmrTextureCombinerTests.cs` — unit tests and property-based tests
  - [x] 5.1 Write unit tests for the fluent API and basic correctness
    - **Fluent API smoke test**: chain `WithOcclusion`, `WithMetallic`,
      `WithRoughness` (each with a 4×4 `RGBA_UN8` image) and call `Combine()`;
      assert the result is non-null and has `Format.RGBA_UN8`
    - **All-constant + explicit dimensions**: `new OmrTextureCombiner().Combine(64, 64)`
      produces a 64×64 `RGBA_UN8` image with all pixels `(0, 0, 0, 255)`
    - **All-constant + no dimensions**: `new OmrTextureCombiner().Combine()` throws
      `ArgumentException`
    - **Null source image**: `WithOcclusion(null!, ChannelComponent.R)` throws
      `ArgumentNullException`
    - **Disposed source image**: dispose the source before calling `Combine()`,
      verify `ObjectDisposedException` is thrown
    - **Multi-mip source**: create a source image with 2 mip levels, use it as
      occlusion source; verify the output is 1 mip level and only mip-0 data is
      reflected in the output
    - **Same image for multiple channels**: use one `RGBA_UN8` image for both
      occlusion (channel R) and metallic (channel G); verify output R and G match
      the source R and G channels respectively
    - **Integration — save to stream**: call `output.Save(stream, ImageFileType.Png)`
      on the combined image and verify the stream length is greater than zero
    - _Requirements: 2.1, 2.6, 3.3, 3.4, 4.6, 6.4, 6.5, 7.1, 7.2, 8.2_

  - [x] 5.2 Write property test — Property 1: Output image structure
    - `// Feature: omr-texture-combiner, Property 1: OutputImageHasCorrectFormatAndMipLayout`
    - Generator: random combination of constant and image-sourced channel mappings
      (at least one image source to avoid the all-constant exception); random small
      image sizes (4–32); random supported formats
    - Property: `output.Description.Format == Format.RGBA_UN8`,
      `output.Description.MipLevels == 1`, `output.Description.ArraySize == 1`
    - 100 iterations via `Prop.ForAll(...).QuickCheckThrowOnFailure()`
    - **Validates: Requirements 2.1, 2.6, 8.3**

  - [x] 5.3 Write property test — Property 2: Alpha channel is always 255
    - `// Feature: omr-texture-combiner, Property 2: AllOutputPixelsHaveAlpha255`
    - Generator: same as Property 1; also generate a random `(x, y)` within the
      output bounds
    - Property: `outPb.GetPixel<Rgba8Pixel>(x, y).A == 255` for all pixels
    - 100 iterations
    - **Validates: Requirement 2.2**

  - [x] 5.4 Write property test — Property 3: Channel round-trip (image source)
    - `// Feature: omr-texture-combiner, Property 3: ChannelRoundTripPreservesSourceBytes`
    - Generator: pick a supported format, create a small image with random pixel
      data, pick a `ChannelComponent` for the source, pick which OMR output channel
      (R/G/B) to map it to, pick a random `(x, y)` within bounds
    - Property: the byte at the chosen output channel in the combined image at
      `(x, y)` equals `PixelSampler.Sample(sourcePb, format, srcChannel, x, y)`
    - 200 iterations
    - **Validates: Requirements 2.3, 2.4, 2.5, 2.7, 5.1, 5.2, 5.3, 5.4, 5.5**

  - [x] 5.5 Write property test — Property 4: Constant mapping writes constant to all pixels
    - `// Feature: omr-texture-combiner, Property 4: ConstantMappingWritesConstantToAllPixels`
    - Generator: three random `byte` constants for R, G, B; explicit output size
      (4–32); random `(x, y)` within bounds
    - Property: `outPb.GetPixel<Rgba8Pixel>(x, y).R == rConst` (and same for G, B)
    - Also verify the default-zero behavior: an unconfigured channel produces 0 in
      every pixel
    - 100 iterations
    - **Validates: Requirements 2.8, 6.4**

  - [x] 5.6 Write property test — Property 5: Output dimensions match source dimensions
    - `// Feature: omr-texture-combiner, Property 5: OutputDimensionsMatchSourceDimensions`
    - Generator: random width/height (4–64), at least one image-sourced channel
      mapping using an image of that size
    - Property: `output.Description.Width == sourceWidth &&
      output.Description.Height == sourceHeight`
    - 100 iterations
    - **Validates: Requirement 3.1**

  - [x] 5.7 Write property test — Property 6: Dimension mismatch throws `ArgumentException`
    - `// Feature: omr-texture-combiner, Property 6: DimensionMismatchThrowsArgumentException`
    - Generator: two images with different `(width, height)` pairs (ensure they
      differ by generating sizes independently and filtering out equal pairs); assign
      them to any two of the three output channels
    - Property: `Combine()` throws `ArgumentException` and no output `Image` is
      produced (verify by catching and checking the exception type)
    - 100 iterations
    - **Validates: Requirement 3.2**

  - [x] 5.8 Write property test — Property 7: Unsupported format throws `ArgumentException`
    - `// Feature: omr-texture-combiner, Property 7: UnsupportedFormatThrowsArgumentException`
    - Generator: pick a `Format` value not in `{RGBA_UN8, R_UN8, BGRA_UN8}` from
      the existing `Generators.AllValidFormats` list (filter out the three supported
      ones); create a small image with that format; assign it to any output channel
    - Property: `Combine()` throws `ArgumentException`; the exception message
      contains the format name
    - 100 iterations
    - **Validates: Requirement 4.5**

  - [x] 5.9 Write property test — Property 8: Constant value range validation
    - `// Feature: omr-texture-combiner, Property 8: ConstantValueRangeValidation`
    - Note: because the `With*(byte constantValue)` overloads accept `byte`, the
      type system already enforces [0, 255]. This property therefore tests that
      valid `byte` values (the full range 0–255) never throw, and documents the
      design decision. Generate random `byte` values and verify no exception is
      thrown when calling `WithOcclusion(b)`, `WithMetallic(b)`, `WithRoughness(b)`
    - 100 iterations
    - **Validates: Requirements 1.3, 7.3**

- [x] 6. Final checkpoint — run all tests
  - Build the solution and run all tests in `HelixToolkit.Nex.Textures.Tests`
  - Ensure all new unit tests and all property-based tests pass
  - Ensure no regressions in existing tests
  - Ask the user if any questions arise

## Notes

- Sub-tasks marked with `*` are optional and can be skipped for a faster MVP
- Each task references specific requirements for traceability
- The test project uses **MSTest** (not xUnit) with **FsCheck 3.x**; use
  `Prop.ForAll(...).QuickCheckThrowOnFailure()` — not `Property.ForAll` or xUnit
  `[Property]` attributes
- `PixelSampler` is `internal`; tests access it via `InternalsVisibleTo` or by
  testing it indirectly through `OmrTextureCombiner`
- The `Rgba8Pixel` and `Bgra8Pixel` structs are `internal`; define them once in
  `PixelSampler.cs` and reference them from `OmrTextureCombiner.cs` (same assembly)
- `byte` constant overloads (`With*(byte)`) cannot receive out-of-range values at
  compile time; Property 8 documents this design decision rather than testing a
  runtime guard
