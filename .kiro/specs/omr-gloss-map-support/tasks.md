# Implementation Plan: Gloss Map Support for OmrTextureCombiner

## Overview

Add `InvertedImageChannel` as a third `ChannelSource` subtype and two `WithRoughnessFromGloss`
builder overloads to `OmrTextureCombiner`. All changes are confined to `ChannelSource.cs`,
`OmrTextureCombiner.cs`, and `OmrTextureCombinerTests.cs`. No new files are introduced.

## Tasks

- [x] 1. Add `InvertedImageChannel` nested class to `ChannelSource.cs`
  - Declare `public sealed class InvertedImageChannel : ChannelSource` inside `ChannelSource`,
    alongside `ImageChannel` and `Constant`
  - Add `public Image Source { get; }` and `public ChannelComponent Channel { get; }` properties
  - Implement constructor with `ArgumentNullException.ThrowIfNull(source)` guard, mirroring
    the `ImageChannel` constructor
  - _Requirements: 1.1, 1.6_

- [x] 2. Refactor `ValidateSource` and extend for `InvertedImageChannel`
  - [x] 2.1 Extract `ValidateImageSource(Image image, string channelName)` private helper
    - Move the three existing checks (null, disposed, format) from `ValidateSource` into the
      new helper so they can be shared
    - _Requirements: 2.1, 2.2_
  - [x] 2.2 Update `ValidateSource` to use a switch expression that extracts `Image?` from
    both `ImageChannel` and `InvertedImageChannel`, then calls `ValidateImageSource` when
    non-null
    - _Requirements: 2.1, 2.2_

- [x] 3. Extend `CheckDimension` for `InvertedImageChannel`
  - Replace the `if (source is not ChannelSource.ImageChannel imageChannel) return;` guard
    with a switch expression that extracts `Image?` from both `ImageChannel` and
    `InvertedImageChannel`, returning early only when null
  - The dimension consistency logic below the guard is unchanged
  - _Requirements: 2.3, 2.4_

- [x] 4. Extend `GetPixelBuffer` for `InvertedImageChannel`
  - Replace the `if (source is ChannelSource.ImageChannel imageChannel)` branch with a
    switch expression that returns `ic.Source.GetPixelBuffer(0, 0)` for both `ImageChannel`
    and `InvertedImageChannel`, and `null` for all other cases
  - _Requirements: 1.2_

- [x] 5. Add `InvertedImageChannel` arm to `SampleSource`
  - Add a new switch arm for `ChannelSource.InvertedImageChannel ic` that returns
    `(byte)(255 - PixelSampler.Sample(pb!, ic.Source.Description.Format, ic.Channel, x, y))`
  - Place the arm after the existing `ImageChannel` arm and before the default throw arm
  - _Requirements: 1.2, 1.3, 1.4, 1.5_

- [x] 6. Update all-constant guard in `Combine()` to use `IsConstant` helper
  - Add a local function `static bool IsConstant(ChannelSource s) => s is ChannelSource.Constant;`
    inside `Combine()`
  - Replace the existing `_occlusion is ChannelSource.Constant && ...` expression with
    `IsConstant(_occlusion) && IsConstant(_metallic) && IsConstant(_roughness)`
  - _Requirements: 5.1, 5.2_

- [x] 7. Add `WithRoughnessFromGloss(Image, ChannelComponent)` overload to `OmrTextureCombiner`
  - Add the overload in the `// ---- Roughness (B output channel) ----` section, after the
    existing `WithRoughness` overloads
  - Guard with `ArgumentNullException.ThrowIfNull(source)`, assign
    `_roughness = new ChannelSource.InvertedImageChannel(source, channel)`, return `this`
  - Include XML doc comment following the same pattern as `WithRoughness(Image, ChannelComponent)`
  - _Requirements: 3.1, 3.3, 3.5_

- [x] 8. Add `WithRoughnessFromGloss(string, ChannelComponent)` overload to `OmrTextureCombiner`
  - Add immediately after the overload from task 7
  - Load via `Image.Load(filePath)`, throw `FileLoadException` when null (same pattern as
    `WithRoughness(string, ChannelComponent)`), otherwise delegate to
    `WithRoughnessFromGloss(image, channel)`
  - Include XML doc comment following the same pattern as `WithRoughness(string, ChannelComponent)`
  - _Requirements: 3.2, 3.4, 3.5_

