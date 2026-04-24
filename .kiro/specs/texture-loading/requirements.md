# Requirements Document

## Introduction

This feature translates the SharpDX.Toolkit texture loading library to the HelixToolkit.Nex.Graphics API. The translated library provides CPU-side image loading, parsing, and GPU texture creation for 2D, 3D, and Cube textures with mipmap and array slice support. It lives in `HelixToolkit.Nex.Textures` with tests in `HelixToolkit.Nex.Textures.Tests`.

The source library handles DDS file parsing (including DX10 extended headers and legacy D3D9 format conversion), WIC-based image loading (PNG, JPG, BMP, GIF, TIFF), CPU-side pixel buffer management, pitch computation for compressed and uncompressed formats, and GPU texture creation from image data. The target API uses `IContext.CreateTexture`, `TextureDesc`, a smaller `Format` enum (no Texture1D), and supports async upload via `UploadAsync`.

## Glossary

- **Image**: CPU-side container holding pixel data for 2D, 3D, or Cube textures with mipmaps and array slices. Manages an unmanaged memory buffer and provides indexed access to individual pixel buffers.
- **PixelBuffer**: A single 2D slice of pixel data within an Image, described by width, height, format, row stride, buffer stride, and a data pointer.
- **ImageDescription**: Value type describing an Image's dimensions, format, mip levels, array size, and texture dimension.
- **TextureDimension**: Enum identifying the dimensionality of an image: Texture2D, Texture3D, or TextureCube. (Texture1D from the source is not supported in the target API.)
- **MipMapCount**: Value type wrapping a mipmap level count with implicit conversions from bool (true = all mipmaps) and int.
- **MipMapDescription**: Describes a single mipmap level's width, height, depth, row stride, depth stride, and total size.
- **DDS_Loader**: Component that parses DDS file headers (standard and DX10 extended) and decodes pixel data into an Image.
- **Format_Mapper**: Component that maps DXGI Format values from DDS files to the Nex `Format` enum.
- **WIC_Loader**: Component that decodes common image formats (PNG, JPG, BMP, GIF, TIFF) into an Image using platform-appropriate decoding.
- **Image_Loader_Registry**: Pluggable registry of loader/saver delegates keyed by image file type, allowing custom format handlers to be registered.
- **Texture_Creator**: Component that creates GPU `TextureResource` objects from Image data via `IContext`.
- **Pitch_Calculator**: Component that computes row pitch and slice pitch for both compressed (BC1–BC7) and uncompressed pixel formats.
- **Nex_Format**: The `HelixToolkit.Nex.Graphics.Format` enum, which has a smaller set of formats than DXGI.
- **TextureDesc**: Struct describing a GPU texture for creation via `IContext.CreateTexture`.
- **TextureResource**: Reference-counted GPU texture wrapper returned by `IContext.CreateTexture`.
- **AsyncUploadHandle**: Awaitable handle returned by `IContext.UploadAsync` for tracking async GPU upload completion.

## Requirements

### Requirement 1: Image Container

**User Story:** As a developer, I want a CPU-side image container that holds pixel data for 2D, 3D, and Cube textures with mipmaps and array slices, so that I can load, inspect, and manipulate texture data before uploading to the GPU.

#### Acceptance Criteria

1. THE Image SHALL store an ImageDescription containing dimension, width, height, depth, array size, mip levels, and Nex Format.
2. THE Image SHALL support TextureDimension values of Texture2D, Texture3D, and TextureCube.
3. WHEN a Texture1D ImageDescription is provided, THE Image SHALL treat the image as Texture2D with height equal to 1.
4. THE Image SHALL manage an unmanaged memory buffer that holds all pixel data contiguously.
5. THE Image SHALL provide indexed access to individual PixelBuffer instances by array-or-z-slice index and mipmap level.
6. THE Image SHALL provide indexed access to individual PixelBuffer instances by array index, z index, and mipmap level.
7. IF an out-of-range array index, z index, or mipmap level is provided, THEN THE Image SHALL throw an ArgumentException.
8. THE Image SHALL implement IDisposable and release unmanaged memory on disposal.
9. WHEN the Image owns the buffer (bufferIsDisposable is true), THE Image SHALL free the unmanaged memory on disposal.
10. WHEN the Image holds a pinned GCHandle, THE Image SHALL free the GCHandle on disposal.

