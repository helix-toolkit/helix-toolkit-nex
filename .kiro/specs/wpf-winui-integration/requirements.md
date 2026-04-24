# Requirements Document

## Introduction

This feature integrates the HelixToolkit.Nex Vulkan-based 3D engine into WPF and WinUI 3 desktop applications. The integration uses DirectX interop via the `VK_KHR_external_memory_win32` Vulkan extension to share textures between Vulkan and DirectX, enabling the engine's render output to be displayed in WPF's `D3DImage` and WinUI's `SwapChainPanel`. Three new projects are created: a shared DirectX interop layer, a WPF integration control, and a WinUI integration control.

## Glossary

- **Interop_Layer**: The shared `HelixToolkit.Nex.Interop.DirectX` project that manages DirectX device creation, shared texture allocation, and Vulkan external memory import. Both the WPF and WinUI integration projects depend on the Interop_Layer.
- **WPF_Control**: The `HelixToolkit.Nex.Wpf` project providing a WPF control (`HelixViewport`) that hosts the 3D engine output using `D3DImage` and a D3D9 back buffer surface.
- **WinUI_Control**: The `HelixToolkit.Nex.WinUI` project providing a WinUI 3 control (`HelixViewport`) that hosts the 3D engine output using `SwapChainPanel` and a DXGI swap chain.
- **Shared_Texture**: A D3D11 texture created in shared mode whose memory is imported into Vulkan via `VK_KHR_external_memory_win32`, enabling both APIs to access the same GPU memory.
- **LUID**: Locally Unique Identifier for a DXGI adapter. The Vulkan physical device must match the DXGI adapter LUID for shared memory to function.
- **KMT_Handle**: A kernel-mode transport handle (`D3D11TextureKmtBit`) used by WPF's D3D9-to-D3D11 shared texture path.
- **NT_Handle**: A Windows NT handle (`D3D11TextureBit`) used by WinUI's D3D11 shared texture path with keyed mutex synchronization.
- **Engine**: The `HelixToolkit.Nex.Engine.Engine` class that orchestrates the render graph, render nodes, and frame execution.
- **VulkanContext**: The `VulkanContext` class implementing `IContext`, which manages the Vulkan instance, device, queues, and resource creation.
- **RenderContext**: The per-viewport state object holding window size, camera parameters, and the `FinalOutputTexture` that receives the render graph output.
- **Headless_Context**: A `VulkanContext` created via `VulkanBuilder.CreateHeadless()` without a window surface or swapchain, suitable for offscreen rendering to an external texture.

## Requirements

### Requirement 1: Shared DirectX Interop Layer Project

**User Story:** As a developer, I want a shared interop layer that handles DirectX device creation and Vulkan external memory import, so that both WPF and WinUI integrations reuse the same interop logic.

#### Acceptance Criteria

1. THE Interop_Layer SHALL provide a class that creates a D3D11 device and retrieves the DXGI adapter LUID.
2. THE Interop_Layer SHALL provide a method that creates a D3D11 shared texture of a specified width, height, and format, and returns the shared handle (KMT_Handle or NT_Handle depending on the platform path).
3. THE Interop_Layer SHALL provide a method that imports a shared DirectX texture handle into Vulkan as a `VkImage` using `ExternalMemoryImageCreateInfo` and `ImportMemoryWin32HandleInfoKHR`.
4. THE Interop_Layer SHALL query `ExternalMemoryFeatureFlags` for the target format and handle type, and use dedicated allocation when `DedicatedOnlyBit` is set.
5. THE Interop_Layer SHALL target `net8.0-windows` and depend on Vortice.Vulkan (matching the engine) and Silk.NET.Direct3D11, Silk.NET.Direct3D9, and Silk.NET.DXGI for DirectX bindings.
6. WHEN the shared texture is no longer needed, THE Interop_Layer SHALL release the Vulkan image memory, D3D11 texture, and associated handles.

### Requirement 2: LUID-Based Vulkan Physical Device Matching

**User Story:** As a developer, I want the Vulkan physical device to match the DXGI adapter, so that shared memory between Vulkan and DirectX functions correctly.

#### Acceptance Criteria

1. THE Interop_Layer SHALL retrieve the DXGI adapter LUID from the D3D11 device.
2. THE Interop_Layer SHALL query each Vulkan physical device's LUID via `PhysicalDeviceIDProperties` and compare it against the DXGI adapter LUID.
3. THE Interop_Layer SHALL verify that the selected Vulkan physical device supports the `VK_KHR_external_memory_win32` extension.
4. THE Interop_Layer SHALL verify that the selected Vulkan physical device supports the target external memory handle type for the target image format.
5. IF no Vulkan physical device matches the DXGI adapter LUID and supports the required extensions, THEN THE Interop_Layer SHALL throw a descriptive exception indicating the mismatch.

### Requirement 3: VulkanContext Configuration for External Memory

**User Story:** As a developer, I want an opt-in option on the Vulkan backend to enable `VK_KHR_external_memory_win32`, so that the extension is only loaded when rendering onto DirectX surfaces and does not affect normal Vulkan rendering.

#### Acceptance Criteria

