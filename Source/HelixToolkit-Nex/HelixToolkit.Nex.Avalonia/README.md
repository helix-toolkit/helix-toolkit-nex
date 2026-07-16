```markdown
# HelixToolkit.Nex.Avalonia

HelixToolkit.Nex.Avalonia is a C# package that integrates the Vulkan-native HelixToolkit.Nex 3D engine with [Avalonia](https://avaloniaui.net/) applications. It hosts the engine's offscreen Vulkan output inside the Avalonia visual tree on both **Windows** and **Linux**, presenting it through Avalonia's composition GPU-interop layer rather than a WinUI-style `SwapChainPanel` or DXGI swap chain.

## Overview

The engine renders offscreen with Vulkan; each platform shares that output into the Avalonia compositor and presents it through a `CompositionDrawingSurface` driven by `ICompositionGpuInterop` (the same interop surface used by the AvaloniaUI `GpuInterop/D3DDemo` sample).

Two platform composition paths share a single control:

- **Windows** — the engine renders into a D3D11 shared NT-handle texture (created by `SharedTextureFactory`, imported into Vulkan via `VulkanExternalMemoryImporter`). The same NT handle is imported into the Avalonia compositor, and engine writes / composition reads are serialized with keyed-mutex synchronization.
- **Linux** — the engine renders into a Vulkan image whose device memory is exported as external memory (opaque-fd / dma-buf). The exported POSIX file descriptor is imported into the Avalonia Vulkan compositor, and an exported Vulkan semaphore serializes engine writes against composition reads. No OpenGL/EGL or software-composition fallback is used.

The control reuses the shared partial-class viewport logic (`HelixToolkit.Nex.Interop.PlatformShared`) under the `HxAvalonia` compilation symbol — exactly the way the WPF and WinUI hosts do — so input handling, the render loop, and the bindable-property definitions stay consistent across hosts. An Avalonia `StyledProperty` adapter maps the shared WPF/WinUI dependency-property vocabulary onto Avalonia's property system.

The `Engine` is externally owned and shared across viewports; the control creates and owns only its per-viewport `RenderContext` and interop resources, and never disposes the `Engine`.

Key concepts:
- **Composition GPU interop** — engine output is imported into the compositor via `ICompositionGpuInterop` and shown in a `CompositionDrawingSurface`.
- **Platform bridges** — a single `IEngineOutputBridge` abstraction with a Windows shared-texture implementation and a Linux external-memory implementation.
- **Buffered output** — `BufferedEngineOutputBridge` rotates over several independent single-buffer bridges to decouple engine render from compositor read, improving frame rate.
- **Shared partial class** — one `HelixViewport` behavior across WPF/WinUI/Avalonia, selected by the `HxAvalonia` symbol.
- **StyledProperty adapter** — thin shims (`HelixProperty`, `DependencyProperty`, ...) so the shared property definitions compile under Avalonia.

## Key Types

| Type                          | Description                                                                                                   |
| ----------------------------- | ------------------------------------------------------------------------------------------------------------- |
| `HelixViewport`               | The Avalonia control that hosts the HelixToolkit.Nex engine output.                                           |
| `HelixProperty`               | Registers bindable properties on Avalonia's `StyledProperty` system with a WPF/WinUI-style signature.         |
| `DependencyProperty`          | Non-generic handle wrapping an `AvaloniaProperty` plus its change callback (adapter shim).                    |
| `CompositionSurfacePresenter` | Avalonia `Control` that presents the shared image via `CompositionDrawingSurface` + `ICompositionGpuInterop`. |
| `IEngineOutputBridge`         | Abstraction over the per-viewport shared output resource and its synchronization.                             |
| `BufferedEngineOutputBridge`  | Rotates over several independent single-buffer bridges to improve frame rate by decoupling engine render from compositor read. |
| `WindowsSharedTextureBridge`  | Windows path: D3D11 shared NT-handle texture imported into Vulkan, keyed-mutex sync.                          |
| `LinuxExternalMemoryBridge`   | Linux path: exportable Vulkan image (opaque-fd/dma-buf) with an exported semaphore.                           |
| `SharedImageDescription`      | Platform-neutral description of the shared image passed to `ICompositionGpuInterop`.                          |
| `ISurfaceUpdateSync`          | Abstracts the compositor surface update (keyed mutex on Windows, semaphores on Linux).                        |

### Bindable properties on `HelixViewport`

| Property             | Type                  | Default  | Description                                         |
| -------------------- | --------------------- | -------- | --------------------------------------------------- |
| `Engine`             | `Engine?`             | `null`   | The externally-owned engine that renders the scene. |
| `ViewportClient`     | `IViewportClient?`    | `null`   | Supplies per-frame camera and scene data.           |
| `CameraController`   | `ICameraController?`  | `null`   | Translates pointer input into camera movement.      |
| `RotateMouseButton`  | `ViewportMouseButton` | `Left`   | Mouse button that rotates the camera.               |
| `PanMouseButton`     | `ViewportMouseButton` | `Middle` | Mouse button that pans the camera.                  |
| `PointerRingEnabled` | `bool`                | `false`  | Toggles the on-screen pointer ring overlay.         |

## Usage Examples

### Hosting the viewport in XAML

Add the namespace and bind the engine, viewport client, camera controller, and pointer-ring flag:

```xml
<Window
    xmlns="https://github.com/avaloniaui"
    xmlns:hx="clr-namespace:HelixToolkit.Nex.Avalonia;assembly=HelixToolkit.Nex.Avalonia">
    <Grid>
        <hx:HelixViewport
            Engine="{Binding Engine}"
            ViewportClient="{Binding FlyClient}"
            CameraController="{Binding FlyCameraController}"
            PointerRingEnabled="{Binding IsPointerRingEnabled, Mode=OneWay}" />

        <!-- Normal Avalonia controls composite on top of the GPU surface. -->
        <Border HorizontalAlignment="Left" VerticalAlignment="Top" Margin="16"
                Background="#CC1E1E2A" CornerRadius="8" Padding="12">
            <TextBlock Text="Avalonia Interop" Foreground="White" />
        </Border>
    </Grid>
