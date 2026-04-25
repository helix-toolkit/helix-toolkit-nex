- HelixToolkit-Nex is a 3D Graphics engine implemented in C# on the Vulkan 1.3 API.

- Backgrounds and Instructions:
  * In C# code, all matrices are row major. Please verify all matrix multiplication order in c# code.
  * In glsl code, all matrices are column major. Please verify all matrix multiplication order in glsl code.
  * Use Reverse-Z (near = 1, far = 0) for projection matrix across the rendering engine. Please verify the maths related to depth calculation or depth state settings.
  * Engine uses forward plus light culling.
  * Engine uses GPU based instance culling.
  * Engine uses Entity Component System.
  * Engine uses Render Graph for managing Render Node orders and dependencies during rendering.
  * Engine renders Entity Information (Entity Id, Entity Version, Instancing Index) onto an entity texture (RG_F32) during depth prepass and point cloud rendering. This texture is used for mesh/point picking in screen space.
  * Engine supports render to offscreen texture.
  * Engine supports DirectX interoperation with the Vulkan backend, enabling integration with WPF/WinUI frameworks.
  * Engine uses indirect draw calls wherever possible to minimize CPU overhead and improve rendering performance.
  * RenderContext contains a per-viewport resource set (buffers and textures). Resolution-dependent textures and buffers in this resource set are re-created when the viewport size changes.
  * WorldDataProvider contains per-world data collections. This data set is collected and sent to the GPU for rendering.

- Resource Management
  * All graphics resources (such as `PipelineResource`, `ShaderModuleResource` and `BufferResource`) life cycles should be manually managed. Call `Resource.Dispose()` to dispose the resource if no longer needed.
  * `TextureRepository` and `SamplerRepository` cache GPU resources and return `TextureRef` / `SamplerRef` wrapper objects. Each wrapper holds its `TextureResource` / `SamplerResource` internally and exposes `GetHandle()` as a direct, O(1) property — no repository round-trip. `TextureRef` and `SamplerRef` do **not** implement `IDisposable` and must never be disposed directly. To release a resource, call `TextureRepository.Remove(key)` or `SamplerRepository.Remove(key)`, which disposes the underlying GPU resource and fires the ref's `OnDisposed` event.
  * `TextureRef.OnDisposed` and `SamplerRef.OnDisposed` are `event Action?` callbacks that fire synchronously when the repository disposes the resource. `PBRMaterialProperties` subscribes to these events to automatically zero the corresponding bindless texture/sampler indices when a resource is removed.
  * The `Replace*` family of methods has been removed. To hot-swap a texture, call `Remove(key)` followed by the appropriate `GetOrCreateFrom*` method. This ensures `OnDisposed` fires on the old ref before the new one is created.
  * `ITextureRepository` also exposes async creation methods (`GetOrCreateFromStreamAsync`, `GetOrCreateFromFileAsync`, `GetOrCreateFromImageAsync`) that allocate GPU memory synchronously (so the ref is immediately available in the cache) and upload pixel data asynchronously. Await the returned `Task<TextureRef>` on the main thread before assigning the result to a `PBRMaterialProperties` field.