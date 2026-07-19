```markdown
# HelixToolkit.Nex.Textures

The `HelixToolkit.Nex.Textures` package is a comprehensive suite for handling texture data within the HelixToolkit-Nex 3D graphics engine. It provides functionality for loading, manipulating, and saving various image formats, as well as creating GPU textures from CPU-side image data. This package is essential for developers working with textures in 3D graphics applications using the HelixToolkit-Nex engine.

## Overview

The `HelixToolkit.Nex.Textures` package is responsible for:
- Loading and saving image data in multiple formats (e.g., DDS, PNG, JPG).
- Managing pixel data with the `Image` class, which acts as a container for texture data.
- Providing utilities for creating GPU textures from images.
- Supporting operations like mipmap generation and texture dimension management.
- Offering a fluent API for combining multiple images into a single texture using the `OmrTextureCombiner`.

This package integrates seamlessly with the HelixToolkit-Nex engine, leveraging its ECS architecture and rendering capabilities to manage textures efficiently.

## Key Types

| Type                 | Description                                                                      |
| -------------------- | -------------------------------------------------------------------------------- |
| `ChannelComponent`   | Enum identifying a single color channel within a pixel.                          |
| `ChannelSource`      | Abstract class describing the source of a channel value for texture combination. |
| `DDSCodec`           | Internal static class for handling DDS image format encoding and decoding.       |
| `IImageDecoder`      | Interface for pluggable image decoders/encoders.                                 |
| `Image`              | Class representing a CPU-side container for pixel data.                          |
| `ImageDescription`   | Struct describing the dimensions, format, and layout of a texture image.         |
| `OmrTextureCombiner` | Class for combining multiple PBR source images into a single OMR texture.        |
| `PitchCalculator`    | Static utility for computing row pitch, slice pitch, and mipmap level counts.    |
| `TextureCreator`     | Static utility for creating GPU textures from CPU-side image data.               |

## Usage Examples

### Loading and Saving Images

```csharp
// Load an image from a file
Image? image = Image.Load("texture.png");

// Save the image to a different format
if (image != null)
{
    image.Save("texture.dds", ImageFileType.Dds);
}
```

### Creating a GPU Texture

```csharp
// Assume 'context' is a valid IContext instance
Image? image = Image.Load("texture.png");
if (image != null)
{
    using (image)
    {
        TextureResource texture = TextureCreator.CreateTexture(context, image, generateMipmaps: true, "MyTexture");
    }
}
```

### Creating a GPU Texture Asynchronously

```csharp
// Assume 'context' is a valid IContext instance
Image? image = Image.Load("texture.png");
if (image != null)
{
    using (image)
    {
        var (result, texture, uploadHandle) = TextureCreator.CreateTextureAsyncWithResource(context, image, generateMipmaps: true, "MyTexture");
        if (result == ResultCode.Ok)
        {
            // Use texture and await uploadHandle if needed
        }
    }
}
```

### Cubemaps and Face Ordering

Cubemaps (`TextureDimension.TextureCube`, `ArraySize = 6`) store their six faces as array
slices `0..5`. Both the DDS codec and `Image.NewCube(faces)` use — and require — the standard
**D3D/Vulkan cube face order**, which is preserved verbatim through the whole pipeline (no
reordering or flipping happens on load, save, or GPU upload):

| Slice | Face  | Skybox name |
| ----- | ----- | ----------- |
| 0     | `+X`  | right       |
| 1     | `-X`  | left        |
| 2     | `+Y`  | top         |
| 3     | `-Y`  | bottom      |
| 4     | `+Z`  | front       |
| 5     | `-Z`  | back        |

**A cubemap `.dds` must store its faces in this order.** DDS files authored by standard tooling
(e.g. `texassemble`, `texconv`, NVIDIA Texture Tools) already follow it, so a correctly authored
cube DDS loads with no remapping. If a cubemap looks scrambled, the faces are in the wrong slots;
if it looks merely mirrored, upside-down, or front/back-swapped, that is an orientation
(handedness) issue corrected at sample time — see the `FlipX`/`FlipY`/`FlipZ` options on
`EnvironmentMapConfig` — not a face-ordering problem.

```csharp
// Assemble a cube Image from six square, equally sized faces in +X,-X,+Y,-Y,+Z,-Z order.
Image[] faces =
[
    Image.Load("right.png")!,  // +X
    Image.Load("left.png")!,   // -X
    Image.Load("top.png")!,    // +Y
    Image.Load("bottom.png")!, // -Y
    Image.Load("front.png")!,  // +Z
    Image.Load("back.png")!,   // -Z
];
using Image cube = Image.NewCube(faces);
cube.Save("skybox.dds", ImageFileType.Dds); // written back in the same face order
```

### Combining Textures

```csharp
var combiner = new OmrTextureCombiner()
    .WithOcclusion("occlusion.png", ChannelComponent.R)
    .WithMetallic("metallic.png", ChannelComponent.G)
    .WithRoughnessFromGloss("gloss.png", ChannelComponent.B);

Image combinedImage = combiner.Combine();
combinedImage.Save("combined_omr.png", ImageFileType.Png);
```

## Architecture Notes

- **Design Patterns**: The package uses a fluent builder pattern for the `OmrTextureCombiner` to facilitate easy configuration of texture channels.
- **Dependencies**: It relies on the `HelixToolkit.Nex.Graphics` package for GPU texture creation and management.
- **Integration**: The package is designed to work within the HelixToolkit-Nex engine's ECS architecture, allowing for efficient texture management and rendering.
- **Performance**: Utilizes GPU-based operations for efficient texture processing, including mipmap generation and texture uploads.

## Build Configurations

The project now supports additional build configurations for Linux:
- `LinuxDebug`
- `LinuxRelease`

These configurations allow for building and testing the package on Linux environments, expanding the versatility and deployment options for developers using the HelixToolkit-Nex engine.
```
