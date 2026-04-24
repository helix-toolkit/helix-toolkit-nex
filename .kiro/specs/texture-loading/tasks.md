# Implementation Plan: Texture Loading

## Overview

Port the SharpDX.Toolkit texture loading library to `HelixToolkit.Nex.Textures`, targeting the `HelixToolkit.Nex.Graphics` abstraction layer. Implementation proceeds bottom-up: data types → pitch/format utilities → CPU image container → DDS codec → loader registry → GPU texture creation. Property-based tests use FsCheck; unit tests use MSTest (already configured).

## Tasks

- [x] 1. Add FsCheck to the test project and define shared generators
  - Add `FsCheck` and `FsCheck.MsTest` NuGet packages to `HelixToolkit.Nex.Textures.Tests.csproj`
  - Create `Generators.cs` in the test project with `Arb`-registered generators:
    - `ValidImageDescription` — random dimensions (1–4096), valid Nex `Format` values, valid mip levels, array sizes
    - `ValidPixelBufferParams` — random width/height (1–512), uncompressed format, correct strides
    - `PositiveDimension` — random positive integers (1–8192)
    - `NexFormatArb` — arbitrary from the set of valid `Format` enum values (excluding `Invalid`)
    - `UncompressedFormatArb` — subset of `NexFormatArb` for uncompressed formats only
  - Create `TestBase.cs` that registers all custom `Arb` instances via `[AssemblyInitialize]`
  - _Requirements: test infrastructure for all property tests_

- [x] 2. Implement core value types
  - [x] 2.1 Implement `TextureDimension` enum
    - Create `TextureDimension.cs` with values `Texture2D`, `Texture3D`, `TextureCube`
    - _Requirements: 1.2_

  - [x] 2.2 Implement `MipMapCount` struct
    - Create `MipMapCount.cs` with `Count` field, `Auto` static, constructors (bool, int), implicit conversions (bool↔MipMapCount, int↔MipMapCount)
    - Throw `ArgumentException` for negative count
    - _Requirements: 14.1, 14.2, 14.3, 14.4, 14.5_

  - [x]* 2.3 Write property test for `MipMapCount` int round-trip
    - **Property 13: MipMapCount int conversion round-trip**
    - **Validates: Requirements 14.3, 14.4**
    - `// Feature: texture-loading, Property 13: For any non-negative integer n, (int)(MipMapCount)n == n`

  - [x] 2.4 Implement `ImageDescription` struct
    - Create `ImageDescription.cs` with fields `Dimension`, `Width`, `Height`, `Depth`, `ArraySize`, `MipLevels`, `Format`
    - Implement `IEquatable<ImageDescription>`, `==`, `!=`, `GetHashCode`, `ToString` (all fields)
    - _Requirements: 16.1, 16.2, 16.3_

  - [x] 2.5 Implement `MipMapDescription` class
    - Create `MipMapDescription.cs` with readonly fields `Width`, `Height`, `Depth`, `RowStride`, `DepthStride`, `MipmapSize`, `WidthPacked`, `HeightPacked`
    - Compute `MipmapSize = DepthStride * Depth` in constructor
    - Implement `IEquatable<MipMapDescription>`, `==`, `!=`, `GetHashCode`
    - _Requirements: 15.1, 15.2, 15.3_

  - [x]* 2.6 Write property test for `MipMapDescription.MipmapSize` invariant
    - **Property 14: MipMapDescription.MipmapSize invariant**
    - **Validates: Requirements 15.2**
    - `// Feature: texture-loading, Property 14: For any MipMapDescription, MipmapSize == DepthStride * Depth`

  - [x] 2.7 Implement `ImageFileType` enum and `PitchFlags` enum
    - Create `ImageFileType.cs` with values `Dds`, `Png`, `Gif`, `Jpg`, `Bmp`, `Tiff`, `Wmp`, `Tga`
    - Create `PitchFlags.cs` (internal) with `None`, `LegacyDword`, `Bpp24`, `Bpp16`, `Bpp8`
    - _Requirements: supporting types for Req 4, 5, 6_

