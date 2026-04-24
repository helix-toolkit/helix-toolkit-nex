# Design Document — Gloss Map Support for OmrTextureCombiner

## Overview

`OmrTextureCombiner` packs up to three PBR source images into a single OMR texture
(R = Occlusion, G = Metallic, B = Roughness, A = 255). Each output channel is driven by a
`ChannelSource`: either a raw channel read from a source `Image` (`ImageChannel`), or a
constant byte value (`Constant`).

This feature adds a third `ChannelSource` subtype — `InvertedImageChannel` — that reads a
channel from a source image and applies the per-pixel transform `(byte)(255 - rawValue)`
before writing to the output. This is the exact transform needed to convert a **gloss map**
(where 255 = fully smooth) into the **roughness convention** used by the OMR Blue channel
(where 255 = fully rough).

Two new `WithRoughnessFromGloss` overloads on `OmrTextureCombiner` expose this capability
at the call site with explicit, readable intent.

The change is intentionally narrow: only the Roughness channel gets `WithRoughnessFromGloss`
overloads, and `InvertedImageChannel` is a sealed nested class inside `ChannelSource`
alongside the existing `ImageChannel` and `Constant` subtypes.

---

## Architecture

The existing architecture is unchanged. `ChannelSource` is an abstract class with a private
constructor that prevents external subclassing. All three subtypes (`Constant`,
`ImageChannel`, `InvertedImageChannel`) are sealed nested classes inside `ChannelSource`.

`OmrTextureCombiner` dispatches on the runtime type of each `ChannelSource` in four private
helpers (`ValidateSource`, `CheckDimension`, `GetPixelBuffer`, `SampleSource`) and in the
all-constant guard inside `Combine()`. Each of these five sites gains one new arm for
`InvertedImageChannel`.

```
ChannelSource (abstract)
├── Constant                  (existing)
├── ImageChannel              (existing)
└── InvertedImageChannel      (new)

OmrTextureCombiner
├── WithRoughnessFromGloss(Image, ChannelComponent)    (new)
├── WithRoughnessFromGloss(string, ChannelComponent)   (new)
├── ValidateSource            ← extended for InvertedImageChannel
├── CheckDimension            ← extended for InvertedImageChannel
├── GetPixelBuffer            ← extended for InvertedImageChannel
├── SampleSource              ← new switch arm: (byte)(255 - rawValue)
└── Combine()                 ← all-constant guard updated
```

No new files are introduced. All changes are confined to `ChannelSource.cs` and
`OmrTextureCombiner.cs`.

---

## Components and Interfaces

### `ChannelSource.InvertedImageChannel` (new nested class in `ChannelSource.cs`)

`InvertedImageChannel` is structurally identical to `ImageChannel` — it holds a `Source`
(`Image`) and a `Channel` (`ChannelComponent`). The inversion is not stored as state; it is
applied at sampling time inside `SampleSource`.

```csharp
public sealed class InvertedImageChannel : ChannelSource
{
    public Image Source { get; }
    public ChannelComponent Channel { get; }

    public InvertedImageChannel(Image source, ChannelComponent channel)
    {
        ArgumentNullException.ThrowIfNull(source);
        Source = source;
        Channel = channel;
    }
}
```

Key design decisions:
- **Sealed nested class** — consistent with `ImageChannel` and `Constant`; the private
  constructor on `ChannelSource` prevents any external subclassing.
- **Null guard in constructor** — `ArgumentNullException.ThrowIfNull(source)` mirrors the
  `ImageChannel` constructor, so misconfiguration is caught at builder-call time rather than
  at `Combine()` time.
- **No inversion state** — the `(byte)(255 - rawValue)` formula is applied in `SampleSource`,
  not stored in the object. This keeps the data model simple and the inversion logic in one
  place.

### `OmrTextureCombiner.WithRoughnessFromGloss` (two new overloads)

```csharp
// Overload 1: Image already in memory
public OmrTextureCombiner WithRoughnessFromGloss(Image source, ChannelComponent channel)
{
    ArgumentNullException.ThrowIfNull(source);
    _roughness = new ChannelSource.InvertedImageChannel(source, channel);
    return this;
}

// Overload 2: Load from file path
public OmrTextureCombiner WithRoughnessFromGloss(string filePath, ChannelComponent channel)
{
    var image = Image.Load(filePath);
    return image is null
        ? throw new FileLoadException($"Failed to load image from path: {filePath}", filePath)
        : WithRoughnessFromGloss(image, channel);
}
```