### Requirement 2: Image Factory Methods

**User Story:** As a developer, I want factory methods to create images from descriptions or typed dimensions, so that I can allocate image buffers without manually computing sizes.

#### Acceptance Criteria

1. THE Image SHALL provide a static New method that creates an Image from an ImageDescription, allocating the required buffer.
2. THE Image SHALL provide a static New2D method that creates a 2D Image from width, height, MipMapCount, format, and optional array size.
3. THE Image SHALL provide a static NewCube method that creates a Cube Image from width, MipMapCount, and format, with array size fixed at 6.
4. THE Image SHALL provide a static New3D method that creates a 3D Image from width, height, depth, MipMapCount, and format.
5. THE Image SHALL provide overloads of New, New2D, NewCube, and New3D that accept an IntPtr to an existing buffer instead of allocating.
6. WHEN MipMapCount is set to true (value 0), THE Image SHALL calculate the full mipmap chain count based on the texture dimensions.
7. WHEN MipMapCount is set to a specific value greater than 1, THE Image SHALL validate that the value does not exceed the maximum possible mip levels for the given dimensions.
8. IF MipMapCount exceeds the maximum possible mip levels, THEN THE Image SHALL throw an InvalidOperationException.

### Requirement 3: Mipmap Level Calculation

**User Story:** As a developer, I want correct mipmap level calculation for all texture dimensions, so that mipmap chains are properly sized.

#### Acceptance Criteria

1. THE Pitch_Calculator SHALL compute the maximum mip level count for a 2D texture as 1 + floor(log2(max(width, height))).
2. THE Pitch_Calculator SHALL compute the maximum mip level count for a 3D texture as 1 + floor(log2(max(width, height, depth))).
3. THE Pitch_Calculator SHALL compute the size of a mipmap level as max(1, dimension >> mipLevel) for each dimension.
4. WHEN computing mip levels for a 3D texture with MipMapCount greater than 1, THE Pitch_Calculator SHALL require width, height, and depth to be powers of two.
5. IF a 3D texture dimension is not a power of two and MipMapCount is greater than 1, THEN THE Pitch_Calculator SHALL throw an InvalidOperationException.

### Requirement 4: Pitch Computation

**User Story:** As a developer, I want correct row pitch and slice pitch computation for both compressed and uncompressed formats, so that pixel buffer layouts match GPU expectations.

#### Acceptance Criteria

1. WHEN the format is a BCn compressed format (BC1 through BC7), THE Pitch_Calculator SHALL compute the row pitch as ceil(width / 4) multiplied by the block byte size (8 for BC1/BC4, 16 for BC2/BC3/BC5/BC6/BC7).
2. WHEN the format is a BCn compressed format, THE Pitch_Calculator SHALL compute the slice pitch as row pitch multiplied by ceil(height / 4).
3. WHEN the format is an uncompressed format, THE Pitch_Calculator SHALL compute the row pitch as (width * bitsPerPixel + 7) / 8.
4. WHEN the format is an uncompressed format, THE Pitch_Calculator SHALL compute the slice pitch as row pitch multiplied by height.
5. WHEN the legacy DWORD alignment flag is set, THE Pitch_Calculator SHALL compute the row pitch as ((width * bitsPerPixel + 31) / 32) * 4.

### Requirement 5: DDS Loading

**User Story:** As a developer, I want to load DDS files including DX10 extended headers and legacy D3D9 formats, so that I can use industry-standard compressed textures.

#### Acceptance Criteria