- [x] 3. Implement `PitchCalculator`
  - [x] 3.1 Implement `PitchCalculator` static class
    - Create `PitchCalculator.cs` with `ComputePitch`, `CalculateMipLevels` (1D/2D/3D overloads), `CalculateMipSize`, `CountMips` (2D and 3D overloads)
    - BCn compressed pitch: `rowPitch = max(1, (width+3)/4) * blockSize`, `slicePitch = rowPitch * max(1, (height+3)/4)`
    - Uncompressed pitch: `rowPitch = (width * bpp + 7) / 8`, `slicePitch = rowPitch * height`
    - LegacyDword pitch: `rowPitch = ((width * bpp + 31) / 32) * 4`
    - `CountMips(w, h)` = `1 + floor(log2(max(w, h)))`, `CountMips(w, h, d)` = `1 + floor(log2(max(w, h, d)))`
    - `CalculateMipSize(dim, level)` = `max(1, dim >> level)`
    - Throw `InvalidOperationException` for non-power-of-two 3D dimensions with mip count > 1
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 4.1, 4.2, 4.3, 4.4, 4.5_

  - [x]* 3.2 Write property test for `CountMips` logarithmic formula
    - **Property 4: CountMips matches logarithmic formula**
    - **Validates: Requirements 3.1, 3.2**
    - `// Feature: texture-loading, Property 4: For any positive width and height, CountMips(w,h) == 1 + floor(log2(max(w,h)))`

  - [x]* 3.3 Write property test for `CalculateMipSize` halving
    - **Property 5: CalculateMipSize halves dimensions**
    - **Validates: Requirements 3.3**
    - `// Feature: texture-loading, Property 5: For any positive dimension and non-negative mipLevel, CalculateMipSize(dim, level) == max(1, dim >> level)`

  - [x]* 3.4 Write property test for BCn compressed pitch computation
    - **Property 6: BCn compressed pitch computation**
    - **Validates: Requirements 4.1, 4.2**
    - `// Feature: texture-loading, Property 6: For any BCn format and positive width/height, rowPitch = max(1,(w+3)/4)*blockSize and slicePitch = rowPitch*max(1,(h+3)/4)`

  - [x]* 3.5 Write property test for uncompressed pitch computation
    - **Property 7: Uncompressed pitch computation**
    - **Validates: Requirements 4.3, 4.4**
    - `// Feature: texture-loading, Property 7: For any uncompressed format and positive width/height, rowPitch = (w*bpp+7)/8 and slicePitch = rowPitch*height`

  - [x]* 3.6 Write property test for DWORD-aligned pitch computation
    - **Property 8: DWORD-aligned pitch computation**
    - **Validates: Requirements 4.5**
    - `// Feature: texture-loading, Property 8: For any uncompressed format and positive width, with LegacyDword flag, rowPitch = ((w*bpp+31)/32)*4`

- [x] 4. Implement `DxgiFormat` enum and `FormatMapper`
  - [x] 4.1 Implement internal `DxgiFormat` enum
    - Create `DxgiFormat.cs` (internal) mirroring the DXGI format values needed for DDS parsing as listed in the design document
    - _Requirements: 7.1–7.17 (supporting type)_

  - [x] 4.2 Implement `FormatMapper` static class
    - Create `FormatMapper.cs` with `DxgiToNex(DxgiFormat)`, `NexToDxgi(Format)`, `IsSupported(DxgiFormat)`
    - Implement the full mapping table from the design document (15 explicit mappings; all others → `Format.Invalid` / `DxgiFormat.Unknown`)
    - _Requirements: 7.1–7.17_

  - [x]* 4.3 Write property test for `FormatMapper` round-trip
    - **Property 9: Format mapper round-trip**
    - **Validates: Requirements 7.16, 7.17**
    - `// Feature: texture-loading, Property 9: For any Nex Format with a valid DXGI mapping, DxgiToNex(NexToDxgi(f)) == f`

  - [x] 4.4 Write unit tests for all explicit format mappings
    - One test per mapping pair (Requirements 7.1–7.15): verify `DxgiToNex` and `NexToDxgi` for each of the 15 mapped formats
    - Verify unmapped DXGI formats return `Format.Invalid`
    - _Requirements: 7.1–7.15, 7.16_

