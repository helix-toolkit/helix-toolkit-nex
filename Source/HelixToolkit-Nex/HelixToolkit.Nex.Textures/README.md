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

| Type                          | Description                                                                                   |
|-------------------------------|-----------------------------------------------------------------------------------------------|
| `ChannelComponent`            | Enum identifying a single color channel within a pixel.                                       |
| `ChannelSource`               | Abstract class describing the source of a channel value for texture combination.               |
| `DDSCodec`                    | Internal static class for handling DDS image format encoding and decoding.                     |
| `IImageDecoder`               | Interface for pluggable image decoders/encoders.                                              |
| `Image`                       | Class representing a CPU-side container for pixel data.                                       |
| `ImageDescription`            | Struct describing the dimensions, format, and layout of a texture image.                      |
| `OmrTextureCombiner`          | Class for combining multiple PBR source images into a single OMR texture.                     |
| `PitchCalculator`             | Static utility for computing row pitch, slice pitch, and mipmap level counts.                 |
| `TextureCreator`              | Static utility for creating GPU textures from CPU-side image data.                            |

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
        TextureResource texture = TextureCreator.CreateTexture(context, image, "MyTexture");
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
        var (texture, uploadHandle) = TextureCreator.CreateTextureAsyncWithResource(context, image, "MyTexture");
        // Use texture and await uploadHandle if needed
    }
}
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
```
