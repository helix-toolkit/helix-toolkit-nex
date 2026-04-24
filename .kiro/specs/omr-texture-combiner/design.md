# Design Document: OMR Texture Combiner

## Overview

The OMR Texture Combiner is a CPU-side utility that merges up to three separate PBR source images — occlusion, metallic, and roughness — into a single `R8G8B8A8_UNorm` (`RGBA_UN8`) texture following the engine's OMR channel convention:

| Output channel | Semantic    |
| -------------- | ----------- |
| R              | Occlusion   |
| G              | Metallic    |
| B              | Roughness   |
| A              | 255 (fixed) |

Each output channel is driven by a `ChannelSource`: either a specific color channel (R, G, B, or A) extracted from a source `Image`, or a constant byte value. The combiner is exposed through a fluent builder (`OmrTextureCombiner`) and lives entirely inside `HelixToolkit.Nex.Textures` — no new project is required.

The output `Image` is created with `Image.New2D(width, height, MipMapCount.One, Format.RGBA_UN8)` and is immediately usable with `TextureCreator.CreateTexture()` or `Image.Save()`.

---

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│  Public API layer                                               │
│                                                                 │
│  OmrTextureCombiner  (fluent builder)                           │
│    .WithOcclusion(Image, ChannelComponent)                      │
│    .WithOcclusion(byte)                                         │
│    .WithMetallic(Image, ChannelComponent)                       │
│    .WithMetallic(byte)                                          │
│    .WithRoughness(Image, ChannelComponent)                      │
│    .WithRoughness(byte)                                         │
│    .Combine()                → Image (RGBA_UN8)                 │
│    .Combine(int w, int h)    → Image (RGBA_UN8)                 │
└────────────────────────┬────────────────────────────────────────┘
                         │ uses
┌────────────────────────▼────────────────────────────────────────┐
│  Domain types                                                   │
│                                                                 │
│  ChannelComponent  enum  { R, G, B, A }                        │
│  ChannelSource     sealed hierarchy                             │
│    ├─ ImageChannelSource(Image source, ChannelComponent ch)     │
│    └─ ConstantChannelSource(byte value)                         │
└────────────────────────┬────────────────────────────────────────┘
                         │ uses
┌────────────────────────▼────────────────────────────────────────┐
│  Internal sampling layer                                        │
│                                                                 │
│  PixelSampler  (internal static class)                          │
│    Sample(PixelBuffer pb, Format fmt, ChannelComponent ch,      │
│            int x, int y) → byte                                 │
└────────────────────────┬────────────────────────────────────────┘
                         │ reads / writes
┌────────────────────────▼────────────────────────────────────────┐
│  HelixToolkit.Nex.Textures primitives                           │
│  Image  ·  PixelBuffer  ·  Format  ·  MipMapCount              │
└─────────────────────────────────────────────────────────────────┘
```

The combiner is a pure CPU operation with no GPU interaction. It allocates one output `Image` via `Image.New2D`, iterates every pixel once, and writes the four output bytes per pixel in a single pass.

---

## Components and Interfaces

### `ChannelComponent` enum

```csharp
namespace HelixToolkit.Nex.Textures;

/// <summary>
/// Identifies a single color channel within a pixel.
/// </summary>
public enum ChannelComponent : byte
{
    R = 0,
    G = 1,
    B = 2,
    A = 3,
}
```

### `ChannelSource` discriminated union

A sealed class hierarchy models the two possible sources for an output channel. Using a sealed base with two concrete subtypes gives exhaustive pattern matching without a dependency on language-level discriminated unions.

```csharp
namespace HelixToolkit.Nex.Textures;

/// <summary>
/// Describes where the value for one OMR output channel comes from.
/// </summary>
public abstract class ChannelSource
{
    private ChannelSource() { }   // prevent external subclassing

    /// <summary>
    /// Reads a specific color channel from a source <see cref="Image"/>.
    /// </summary>
    public sealed class ImageChannel : ChannelSource
    {
        public Image Source { get; }
        public ChannelComponent Channel { get; }

        public ImageChannel(Image source, ChannelComponent channel)
        {
            ArgumentNullException.ThrowIfNull(source);
            Source = source;
            Channel = channel;
        }
    }

