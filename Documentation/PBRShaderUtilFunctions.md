# psPBRTemplate.glsl Utility Functions

```glsl
PBRProperties getPBRProperties()
```
- **Description**: Retrieves the PBR (Physically Based Rendering) properties for the current material. This function accesses the material buffer using the material ID to obtain the relevant properties.
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
- **Description**: Returns the view matrix, which transforms world coordinates to camera space. This matrix is essential for rendering scenes from the camera's perspective.
- **Return Type**: `mat4`

```glsl
mat4 getInvView()
```
- **Description**: Provides the inverse of the view matrix, allowing transformation from camera space back to world coordinates.
- **Return Type**: `mat4`

```glsl
vec3 getCameraPosition()
```
- **Description**: Retrieves the current position of the camera in world space. This is often used for lighting calculations and effects that depend on the viewer's position.
- **Return Type**: `vec3`

```glsl
vec2 getScreenSize()
```
- **Description**: Returns the dimensions of the screen or viewport. This information is crucial for screen-space effects and calculations.
- **Return Type**: `vec2`

```glsl
bool isPointerRingEnabled()
```
- **Description**: Checks if the pointer ring effect is enabled. This effect is typically used for highlighting or interacting with objects in the scene.
- **Return Type**: `bool`

```glsl
vec3 getPointerRayDirection()
```
- **Description**: Retrieves the direction of the pointer ray in world space. This is used for raycasting or determining interactions with objects.
- **Return Type**: `vec3`

```glsl
vec3 getPointerRayOrigin()
```
- **Description**: Returns the origin of the pointer ray in world space. This point is the starting position for raycasting operations.
- **Return Type**: `vec3`

```glsl
float getPointerRingOuterDistThreshold()
```
- **Description**: Provides the outer distance threshold for the pointer ring effect. This defines the maximum distance from the pointer ray at which the effect is visible.
- **Return Type**: `float`

```glsl
float getPointerRingInnerDistThreshold()
```
- **Description**: Returns the inner distance threshold for the pointer ring effect. This defines the minimum distance from the pointer ray at which the effect begins.
- **Return Type**: `float`

```glsl
float getPointerRingColorMix()
```
- **Description**: Retrieves the mix factor for blending the pointer ring color with the base color. This controls the intensity of the pointer ring effect.
- **Return Type**: `float`

```glsl
vec3 getPointerRingColor()
```
- **Description**: Returns the color of the pointer ring effect. This color is blended with the base color of the fragment when the effect is active.
- **Return Type**: `vec3`

```glsl
float getFragToPointerRayDistance()
```
- **Description**: Calculates the distance from the current fragment to the closest point on the pointer ray. This is used to determine if the fragment is within the pointer ring.
- **Return Type**: `float`

```glsl
bool isInPointerRing()
```
- **Description**: Determines if the current fragment is within the bounds of the pointer ring effect, based on its distance to the pointer ray.
- **Return Type**: `bool`

```glsl
vec4 mixWithPointerRing(in vec4 color)
```
- **Description**: Blends the input color with the pointer ring color if the pointer ring effect is enabled and the fragment is within the ring. This function modulates the ring's brightness based on the surface normal and view direction.
- **Return Type**: `vec4`
