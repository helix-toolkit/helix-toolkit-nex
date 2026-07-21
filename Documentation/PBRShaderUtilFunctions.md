# psPBRTemplate.glsl Utility Functions

```glsl
PBRProperties getPBRProperties()
```
- **Description**: Retrieves the PBR (Physically Based Rendering) properties for the current material. This function accesses the material buffer using the material ID to obtain the relevant properties.
- **Return Type**: `PBRProperties`

```glsl
uint64_t getTimeMs()
```
- **Description**: Returns the current time in milliseconds. This is useful for time-based animations or effects.
- **Return Type**: `uint64_t`

```glsl
mat4 getViewProjection()
```
- **Description**: Retrieves the view-projection matrix, which is used to transform world coordinates to clip space.
- **Return Type**: `mat4`

```glsl
mat4 getInvViewProjection()
```
- **Description**: Retrieves the inverse of the view-projection matrix. This is often used for transforming coordinates from clip space back to world space.
- **Return Type**: `mat4`

```glsl
mat4 getView()
```
- **Description**: Returns the view matrix, which transforms world coordinates to view space.
- **Return Type**: `mat4`

```glsl
mat4 getInvView()
```
- **Description**: Returns the inverse of the view matrix, used for transforming coordinates from view space back to world space.
- **Return Type**: `mat4`

```glsl
vec3 getCameraPosition()
```
- **Description**: Retrieves the position of the camera in world space.
- **Return Type**: `vec3`

```glsl
vec2 getScreenSize()
```
- **Description**: Returns the dimensions of the screen or viewport.
- **Return Type**: `vec2`

```glsl
bool isPointerRingEnabled()
```
- **Description**: Checks if the pointer ring feature is enabled. This feature is typically used for visual effects related to pointer interactions.
- **Return Type**: `bool`

```glsl
vec3 getPointerRayDirection()
```
- **Description**: Retrieves the direction of the ray associated with the pointer ring, used for intersection tests.
- **Return Type**: `vec3`

```glsl
vec3 getPointerRayOrigin()
```
- **Description**: Returns the origin of the ray associated with the pointer ring in world space.
- **Return Type**: `vec3`

```glsl
float getPointerRingOuterDistThreshold()
```
- **Description**: Retrieves the outer distance threshold for the pointer ring, used to determine the ring's extent.
- **Return Type**: `float`

```glsl
float getPointerRingInnerDistThreshold()
```
- **Description**: Retrieves the inner distance threshold for the pointer ring, used to determine the ring's inner boundary.
- **Return Type**: `float`

```glsl
float getPointerRingColorMix()
```
- **Description**: Returns the mix factor for blending the pointer ring color with the underlying color.
- **Return Type**: `float`

```glsl
vec3 getPointerRingColor()
```
- **Description**: Retrieves the color of the pointer ring.
- **Return Type**: `vec3`

```glsl
float getFragToPointerRayDistance()
```
- **Description**: Calculates the distance from the fragment's world position to the closest point on the pointer ray.
- **Return Type**: `float`

```glsl
bool isInPointerRing()
```
- **Description**: Determines if the current fragment is within the bounds of the pointer ring based on distance thresholds.
- **Return Type**: `bool`

```glsl
vec3 getViewDirection()
```
- **Description**: Computes the normalized direction vector from the camera position to the fragment's world position.
- **Return Type**: `vec3`

```glsl
vec4 mixWithPointerRing(in vec4 color)
```
- **Description**: Blends the input color with the pointer ring color if the pointer ring is enabled and the fragment is within the ring. The blending is modulated by the surface normal and view direction to achieve natural shading.
- **Return Type**: `vec4`
