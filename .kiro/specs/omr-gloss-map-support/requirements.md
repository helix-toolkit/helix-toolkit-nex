# Requirements Document

## Introduction

`OmrTextureCombiner` is a fluent builder that packs up to three PBR source images into a
single OMR texture (R = Occlusion, G = Metallic, B = Roughness, A = 255).

Currently each output channel is driven by a `ChannelSource`: either a raw channel read
from a source `Image`, or a constant byte value. There is no way to invert a sampled
channel value, which is required when the roughness source is a **gloss map** — a texture
where `0` means fully rough and `255` means fully smooth, the inverse of the roughness
convention used by the OMR format.

This feature adds inversion support to `ChannelSource` specifically for the Roughness
channel, allowing it to apply the transform `255 − value` to each sampled byte before
writing it to the output texture.

---

## Glossary

- **OmrTextureCombiner**: The fluent builder class that produces OMR textures.
- **ChannelSource**: The abstract base class that describes where one OMR output channel's
  value comes from. Currently has two concrete subtypes: `Constant` and `ImageChannel`.
- **ImageChannel**: A `ChannelSource` subtype that reads a specific color channel from a
  source `Image`.
- **Constant**: A `ChannelSource` subtype that writes a fixed byte value to every output
  pixel.
- **InvertedImageChannel**: The new `ChannelSource` subtype introduced by this feature.
  Reads a color channel from a source `Image` and applies `255 − value` to each sampled
  byte before writing it to the output.
- **Gloss map**: A texture in which `255` represents fully smooth (zero roughness) and `0`
  represents fully rough (maximum roughness) — the inverse of the roughness convention.
- **Roughness map**: A texture in which `255` represents fully rough and `0` represents
  fully smooth — the convention used by the OMR Blue channel.
- **Inversion transform**: The per-pixel byte operation `255 − value`, equivalent to
  `1 − value` in normalized [0, 1] space.
- **WithRoughnessFromGloss**: The new `OmrTextureCombiner` builder method that configures
  the Roughness (B) channel from a gloss map by applying the inversion transform.

---

## Requirements

### Requirement 1: InvertedImageChannel ChannelSource subtype

**User Story:** As an engine developer, I want a `ChannelSource` variant that inverts the
sampled byte value, so that I can use a gloss map as a roughness source without
pre-processing the texture.

#### Acceptance Criteria

1. THE `ChannelSource` class SHALL expose a sealed nested class named `InvertedImageChannel`
   declared inside `ChannelSource` alongside `ImageChannel` and `Constant`, holding a
   source `Image` reference and a `ChannelComponent` selector identical in structure to
   `ImageChannel`.
2. WHEN the `OmrTextureCombiner` samples an `InvertedImageChannel` source at pixel (x, y),
   THE `OmrTextureCombiner` SHALL compute the output byte as `(byte)(255 - rawValue)`,
   where `rawValue` is the byte returned by `PixelSampler.Sample` for the configured
   channel.
3. WHEN `rawValue` is `0`, THE `OmrTextureCombiner` SHALL write `255` to the output pixel.
4. WHEN `rawValue` is `255`, THE `OmrTextureCombiner` SHALL write `0` to the output pixel.
5. WHEN `rawValue` is `v` where `0 ≤ v ≤ 255`, THE `OmrTextureCombiner` SHALL write
   `255 - v` to the output pixel, with no clamping or rounding required because the result
   is always in [0, 255].
6. THE `InvertedImageChannel` constructor SHALL throw `ArgumentNullException` when the
   supplied source `Image` is null, consistent with the `ImageChannel` constructor.

---

### Requirement 2: Validation of InvertedImageChannel sources

**User Story:** As an engine developer, I want the combiner to validate `InvertedImageChannel`
sources with the same rigor as `ImageChannel` sources, so that misconfigured gloss maps
produce clear error messages rather than silent corruption.

#### Acceptance Criteria

1. WHEN `Combine()` or `Combine(width, height)` is called and a channel is configured with
   an `InvertedImageChannel` whose source `Image` has been disposed, THE
   `OmrTextureCombiner` SHALL throw `ObjectDisposedException` before allocating the output
   image.