1. WHEN a DDS file with a valid magic number (0x20534444) is provided, THE DDS_Loader SHALL parse the standard DDS header and produce an ImageDescription.
2. WHEN a DDS file contains a DX10 extended header (FourCC = 'DX10'), THE DDS_Loader SHALL parse the extended header to determine format, dimension, and array size.
3. WHEN a DDS file uses a legacy D3D9 pixel format, THE DDS_Loader SHALL map the legacy format to the corresponding DXGI format using the legacy format table.
4. WHEN a DDS file describes a cubemap, THE DDS_Loader SHALL require all six faces to be present and set the dimension to TextureCube with array size 6.
5. WHEN a DDS file describes a 3D/volume texture, THE DDS_Loader SHALL set the dimension to Texture3D and read the depth from the header.
6. WHEN a DDS file describes a 1D texture (via DX10 header), THE DDS_Loader SHALL convert the dimension to Texture2D with height equal to 1.
7. IF the DDS header contains an invalid or unsupported pixel format, THEN THE DDS_Loader SHALL throw an InvalidOperationException.
8. IF the DDS data is too small to contain a valid header, THEN THE DDS_Loader SHALL return null.
9. THE DDS_Loader SHALL support legacy format conversions including 24-bit RGB expansion, 16-bit format expansion, palette expansion, and BGR/RGB swizzle.
10. THE DDS_Loader SHALL decode pixel data from the DDS body into the Image's contiguous pixel buffer, respecting mipmap and array slice layout.

### Requirement 6: DDS Saving

**User Story:** As a developer, I want to save Image data to DDS format, so that I can export textures for use in other tools.

#### Acceptance Criteria

1. WHEN an Image is saved to DDS format, THE DDS_Loader SHALL write a valid DDS header with magic number, header struct, and pixel format.
2. WHEN the Image has an array size greater than 1 (and is not a simple cubemap or 2D texture), THE DDS_Loader SHALL write a DX10 extended header.
3. THE DDS_Loader SHALL write pixel data for all array slices and mipmap levels in the correct DDS layout order.

### Requirement 7: DXGI-to-Nex Format Mapping

**User Story:** As a developer, I want a mapping from DXGI Format values (found in DDS files) to the Nex Format enum, so that loaded textures use the correct GPU format.

#### Acceptance Criteria

1. THE Format_Mapper SHALL map DXGI R8G8B8A8_UNorm to Nex RGBA_UN8.
2. THE Format_Mapper SHALL map DXGI R8G8B8A8_UNorm_SRgb to Nex RGBA_SRGB8.
3. THE Format_Mapper SHALL map DXGI B8G8R8A8_UNorm to Nex BGRA_UN8.
4. THE Format_Mapper SHALL map DXGI B8G8R8A8_UNorm_SRgb to Nex BGRA_SRGB8.
5. THE Format_Mapper SHALL map DXGI R8_UNorm to Nex R_UN8.
6. THE Format_Mapper SHALL map DXGI R16_UNorm to Nex R_UN16.
7. THE Format_Mapper SHALL map DXGI R16_Float to Nex R_F16.
8. THE Format_Mapper SHALL map DXGI R32_Float to Nex R_F32.
9. THE Format_Mapper SHALL map DXGI R8G8_UNorm to Nex RG_UN8.
10. THE Format_Mapper SHALL map DXGI R16G16_Float to Nex RG_F16.
11. THE Format_Mapper SHALL map DXGI R32G32_Float to Nex RG_F32.
12. THE Format_Mapper SHALL map DXGI R16G16B16A16_Float to Nex RGBA_F16.
13. THE Format_Mapper SHALL map DXGI R32G32B32A32_Float to Nex RGBA_F32.
14. THE Format_Mapper SHALL map DXGI BC7_UNorm to Nex BC7_RGBA.
15. THE Format_Mapper SHALL map DXGI R10G10B10A2_UNorm to Nex A2R10G10B10_UN.
16. IF a DXGI format has no corresponding Nex format, THEN THE Format_Mapper SHALL return Format.Invalid.
17. THE Format_Mapper SHALL provide a reverse mapping from Nex Format to DXGI format for DDS saving.

