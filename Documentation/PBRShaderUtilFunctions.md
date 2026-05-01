# psPBRTemplate.glsl Utility Functions

```glsl
PBRProperties getPBRProperties()
```
- **Description**: Retrieves the PBR (Physically Based Rendering) properties for the current material. This function accesses the `MaterialBuffer` using the `materialBufferAddress` and returns the properties associated with the `materialId`.
- **Return Type**: `PBRProperties`

```glsl
uint64_t getTimeMs()
```
- **Description**: Returns the current time in milliseconds. This can be used for animations or time-based effects within the shader.
- **Return Type**: `uint64_t`

```glsl
mat4 getViewProjection()
```
- **Description**: Retrieves the view-projection matrix. This matrix is used to transform world coordinates into clip space.
- **Return Type**: `mat4`

```glsl
mat4 getInvViewProjection()
```
- **Description**: Retrieves the inverse of the view-projection matrix. This is useful for transforming coordinates from clip space back to world space.
- **Return Type**: `mat4`

```glsl
mat4 getView()
```
- **Description**: Retrieves the view matrix, which transforms world coordinates to view (camera) space.
- **Return Type**: `mat4`

```glsl
mat4 getInvView()
```
- **Description**: Retrieves the inverse of the view matrix. This is used to transform coordinates from view space back to world space.
- **Return Type**: `mat4`

```glsl
vec3 getCameraPosition()
```
- **Description**: Returns the position of the camera in world space. This is often used in lighting calculations and effects that depend on the camera's position.
- **Return Type**: `vec3`

```glsl
vec2 getScreenSize()
```
- **Description**: Retrieves the dimensions of the screen. This can be used for screen-space calculations and effects.
- **Return Type**: `vec2`

```glsl
bool isPointerRingEnabled()
```
- **Description**: Checks if the pointer ring effect is enabled. This effect is used to highlight or interact with objects in the scene.
- **Return Type**: `bool`

```glsl
vec3 getPointerRayDirection()
```
- **Description**: Returns the direction of the pointer ray in world space. This is used for raycasting and interaction with objects.
- **Return Type**: `vec3`

```glsl
vec3 getPointerRayOrigin()
```
- **Description**: Returns the origin of the pointer ray in world space. This is the starting point for raycasting.
- **Return Type**: `vec3`

```glsl
float getPointerRingOuterDistThreshold()
```
- **Description**: Retrieves the outer distance threshold for the pointer ring effect. This defines the maximum distance from the pointer ray at which the effect is visible.
- **Return Type**: `float`

```glsl
float getPointerRingInnerDistThreshold()
```
- **Description**: Retrieves the inner distance threshold for the pointer ring effect. This defines the minimum distance from the pointer ray at which the effect is visible.
- **Return Type**: `float`

```glsl
float getPointerRingColorMix()
```
- **Description**: Returns the mix factor for blending the pointer ring color with the underlying color. This controls the intensity of the pointer ring effect.
- **Return Type**: `float`

```glsl
vec3 getPointerRingColor()
```
- **Description**: Retrieves the color of the pointer ring effect. This color is used to highlight objects within the ring.
- **Return Type**: `vec3`

```glsl
float getFragToPointerRayDistance()
```
- **Description**: Calculates the distance from the fragment's world position to the closest point on the pointer ray. This is used to determine if a fragment is within the pointer ring.
- **Return Type**: `float`

```glsl
bool isInPointerRing()
```
- **Description**: Determines if the current fragment is within the pointer ring based on its distance to the pointer ray and the defined thresholds.
- **Return Type**: `bool`

```glsl
vec4 mixWithPointerRing(in vec4 color)
```
- **Description**: Blends the input color with the pointer ring color if the pointer ring effect is enabled and the fragment is within the ring. This function modulates the ring brightness based on the surface normal and view direction for natural shading.
- **Return Type**: `vec4`
