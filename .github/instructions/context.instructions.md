- HelixToolkit-Nex is a 3D Graphics engine implemented by C# on Vulkan API.

- Backgrounds and Instructions:
  * In C# code, all matrices are row major. Please verify all matrix multiplication order in c# code.
  * In glsl code, all matrices are column major. Please verify all matrix multiplication order in glsl code.
  * Use Reverse-Z for projection matrix across the rendering engine. Please verify the maths related to depth calculation or depth state settings.
  * Engine uses forward plus light culling.
  * Engine uses GPU based instance culling.
  * Engine uses Entity Component System based on [Arch ECS](https://github.com/genaray/Arch) library.
  * Engine uses Render Graph for managing Render Node orders during rendering.
  * Engine renders Entity Information (Entity Id, Entity Version, Instancing Index) onto a entity texture (RG_F32) during depth prepass. This texture is used for mesh picking on screen space.
