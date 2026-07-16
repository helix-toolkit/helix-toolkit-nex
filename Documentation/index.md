# Helix Toolkit NEX Documentation

Welcome to the Helix Toolkit NEX documentation. This is a modern, high-performance 3D graphics toolkit for .NET 8.

## Overview

Helix Toolkit NEX is a collection of libraries for 3D graphics programming using Vulkan as the rendering backend.

### Core & Foundation

- **[HelixToolkit.Nex](../Source/HelixToolkit-Nex/HelixTookit.Nex/README.md)** - Core 3D graphics engine leveraging the Vulkan API, with Reverse-Z, Forward Plus light culling, and GPU-based culling.
- **[HelixToolkit.Nex.Maths](../Source/HelixToolkit-Nex/HelixToolkit.Nex.Maths/README.md)** - Mathematics utilities: vectors, matrices, colors, bounding volumes, and collision detection.
- **[HelixToolkit.Nex.ECS](../Source/HelixToolkit-Nex/HelixToolkit.Nex.EntityComponentSystem/README.md)** - Data-oriented Entity Component System with isolated worlds, an event bus, and a deferred command buffer.
- **[HelixToolkit.Nex.Repository](../Source/HelixToolkit-Nex/HelixToolkit.Nex.Repository/README.md)** - Thread-safe LRU cache for GPU resources such as textures, shaders, and samplers.

### Graphics

- **[HelixToolkit.Nex.Graphics](../Source/HelixToolkit-Nex/HelixToolkit.Nex.Graphics/README.md)** - Core graphics abstraction for GPU resources, command buffers, and rendering operations.
- **[HelixToolkit.Nex.Graphics.Vulkan](../Source/HelixToolkit-Nex/HelixToolkit.Nex.Graphics.Vulkan/README.md)** - Vulkan implementation of the graphics abstraction layer.
- **[HelixToolkit.Nex.Shaders](../Source/HelixToolkit-Nex/HelixToolkit.Nex.Shaders/README.md)** - Shader creation, compilation, and caching for advanced rendering techniques.
- **[HelixToolkit.Nex.CodeGen](../Source/HelixToolkit-Nex/HelixToolkit.Nex.CodeGen/README.md)** - Source generators that convert GLSL definitions into C# structs and observable properties.

### Scene & Rendering

- **[HelixToolkit.Nex.Engine](../Source/HelixToolkit-Nex/HelixToolkit.Nex.Engine/README.md)** - Engine managing the rendering pipeline and scene graph, including cameras, lighting, and meshes.
- **[HelixToolkit.Nex.Scene](../Source/HelixToolkit-Nex/HelixToolkit.Nex.Scene/README.md)** - Scene-graph node management with hierarchical transforms, built on the ECS framework.
- **[HelixToolkit.Nex.Rendering](../Source/HelixToolkit-Nex/HelixToolkit.Nex.Rendering/README.md)** - High-level rendering with Forward Plus lighting, GPU culling, and post-processing.
- **[HelixToolkit.Nex.Material](../Source/HelixToolkit-Nex/HelixToolkit.Nex.Material/README.md)** - PBR, point, billboard, and line material framework.

### Geometry & Assets

- **[HelixToolkit.Nex.Geometries](../Source/HelixToolkit-Nex/HelixToolkit.Nex.Geometry/README.md)** - Geometric data structures and operations for vertices, indices, and vertex properties.
- **[HelixToolkit.Nex.Geometries.Builders](../Source/HelixToolkit-Nex/HelixToolkit.Nex.Geometries.Builders/README.md)** - Utilities for building meshes, polygons, and other 3D shapes.
- **[HelixToolkit.Nex.Textures](../Source/HelixToolkit-Nex/HelixToolkit.Nex.Textures/README.md)** - Loading, manipulating, and saving image formats and creating GPU textures.
- **[HelixToolkit.Nex.glTF](../Source/HelixToolkit-Nex/HelixToolkit.Nex.glTF/README.md)** - glTF 2.0 / GLB importer that integrates assets into the scene graph.

### Integration & Platform

- **[HelixToolkit.Nex.ImGui](../Source/HelixToolkit-Nex/HelixToolkit.Nex.ImGui/README.md)** - Dear ImGui integration for immediate-mode debug interfaces.
- **[HelixToolkit.Nex.Interop.DirectX](../Source/HelixToolkit-Nex/HelixToolkit.Nex.Interop.DirectX/README.md)** - DirectX/Vulkan interop for sharing textures and resources.
- **[HelixToolkit.Nex.WinUI](../Source/HelixToolkit-Nex/HelixToolkit.Nex.WinUI/README.md)** - WinUI 3 integration for rendering 3D content via DirectX interop.
- **[HelixToolkit.Nex.Wpf](../Source/HelixToolkit-Nex/HelixToolkit.Nex.Wpf/README.md)** - WPF integration using Direct3D9 interop with D3DImage.

## Getting Started

Browse the [API Documentation](api/index.md) to explore the available classes and methods.

## Key Features

- Modern Vulkan-based rendering pipeline
- Cross-platform support (.NET 8)
- High-performance graphics operations
- Comprehensive mathematics library
- Scene graph management
- ImGui integration for debug UI

## Documentation Sections

- [API Reference](api/index.md) - Complete API documentation
- [Articles](articles/toc.yml) - Tutorials and guides

## Requirements

- .NET 8.0 or later
- Vulkan 1.3 or later compatible GPU

## License

See the LICENSE file in the repository for license information.
