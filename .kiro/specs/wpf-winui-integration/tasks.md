# Implementation Plan: WPF & WinUI Integration

## Overview

Implement Vulkan-to-DirectX interop for embedding the HelixToolkit.Nex 3D engine in WPF and WinUI 3 applications. Three new projects are created: a shared DirectX interop layer, a WPF control, and a WinUI control. The engine renders offscreen via `Engine.RenderOffscreen()` into a Vulkan image backed by shared DirectX memory, which is then presented through `D3DImage` (WPF) or `SwapChainPanel` (WinUI).

## Tasks

- [x] 1. Extend VulkanContextConfig and VulkanContext for external memory support
  - [x] 1.1 Add `EnableExternalMemoryWin32` and `RequiredDeviceLuid` fields to `VulkanContextConfig`
    - Add `public bool EnableExternalMemoryWin32 = false;` to `VulkanContextConfig` in `HelixToolkit.Nex.Graphics.Vulkan/VulkanContext.cs`
    - Add `public byte[]? RequiredDeviceLuid = null;` to `VulkanContextConfig`
    - _Requirements: 3.1, 3.2_

  - [x] 1.2 Modify VulkanContext device creation to conditionally enable external memory extensions
    - In `VulkanContext.InitContext()`, when `EnableExternalMemoryWin32` is `true`, add `VK_KHR_external_memory_win32` to `_deviceExtensions`
    - Verify the physical device supports the extension before proceeding; throw `InvalidOperationException` if not
    - When `EnableExternalMemoryWin32` is `false`, do not load or enable the extension
    - _Requirements: 3.2, 3.3_

  - [x] 1.3 Implement LUID-based physical device filtering in VulkanContext
    - When `RequiredDeviceLuid` is set, query `VkPhysicalDeviceIDProperties` for each candidate physical device
    - Skip devices whose LUID does not match; throw `InvalidOperationException` if no device matches
    - _Requirements: 2.2, 2.5_

  - [x] 1.4 Write property test: LUID comparison is byte-exact (Property 3)
    - **Property 3: LUID comparison is byte-exact**
    - Generate random 8-byte arrays, verify LUID comparison returns true iff all bytes match
    - Use FsCheck with MSTest; minimum 100 iterations
    - **Validates: Requirements 2.2**

  - [x] 1.5 Write unit tests for VulkanContextConfig defaults
    - Verify `EnableExternalMemoryWin32` defaults to `false`
    - Verify `RequiredDeviceLuid` defaults to `null`
    - _Requirements: 3.1, 3.3_

- [x] 2. Checkpoint — Ensure VulkanContext changes compile and tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 3. Create the HelixToolkit.Nex.Interop.DirectX project and D3D11DeviceManager
  - [x] 3.1 Create project structure and .csproj
    - Create `Source/HelixToolkit-Nex/HelixToolkit.Nex.Interop.DirectX/` directory
    - Create `HelixToolkit.Nex.Interop.DirectX.csproj` targeting `net8.0-windows` with NuGet dependencies: `Silk.NET.Direct3D11`, `Silk.NET.Direct3D9`, `Silk.NET.DXGI`
    - Add project references to `HelixToolkit.Nex.Graphics` and `HelixToolkit.Nex.Graphics.Vulkan`
    - Add the project to `HelixToolkit.Nex.sln`
    - _Requirements: 10.1, 10.6, 1.5_

  - [x] 3.2 Implement D3D11DeviceManager
    - Create `D3D11DeviceManager` class implementing `IDisposable`
    - Create D3D11 device via `D3D11.D3D11CreateDevice`
    - Retrieve DXGI adapter LUID via `IDXGIDevice` → `IDXGIAdapter` → `AdapterDesc.Luid`
    - Expose `Device`, `DeviceContext`, and `AdapterLuid` properties
    - Implement `Dispose()` to release COM pointers
    - _Requirements: 1.1, 2.1, 9.1_

