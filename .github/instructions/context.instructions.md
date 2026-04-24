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