### Requirement 8: Image Loading from Multiple Sources

**User Story:** As a developer, I want to load images from streams, file paths, byte arrays, and unmanaged memory pointers, so that I can integrate with various data sources.

#### Acceptance Criteria

1. WHEN a Stream is provided, THE Image SHALL read the entire stream into memory and attempt to load using registered loaders.
2. WHEN a file path is provided, THE Image SHALL open the file, read its contents, and attempt to load using registered loaders.
3. WHEN a byte array is provided, THE Image SHALL pin large arrays (greater than 85KB) on the Large Object Heap instead of copying, and copy small arrays.
4. WHEN an unmanaged memory pointer and size are provided, THE Image SHALL attempt to load using registered loaders.
5. WHEN makeACopy is false and loading succeeds, THE Image SHALL take ownership of the provided unmanaged buffer.
6. WHEN makeACopy is true, THE Image SHALL copy the data to a new buffer and the caller retains ownership of the original.
7. IF no registered loader can decode the data, THEN THE Image SHALL return null.

### Requirement 9: Pluggable Loader/Saver Registry

**User Story:** As a developer, I want to register custom image loaders and savers by file type, so that I can extend the library with additional format support.

#### Acceptance Criteria

1. THE Image_Loader_Registry SHALL provide a Register method accepting an ImageFileType, a loader delegate, and a saver delegate.
2. WHEN a loader is registered for a file type that already has a loader, THE Image_Loader_Registry SHALL replace the existing loader.
3. WHEN loading an image, THE Image_Loader_Registry SHALL iterate through registered loaders in order until one succeeds or all return null.
4. IF both loader and saver are null, THEN THE Image_Loader_Registry SHALL throw an ArgumentNullException.
5. THE Image_Loader_Registry SHALL register the DDS_Loader by default at initialization.

### Requirement 10: WIC/Cross-Platform Image Loading

**User Story:** As a developer, I want to load common image formats (PNG, JPG, BMP, GIF, TIFF) using a cross-platform approach, so that the library works beyond Windows.

#### Acceptance Criteria

1. THE WIC_Loader SHALL decode PNG, JPG, BMP, GIF, and TIFF images into Image objects with Texture2D dimension.
2. THE WIC_Loader SHALL convert source pixel formats to the nearest supported Nex Format during decoding.
3. WHEN a multi-frame image (GIF, TIFF) is loaded with the AllFrames flag, THE WIC_Loader SHALL decode all frames into an Image with array size equal to the frame count.
4. WHEN a frame's dimensions differ from the first frame, THE WIC_Loader SHALL resize the frame to match the first frame's dimensions.
5. IF the source pixel format is not directly supported, THEN THE WIC_Loader SHALL convert to the nearest supported format using a conversion table.
6. THE WIC_Loader SHALL be implemented behind an interface to allow platform-specific or third-party decoder implementations.

### Requirement 11: GPU Texture Creation from Image

**User Story:** As a developer, I want to create GPU TextureResource objects from loaded Image data, so that I can use textures for rendering.

#### Acceptance Criteria

1. WHEN an Image with Texture2D dimension is provided, THE Texture_Creator SHALL create a TextureResource via IContext.CreateTexture with TextureType.Texture2D.
2. WHEN an Image with Texture3D dimension is provided, THE Texture_Creator SHALL create a TextureResource via IContext.CreateTexture with TextureType.Texture3D.
3. WHEN an Image with TextureCube dimension is provided, THE Texture_Creator SHALL create a TextureResource via IContext.CreateTexture with TextureType.TextureCube.
4. THE Texture_Creator SHALL populate TextureDesc with the correct Format, Dimensions, NumLayers, NumMipLevels, Usage, and Storage from the Image.
5. THE Texture_Creator SHALL set TextureDesc.Data and DataSize to point to the Image's contiguous pixel buffer for initial data upload.
6. THE Texture_Creator SHALL default to TextureUsageBits.Sampled for the texture usage.
7. THE Texture_Creator SHALL default to StorageType.Device for the texture storage.