Both overloads follow the exact same pattern as the existing `WithRoughness` overloads.
The `ArgumentNullException` guard in overload 1 is redundant with the guard inside the
`InvertedImageChannel` constructor, but is kept for consistency with `WithRoughness(Image, ...)`,
which also guards at the builder level. Calling `WithRoughnessFromGloss` after `WithRoughness`
(or vice versa) simply overwrites `_roughness` — last-write-wins, consistent with all other
builder methods.

### `OmrTextureCombiner.ValidateSource` (extended)

The current implementation early-returns for any non-`ImageChannel` source. After this
change, `InvertedImageChannel` must also be validated. To avoid duplicating the three
validation checks (null, disposed, format), the shared logic is extracted into a private
helper `ValidateImageSource(Image image, string channelName)`, and both `ImageChannel` and
`InvertedImageChannel` arms call it.

```csharp
private static void ValidateSource(ChannelSource source, string channelName)
{
    Image? image = source switch
    {
        ChannelSource.ImageChannel ic         => ic.Source,
        ChannelSource.InvertedImageChannel ic => ic.Source,
        _                                     => null,
    };

    if (image is not null)
        ValidateImageSource(image, channelName);
}

private static void ValidateImageSource(Image image, string channelName)
{
    if (image is null)
        throw new ArgumentNullException(channelName,
            $"Source image for {channelName} channel must not be null.");

    if (image.DataPointer == IntPtr.Zero)
        throw new ObjectDisposedException(channelName,
            $"Source image for {channelName} channel has been disposed.");

    var format = image.Description.Format;
    if (!Array.Exists(SupportedFormats, f => f == format))
        throw new ArgumentException(
            $"Source image format '{format}' for {channelName} channel is not supported by OmrTextureCombiner. "
            + $"Supported formats: {Format.RGBA_UN8}, {Format.R_UN8}, {Format.BGRA_UN8}.",
            channelName);
}
```

This refactor eliminates duplication and ensures `InvertedImageChannel` sources receive
identical validation to `ImageChannel` sources.

### `OmrTextureCombiner.CheckDimension` (extended)

The current implementation pattern-matches on `ChannelSource.ImageChannel`. A second arm
is added for `InvertedImageChannel`, extracting the same `Source.Description.Width/Height`
values and applying the same consistency check.

```csharp
private static void CheckDimension(
    ChannelSource source, string channelName,
    ref (int w, int h, string name)? firstDim,
    ref int width, ref int height)
{
    Image? image = source switch
    {
        ChannelSource.ImageChannel ic         => ic.Source,
        ChannelSource.InvertedImageChannel ic => ic.Source,
        _                                     => null,
    };

    if (image is null) return;

    int srcW = image.Description.Width;
    int srcH = image.Description.Height;

    if (firstDim is null)
    {
        firstDim = (srcW, srcH, channelName);
        width = srcW;
        height = srcH;
    }
    else if (firstDim.Value.w != srcW || firstDim.Value.h != srcH)
    {
        throw new ArgumentException(
            $"Source image dimensions are inconsistent: {firstDim.Value.name} is "
            + $"{firstDim.Value.w}×{firstDim.Value.h} but {channelName} is {srcW}×{srcH}.");
    }
}
```

### `OmrTextureCombiner.GetPixelBuffer` (extended)

```csharp
private static PixelBuffer? GetPixelBuffer(ChannelSource source) =>
    source switch
    {
        ChannelSource.ImageChannel ic         => ic.Source.GetPixelBuffer(0, 0),
        ChannelSource.InvertedImageChannel ic => ic.Source.GetPixelBuffer(0, 0),
        _                                     => null,
    };
```

### `OmrTextureCombiner.SampleSource` (new switch arm)

The new arm applies `(byte)(255 - rawValue)` after delegating the raw read to
`PixelSampler.Sample`. The formula is always in range because both operands are bytes and
`255 - v` for `v ∈ [0, 255]` is always in `[0, 255]` — no clamping is needed.

```csharp
private static byte SampleSource(ChannelSource source, PixelBuffer? pb, int x, int y) =>
    source switch
    {
        ChannelSource.Constant c => c.Value,
        ChannelSource.ImageChannel ic => PixelSampler.Sample(
            pb!, ic.Source.Description.Format, ic.Channel, x, y),
        ChannelSource.InvertedImageChannel ic =>
            (byte)(255 - PixelSampler.Sample(
                pb!, ic.Source.Description.Format, ic.Channel, x, y)),
        _ => throw new InvalidOperationException(
            $"Unknown ChannelSource type: {source.GetType()}"),
    };
```