- [x] 4. Implement SharedTextureFactory
  - [x] 4.1 Implement SharedTextureResult and SharedTextureFactory.CreateForWpf
    - Create `SharedTextureResult` class with `Texture`, `SharedHandle`, `HandleType`, `Width`, `Height` properties and `IDisposable`
    - Create `SharedHandleType` enum (`Kmt`, `Nt`)
    - Implement `CreateForWpf()`: open D3D9 shared texture on D3D11 side via `OpenSharedResource`, query `IDXGIResource` for KMT handle
    - _Requirements: 1.2, 4.4_

  - [x] 4.2 Implement SharedTextureFactory.CreateForWinUI
    - Implement `CreateForWinUI()`: create D3D11 texture with `SharedNthandle` + `SharedKeyedmutex` flags
    - Obtain NT handle via `IDXGIResource1.CreateSharedHandle`
    - Return `SharedTextureResult` with NT handle type
    - _Requirements: 1.2, 5.4_

- [x] 5. Implement VulkanExternalMemoryImporter
  - [x] 5.1 Implement ImportedVulkanTexture and VulkanExternalMemoryImporter.Import
    - Create `ImportedVulkanTexture` class owning `VkImage`, `VkDeviceMemory`, `VkImageView`, and `TextureHandle`
    - Implement `Import()`: create `VkImage` with `ExternalMemoryImageCreateInfo`, allocate memory with `ImportMemoryWin32HandleInfoKHR`
    - Query `ExternalMemoryFeatureFlags`; use `MemoryDedicatedAllocateInfo` when `DedicatedOnlyBit` is set
    - Wrap the image in a `VulkanImage` using the existing constructor for pre-created `VkImage` (`isOwningVkImage = false`)
    - Register in `TexturesPool` to obtain a `TextureHandle`
    - Implement `Dispose()` to free Vulkan resources (image, memory, image view, texture handle)
    - _Requirements: 1.3, 1.4, 1.6_

  - [x] 5.2 Write property test: Dedicated allocation when DedicatedOnlyBit is set (Property 2)
    - **Property 2: Dedicated allocation when DedicatedOnlyBit is set**
    - Generate random format/handleType combinations, mock feature flags, verify dedicated allocation logic
    - Use FsCheck with MSTest; minimum 100 iterations
    - **Validates: Requirements 1.4**

  - [x] 5.3 Write property test: ImportedVulkanTexture disposal clears resources (Property 6)
    - **Property 6: ImportedVulkanTexture disposal clears resources**
    - Create `ImportedVulkanTexture`, dispose, verify `VkImage`, `VkDeviceMemory`, and `TextureHandle` are null/default
    - Use FsCheck with MSTest; minimum 100 iterations
    - **Validates: Requirements 1.6**

- [x] 6. Implement KeyedMutexHelper for WinUI synchronization
  - [x] 6.1 Implement KeyedMutexSyncConfig and KeyedMutexHelper
    - Create `KeyedMutexSyncConfig` record struct with `VulkanAcquireKey=0`, `VulkanReleaseKey=1`, `CopyAcquireKey=1`, `CopyReleaseKey=0`, `TimeoutMs=5000`
    - Implement `KeyedMutexHelper.CreateSubmitInfo()` to build `VkWin32KeyedMutexAcquireReleaseInfoKHR`
    - _Requirements: 6.1, 6.2, 6.3_

  - [x] 6.2 Write unit tests for KeyedMutexSyncConfig defaults
    - Verify default key values: Vulkan acquire=0, release=1; copy acquire=1, release=0
    - _Requirements: 6.2_