    /// <summary>
    /// Writes a fixed byte value to every pixel of the output channel.
    /// </summary>
    public sealed class Constant : ChannelSource
    {
        public byte Value { get; }

        public Constant(byte value)
        {
            Value = value;
        }
    }
}
```

**Design rationale**: The private constructor prevents callers from subclassing `ChannelSource`, making the switch exhaustive. The two subtypes are `sealed` so the compiler can devirtualize calls in the hot pixel loop.

### `OmrTextureCombiner` fluent builder

```csharp
namespace HelixToolkit.Nex.Textures;

/// <summary>
/// Fluent builder that combines up to three PBR source images into a single
/// OMR texture (R = Occlusion, G = Metallic, B = Roughness, A = 255).
/// </summary>
public sealed class OmrTextureCombiner
{
    private ChannelSource _occlusion = new ChannelSource.Constant(0);
    private ChannelSource _metallic  = new ChannelSource.Constant(0);
    private ChannelSource _roughness = new ChannelSource.Constant(0);

    // --- Occlusion (R output channel) ---

    public OmrTextureCombiner WithOcclusion(Image source, ChannelComponent channel);
    public OmrTextureCombiner WithOcclusion(byte constantValue);

    // --- Metallic (G output channel) ---

    public OmrTextureCombiner WithMetallic(Image source, ChannelComponent channel);
    public OmrTextureCombiner WithMetallic(byte constantValue);

    // --- Roughness (B output channel) ---

    public OmrTextureCombiner WithRoughness(Image source, ChannelComponent channel);
    public OmrTextureCombiner WithRoughness(byte constantValue);

    // --- Execute ---

    /// <summary>
    /// Executes the combine operation. Output dimensions are inferred from the
    /// source images. Throws <see cref="ArgumentException"/> if all three
    /// channels are constant (no dimensions can be inferred).
    /// </summary>
    public Image Combine();

    /// <summary>
    /// Executes the combine operation with explicit output dimensions.
    /// Required when all three channels are constant-value mappings.
    /// </summary>
    public Image Combine(int width, int height);
}
```

Each `With*` method validates its argument immediately (null check, range check for constants) and returns `this` for chaining. The builder is not thread-safe; callers must not share an instance across threads.

### `PixelSampler` internal static class

```csharp
namespace HelixToolkit.Nex.Textures;

