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
- :white_check_mark: Linux support.
- :white_check_mark: [ImGui integration](Source/HelixToolkit-Nex/Samples/GraphicsAPI/ImGuiTest/README.md)
- :white_check_mark: [Forward+(Tiled based GPU light culling)](Source/HelixToolkit-Nex/Samples/GraphicsAPI/ForwardPlusSimple/README.md) rendering pipeline.
- Material systems.
  - :white_check_mark: Physically Based Rendering
  - :white_check_mark: Point cloud
  - :white_check_mark: Billboard ([MSDF based Font support](https://github.com/chlumsky/msdfgen))
  - :white_check_mark: Material registry and shader generation system.
  - :hourglass: Line (Planned)  
  - :hourglass: Particle System (Planned)
  - :hourglass: Skeletal/Morph Target Animation (Planned)
  - :hourglass: Model importer (Planned)
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
- :hourglass: Avalonia UI Interoperation (Planned)

## Rendering Samples

<img src="Source/HelixToolkit-Nex/Samples/Integration/LightCulling/Screenshots/LargeScene.gif" width=400>

<img src="Source/HelixToolkit-Nex/Samples/Integration/ImGui/Screenshots/Sample.jpg" width=400>

<img src="Source/HelixToolkit-Nex/Samples/Integration/Points/Screenshots/Points.gif" width=400>

<img src="Source/HelixToolkit-Nex/Samples/Integration/TextureTest/Screenshots/Sample.jpg" width=400>

## Linux Support

<img src="Assets/Screenshots/linux_support.png" width=400>

## Interoperability

- Render content with Vulkan Backend in Wpf and WinUI applications using D3D11 interoperation. (Requires Vulkan Extension: `VK_KHR_external_memory_win32`. Only tested on Discrete Graphics Card.)

    - [WPF](Source/HelixToolkit-Nex/Samples/Interop/Wpf)
    <img src="Source/HelixToolkit-Nex/Samples/Interop/Wpf/Screenshots/Sample.jpg" width=400>

    - [WinUI](Source/HelixToolkit-Nex/Samples/Interop/WinUI)
    <img src="Source/HelixToolkit-Nex/Samples/Interop/WinUI/Screenshots/Sample.jpg" width=400>

## Contributing

Interested in contributing? Please read our [Contributing Guide](CONTRIBUTING.md) for information on:
- Development setup and prerequisites
- Code formatting requirements
- Building and testing
- Submitting pull requests