- [x] 7. Checkpoint — Ensure interop layer compiles and tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 8. Create the HelixToolkit.Nex.Wpf project and HelixViewport control
  - [x] 8.1 Create WPF project structure and .csproj
    - Create `Source/HelixToolkit-Nex/HelixToolkit.Nex.Wpf/` directory
    - Create `HelixToolkit.Nex.Wpf.csproj` targeting `net8.0-windows` with `<UseWPF>true</UseWPF>`
    - Add project references to `HelixToolkit.Nex.Engine` and `HelixToolkit.Nex.Interop.DirectX`
    - Add the project to `HelixToolkit.Nex.sln`
    - _Requirements: 10.2, 10.4, 10.5_

  - [x] 8.2 Implement D3D9DeviceManager for WPF
    - Create `D3D9DeviceManager` class implementing `IDisposable`
    - Create D3D9 context (`Direct3DCreate9`) and D3D9 device (`CreateDevice` with `D3DDEVTYPE_HAL`)
    - Expose `Device` and `Context` properties
    - Implement `Dispose()` to release COM pointers
    - _Requirements: 4.3_

  - [x] 8.3 Implement HelixViewport (WPF) — initialization and resource creation
    - Create `HelixViewport` class extending `FrameworkElement` with `D3DImage`
    - Implement `OnLoaded`: initialize D3D9 context/device, create D3D9 back buffer (`X8R8G8B8`, shared handle), get D3D9 surface
    - Open D3D9 shared texture on D3D11 side, query KMT handle, import into Vulkan as `B8G8R8A8Unorm`
    - Create headless VulkanContext with `EnableExternalMemoryWin32 = true` and `RequiredDeviceLuid` set to D3D11 adapter LUID
    - Build engine via `EngineBuilder.Create(context).WithDefaultNodes(renderToSwapchain: false).Build()`
    - Add `RenderToFinalNode` with `B8G8R8A8Unorm` format
    - Set `RenderContext.FinalOutputTexture` to the imported texture handle
    - _Requirements: 4.1, 4.3, 4.4, 4.5, 4.6, 3.4, 3.5_

  - [x] 8.4 Implement HelixViewport (WPF) — render loop
    - Subscribe to `CompositionTarget.Rendering`
    - In `OnRendering`: call `Engine.RenderOffscreen()`, submit command buffer via `IContext.Submit(cmdBuf, TextureHandle.Null)`, wait idle
    - Lock `D3DImage`, set back buffer to D3D9 surface, add dirty rect, unlock
    - Skip frame if `D3DImage.IsFrontBufferAvailable` is false
    - _Requirements: 4.7_

  - [x] 8.5 Implement HelixViewport (WPF) — resize and disposal
    - Implement `OnSizeChanged`: wait for Vulkan device idle, release size-dependent resources (D3D9 surface, D3D11 texture, Vulkan image), recreate at new size, update `RenderContext.WindowSize` and `FinalOutputTexture`
    - Skip rendering if width or height is zero
    - Implement `OnUnloaded` and `Dispose()`: unsubscribe from `CompositionTarget.Rendering`, dispose Engine, RenderContext, WorldDataProvider, interop resources, D3D9 device/context
    - Guard against double dispose with `_disposed` flag
    - _Requirements: 4.8, 4.9, 8.1, 8.2, 8.3, 9.2, 9.3, 9.4_

- [x] 9. Checkpoint — Ensure WPF project compiles
  - Ensure all tests pass, ask the user if questions arise.

- [x] 10. Create the HelixToolkit.Nex.WinUI project and HelixViewport control
  - [x] 10.1 Create WinUI project structure and .csproj
    - Create `Source/HelixToolkit-Nex/HelixToolkit.Nex.WinUI/` directory
    - Create `HelixToolkit.Nex.WinUI.csproj` targeting `net8.0-windows10.0.22621.0` with `Microsoft.WindowsAppSDK` dependency
    - Add project references to `HelixToolkit.Nex.Engine` and `HelixToolkit.Nex.Interop.DirectX`
    - Add the project to `HelixToolkit.Nex.sln`
    - _Requirements: 10.3, 10.4, 10.5_

  - [x] 10.2 Implement HelixViewport (WinUI) — initialization and resource creation
    - Create `HelixViewport` class extending `UserControl` with `SwapChainPanel`
    - Implement `OnLoaded`: create DXGI swap chain for composition, get back buffer, set swap chain on `SwapChainPanel` via `ISwapChainPanelNative`
    - Create D3D11 render target texture with `SharedNthandle` + `SharedKeyedmutex` flags, obtain NT handle, import into Vulkan as `R8G8B8A8Unorm`
    - Create headless VulkanContext with `EnableExternalMemoryWin32 = true` and `RequiredDeviceLuid` set to D3D11 adapter LUID
    - Build engine via `EngineBuilder.Create(context).WithDefaultNodes(renderToSwapchain: false).Build()`
    - Add `RenderToFinalNode` with `R8G8B8A8Unorm` format
    - Set `RenderContext.FinalOutputTexture` to the imported texture handle
    - Obtain `IDXGIKeyedMutex` from the shared render target texture
    - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5, 5.6, 3.4, 3.5, 6.1_

  - [x] 10.3 Implement HelixViewport (WinUI) — render loop with keyed mutex sync
    - Subscribe to `CompositionTarget.Rendering`
    - In `OnRendering`: call `Engine.RenderOffscreen()`, submit command buffer with `VkWin32KeyedMutexAcquireReleaseInfoKHR` (acquire key=0, release key=1)
    - Acquire D3D11 keyed mutex (key=1), `CopyResource` render target to back buffer, release keyed mutex (key=0), call `swapchain.Present()`
    - If keyed mutex acquire times out, log warning and skip frame
    - _Requirements: 5.7, 6.1, 6.2, 6.3, 6.4_

  - [x] 10.4 Implement HelixViewport (WinUI) — resize and disposal
    - Implement `OnSizeChanged`: wait for Vulkan device idle, release size-dependent resources (swap chain buffers, keyed mutex, D3D11 textures, Vulkan image), recreate at new size, update `RenderContext.WindowSize` and `FinalOutputTexture`
    - Skip rendering if width or height is zero
    - Implement `OnUnloaded` and `Dispose()`: unsubscribe from `CompositionTarget.Rendering`, dispose Engine, RenderContext, WorldDataProvider, interop resources, swap chain, DXGI factory/adapter
    - Guard against double dispose with `_disposed` flag
    - _Requirements: 5.8, 5.9, 8.1, 8.2, 8.3, 9.2, 9.3, 9.4_