- [x] 5. Checkpoint — Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 6. Implement `PixelBuffer`
  - [x] 6.1 Implement `PixelBuffer` class
    - Create `PixelBuffer.cs` with properties `Width`, `Height`, `Format`, `PixelSize`, `RowStride`, `BufferStride`, `DataPointer`
    - Implement `GetPixel<T>`, `SetPixel<T>`, `GetPixels<T>`, `SetPixels<T>` using `unsafe` pointer arithmetic
    - Implement `CopyTo(PixelBuffer destination)` handling differing row strides
    - Throw `ArgumentException` if `DataPointer` is `IntPtr.Zero`
    - Throw `ArgumentException` in `CopyTo` for mismatched dimensions or format
    - Throw `ArgumentException` in `Format` setter if new format has different pixel size
    - _Requirements: 13.1, 13.2, 13.3, 13.4, 13.5_

  - [x]* 6.2 Write property test for `PixelBuffer` SetPixel/GetPixel round-trip
    - **Property 11: PixelBuffer SetPixel/GetPixel round-trip**
    - **Validates: Requirements 13.2, 13.3**
    - `// Feature: texture-loading, Property 11: For any valid PixelBuffer, uncompressed format, valid (x,y), SetPixel then GetPixel returns original value`

  - [x]* 6.3 Write property test for `PixelBuffer.CopyTo` preserves pixel data
    - **Property 12: PixelBuffer CopyTo preserves pixel data**
    - **Validates: Requirements 13.4**
    - `// Feature: texture-loading, Property 12: For any two PixelBuffers with same dimensions and format, after CopyTo all pixels are equal`

- [x] 7. Implement `Image` container and factory methods
  - [x] 7.1 Implement `Image` class — core container
    - Create `Image.cs` with `Description`, `TotalSizeInBytes`, `DataPointer` properties
    - Implement unmanaged buffer allocation and `IDisposable` (free buffer when owned, free `GCHandle` when held)
    - Implement `GetPixelBuffer(arrayOrZSliceIndex, mipmap)` and `GetPixelBuffer(arrayIndex, zIndex, mipmap)` with bounds checking
    - Implement `GetMipMapDescription(mipmap)` using `PitchCalculator`
    - Throw `ArgumentException` for out-of-range indices
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 1.7, 1.8, 1.9, 1.10_

  - [x] 7.2 Implement `Image` factory methods
    - Implement `New(ImageDescription)`, `New(ImageDescription, IntPtr)`
    - Implement `New2D`, `NewCube`, `New3D` with and without `IntPtr` overloads
    - Implement `MipMapCount.Auto` resolution: compute full mip chain from dimensions via `PitchCalculator.CountMips`
    - Validate mip count does not exceed maximum; throw `InvalidOperationException` if exceeded
    - Validate cube array size is multiple of 6; validate 3D power-of-two constraint when mips > 1
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 2.7, 2.8_

  - [x]* 7.3 Write property test for image creation preserving description
    - **Property 1: Image creation preserves description**
    - **Validates: Requirements 1.1, 2.1**
    - `// Feature: texture-loading, Property 1: For any valid ImageDescription, Image.New(desc).Description equals the input description`

  - [x]* 7.4 Write property test for PixelBuffer mip-level dimensions
    - **Property 2: PixelBuffer access returns correct mip-level dimensions**
    - **Validates: Requirements 1.5, 1.6**
    - `// Feature: texture-loading, Property 2: For any valid Image and valid (arrayOrZSlice, mipLevel), GetPixelBuffer returns buffer with correct halved dimensions`

  - [x]* 7.5 Write property test for out-of-range pixel buffer access throws
    - **Property 3: Out-of-range pixel buffer access throws**
    - **Validates: Requirements 1.7**
    - `// Feature: texture-loading, Property 3: For any valid Image and any out-of-range index tuple, GetPixelBuffer throws ArgumentException`

  - [x] 7.6 Write unit tests for `Image` factory methods
    - Test `New2D`, `NewCube`, `New3D` produce correct `ImageDescription`
    - Test `MipMapCount.Auto` resolves to correct full mip chain count
    - Test Texture1D promotion to Texture2D with height=1 (Req 1.3)
    - Test cube array size validation (must be multiple of 6)
    - Test `IDisposable` releases unmanaged memory (Req 1.8, 1.9, 1.10)
    - _Requirements: 1.3, 2.2, 2.3, 2.4, 2.6, 2.7, 2.8_

