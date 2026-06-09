# psPBRTemplate.glsl Utility Functions

```glsl
PBRProperties getPBRProperties()
```
- **Description**: Retrieves the PBR (Physically Based Rendering) properties for the current material from the material buffer.
- **Return Type**: `PBRProperties`

```glsl
uint64_t getTimeMs()
```
- **Description**: Returns the current time in milliseconds as provided by the frame constant buffer.
- **Return Type**: `uint64_t`

```glsl
mat4 getViewProjection()
```
- **Description**: Retrieves the view-projection matrix from the frame constant buffer, used for transforming world coordinates to clip space.
- **Return Type**: `mat4`

```glsl
mat4 getInvViewProjection()
```
- **Description**: Retrieves the inverse of the view-projection matrix, useful for transforming clip space coordinates back to world coordinates.
- **Return Type**: `mat4`

```glsl
mat4 getView()
```
- **Description**: Retrieves the view matrix from the frame constant buffer, which transforms world coordinates to view space.
- **Return Type**: `mat4`

```glsl
mat4 getInvView()
```
- **Description**: Retrieves the inverse view matrix, useful for transforming view space coordinates back to world coordinates.
- **Return Type**: `mat4`

```glsl
vec3 getCameraPosition()
```
- **Description**: Returns the camera's position in world space as provided by the frame constant buffer.
- **Return Type**: `vec3`

```glsl
vec2 getScreenSize()
```
- **Description**: Retrieves the dimensions of the screen in pixels from the frame constant buffer.
- **Return Type**: `vec2`

```glsl
bool isPointerRingEnabled()
```
- **Description**: Checks if the pointer ring effect is enabled, based on the frame constant buffer settings.
- **Return Type**: `bool`

```glsl
vec3 getPointerRayDirection()
```
- **Description**: Retrieves the direction of the pointer ray in world space, used for pointer-based interactions.
- **Return Type**: `vec3`

```glsl
vec3 getPointerRayOrigin()
```
- **Description**: Retrieves the origin of the pointer ray in world space.
- **Return Type**: `vec3`

```glsl
float getPointerRingOuterDistThreshold()
```
- **Description**: Returns the outer distance threshold for the pointer ring effect, defining the maximum distance from the ray for the effect to be visible.
- **Return Type**: `float`

```glsl
float getPointerRingInnerDistThreshold()
```
- **Description**: Returns the inner distance threshold for the pointer ring effect, defining the minimum distance from the ray for the effect to start.
- **Return Type**: `float`

```glsl
float getPointerRingColorMix()
```
- **Description**: Retrieves the mix factor for blending the pointer ring color with the underlying surface color.
- **Return Type**: `float`

```glsl
vec3 getPointerRingColor()
```
- **Description**: Retrieves the color of the pointer ring effect.
- **Return Type**: `vec3`

```glsl
float getFragToPointerRayDistance()
```
- **Description**: Calculates the distance from the current fragment's world position to the closest point on the pointer ray.
- **Return Type**: `float`

```glsl
bool isInPointerRing()
```
- **Description**: Determines if the current fragment is within the pointer ring effect's distance thresholds.
- **Return Type**: `bool`

```glsl
vec4 mixWithPointerRing(in vec4 color)
```
- **Description**: Blends the input color with the pointer ring color if the pointer ring effect is enabled and the fragment is within the ring's thresholds.
- **Return Type**: `vec4`
