# Requirements Document

## Introduction

The OMR Texture Combiner is a CPU-side utility for the HelixToolkit Nex engine that merges up to three separate PBR source images — an occlusion map, a metallic map, and a roughness map — into a single OMR texture following the engine's channel convention (R = Occlusion, G = Metallic, B = Roughness). Each output channel is driven by a configurable channel source: any single color channel (R, G, B, or A) from a designated source image, or a constant scalar value. The combiner operates on `Image` objects and produces a new `Image` that can be saved to disk or uploaded to the GPU via the existing `TextureCreator` pipeline.

---

## Glossary

- **OMR_Combiner**: The static utility class (or fluent builder) that performs the channel-combining operation.
- **OMR_Image**: The output `Image` produced by the OMR_Combiner, with format `R8G8B8A8_UNorm` and alpha fixed at 255.
- **Source_Image**: A CPU-side `Image` object supplied by the caller as the source for one or more channel mappings.
- **Channel_Source**: A discriminated value that identifies either a specific color channel (R, G, B, or A) of a named Source_Image, or a constant byte value in the range [0, 255].
- **Channel_Mapping**: The association between one OMR output channel (R, G, or B) and a Channel_Source.
- **OMR_Config**: The complete set of three Channel_Mappings (one per output channel) that fully describes a combine operation.
- **Pixel_Sampler**: The internal component responsible for reading a single normalized byte value from a Source_Image pixel given a Channel_Source.
- **Dimension_Policy**: The rule that governs source image dimensions: all Source_Images referenced by Channel_Mappings must have identical width and height; any mismatch causes the OMR_Combiner to throw an `ArgumentException`.

---

## Requirements

### Requirement 1: Channel Mapping Configuration

**User Story:** As a developer, I want to specify which source image and color channel feeds each OMR output channel, so that I can combine occlusion, metallic, and roughness data from separate texture files with full control over channel layout.

#### Acceptance Criteria

1. THE OMR_Combiner SHALL accept an OMR_Config that contains exactly three Channel_Mappings: one for the R (Occlusion) output channel, one for the G (Metallic) output channel, and one for the B (Roughness) output channel.
2. WHEN a Channel_Mapping references a Source_Image channel, THE OMR_Combiner SHALL accept any of the four channels: R, G, B, or A.
3. WHEN a Channel_Mapping specifies a constant value, THE OMR_Combiner SHALL accept any integer value in the range [0, 255] inclusive.
4. THE OMR_Config SHALL allow each of the three output channels to reference a different Source_Image, the same Source_Image, or a constant value independently.
5. THE OMR_Config SHALL allow any combination of image-sourced and constant-value Channel_Mappings across the three output channels.

---

### Requirement 2: Output Image Production

**User Story:** As a developer, I want the combiner to produce a valid `Image` object I can immediately save or upload to the GPU, so that the result integrates seamlessly with the existing texture pipeline.

#### Acceptance Criteria

1. WHEN the OMR_Combiner executes a combine operation, THE OMR_Combiner SHALL produce an OMR_Image with format `R8G8B8A8_UNorm`.
2. THE OMR_Combiner SHALL set the alpha channel of every pixel in the OMR_Image to 255.
3. THE OMR_Combiner SHALL set the R channel of every pixel in the OMR_Image to the value derived from the R Channel_Mapping.
4. THE OMR_Combiner SHALL set the G channel of every pixel in the OMR_Image to the value derived from the G Channel_Mapping.
5. THE OMR_Combiner SHALL set the B channel of every pixel in the OMR_Image to the value derived from the B Channel_Mapping.
6. THE OMR_Image SHALL have `MipLevels = 1` and `ArraySize = 1`.
7. WHEN a Channel_Mapping references a Source_Image channel, THE OMR_Combiner SHALL read the channel value from the corresponding pixel of that Source_Image after normalizing it to an 8-bit unsigned integer in [0, 255].
8. WHEN a Channel_Mapping specifies a constant value, THE OMR_Combiner SHALL write that constant byte value to every pixel of the corresponding output channel.

---

### Requirement 3: Dimension Resolution

**User Story:** As a developer, I want the combiner to enforce consistent source image dimensions, so that channel data is always spatially aligned and the output dimensions are unambiguous.

#### Acceptance Criteria

1. WHEN all Channel_Mappings that reference a Source_Image use images of identical width and height, THE OMR_Combiner SHALL produce an OMR_Image with that same width and height.
2. IF two or more Channel_Mappings reference Source_Images with differing width or height, THEN THE OMR_Combiner SHALL throw an `ArgumentException` identifying the conflicting dimensions before producing any output.
3. IF all three Channel_Mappings specify constant values and no Source_Image is provided, THEN THE OMR_Combiner SHALL require the caller to supply explicit output width and height dimensions.
4. IF all three Channel_Mappings specify constant values and the caller does not supply explicit output dimensions, THEN THE OMR_Combiner SHALL throw an `ArgumentException` with a descriptive message.

---

### Requirement 4: Source Image Format Compatibility