/// <summary>
/// Internal helper that reads a single byte channel value from a pixel buffer,
/// handling per-format byte layout differences.
/// </summary>
internal static class PixelSampler
{
    /// <summary>
    /// Reads the requested channel from pixel (x, y) in the given pixel buffer.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="format"/> is not a supported source format.
    /// </exception>
    internal static byte Sample(
        PixelBuffer pixelBuffer,
        Format format,
        ChannelComponent channel,
        int x,
        int y);
}
```

---

## Data Models

### Pixel struct used for output writes

The output format is always `RGBA_UN8` (4 bytes per pixel). The combiner writes the output using a value-type struct that maps directly onto the 4-byte pixel layout:

```csharp
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct Rgba8Pixel
{
    public byte R;
    public byte G;
    public byte B;
    public byte A;
}
```

`PixelBuffer.SetPixel<Rgba8Pixel>(x, y, pixel)` writes all four bytes in one call, avoiding four separate `SetPixel<byte>` calls per pixel.

### Per-format read structs

For source formats, the sampler reads the raw pixel bytes using matching structs:

| Format     | Struct                                    | Size |
| ---------- | ----------------------------------------- | ---- |
| `RGBA_UN8` | `Rgba8Pixel` (R, G, B, A)                 | 4 B  |
| `R_UN8`    | `byte` (single channel)                   | 1 B  |
| `BGRA_UN8` | `Bgra8Pixel` (B, G, R, A in memory order) | 4 B  |

```csharp
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct Bgra8Pixel
{
    public byte B;   // byte offset 0
    public byte G;   // byte offset 1
    public byte R;   // byte offset 2
    public byte A;   // byte offset 3
}
```

> **Note on `R8G8B8_UNorm` (24-bit RGB):** The requirements document lists `R8G8B8_UNorm` as a supported source format, but the engine's `Format` enum contains no 24-bit RGB entry. The closest formats are `RGBA_UN8` (32-bit, alpha present) and `BGRA_UN8` (32-bit, BGRA order). Images loaded from disk in 24-bit RGB are decoded by `ImageSharpDecoder` and promoted to `RGBA_UN8` with alpha = 255 before being stored in an `Image`. Therefore, callers will never hold an `Image` with a 24-bit RGB format at runtime, and the combiner does not need a dedicated code path for it. If a future format is added to the enum, it will be rejected by the unsupported-format guard and the caller will receive a descriptive `ArgumentException`.

---

## Internal `PixelSampler` Logic

The sampler is a single `static byte Sample(...)` method with a format switch. All reads are raw byte reads — no floating-point conversion occurs.

### `RGBA_UN8` (4 bytes: R G B A)

```
GetPixel<Rgba8Pixel>(x, y)
channel R → pixel.R
channel G → pixel.G
channel B → pixel.B
channel A → pixel.A
```

### `R_UN8` (1 byte: R only)

```
GetPixel<byte>(x, y)
channel R → raw byte
channel G → 0
channel B → 0
channel A → 255
```

Rationale: a single-channel grayscale image has no green, blue, or alpha data. Returning 0 for G/B and 255 for A matches the convention used by `ImageSharpDecoder` when promoting grayscale images.

### `BGRA_UN8` (4 bytes: B G R A in memory order)

```
GetPixel<Bgra8Pixel>(x, y)
channel R → pixel.R  (byte offset 2)
channel G → pixel.G  (byte offset 1)
channel B → pixel.B  (byte offset 0)
channel A → pixel.A  (byte offset 3)
```

The struct field names match the logical channel names, so the mapping is transparent. The byte-order swap is handled entirely by the struct layout.

### Unsupported formats

Any `Format` value not in the set `{RGBA_UN8, R_UN8, BGRA_UN8}` causes the sampler to throw:

```csharp
throw new ArgumentException(
    $"Source image format '{format}' is not supported by OmrTextureCombiner. " +
    $"Supported formats: {Format.RGBA_UN8}, {Format.R_UN8}, {Format.BGRA_UN8}.",
    nameof(format));
```

---

## Combine Algorithm

### Validation order (inside `Combine(int width, int height)`)

All validation runs before any output memory is allocated, so no cleanup is needed on validation failure.

1. **Null check** — for each `ImageChannelSource`, verify `Source` is not null (`ArgumentNullException`).
2. **Disposed check** — for each `ImageChannelSource`, verify `Source.DataPointer != IntPtr.Zero` (proxy for disposal; `ObjectDisposedException`).
3. **Format check** — for each `ImageChannelSource`, verify `Source.Description.Format` is in the supported set (`ArgumentException` naming the affected output channel and the unsupported format).
4. **Dimension consistency check** — collect `(width, height)` from all `ImageChannelSource` entries; if any two differ, throw `ArgumentException` identifying the conflicting dimensions.
5. **Dimension resolution** — if at least one `ImageChannelSource` exists, use its dimensions (all are equal after step 4). Otherwise use the caller-supplied `(width, height)`.
6. **Explicit dimension required** — if `Combine()` (no args) is called and all three sources are `Constant`, throw `ArgumentException("Output dimensions must be supplied explicitly when all channel mappings are constant values.")`.

### Pixel loop

```
output = Image.New2D(width, height, MipMapCount.One, Format.RGBA_UN8)
outPb  = output.GetPixelBuffer(arrayIndex: 0, mipmap: 0)

for y in [0, height):
    for x in [0, width):
        r = Sample(_occlusion, x, y)
        g = Sample(_metallic,  x, y)
        b = Sample(_roughness, x, y)
        outPb.SetPixel<Rgba8Pixel>(x, y, new Rgba8Pixel { R=r, G=g, B=b, A=255 })

