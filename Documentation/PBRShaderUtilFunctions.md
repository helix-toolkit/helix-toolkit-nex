# psPBRTemplate.glsl Utility Functions

```glsl
PBRProperties getPBRProperties()
```
- **Description**: Retrieves the PBR (Physically Based Rendering) properties for the current material. This function accesses the material buffer using the material ID to obtain the relevant material properties.
- **Return Type**: `PBRProperties`

```glsl
uint64_t getTimeMs()
```
- **Description**: Returns the current time in milliseconds. This can be used for animations or time-based effects within the shader.
- **Return Type**: `uint64_t`

```glsl
mat4 getViewProjection()
```
- **Description**: Retrieves the view-projection matrix, which is used to transform world coordinates to clip space. This matrix combines both the view and projection transformations.
- **Return Type**: `mat4`

```glsl
mat4 getInvViewProjection()
```
- **Description**: Retrieves the inverse of the view-projection matrix. This is useful for transforming coordinates from clip space back to world space.
- **Return Type**: `mat4`

```glsl
mat4 getView()
```
- **Description**: Returns the view matrix, which transforms world coordinates to camera space. This matrix represents the camera's position and orientation.
- **Return Type**: `mat4`

```glsl
mat4 getInvView()
```
- **Description**: Returns the inverse of the view matrix. This is used to transform coordinates from camera space back to world space.
- **Return Type**: `mat4`

```glsl
vec3 getCameraPosition()
```
- **Description**: Retrieves the position of the camera in world space. This is often used for lighting calculations and effects that depend on the camera's location.
- **Return Type**: `vec3`

```glsl
vec2 getScreenSize()
```
- **Description**: Returns the dimensions of the screen or viewport. This is useful for calculations that depend on the screen size, such as screen-space effects.
- **Return Type**: `vec2`

```glsl
bool isPointerRingEnabled()
```
- **Description**: Checks if the pointer ring effect is enabled. This effect highlights a ring around the pointer or cursor in the 3D scene.
- **Return Type**: `bool`

```glsl
vec3 getPointerRayDirection()
```
- **Description**: Retrieves the direction of the ray associated with the pointer or cursor. This is used for raycasting and intersection tests.
- **Return Type**: `vec3`

```glsl
vec3 getPointerRayOrigin()
```
- **Description**: Returns the origin of the pointer ray in world space. This is the starting point for raycasting from the pointer's position.
- **Return Type**: `vec3`

```glsl
float getPointerRingOuterDistThreshold()
```
- **Description**: Retrieves the outer distance threshold for the pointer ring effect. This defines the maximum distance from the pointer ray at which the ring effect is applied.
- **Return Type**: `float`

```glsl
float getPointerRingInnerDistThreshold()
```
- **Description**: Retrieves the inner distance threshold for the pointer ring effect. This defines the minimum distance from the pointer ray at which the ring effect begins.
- **Return Type**: `float`

```glsl
float getPointerRingColorMix()
```
- **Description**: Returns the mix factor for blending the pointer ring color with the underlying surface color. This controls the intensity of the ring effect.
- **Return Type**: `float`

```glsl
vec3 getPointerRingColor()
```
- **Description**: Retrieves the color of the pointer ring. This color is used to highlight the area around the pointer in the 3D scene.
- **Return Type**: `vec3`

```glsl
float getFragToPointerRayDistance()
```
- **Description**: Calculates the distance from the current fragment to the closest point on the pointer ray. This is used to determine if the fragment is within the pointer ring.
- **Return Type**: `float`

```glsl
bool isInPointerRing()
```
- **Description**: Determines if the current fragment is within the pointer ring effect based on its distance to the pointer ray and the defined thresholds.
- **Return Type**: `bool`

```glsl
vec4 mixWithPointerRing(in vec4 color)
```
- **Description**: Blends the input color with the pointer ring color if the pointer ring effect is enabled and the fragment is within the ring. This function modulates the ring brightness based on the surface normal and view direction for natural shading.
- **Return Type**: `vec4`