- [x] 11. Checkpoint — Ensure WinUI project compiles
  - Ensure all tests pass, ask the user if questions arise.

- [x] 12. Create test project and write remaining tests
  - [x] 12.1 Create HelixToolkit.Nex.Interop.DirectX.Tests project
    - Create `Source/HelixToolkit-Nex/HelixToolkit.Nex.Interop.DirectX.Tests/` directory
    - Create `.csproj` using `MSTest.Sdk/3.6.4`, targeting `net8.0-windows`
    - Add NuGet dependency on `FsCheck` for property-based tests
    - Add project reference to `HelixToolkit.Nex.Interop.DirectX`
    - Add the project to `HelixToolkit.Nex.sln`
    - _Requirements: 10.4_

  - [x] 12.2 Write property test: FinalOutputTexture round-trip (Property 4)
    - **Property 4: FinalOutputTexture round-trip**
    - Generate random `TextureHandle` indices, set `RenderContext.FinalOutputTexture`, read back, verify equality
    - Use FsCheck with MSTest; minimum 100 iterations
    - **Validates: Requirements 7.2**

  - [x] 12.3 Write property test: Resize updates WindowSize and FinalOutputTexture (Property 5)
    - **Property 5: Resize updates WindowSize and FinalOutputTexture**
    - Generate random valid resize dimensions (width > 0, height > 0), verify `RenderContext.WindowSize` equals new dimensions and `FinalOutputTexture` is non-null after resize
    - Use FsCheck with MSTest; minimum 100 iterations; requires GPU — mark with `[TestCategory("GPU")]`
    - **Validates: Requirements 4.8, 5.8, 8.2**

  - [x] 12.4 Write unit tests for format mapping
    - Verify WPF path uses `B8G8R8A8Unorm` and WinUI path uses `R8G8B8A8Unorm`
    - Verify WPF uses `D3D11TextureKmtBit` handle type and WinUI uses `D3D11TextureBit`
    - _Requirements: 4.4, 5.4_

  - [x] 12.5 Write unit test for zero-size guard
    - Verify that resize with width=0 or height=0 skips resource creation
    - _Requirements: 8.3_

  - [x] 12.6 Write property test: Interop layer disposal releases all handles (Property 7)
    - **Property 7: Interop layer disposal releases all handles**
    - Create full interop stack (D3D11DeviceManager + SharedTextureResult + ImportedVulkanTexture), dispose all, verify handles are null
    - Requires GPU — mark with `[TestCategory("GPU")]`
    - Use FsCheck with MSTest; minimum 100 iterations
    - **Validates: Requirements 9.1**

- [x] 13. Final checkpoint — Ensure all projects compile and tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- The existing test projects use MSTest (not xUnit); property-based tests use FsCheck with MSTest integration
- The `vulkan-interop-directx` reference project at `c:\Users\luncihua\Documents\GitHub\vulkan-interop-directx` is read-only reference — do not modify it
- All new project files go under `helix-toolkit-nex/Source/HelixToolkit-Nex/`
- Property tests validate universal correctness properties from the design document
- GPU-dependent tests are marked with `[TestCategory("GPU")]` for conditional CI execution