</Window>
```

### Building a platform-appropriate engine

The Vulkan context must be created with the external-memory feature that matches the host platform:

```csharp
#if WINDOWS
using var d3d11 = new D3D11DeviceManager();
IContext context = VulkanBuilder.CreateHeadless(new VulkanContextConfig
{
    EnableExternalMemoryWin32 = true,        // Windows: D3D11 shared-texture interop
    RequiredDeviceLuid = d3d11.AdapterLuid,  // pick the same adapter as D3D11
});
#else
IContext context = VulkanBuilder.CreateHeadless(new VulkanContextConfig
{
    EnableExternalMemoryFd = true,           // Linux: opaque-fd / dma-buf export
});
#endif

var engine = EngineBuilder.Create(context)
    .WithDefaultNodes(renderToSwapchain: false)
    .RenderToCustomTarget(Format.RGBA_UN8)   // matches both platform bridges
    .Build();
```

### Registering a bindable property (shared partial class)

The shared `ViewportProperties.cs` registers properties through `HelixProperty`, which the Avalonia adapter maps onto `StyledProperty`:

```csharp
public static readonly DependencyProperty EngineDp = HelixProperty.Register<HelixViewport, Engine?>(
    "Engine",
    null,
    static (d, e) => ((HelixViewport)d).SetEngine((Engine?)e.NewValue));
```

## Architecture Notes

- **Presentation** — `CompositionSurfacePresenter` obtains the compositor, creates a `CompositionDrawingSurface` + `CompositionSurfaceVisual`, wires it as the element's child visual, and imports the shared image through `ICompositionGpuInterop`. There is no `SwapChainPanel` or DXGI swap chain. If GPU interop is unavailable in the current render session, the presenter logs a warning and skips presentation without throwing.
- **Bridges** — the control selects `WindowsSharedTextureBridge` or `LinuxExternalMemoryBridge` via `OperatingSystem.IsWindows()`. Both implement `IEngineOutputBridge` (render target, import description, engine sync info, surface sync, resize).
- **Buffered Output** — `BufferedEngineOutputBridge` rotates over several independent single-buffer bridges to decouple engine render from compositor read, allowing the frame rate to reach the display refresh rate.
- **Render loop** — frames are driven from the compositor via `TopLevel.RequestAnimationFrame` (with a `DispatcherTimer` fallback), guarded by a valid engine/render-context/viewport-client and a nonzero size. Each tick renders the offscreen frame into the bridge target and presents it.
- **Resize & teardown** — on resize the control waits for engine idle, recreates the shared output at the new size, and updates the camera-controller viewport size. On unload/dispose it releases the `RenderContext` and all interop resources while leaving the `Engine` intact.

## Notes & Gotchas

- **Pointer input requires a hit-test surface.** The 3D output is shown through an attached composition child visual, which is not part of standard visual hit-testing. `HelixViewport` fills its bounds with a transparent brush in `Render` so pointer events reach the control and drive the camera. Interactive Avalonia controls placed over the viewport still receive their own input.
- **Orientation.** The compositor samples the imported image with the opposite vertical origin to the engine's output, so the surface visual is mirrored on the Y axis to present the scene right-side up.
- **Imported image is cached.** Importing a shared GPU texture is expensive, so the presenter imports once and reuses it every frame, re-importing only on the first frame or after a resize. Per-frame work is just the keyed-mutex/semaphore surface update.
- **Build the view model off the UI thread.** Constructing the engine and scene can block (scene building uses a synchronous-over-asynchronous path); doing it on the UI thread can deadlock and prevents the window from appearing. Build it on a background thread and assign the `DataContext` on the UI thread.

## Platform Support

- **Windows** — targets `net8.0-windows` and references `HelixToolkit.Nex.Interop.DirectX` for the D3D11 shared-texture path. Requires a Vulkan context created with `EnableExternalMemoryWin32 = true` and a `RequiredDeviceLuid` matching the D3D11 adapter.
- **Linux** — targets `net8.0` with no DirectX dependency. Requires a Vulkan context created with `EnableExternalMemoryFd = true` (enables `VK_KHR_external_memory_fd`, the optional `VK_EXT_external_memory_dma_buf`, and `VK_KHR_external_semaphore_fd`).

A runnable cross-platform sample is available under `Samples/Interop/Avalonia` (`AvaloniaInterop`), demonstrating the viewport with an engine, a `SceneSamples` scene, an `OrbitCameraController`, and an Avalonia UI overlay composited on top of the 3D output.
```