**User Story:** As a developer, I want to use source images in common formats without manual pre-conversion, so that I can feed the combiner directly with images loaded from disk.

#### Acceptance Criteria

1. THE OMR_Combiner SHALL accept Source_Images with format `R8G8B8A8_UNorm`.
2. THE OMR_Combiner SHALL accept Source_Images with format `R8_UNorm`, treating the single channel as the R channel value and returning 0 for G, B, and 255 for A.
3. THE OMR_Combiner SHALL accept Source_Images with format `R8G8B8_UNorm` (24-bit RGB), treating the A channel as 255.
4. THE OMR_Combiner SHALL accept Source_Images with format `B8G8R8A8_UNorm`, treating the bytes in memory order as B, G, R, A respectively (i.e., channel R maps to the third byte, G to the second byte, B to the first byte, and A to the fourth byte).
5. IF a Source_Image has a format not listed in acceptance criteria 1–4, THEN THE OMR_Combiner SHALL throw an `ArgumentException` identifying the unsupported format and the affected output channel.
6. THE OMR_Combiner SHALL only inspect the first mip level (mip 0) and first array slice (index 0) of any Source_Image.

---

### Requirement 5: Pixel Sampling Correctness

**User Story:** As a developer, I want the channel extraction to be bit-accurate, so that the combined texture faithfully represents the source data without precision loss.

#### Acceptance Criteria

1. WHEN extracting a channel from a Source_Image with format `R8G8B8A8_UNorm`, THE Pixel_Sampler SHALL read the raw byte value of the requested channel without any floating-point conversion.
2. WHEN extracting a channel from a Source_Image with format `R8_UNorm` and the requested channel is R, THE Pixel_Sampler SHALL read the raw byte value of that single channel.
3. WHEN extracting a channel from a Source_Image with format `R8_UNorm` and the requested channel is G, B, or A, THE Pixel_Sampler SHALL return 0 for G and B, and 255 for A.
4. WHEN extracting a channel from a Source_Image with format `B8G8R8A8_UNorm`, THE Pixel_Sampler SHALL read the raw byte at the byte-swapped position: B from byte offset 0, G from byte offset 1, R from byte offset 2, and A from byte offset 3.
5. FOR ALL valid OMR_Config values and Source_Images, combining a Source_Image into an OMR_Image and then reading back the same channel from the OMR_Image SHALL return the same byte value that was read from the Source_Image (round-trip property).

---

### Requirement 6: Fluent Builder API

**User Story:** As a developer, I want a readable, fluent API for constructing an OMR_Config, so that combine operations are self-documenting at the call site.

#### Acceptance Criteria

1. THE OMR_Combiner SHALL expose a fluent builder that allows setting each output channel mapping independently via named methods (e.g., `WithOcclusion(...)`, `WithMetallic(...)`, `WithRoughness(...)`).
2. THE OMR_Combiner SHALL expose overloads that accept a `(Image source, ChannelComponent channel)` pair for image-sourced mappings.
3. THE OMR_Combiner SHALL expose overloads that accept a `byte constantValue` for constant-value mappings.
4. WHEN the caller does not configure a Channel_Mapping for an output channel before calling `Combine()`, THE OMR_Combiner SHALL default that channel to a constant value of 0.
5. THE OMR_Combiner SHALL expose a `Combine()` method that executes the combine operation and returns the OMR_Image.
6. THE OMR_Combiner SHALL expose a `Combine(int width, int height)` overload that executes the combine operation with explicit output dimensions (required when all mappings are constant).

---

### Requirement 7: Error Handling

**User Story:** As a developer, I want clear, actionable error messages when I misconfigure the combiner, so that I can diagnose problems quickly.

#### Acceptance Criteria

1. IF a Source_Image supplied to the OMR_Combiner is null, THEN THE OMR_Combiner SHALL throw an `ArgumentNullException` identifying which channel mapping received the null image.
2. IF a Source_Image has been disposed before `Combine()` is called, THEN THE OMR_Combiner SHALL throw an `ObjectDisposedException`.
3. IF a constant value supplied to a Channel_Mapping is outside the range [0, 255], THEN THE OMR_Combiner SHALL throw an `ArgumentOutOfRangeException` identifying the affected output channel and the invalid value.
4. WHEN an error occurs during pixel processing, THE OMR_Combiner SHALL release any partially allocated OMR_Image memory before propagating the exception.

---

### Requirement 8: Integration with Existing Texture Pipeline

**User Story:** As a developer, I want the OMR_Image to be directly usable with `TextureCreator`, so that I can upload the combined texture to the GPU without additional conversion steps.

#### Acceptance Criteria

1. THE OMR_Image produced by the OMR_Combiner SHALL be a valid `Image` instance that can be passed directly to `TextureCreator.CreateTexture()` without modification.
2. THE OMR_Image SHALL be saveable to disk via `Image.Save()` in any format supported by `ImageFileType` (PNG, JPG, BMP, TGA, etc.).
3. THE OMR_Combiner SHALL produce the OMR_Image using `Image.New2D()` with `MipMapCount = 1` so that the result is compatible with the existing `Image` lifecycle and disposal pattern.