- [x] 8. Checkpoint — Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 9. Implement `DDSCodec` (loading)
  - [x] 9.1 Implement DDS header structs and magic number constants
    - Create `DDSHeader.cs` (internal) with `DDS_HEADER`, `DDS_HEADER_DXT10`, `DDS_PIXELFORMAT` structs using `unsafe` fixed-size fields
    - Define magic number `0x20534444` and all relevant flag constants (DDSD_*, DDPF_*, DDSCAPS2_*)
    - _Requirements: 5.1, 5.2_

  - [x] 9.2 Implement `DDSCodec.LoadFromMemory`
    - Create `DDSCodec.cs` (internal static class)
    - Parse standard DDS header: validate magic, header size, pixel format size
    - Parse DX10 extended header when FourCC = `'DX10'`
    - Map legacy D3D9 pixel formats to `DxgiFormat` via legacy format table
    - Convert `DxgiFormat` to Nex `Format` via `FormatMapper`
    - Handle cubemap (require all 6 faces, set `TextureCube`), volume texture (`Texture3D`), 1D→2D promotion
    - Decode pixel data into contiguous `Image` buffer respecting mipmap and array slice layout
    - Return `null` for data too small or invalid magic; throw `InvalidOperationException` for unsupported formats
    - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5, 5.6, 5.7, 5.8, 5.9, 5.10_

  - [x] 9.3 Write unit tests for DDS loading error paths
    - Test: data too small → returns `null`
    - Test: invalid magic number → returns `null`
    - Test: invalid pixel format → throws `InvalidOperationException`
    - Test: DX10 header with `ArraySize == 0` → throws
    - Test: cubemap without all 6 faces → throws
    - Test: Texture3D with `ArraySize > 1` → throws
    - _Requirements: 5.7, 5.8_

- [x] 10. Implement `DDSCodec` (saving)
  - [x] 10.1 Implement `DDSCodec.SaveToStream`
    - Write DDS magic number, `DDS_HEADER`, and `DDS_PIXELFORMAT` from `ImageDescription`
    - Write DX10 extended header when array size > 1 (and not a simple cubemap or 2D)
    - Write pixel data for all array slices and mipmap levels in correct DDS layout order
    - _Requirements: 6.1, 6.2, 6.3_

- [x] 11. Implement `IImageDecoder` interface and `ImageLoaderRegistry`
  - [x] 11.1 Implement `IImageDecoder` interface
    - Create `IImageDecoder.cs` with `Decode(IntPtr, int, bool, GCHandle?)` and `Save(PixelBuffer[], int, ImageDescription, Stream)` methods
    - _Requirements: 10.6_

  - [x] 11.2 Implement `ImageLoaderRegistry` (internal, used by `Image`)
    - Create `ImageLoaderRegistry.cs` with `Register(ImageFileType, ImageLoadDelegate?, ImageSaveDelegate?)` method
    - Throw `ArgumentNullException` if both loader and saver are null
    - Replace existing loader/saver when re-registering the same `ImageFileType`
    - Iterate loaders in registration order; return first non-null result
    - Register `DDSCodec` as the default loader/saver at initialization
    - _Requirements: 9.1, 9.2, 9.3, 9.4, 9.5_

- [x] 12. Implement `Image` loading and saving entry points
  - [x] 12.1 Implement `Image.Load` overloads
    - `Load(Stream)`: read entire stream into byte array, then load from memory
    - `Load(string fileName)`: open file, read contents, load from memory
    - `Load(byte[])`: pin arrays > 85KB on LOH; copy small arrays
    - `Load(IntPtr, int, bool makeACopy)`: dispatch to `ImageLoaderRegistry`; handle ownership per `makeACopy`
    - Return `null` if no loader succeeds
    - _Requirements: 8.1, 8.2, 8.3, 8.4, 8.5, 8.6, 8.7_

  - [x] 12.2 Implement `Image.Save` overloads and `Image.Register`
    - `Save(Stream, ImageFileType)`: dispatch to registered saver
    - `Save(string fileName, ImageFileType)`: open file stream, delegate to `Save(Stream, ...)`
    - `Register(ImageFileType, ImageLoadDelegate?, ImageSaveDelegate?)`: delegate to `ImageLoaderRegistry`
    - _Requirements: 6.1, 9.1_

  - [x] 12.3 Write unit tests for `Image.Load` source variants
    - Test loading from `Stream`, file path, byte array, and `IntPtr`
    - Test `makeACopy = false` transfers ownership; `makeACopy = true` copies
    - Test no-loader-matches returns `null`
    - Test `Register` with both null delegates throws `ArgumentNullException`
    - _Requirements: 8.1–8.7, 9.4_

