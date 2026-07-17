# HelixToolkit Nex <img src="Assets/icon.png" width=32 height=32 style="background-color: transparent;">

[![License: MIT](https://img.shields.io/github/license/helix-toolkit/helix-toolkit-nex)](https://github.com/helix-toolkit/helix-toolkit-nex/blob/main/LICENSE)
[![Github Action](https://github.com/helix-toolkit/helix-toolkit-nex/actions/workflows/ci.yml/badge.svg)](https://github.com/helix-toolkit/helix-toolkit-nex/actions?query=workflow%3ACI
)

HelixToolkit Nex is the next generation 3D graphics engine from HelixToolkit. It offers a unified graphics interface designed to support multiple backend implementations, with an initial focus on Vulkan 1.3.

The graphics interface and Vulkan backend are inspired by [LightWeightVk](https://github.com/corporateshark/lightweightvk).

Currently in development.

## Minimum Requirements
- Windows 10 or later
- Linux (Tested on Ubuntu 26.04)
- Vulkan 1.3 compatible GPU and drivers
- .NET 8.0 or later

## Features (Done or In progress)

- :white_check_mark: [Vulkan backend implementation](Source/HelixToolkit-Nex/HelixToolkit.Nex.Graphics.Vulkan/README.md)
- :white_check_mark: Complete bindless descriptor architecture.
- :white_check_mark: Support AOT compilation.
- :white_check_mark: Linux support.
- :white_check_mark: [ImGui integration](Source/HelixToolkit-Nex/Samples/GraphicsAPI/ImGuiTest/README.md)
- :white_check_mark: [Forward+(Tiled based GPU light culling)](Source/HelixToolkit-Nex/Samples/GraphicsAPI/ForwardPlusSimple/README.md) rendering pipeline.
- Material systems.
  - :white_check_mark: Physically Based Rendering
  - :white_check_mark: Point cloud
  - :white_check_mark: Billboard ([MSDF based Font support](https://github.com/chlumsky/msdfgen))
  - :white_check_mark: Material registry and shader generation system.
  - :white_check_mark: Line
  - :white_check_mark: Gizmo
  - :hourglass: Particle System (Planned)
  - :hourglass: Skeletal/Morph Target Animation (Planned)
  - :hourglass: Model importer (Planned)
    - :white_check_mark: glTF importer. [Demo](Source/HelixToolkit-Nex/Samples/Integration/gltfImporter)
- :white_check_mark: [GPU Frustum Culling](Source/HelixToolkit-Nex/Samples/GraphicsAPI/MeshCulling/README.md) and [GPU Frustum Culling on Instancing](Source/HelixToolkit-Nex/Samples/GraphicsAPI/InstancingMeshCulling/README.md). (Done)
- :white_check_mark: ECS based scene management system.
- :white_check_mark: Engine architecture design.
  - :white_check_mark: Render Graph based rendering architecture.
  - :white_check_mark: WB Order independent transparency rendering.
  - Anti-aliasing:
      - :white_check_mark: SMAA anti-aliasing.
      - :white_check_mark: FXAA anti-aliasing.
  - Effects:
    - :white_check_mark: Bloom post-processing effect.
    - :white_check_mark: Object border highlighting effect.
    - :white_check_mark: Wireframe rendering.
    - :white_check_mark: Tone mapping post-processing effect.
  - :white_check_mark: GPU picking.
  - :white_check_mark: Async Buffer/Texture upload with transfer queue.
  - :white_check_mark: Texture loading and caching system.
  - :white_check_mark: Shader compilation and management system.
 
- :white_check_mark: Wpf Framework Interoperation
- :white_check_mark: WinUI Interoperation
- :white_check_mark: Avalonia (Windows & Linux) UI Interoperation

## Rendering Samples

<img src="Source/HelixToolkit-Nex/Samples/Integration/LightCulling/Screenshots/LargeScene.gif" width=400>

<img src="Source/HelixToolkit-Nex/Samples/Integration/ImGui/Screenshots/Sample.gif" width=400>

<img src="Source/HelixToolkit-Nex/Samples/Integration/GizmoTest/Screenshots/Sample.gif" width=400>

<img src="Source/HelixToolkit-Nex/Samples/Integration/Points/Screenshots/Points.gif" width=400>

<img src="Source/HelixToolkit-Nex/Samples/Integration/gltfImporter/Screenshots/Sample.jpg" width=400>

## Linux Support

<img src="Assets/Screenshots/linux_support.png" width=400>

## Interoperability

- Render content with Vulkan Backend in Wpf and WinUI applications using D3D11 interoperation. (Requires Vulkan Extension: `VK_KHR_external_memory_win32`. Only tested on Discrete Graphics Card.)

    - [WPF](Source/HelixToolkit-Nex/Samples/Interop/Wpf)

      <img src="Source/HelixToolkit-Nex/Samples/Interop/Wpf/Screenshots/Sample.jpg" width=400>

    - [WinUI](Source/HelixToolkit-Nex/Samples/Interop/WinUI)
  
      <img src="Source/HelixToolkit-Nex/Samples/Interop/WinUI/Screenshots/Sample.jpg" width=400>

    - [Avalonia](Source/HelixToolkit-Nex/Samples/Interop/Avalonia) [Supports both Windows and Linux]

      <img src="Source/HelixToolkit-Nex/Samples/Interop/Avalonia/Screenshots/Sample.jpg" width=400>

## Packages

> **Note:** Release packages are published to GitHub Packages and NuGet.org. Nightly builds are published to GitHub Packages only.

| Project                                | GitHub                                                                                                                                                              | NuGet                                                                                                                               |
| -------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------- |
| HelixToolkit.Nex                       | [![GitHub](https://img.shields.io/badge/GitHub-package-blue)](https://github.com/helix-toolkit/helix-toolkit-nex/pkgs/nuget/HelixToolkit.Nex)                       | [![NuGet](https://img.shields.io/badge/Nuget-package-green)](https://www.nuget.org/packages/HelixToolkit.Nex)                       |
| HelixToolkit.Nex.Engine                | [![GitHub](https://img.shields.io/badge/GitHub-package-blue)](https://github.com/helix-toolkit/helix-toolkit-nex/pkgs/nuget/HelixToolkit.Nex.Engine)                | [![NuGet](https://img.shields.io/badge/Nuget-package-green)](https://www.nuget.org/packages/HelixToolkit.Nex.Engine)                |
| HelixToolkit.Nex.EntityComponentSystem | [![GitHub](https://img.shields.io/badge/GitHub-package-blue)](https://github.com/helix-toolkit/helix-toolkit-nex/pkgs/nuget/HelixToolkit.Nex.EntityComponentSystem) | [![NuGet](https://img.shields.io/badge/Nuget-package-green)](https://www.nuget.org/packages/HelixToolkit.Nex.EntityComponentSystem) |
| HelixToolkit.Nex.Geometries.Builders   | [![GitHub](https://img.shields.io/badge/GitHub-package-blue)](https://github.com/helix-toolkit/helix-toolkit-nex/pkgs/nuget/HelixToolkit.Nex.Geometries.Builders)   | [![NuGet](https://img.shields.io/badge/Nuget-package-green)](https://www.nuget.org/packages/HelixToolkit.Nex.Geometries.Builders)   |
| HelixToolkit.Nex.Geometry              | [![GitHub](https://img.shields.io/badge/GitHub-package-blue)](https://github.com/helix-toolkit/helix-toolkit-nex/pkgs/nuget/HelixToolkit.Nex.Geometry)              | [![NuGet](https://img.shields.io/badge/Nuget-package-green)](https://www.nuget.org/packages/HelixToolkit.Nex.Geometry)              |
| HelixToolkit.Nex.glTF                  | [![GitHub](https://img.shields.io/badge/GitHub-package-blue)](https://github.com/helix-toolkit/helix-toolkit-nex/pkgs/nuget/HelixToolkit.Nex.glTF)                  | [![NuGet](https://img.shields.io/badge/Nuget-package-green)](https://www.nuget.org/packages/HelixToolkit.Nex.glTF)                  |
| HelixToolkit.Nex.Graphics              | [![GitHub](https://img.shields.io/badge/GitHub-package-blue)](https://github.com/helix-toolkit/helix-toolkit-nex/pkgs/nuget/HelixToolkit.Nex.Graphics)              | [![NuGet](https://img.shields.io/badge/Nuget-package-green)](https://www.nuget.org/packages/HelixToolkit.Nex.Graphics)              |
| HelixToolkit.Nex.Graphics.Mock         | [![GitHub](https://img.shields.io/badge/GitHub-package-blue)](https://github.com/helix-toolkit/helix-toolkit-nex/pkgs/nuget/HelixToolkit.Nex.Graphics.Mock)         | [![NuGet](https://img.shields.io/badge/Nuget-package-green)](https://www.nuget.org/packages/HelixToolkit.Nex.Graphics.Mock)         |
| HelixToolkit.Nex.Graphics.Vulkan       | [![GitHub](https://img.shields.io/badge/GitHub-package-blue)](https://github.com/helix-toolkit/helix-toolkit-nex/pkgs/nuget/HelixToolkit.Nex.Graphics.Vulkan)       | [![NuGet](https://img.shields.io/badge/Nuget-package-green)](https://www.nuget.org/packages/HelixToolkit.Nex.Graphics.Vulkan)       |
| HelixToolkit.Nex.ImGui                 | [![GitHub](https://img.shields.io/badge/GitHub-package-blue)](https://github.com/helix-toolkit/helix-toolkit-nex/pkgs/nuget/HelixToolkit.Nex.ImGui)                 | [![NuGet](https://img.shields.io/badge/Nuget-package-green)](https://www.nuget.org/packages/HelixToolkit.Nex.ImGui)                 |
| HelixToolkit.Nex.Interop               | [![GitHub](https://img.shields.io/badge/GitHub-package-blue)](https://github.com/helix-toolkit/helix-toolkit-nex/pkgs/nuget/HelixToolkit.Nex.Interop)               | [![NuGet](https://img.shields.io/badge/Nuget-package-green)](https://www.nuget.org/packages/HelixToolkit.Nex.Interop)               |
| HelixToolkit.Nex.Interop.DirectX       | [![GitHub](https://img.shields.io/badge/GitHub-package-blue)](https://github.com/helix-toolkit/helix-toolkit-nex/pkgs/nuget/HelixToolkit.Nex.Interop.DirectX)       | [![NuGet](https://img.shields.io/badge/Nuget-package-green)](https://www.nuget.org/packages/HelixToolkit.Nex.Interop.DirectX)       |
| HelixToolkit.Nex.Material              | [![GitHub](https://img.shields.io/badge/GitHub-package-blue)](https://github.com/helix-toolkit/helix-toolkit-nex/pkgs/nuget/HelixToolkit.Nex.Material)              | [![NuGet](https://img.shields.io/badge/Nuget-package-green)](https://www.nuget.org/packages/HelixToolkit.Nex.Material)              |
| HelixToolkit.Nex.Maths                 | [![GitHub](https://img.shields.io/badge/GitHub-package-blue)](https://github.com/helix-toolkit/helix-toolkit-nex/pkgs/nuget/HelixToolkit.Nex.Maths)                 | [![NuGet](https://img.shields.io/badge/Nuget-package-green)](https://www.nuget.org/packages/HelixToolkit.Nex.Maths)                 |
| HelixToolkit.Nex.Rendering             | [![GitHub](https://img.shields.io/badge/GitHub-package-blue)](https://github.com/helix-toolkit/helix-toolkit-nex/pkgs/nuget/HelixToolkit.Nex.Rendering)             | [![NuGet](https://img.shields.io/badge/Nuget-package-green)](https://www.nuget.org/packages/HelixToolkit.Nex.Rendering)             |
| HelixToolkit.Nex.Repository            | [![GitHub](https://img.shields.io/badge/GitHub-package-blue)](https://github.com/helix-toolkit/helix-toolkit-nex/pkgs/nuget/HelixToolkit.Nex.Repository)            | [![NuGet](https://img.shields.io/badge/Nuget-package-green)](https://www.nuget.org/packages/HelixToolkit.Nex.Repository)            |
| HelixToolkit.Nex.Scene                 | [![GitHub](https://img.shields.io/badge/GitHub-package-blue)](https://github.com/helix-toolkit/helix-toolkit-nex/pkgs/nuget/HelixToolkit.Nex.Scene)                 | [![NuGet](https://img.shields.io/badge/Nuget-package-green)](https://www.nuget.org/packages/HelixToolkit.Nex.Scene)                 |
| HelixToolkit.Nex.Shaders               | [![GitHub](https://img.shields.io/badge/GitHub-package-blue)](https://github.com/helix-toolkit/helix-toolkit-nex/pkgs/nuget/HelixToolkit.Nex.Shaders)               | [![NuGet](https://img.shields.io/badge/Nuget-package-green)](https://www.nuget.org/packages/HelixToolkit.Nex.Shaders)               |
| HelixToolkit.Nex.Textures              | [![GitHub](https://img.shields.io/badge/GitHub-package-blue)](https://github.com/helix-toolkit/helix-toolkit-nex/pkgs/nuget/HelixToolkit.Nex.Textures)              | [![NuGet](https://img.shields.io/badge/Nuget-package-green)](https://www.nuget.org/packages/HelixToolkit.Nex.Textures)              |
| HelixToolkit.Nex.WinUI                 | [![GitHub](https://img.shields.io/badge/GitHub-package-blue)](https://github.com/helix-toolkit/helix-toolkit-nex/pkgs/nuget/HelixToolkit.Nex.WinUI)                 | [![NuGet](https://img.shields.io/badge/Nuget-package-green)](https://www.nuget.org/packages/HelixToolkit.Nex.WinUI)                 |
| HelixToolkit.Nex.Wpf                   | [![GitHub](https://img.shields.io/badge/GitHub-package-blue)](https://github.com/helix-toolkit/helix-toolkit-nex/pkgs/nuget/HelixToolkit.Nex.Wpf)                   | [![NuGet](https://img.shields.io/badge/Nuget-package-green)](https://www.nuget.org/packages/HelixToolkit.Nex.Wpf)                   |
| HelixToolkit.Nex.Avalonia              | [![GitHub](https://img.shields.io/badge/GitHub-package-blue)](https://github.com/helix-toolkit/helix-toolkit-nex/pkgs/nuget/HelixToolkit.Nex.Avalonia)              | [![NuGet](https://img.shields.io/badge/Nuget-package-green)](https://www.nuget.org/packages/HelixToolkit.Nex.Avalonia)              |

## Development

See the [Development Setup Guide](DEV.md) for full instructions on prerequisites, Git LFS, Git hooks, building (Windows and Linux), running tests, and formatting.

## Contributing

Interested in contributing? Please read our [Contributing Guide](CONTRIBUTING.md) for information on:
- Development setup and prerequisites
- Code formatting requirements
- Building and testing
- Submitting pull requests