return output
```

Where `Sample(source, x, y)` dispatches on the `ChannelSource` type:
- `ConstantChannelSource` → return `source.Value`
- `ImageChannelSource` → `PixelSampler.Sample(pb, format, channel, x, y)` using the pre-fetched `PixelBuffer` for that source image (mip 0, array 0)

**Pre-fetching pixel buffers**: Before the loop, each `ImageChannelSource` has its `PixelBuffer` retrieved once via `source.GetPixelBuffer(0, 0)`. This avoids repeated dictionary/array lookups inside the hot loop.

### Exception safety

The output `Image` is allocated inside a `try` block. If any exception escapes the pixel loop (e.g., an unexpected format discovered mid-loop, or an `ObjectDisposedException` from a source image disposed on another thread), the partially-written output image is disposed before the exception propagates:

```csharp
Image? output = null;
try
{
    output = Image.New2D(width, height, MipMapCount.One, Format.RGBA_UN8);
    // ... pixel loop ...
    return output;
}
catch
{
    output?.Dispose();
    throw;
}
```

---

## Error Handling

| Condition                          | Exception                     | Message pattern                                                                                    |
| ---------------------------------- | ----------------------------- | -------------------------------------------------------------------------------------------------- |
| `null` image passed to `With*`     | `ArgumentNullException`       | `"Source image for {channel} channel must not be null."`                                           |
| Image disposed before `Combine()`  | `ObjectDisposedException`     | Standard `ObjectDisposedException` message                                                         |
| Constant value outside [0, 255]    | `ArgumentOutOfRangeException` | `"Constant value {v} for {channel} channel is outside the valid range [0, 255]."`                  |
| Unsupported source format          | `ArgumentException`           | `"Source image format '{fmt}' is not supported … Supported: RGBA_UN8, R_UN8, BGRA_UN8."`           |
| Dimension mismatch between sources | `ArgumentException`           | `"Source image dimensions are inconsistent: {channel1} is {w1}×{h1} but {channel2} is {w2}×{h2}."` |
| All-constant config + `Combine()`  | `ArgumentException`           | `"Output dimensions must be supplied explicitly when all channel mappings are constant values."`   |

All validation that can be performed before allocation is performed before allocation. The only exception that can occur after allocation is an `ObjectDisposedException` from a source image disposed concurrently, which is caught and handled by the exception-safety wrapper.

---

## Correctness Properties

*A property is a characteristic or behavior that should hold true across all valid executions of a system — essentially, a formal statement about what the system should do. Properties serve as the bridge between human-readable specifications and machine-verifiable correctness guarantees.*

### Property 1: Output image structure

*For any* valid `OmrTextureCombiner` configuration (any combination of image-sourced and constant channel mappings), the `Image` returned by `Combine()` shall have `Format == Format.RGBA_UN8`, `MipLevels == 1`, and `ArraySize == 1`.

**Validates: Requirements 2.1, 2.6, 8.3**

---

### Property 2: Alpha channel is always 255

*For any* valid configuration and any pixel position `(x, y)` in the output image, the alpha byte of that pixel shall equal 255.

**Validates: Requirements 2.2**

---

### Property 3: Channel round-trip (image source)

*For any* source `Image` in a supported format (`RGBA_UN8`, `R_UN8`, or `BGRA_UN8`), any `ChannelComponent`, and any pixel position `(x, y)`: if that image and channel are used as the source for one OMR output channel, then reading that output channel from the combined image at `(x, y)` shall return the same byte value that `PixelSampler.Sample` would return for the source image at `(x, y)`.

Concretely, for each output channel `ch ∈ {R, G, B}`:

```
output.GetPixelBuffer(0,0).GetPixel<Rgba8Pixel>(x,y)[ch]
  == PixelSampler.Sample(source.GetPixelBuffer(0,0), source.Format, mappedChannel, x, y)