2. WHEN `Combine()` or `Combine(width, height)` is called and a channel is configured with
   an `InvertedImageChannel` whose source `Image` has an unsupported pixel format, THE
   `OmrTextureCombiner` SHALL throw `ArgumentException` before allocating the output image.
3. WHEN `Combine()` or `Combine(width, height)` is called and an `InvertedImageChannel`
   source has dimensions that differ from another image source already validated, THE
   `OmrTextureCombiner` SHALL throw `ArgumentException` naming both the conflicting channel
   and the first-seen channel with their respective dimensions.
4. WHEN `Combine()` is called and the only image-backed sources are `InvertedImageChannel`
   sources (no `ImageChannel` sources), THE `OmrTextureCombiner` SHALL infer output
   dimensions from those `InvertedImageChannel` sources rather than throwing
   `ArgumentException`.

---

### Requirement 3: WithRoughnessFromGloss builder methods

**User Story:** As an engine developer, I want dedicated `WithRoughnessFromGloss` overloads
on `OmrTextureCombiner`, so that the intent of using a gloss map is explicit and readable
at the call site.

#### Acceptance Criteria

1. THE `OmrTextureCombiner` SHALL expose a method `WithRoughnessFromGloss(Image source,
   ChannelComponent channel)` that configures the Roughness (B) output channel with an
   `InvertedImageChannel` source and returns `this` for method chaining.
2. THE `OmrTextureCombiner` SHALL expose a method `WithRoughnessFromGloss(string filePath,
   ChannelComponent channel)` that loads an `Image` from `filePath`, configures the
   Roughness (B) output channel with an `InvertedImageChannel` source, and returns `this`
   for method chaining.
3. WHEN `WithRoughnessFromGloss(Image source, ChannelComponent channel)` is called with a
   null `source`, THE `OmrTextureCombiner` SHALL throw `ArgumentNullException`.
4. WHEN `WithRoughnessFromGloss(string filePath, ChannelComponent channel)` is called and
   `Image.Load(filePath)` returns null, THE `OmrTextureCombiner` SHALL throw
   `FileLoadException` with a message that includes `filePath`, consistent with the
   existing `WithRoughness(string, ChannelComponent)` overload.
5. WHEN `WithRoughnessFromGloss` is called after `WithRoughness` (or vice versa) on the
   same builder instance, THE `OmrTextureCombiner` SHALL use the configuration supplied by
   the most recent call, replacing the previous roughness configuration.

---

### Requirement 4: Inversion correctness — round-trip and boundary properties

**User Story:** As an engine developer, I want the inversion transform to be mathematically
correct for all byte values, so that gloss-to-roughness conversion produces no rounding
errors or out-of-range outputs.

#### Acceptance Criteria

1. FOR ALL byte values `v` in [0, 255], THE `OmrTextureCombiner` SHALL produce an output
   roughness byte equal to `(byte)(255 - v)` when the roughness channel is configured with
   an `InvertedImageChannel` sampling a pixel whose channel value is `v`.
2. FOR ALL byte values `v` in [0, 255], applying the inversion transform twice SHALL
   produce `v` (double-inversion round-trip: `255 - (255 - v) == v`).
3. THE `OmrTextureCombiner` SHALL produce output roughness bytes strictly within [0, 255]
   for all valid input byte values, with no overflow or underflow.

---

### Requirement 5: Combiner all-constant guard is unaffected

**User Story:** As an engine developer, I want the existing guard that rejects a
`Combine()` call when all three channels are constant values to remain correct after the
new subtype is introduced, so that the error message is not accidentally suppressed.

#### Acceptance Criteria

1. WHEN `Combine()` is called and all three channels are `ChannelSource.Constant` sources,
   THE `OmrTextureCombiner` SHALL throw `ArgumentException` with a message stating that
   explicit dimensions are required.
2. WHEN `Combine()` is called and at least one channel is an `InvertedImageChannel` source,
   THE `OmrTextureCombiner` SHALL NOT throw the all-constant `ArgumentException` and SHALL
   infer output dimensions from that source.