### All-constant guard in `Combine()` (updated)

The guard currently checks `_occlusion is ChannelSource.Constant && ...`. It must also
treat `InvertedImageChannel` as a non-constant source. The cleanest approach is to invert
the condition: a source is "constant" only if it is `ChannelSource.Constant`.

```csharp
static bool IsConstant(ChannelSource s) => s is ChannelSource.Constant;

bool allConstant = IsConstant(_occlusion) && IsConstant(_metallic) && IsConstant(_roughness);
```

This is logically equivalent to the existing check for the two existing subtypes, and
automatically handles `InvertedImageChannel` (and any future subtypes) correctly.

---

## Data Models

No new persistent data models are introduced. `InvertedImageChannel` holds the same two
fields as `ImageChannel`:

| Field     | Type               | Description                                |
| --------- | ------------------ | ------------------------------------------ |
| `Source`  | `Image`            | The source image to sample from (non-null) |
| `Channel` | `ChannelComponent` | The color channel to read (R, G, B, or A)  |

The inversion formula `(byte)(255 - rawValue)` is stateless and applied at sampling time.

---

## Correctness Properties

*A property is a characteristic or behavior that should hold true across all valid
executions of a system — essentially, a formal statement about what the system should do.
Properties serve as the bridge between human-readable specifications and machine-verifiable
correctness guarantees.*

### Property 1: Inversion correctness

*For any* supported image format, any `ChannelComponent`, any pixel coordinate (x, y), and
any byte value `v` stored at that pixel's channel, when the Roughness channel is configured
with `WithRoughnessFromGloss`, the output Blue channel at (x, y) SHALL equal `(byte)(255 - v)`.

**Validates: Requirements 1.2, 1.3, 1.4, 1.5, 4.1, 4.3**

### Property 2: Double-inversion round-trip

*For any* byte value `v` in [0, 255], applying the inversion transform twice SHALL return
the original value: `(byte)(255 - (byte)(255 - v)) == v`.

**Validates: Requirement 4.2**

### Property 3: InvertedImageChannel unsupported format throws ArgumentException

*For any* image format not in `{RGBA_UN8, R_UN8, BGRA_UN8}`, configuring the Roughness
channel with `WithRoughnessFromGloss` using a source image of that format and calling
`Combine()` SHALL throw `ArgumentException` before allocating the output image.

**Validates: Requirement 2.2**

### Property 4: InvertedImageChannel dimension mismatch throws ArgumentException

*For any* pair of image sources with differing dimensions, mixing one as an `ImageChannel`
(or `InvertedImageChannel`) source and the other as an `InvertedImageChannel` source SHALL
cause `Combine()` to throw `ArgumentException` naming both the conflicting channel and the
first-seen channel with their respective dimensions.

**Validates: Requirement 2.3**

### Property 5: All-constant guard unaffected by InvertedImageChannel

*For any* configuration where at least one channel is an `InvertedImageChannel` source,
calling `Combine()` (without explicit dimensions) SHALL NOT throw the all-constant
`ArgumentException`, and SHALL infer output dimensions from the `InvertedImageChannel`
source.

**Validates: Requirements 5.2, 2.4**

---

## Error Handling

All error handling follows the existing patterns in `OmrTextureCombiner`:

| Condition                                                            | Exception                 | When thrown                                   |
| -------------------------------------------------------------------- | ------------------------- | --------------------------------------------- |
| `null` passed to `WithRoughnessFromGloss(Image, ...)`                | `ArgumentNullException`   | At builder call time                          |
| `null` `Source` inside `InvertedImageChannel` constructor            | `ArgumentNullException`   | At constructor call time                      |
| `Image.Load` returns `null` in `WithRoughnessFromGloss(string, ...)` | `FileLoadException`       | At builder call time                          |
| `InvertedImageChannel` source image has been disposed                | `ObjectDisposedException` | In `ValidateSource`, before output allocation |
| `InvertedImageChannel` source image has unsupported format           | `ArgumentException`       | In `ValidateSource`, before output allocation |
| `InvertedImageChannel` source dimensions differ from another source  | `ArgumentException`       | In `CheckDimension`, before output allocation |
| All three channels are `Constant` (no image sources)                 | `ArgumentException`       | In `Combine()`, before output allocation      |

The `(byte)(255 - rawValue)` formula never throws — the result is always in [0, 255] for
any byte input, so no range-check or clamping is needed.

---

## Testing Strategy