- [x] 9. Checkpoint — verify existing tests still pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 10. Write unit tests for `InvertedImageChannel` and `WithRoughnessFromGloss`
  - [x] 10.1 Add `InvertedImageChannel_Constructor_SetsSourceAndChannel`
    - Construct with a valid image, assert `Source` and `Channel` properties match
    - _Requirements: 1.1_
  - [x] 10.2 Add `InvertedImageChannel_Constructor_NullSource_ThrowsArgumentNullException`
    - Pass `null!` as source, assert `ArgumentNullException`
    - _Requirements: 1.6_
  - [x] 10.3 Add `WithRoughnessFromGloss_Image_ReturnsThis`
    - Call `WithRoughnessFromGloss(image, channel)`, assert the returned reference is the
      same combiner instance (method chaining)
    - _Requirements: 3.1_
  - [x] 10.4 Add `WithRoughnessFromGloss_NullImage_ThrowsArgumentNullException`
    - Pass `null!` as source, assert `ArgumentNullException`
    - _Requirements: 3.3_
  - [x] 10.5 Add `WithRoughnessFromGloss_DisposedImage_ThrowsObjectDisposedException`
    - Dispose the image before calling `Combine()`, assert `ObjectDisposedException`
    - _Requirements: 2.1_
  - [x] 10.6 Add `WithRoughnessFromGloss_AfterWithRoughness_UsesInversion`
    - Call `WithRoughness` then `WithRoughnessFromGloss` on the same builder; verify the
      output B channel equals `(byte)(255 - rawValue)` (last-write-wins)
    - _Requirements: 3.5_
  - [x] 10.7 Add `WithRoughness_AfterWithRoughnessFromGloss_NoInversion`
    - Call `WithRoughnessFromGloss` then `WithRoughness` on the same builder; verify the
      output B channel equals the raw source value with no inversion (last-write-wins)
    - _Requirements: 3.5_
  - [x] 10.8 Add `WithRoughnessFromGloss_OnlyInvertedSource_InfersDimensions`
    - Configure all non-roughness channels as `Constant`, roughness as
      `WithRoughnessFromGloss`; call `Combine()` without explicit dimensions; assert
      success and output dimensions match the source image
    - _Requirements: 2.4, 5.2_
  - [x] 10.9 Add boundary unit tests for inversion at `v = 0` and `v = 255`
    - Create a 1×1 image with value `0`; assert output B = `255`
    - Create a 1×1 image with value `255`; assert output B = `0`
    - _Requirements: 1.3, 1.4_

- [x] 11. Write property-based tests (FsCheck) for gloss map support
  - [x]* 11.1 Add `Property9_InversionCorrectness_OutputEquals255MinusRawValue`
    - Generator: random supported format, random width/height (4–32), random
      `ChannelComponent`, random pixel (x, y); fill with `FillWithTestData`; read
      `rawValue` via `PixelSampler.Sample`; configure roughness via
      `WithRoughnessFromGloss`; assert output B == `(byte)(255 - rawValue)`
    - Tag: `// Feature: omr-gloss-map-support, Property 9: InversionCorrectness`
    - **Property 9 (design Property 1): Inversion correctness**
    - **Validates: Requirements 1.2, 1.3, 1.4, 1.5, 4.1, 4.3**
  - [x]* 11.2 Add `Property10_DoubleInversionRoundTrip`
    - Generator: random byte value `v` in [0, 255]; assert
      `(byte)(255 - (byte)(255 - v)) == v` (pure arithmetic, no image I/O needed)
    - Tag: `// Feature: omr-gloss-map-support, Property 10: DoubleInversionRoundTrip`
    - **Property 10 (design Property 2): Double-inversion round-trip**
    - **Validates: Requirement 4.2**
  - [x]* 11.3 Add `Property11_InvertedImageChannel_UnsupportedFormat_ThrowsArgumentException`
    - Generator: formats from `Generators.AllValidFormatsPublic` excluding `RGBA_UN8`,
      `R_UN8`, `BGRA_UN8` (and compressed formats that cannot be used to create an
      `Image`); configure roughness with `WithRoughnessFromGloss`; assert
      `ArgumentException` is thrown
    - Tag: `// Feature: omr-gloss-map-support, Property 11: UnsupportedFormatThrows`
    - **Property 11 (design Property 3): Unsupported format throws ArgumentException**
    - **Validates: Requirement 2.2**
  - [x]* 11.4 Add `Property12_InvertedImageChannel_DimensionMismatch_ThrowsArgumentException`
    - Generator: two dimension pairs `(w1, h1)` and `(w2, h2)` where `w1 != w2 || h1 != h2`;
      configure occlusion as `ImageChannel` with first image, roughness as
      `WithRoughnessFromGloss` with second image; assert `ArgumentException` is thrown
    - Tag: `// Feature: omr-gloss-map-support, Property 12: DimensionMismatchThrows`
    - **Property 12 (design Property 4): Dimension mismatch throws ArgumentException**
    - **Validates: Requirement 2.3**
  - [x]* 11.5 Add `Property13_InvertedImageChannel_NotTreatedAsConstant`
    - Generator: random supported format, random width/height (4–32); configure at least
      one channel as `WithRoughnessFromGloss`, others as `Constant`; call `Combine()`
      without explicit dimensions; assert no `ArgumentException` is thrown and output
      dimensions match the source
    - Tag: `// Feature: omr-gloss-map-support, Property 13: AllConstantGuardUnaffected`
    - **Property 13 (design Property 5): All-constant guard unaffected by InvertedImageChannel**
    - **Validates: Requirements 5.2, 2.4**

- [x] 12. Final checkpoint — ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Implementation language: C# (.NET 8.0), consistent with the rest of the project
- All changes are confined to `ChannelSource.cs`, `OmrTextureCombiner.cs`, and
  `OmrTextureCombinerTests.cs` — no new files are introduced
- New property tests are numbered 9–13, continuing from the existing suite (Properties 1–8)
- `FillWithTestData` and `Generators.AllValidFormatsPublic` are already available in the
  test class and can be reused directly
- The `(byte)(255 - rawValue)` formula never overflows — no clamping is needed