1. THE `VulkanContextConfig` SHALL provide a boolean property `EnableExternalMemoryWin32` (default `false`) that controls whether the `VK_KHR_external_memory_win32` device extension is enabled during Vulkan device creation.
2. WHEN `EnableExternalMemoryWin32` is `true`, THE VulkanContext SHALL add `VK_KHR_external_memory_win32` to the device extensions list during device creation, and verify that the physical device supports the extension before proceeding.
3. WHEN `EnableExternalMemoryWin32` is `false` (default), THE VulkanContext SHALL NOT load or enable the `VK_KHR_external_memory_win32` extension, ensuring no impact on normal Vulkan rendering.
4. THE Interop_Layer SHALL set `VulkanContextConfig.EnableExternalMemoryWin32 = true` before creating the VulkanContext, rather than manually adding the extension string to `ExtensionsDevice`.
5. THE Interop_Layer SHALL create the VulkanContext using `VulkanBuilder.CreateHeadless()` to obtain a Headless_Context without a window surface or swapchain.
6. THE Interop_Layer SHALL expose the Vulkan physical device LUID so that LUID matching can be performed after context creation.

### Requirement 4: WPF Integration Control

**User Story:** As a WPF developer, I want a WPF control that displays the HelixToolkit.Nex 3D engine output, so that I can embed 3D viewports in WPF applications.

#### Acceptance Criteria

1. THE WPF_Control SHALL provide a `HelixViewport` control that extends `FrameworkElement` and contains a `D3DImage` for displaying the render output.
2. THE WPF_Control SHALL target `net8.0-windows` with WPF enabled (`<UseWPF>true</UseWPF>`).
3. WHEN the WPF_Control is loaded, THE WPF_Control SHALL initialize the D3D9 context, D3D9 device, and create a D3D9 back buffer texture with a shared handle in `X8R8G8B8` format.
4. WHEN the WPF_Control is loaded, THE WPF_Control SHALL open the D3D9 shared texture on the D3D11 side as a render target, query the `IDXGIResource` shared handle (KMT_Handle), and import the handle into Vulkan as an image with format `B8G8R8A8Unorm`.
5. THE WPF_Control SHALL create the Engine using `EngineBuilder.Create(context).WithDefaultNodes(renderToSwapchain: false).Build()` to configure the render pipeline for offscreen output.
6. THE WPF_Control SHALL set `RenderContext.FinalOutputTexture` to the Vulkan image imported from the shared D3D11 texture.
7. WHEN `CompositionTarget.Rendering` fires, THE WPF_Control SHALL call `Engine.RenderOffscreen()` to render the scene, then submit the command buffer, then lock the `D3DImage`, set the back buffer to the D3D9 surface, add a dirty rect, and unlock the `D3DImage`.
8. WHEN the WPF_Control is resized, THE WPF_Control SHALL release the existing shared resources (D3D9 surface, D3D11 texture, Vulkan image), recreate the shared resources at the new size, and update the `RenderContext.WindowSize`.
9. WHEN the WPF_Control is unloaded, THE WPF_Control SHALL unsubscribe from `CompositionTarget.Rendering`, dispose the Engine, dispose the Interop_Layer resources, and dispose the D3D9 device and context.

### Requirement 5: WinUI Integration Control

**User Story:** As a WinUI developer, I want a WinUI 3 control that displays the HelixToolkit.Nex 3D engine output, so that I can embed 3D viewports in WinUI applications.

#### Acceptance Criteria

1. THE WinUI_Control SHALL provide a `HelixViewport` control that extends `UserControl` and contains a `SwapChainPanel` for displaying the render output.
2. THE WinUI_Control SHALL target `net8.0-windows10.0.22621.0` with WinUI enabled and depend on `Microsoft.WindowsAppSDK`.
3. WHEN the WinUI_Control is loaded, THE WinUI_Control SHALL create a DXGI swap chain for composition, retrieve the back buffer texture, and set the swap chain on the `SwapChainPanel` via `ISwapChainPanelNative`.
4. WHEN the WinUI_Control is loaded, THE WinUI_Control SHALL create a D3D11 render target texture with `SharedNthandle` and `SharedKeyedmutex` flags, obtain the NT_Handle via `IDXGIResource1.CreateSharedHandle`, and import the handle into Vulkan as an image with format `R8G8B8A8Unorm`.
5. THE WinUI_Control SHALL create the Engine using `EngineBuilder.Create(context).WithDefaultNodes(renderToSwapchain: false).Build()` to configure the render pipeline for offscreen output.
6. THE WinUI_Control SHALL set `RenderContext.FinalOutputTexture` to the Vulkan image imported from the shared D3D11 texture.
7. WHEN `CompositionTarget.Rendering` fires, THE WinUI_Control SHALL call `Engine.RenderOffscreen()` to render the scene with keyed mutex synchronization, then acquire the D3D11 keyed mutex, copy the render target resource to the back buffer resource via `ID3D11DeviceContext.CopyResource`, release the keyed mutex, and call `swapchain.Present`.
8. WHEN the WinUI_Control is resized, THE WinUI_Control SHALL release the existing shared resources (swap chain, keyed mutex, D3D11 textures, Vulkan image), recreate the shared resources at the new size, and update the `RenderContext.WindowSize`.
9. WHEN the WinUI_Control is unloaded, THE WinUI_Control SHALL unsubscribe from `CompositionTarget.Rendering`, dispose the Engine, dispose the Interop_Layer resources, dispose the swap chain, and dispose the DXGI factory and adapter.