Tests live in `HelixToolkit.Nex.Textures.Tests/OmrTextureCombinerTests.cs`, which already
contains the existing combiner tests. New tests are added to the same class.

The project uses **MSTest** as the test runner and **FsCheck 3.x** for property-based
testing, consistent with the existing test suite.

### Unit tests (example-based)

These cover specific scenarios, API contracts, and error conditions:

- `InvertedImageChannel_Constructor_SetsSourceAndChannel` — construct with a valid image,
  verify `Source` and `Channel` properties.
- `InvertedImageChannel_Constructor_NullSource_ThrowsArgumentNullException` — verify null
  guard.
- `WithRoughnessFromGloss_Image_ReturnsThis` — verify method chaining.
- `WithRoughnessFromGloss_NullImage_ThrowsArgumentNullException`.
- `WithRoughnessFromGloss_DisposedImage_ThrowsObjectDisposedException` — dispose before
  `Combine()`.
- `WithRoughnessFromGloss_AfterWithRoughness_UsesInversion` — last-write-wins: call
  `WithRoughness` then `WithRoughnessFromGloss`, verify inversion is applied.
- `WithRoughness_AfterWithRoughnessFromGloss_NoInversion` — last-write-wins: call
  `WithRoughnessFromGloss` then `WithRoughness`, verify no inversion.
- `WithRoughnessFromGloss_OnlyInvertedSource_InfersDimensions` — configure all non-roughness
  channels as `Constant`, roughness as `InvertedImageChannel`, call `Combine()` without
  dimensions, verify success and correct output size.
- `AllConstant_NoDimensions_StillThrowsArgumentException` — regression: existing guard
  unchanged.
- Boundary values: explicit unit tests for `v = 0` → output `255`, and `v = 255` → output
  `0`.

### Property-based tests (FsCheck)

Each property test uses `Prop.ForAll(...).QuickCheckThrowOnFailure()` or
`Check.One(Config.QuickThrowOnFailure.WithMaxTest(200), prop)` with a minimum of 100
iterations, consistent with the existing suite.

**Property 1 test** — `Property9_InversionCorrectness_OutputEquals255MinusRawValue`:
- Generator: random supported format, random width/height (4–32), random `ChannelComponent`,
  random pixel coordinate (x, y). Fill the image with deterministic test data (reuse
  `FillWithTestData`). Read `rawValue` via `PixelSampler.Sample`. Configure roughness via
  `WithRoughnessFromGloss`. Assert output B channel == `(byte)(255 - rawValue)`.
- Tag: `// Feature: omr-gloss-map-support, Property 1: InversionCorrectness`

**Property 2 test** — `Property10_DoubleInversionRoundTrip`:
- Generator: random byte value `v` in [0, 255].
- Assert `(byte)(255 - (byte)(255 - v)) == v`.
- This is a pure arithmetic property; no image I/O needed. Can be verified directly or
  through two combiner passes (create a 1×1 image with value `v`, run through
  `WithRoughnessFromGloss` twice via two combiners, verify final output == `v`).
- Tag: `// Feature: omr-gloss-map-support, Property 2: DoubleInversionRoundTrip`

**Property 3 test** — `Property11_InvertedImageChannel_UnsupportedFormat_ThrowsArgumentException`:
- Generator: unsupported formats (reuse `Generators.AllValidFormatsPublic` filtered to
  exclude `RGBA_UN8`, `R_UN8`, `BGRA_UN8`).
- Configure roughness with `WithRoughnessFromGloss` using that format. Assert
  `ArgumentException` is thrown.
- Tag: `// Feature: omr-gloss-map-support, Property 3: UnsupportedFormatThrows`

**Property 4 test** — `Property12_InvertedImageChannel_DimensionMismatch_ThrowsArgumentException`:
- Generator: two dimension pairs `(w1, h1)` and `(w2, h2)` where `w1 != w2 || h1 != h2`.
- Configure occlusion as `ImageChannel` with first image, roughness as `InvertedImageChannel`
  with second image. Assert `ArgumentException` is thrown.
- Tag: `// Feature: omr-gloss-map-support, Property 4: DimensionMismatchThrows`

**Property 5 test** — `Property13_InvertedImageChannel_NotTreatedAsConstant`:
- Generator: random supported format, random width/height. Configure at least one channel
  as `InvertedImageChannel`, others as `Constant`. Assert `Combine()` does not throw
  `ArgumentException` and output dimensions match the source.
- Tag: `// Feature: omr-gloss-map-support, Property 5: AllConstantGuardUnaffected`