- [x] 13. Checkpoint — Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 14. Implement DDS round-trip property test
  - [ ]* 14.1 Write property test for DDS round-trip
    - **Property 15: DDS round-trip preserves description and pixel data**
    - **Validates: Requirements 17.1, 17.2**
    - Generate random valid `Image` instances whose format maps to a DXGI format; save to `MemoryStream`; reload; assert equivalent `ImageDescription` and byte-identical pixel buffers
    - `// Feature: texture-loading, Property 15: For any valid Image with mappable format, save-then-load produces equivalent ImageDescription and identical pixel data`

- [x] 15. Implement `TextureCreator`
  - [x] 15.1 Implement `TextureCreator` static class — synchronous path
    - Create `TextureCreator.cs`
    - Implement `CreateTexture(IContext, Image, string?)`:
      - Map `ImageDescription.Dimension` to `TextureType` (Texture2D/3D/Cube)
      - Map `Format` via `FormatMapper.NexToDxgi` in reverse (format is already Nex, pass directly)
      - Populate `TextureDesc` with `Format`, `Dimensions`, `NumLayers`, `NumMipLevels`, `Usage = Sampled`, `Storage = Device`
      - Set `TextureDesc.Data` and `DataSize` to the Image's contiguous pixel buffer
      - Throw `InvalidOperationException` if `Image.Description.Format == Format.Invalid`
      - Call `IContext.CreateTexture`; propagate `ResultCode` via `CheckResult()`
    - Implement `CreateTextureFromStream(IContext, Stream, string?)`: load image then call `CreateTexture`
    - _Requirements: 11.1, 11.2, 11.3, 11.4, 11.5, 11.6, 11.7_

  - [x] 15.2 Implement `TextureCreator` async upload path
    - Implement `CreateTextureAsync(IContext, Image, string?)`:
      - Create texture via `IContext.CreateTexture` without initial data (`Data = IntPtr.Zero`)
      - Call `IContext.UploadAsync` with pixel buffer data for each mip/layer
      - Return `AsyncUploadHandle<TextureHandle>`
    - Implement `CreateTextureFromStreamAsync(IContext, Stream, string?)`: load image then call `CreateTextureAsync`
    - _Requirements: 12.1, 12.2, 12.3, 12.4, 12.5_

  - [ ]* 15.3 Write property test for `TextureDesc` mapping from `Image`
    - **Property 10: TextureDesc mapping from Image**
    - **Validates: Requirements 11.1, 11.2, 11.3, 11.4, 11.5**
    - Use a mock `IContext` that captures the `TextureDesc` passed to `CreateTexture`; generate random valid `Image` instances; assert all `TextureDesc` fields match the `ImageDescription`
    - `// Feature: texture-loading, Property 10: For any valid Image, CreateTexture produces TextureDesc with matching Type, Format, Dimensions, NumLayers, NumMipLevels`

  - [x] 15.4 Write unit tests for `TextureCreator`
    - Test default `Usage = Sampled` and `Storage = Device` (Req 11.6, 11.7)
    - Test `Format.Invalid` image throws `InvalidOperationException` before calling `IContext`
    - Test async path creates texture without data then calls `UploadAsync`
    - _Requirements: 11.6, 11.7, 12.1, 12.2_

- [x] 16. Final checkpoint — Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for a faster MVP
- Property tests require FsCheck (added in Task 1) and run a minimum of 100 iterations each
- Each property test comment references the design document property number and text for traceability
- The `WicImageDecoder` implementation (Req 10) is intentionally omitted from this plan — it requires a platform-specific or third-party decoder (e.g., ImageSharp) and can be added as a follow-up once the core library is in place
- The mock `IContext` needed for `TextureCreator` tests can be sourced from `HelixToolkit.Nex.Graphics.Mock` if available, or implemented inline in the test project
- All `unsafe` code lives in `HelixToolkit.Nex.Textures` which already has `AllowUnsafeBlocks = true`