### Requirement 6: Keyed Mutex Synchronization for WinUI

**User Story:** As a developer, I want proper synchronization between Vulkan rendering and D3D11 copy operations in the WinUI path, so that there are no race conditions or visual artifacts.

#### Acceptance Criteria

1. THE WinUI_Control SHALL use `IDXGIKeyedMutex` on the shared render target texture to synchronize access between Vulkan and D3D11.
2. THE WinUI_Control SHALL configure keyed mutex sync info so that Vulkan acquires with key 0 and releases with key 1, and the D3D11 copy acquires with key 1 and releases with key 0.
3. WHEN submitting Vulkan render work, THE Interop_Layer SHALL include `VkWin32KeyedMutexAcquireReleaseInfoKHR` in the submit info's `pNext` chain with the appropriate acquire and release keys.
4. IF the keyed mutex acquire times out, THEN THE WinUI_Control SHALL log a warning and skip the frame.

### Requirement 7: Engine Offscreen Rendering Integration

**User Story:** As a developer, I want the engine to render to an externally provided Vulkan texture (imported from DirectX), so that the render output can be displayed in WPF or WinUI controls.

#### Acceptance Criteria

1. THE Engine SHALL support rendering to an external texture by using `Engine.RenderOffscreen()` which returns an `ICommandBuffer` for the caller to submit.
2. THE RenderContext SHALL accept an externally created `TextureHandle` as `FinalOutputTexture`, and the render graph's `RenderToFinalNode` SHALL copy the final color buffer to the external texture.
3. WHEN `Engine.RenderOffscreen()` is used, THE caller SHALL submit the returned `ICommandBuffer` via `IContext.Submit(cmdBuf, TextureHandle.Null)` since there is no swapchain presentation.
4. THE EngineBuilder SHALL support `WithDefaultNodes(renderToSwapchain: false)` to omit the `RenderToFinalNode` when the caller manages the final output copy externally, or include the `RenderToFinalNode` configured for the external texture format.

### Requirement 8: Resize Handling

**User Story:** As a developer, I want the integration controls to handle window resize correctly, so that the 3D viewport always matches the control size.

#### Acceptance Criteria

1. WHEN the WPF_Control or WinUI_Control is resized, THE control SHALL wait for the Vulkan device to become idle before releasing size-dependent resources.
2. WHEN the WPF_Control or WinUI_Control is resized, THE control SHALL recreate the shared D3D11 texture, reimport the Vulkan image, and update the `RenderContext.WindowSize` and `RenderContext.FinalOutputTexture`.
3. IF the new size has a width or height of zero, THEN THE control SHALL skip rendering until the size becomes non-zero.

### Requirement 9: Resource Lifecycle Management

**User Story:** As a developer, I want all GPU resources to be properly managed and released, so that there are no memory leaks or dangling handles.

#### Acceptance Criteria

1. THE Interop_Layer SHALL implement `IDisposable` and release all Vulkan image memory, D3D11 textures, D3D9 surfaces, DXGI resources, and shared handles on disposal.
2. THE WPF_Control and WinUI_Control SHALL implement `IDisposable` and dispose the Engine, RenderContext, WorldDataProvider, and Interop_Layer resources in the correct order.
3. WHEN the WPF_Control or WinUI_Control is unloaded from the visual tree, THE control SHALL automatically trigger disposal of GPU resources.
4. THE Interop_Layer SHALL ensure that Vulkan device idle is waited on before destroying Vulkan resources that may be in use by pending GPU work.

### Requirement 10: Project Structure and Build Configuration

**User Story:** As a developer, I want the new projects to follow the existing solution structure and build conventions, so that the integration is consistent with the rest of the HelixToolkit.Nex codebase.

#### Acceptance Criteria

1. THE Interop_Layer project SHALL be located at `Source/HelixToolkit-Nex/HelixToolkit.Nex.Interop.DirectX/` and target `net8.0-windows`.
2. THE WPF_Control project SHALL be located at `Source/HelixToolkit-Nex/HelixToolkit.Nex.Wpf/` and target `net8.0-windows` with `<UseWPF>true</UseWPF>`.
3. THE WinUI_Control project SHALL be located at `Source/HelixToolkit-Nex/HelixToolkit.Nex.WinUI/` and target `net8.0-windows10.0.22621.0` with a dependency on `Microsoft.WindowsAppSDK`.
4. THE three new projects SHALL be added to the existing `HelixToolkit.Nex.sln` solution file.
5. THE WPF_Control and WinUI_Control projects SHALL reference `HelixToolkit.Nex.Engine` and `HelixToolkit.Nex.Interop.DirectX`.
6. THE Interop_Layer project SHALL reference `HelixToolkit.Nex.Graphics` and `HelixToolkit.Nex.Graphics.Vulkan`.