```

This property subsumes the per-format sampling correctness requirements (5.1–5.4) and the general round-trip requirement (5.5).

**Validates: Requirements 2.3, 2.4, 2.5, 2.7, 5.1, 5.2, 5.3, 5.4, 5.5**

---

### Property 4: Constant mapping writes constant to all pixels

*For any* byte constant `c` used as a channel mapping and *for any* pixel position `(x, y)` in the output image, the corresponding output channel byte shall equal `c`.

This also covers the default-zero behavior: an unconfigured channel defaults to `ConstantChannelSource(0)`, so every pixel in that channel is 0.

**Validates: Requirements 2.8, 6.4**

---

### Property 5: Output dimensions match source dimensions

*For any* configuration where at least one channel mapping references a source image, the output image's `Width` and `Height` shall equal the `Width` and `Height` of those source images (which are all equal by the dimension consistency invariant).

**Validates: Requirements 3.1**

---

### Property 6: Dimension mismatch throws `ArgumentException`

*For any* two source images with different `(width, height)` pairs, using both in the same `OmrTextureCombiner` configuration (on any two of the three output channels) shall cause `Combine()` to throw `ArgumentException` before producing any output.

**Validates: Requirements 3.2**

---

### Property 7: Unsupported format throws `ArgumentException`

*For any* `Format` value not in `{Format.RGBA_UN8, Format.R_UN8, Format.BGRA_UN8}`, using an `Image` with that format as a channel source shall cause `Combine()` to throw `ArgumentException` identifying the unsupported format.

**Validates: Requirements 4.5**

---

### Property 8: Constant value range validation

*For any* integer value outside the range [0, 255], passing it to a `With*` overload that accepts a constant value shall throw `ArgumentOutOfRangeException`. *For any* byte value in [0, 255], the call shall succeed without throwing.

**Validates: Requirements 1.3, 7.3**

---

## Testing Strategy

### Unit tests (example-based)

Focus on concrete scenarios and error conditions that are not covered by the property tests:

- **Fluent API smoke test**: chain all three `With*` methods and call `Combine()` — verify it compiles and returns a non-null `Image`.
- **All-constant + explicit dimensions**: `Combine(64, 64)` with three constant mappings produces a 64×64 image.
- **All-constant + no dimensions**: `Combine()` with three constant mappings throws `ArgumentException`.
- **Null source image**: `WithOcclusion(null, ChannelComponent.R)` throws `ArgumentNullException`.
- **Disposed source image**: dispose the source before `Combine()`, verify `ObjectDisposedException`.
- **Multi-mip source**: verify only mip 0 data appears in the output (mip 1+ data is ignored).
- **Same image for multiple channels**: use one image for both occlusion and metallic — verify both output channels are correct.
- **Integration — save to stream**: call `output.Save(stream, ImageFileType.Png)` and verify the stream is non-empty.

### Property-based tests

Use [FsCheck](https://fscheck.github.io/FsCheck/) (the standard PBT library for .NET/xUnit) with a minimum of **100 iterations** per property. Each test is tagged with a comment referencing the design property.

**Tag format**: `// Feature: omr-texture-combiner, Property {N}: {property_text}`

**Generators needed**:
- `Gen<Format>` restricted to `{RGBA_UN8, R_UN8, BGRA_UN8}` for valid source formats.
- `Gen<Format>` restricted to formats *not* in the supported set for the unsupported-format property.
- `Gen<Image>` that creates a small (e.g., 4×4 to 32×32) `Image` with random pixel data in a given format.
- `Gen<ChannelComponent>` over all four enum values.
- `Gen<byte>` (standard) for constant values.
- `Gen<int>` outside [0, 255] for range-violation tests.

**Property test mapping**:

| Property                       | Test name                                  | Iterations |
| ------------------------------ | ------------------------------------------ | ---------- |
| P1 — Output image structure    | `OutputImageHasCorrectFormatAndMipLayout`  | 100        |
| P2 — Alpha always 255          | `AllOutputPixelsHaveAlpha255`              | 100        |
| P3 — Channel round-trip        | `ChannelRoundTripPreservesSourceBytes`     | 200        |
| P4 — Constant mapping          | `ConstantMappingWritesConstantToAllPixels` | 100        |
| P5 — Output dimensions         | `OutputDimensionsMatchSourceDimensions`    | 100        |
| P6 — Dimension mismatch throws | `DimensionMismatchThrowsArgumentException` | 100        |
| P7 — Unsupported format throws | `UnsupportedFormatThrowsArgumentException` | 100        |
| P8 — Constant range validation | `ConstantValueRangeValidation`             | 100        |

Property 3 (channel round-trip) is the most important and runs 200 iterations to exercise the full cross-product of formats × channels × pixel positions.

### Test project

Tests live in the existing `HelixToolkit.Nex.Textures.Tests` project. No new test project is needed.