### Requirement 12: Async GPU Texture Upload

**User Story:** As a developer, I want to upload texture data to the GPU asynchronously, so that texture loading does not block the render thread.

#### Acceptance Criteria

1. THE Texture_Creator SHALL provide an async upload method that returns an AsyncUploadHandle of TextureHandle.
2. WHEN async upload is requested, THE Texture_Creator SHALL create the texture with IContext.CreateTexture (without initial data) and then call IContext.UploadAsync with the pixel data.
3. THE AsyncUploadHandle SHALL be awaitable and provide IsCompleted for polling.
4. WHEN the upload completes, THE AsyncUploadHandle SHALL contain the ResultCode and the TextureHandle.
5. THE Texture_Creator SHALL provide a convenience method that loads from a Stream and returns an AsyncUploadHandle.

### Requirement 13: PixelBuffer Type

**User Story:** As a developer, I want a PixelBuffer type that provides typed access to a 2D slice of pixel data, so that I can read and write individual pixels.

#### Acceptance Criteria

1. THE PixelBuffer SHALL store width, height, Nex Format, row stride, buffer stride, and a data pointer.
2. THE PixelBuffer SHALL provide generic GetPixel and SetPixel methods for typed pixel access by x, y coordinates.
3. THE PixelBuffer SHALL provide generic GetPixels and SetPixels methods for scanline-based bulk access.
4. THE PixelBuffer SHALL provide a CopyTo method that copies pixel data to another PixelBuffer of the same dimensions and format, handling differing row strides.
5. IF the data pointer is IntPtr.Zero, THEN THE PixelBuffer constructor SHALL throw an ArgumentException.

### Requirement 14: MipMapCount Type

**User Story:** As a developer, I want a MipMapCount value type with implicit conversions, so that mipmap specification is ergonomic.

#### Acceptance Criteria

1. THE MipMapCount SHALL store an integer Count where 0 means generate all mipmaps and 1 means a single level.
2. THE MipMapCount SHALL provide implicit conversion from bool (true = all mipmaps, false = single mipmap).
3. THE MipMapCount SHALL provide implicit conversion from int.
4. THE MipMapCount SHALL provide implicit conversion to int.
5. IF a negative count is provided, THEN THE MipMapCount constructor SHALL throw an ArgumentException.

### Requirement 15: MipMapDescription Type

**User Story:** As a developer, I want a MipMapDescription type that describes a single mipmap level, so that I can query mipmap properties.

#### Acceptance Criteria

1. THE MipMapDescription SHALL store Width, Height, Depth, RowStride, DepthStride, MipmapSize, WidthPacked, and HeightPacked.
2. THE MipMapDescription SHALL compute MipmapSize as DepthStride multiplied by Depth.
3. THE MipMapDescription SHALL implement value equality (IEquatable, ==, !=, GetHashCode).

### Requirement 16: ImageDescription Type

**User Story:** As a developer, I want an ImageDescription value type that fully describes an image's layout, so that images can be created and validated from descriptions.

#### Acceptance Criteria

1. THE ImageDescription SHALL contain Dimension, Width, Height, Depth, ArraySize, MipLevels, and Nex Format fields.
2. THE ImageDescription SHALL implement value equality (IEquatable, ==, !=, GetHashCode).
3. THE ImageDescription SHALL provide a ToString method that includes all field values.

### Requirement 17: DDS Round-Trip

**User Story:** As a developer, I want DDS load-then-save to produce equivalent output, so that I can trust the DDS codec is correct.

#### Acceptance Criteria

1. FOR ALL valid DDS files with formats mappable to Nex Format, loading then saving then loading SHALL produce an Image with an equivalent ImageDescription.
2. FOR ALL valid DDS files with formats mappable to Nex Format, loading then saving then loading SHALL produce pixel buffers with identical byte content.
