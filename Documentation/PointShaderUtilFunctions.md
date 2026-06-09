# psPointTemplate.glsl Utility Functions

```glsl
uint64_t getTimeMs()
```
- **Description**: Retrieves the current time in milliseconds from the shader's constant buffer.
- **Return Type**: `uint64_t`

```glsl
mat4 getViewProjection()
```
- **Description**: Returns the view-projection matrix, which transforms world coordinates to clip space.
- **Return Type**: `mat4`

```glsl
mat4 getInvViewProjection()
```
- **Description**: Provides the inverse of the view-projection matrix, useful for transforming clip space coordinates back to world coordinates.
- **Return Type**: `mat4`

```glsl
mat4 getView()
```
- **Description**: Retrieves the view matrix, which transforms world coordinates to view space.
- **Return Type**: `mat4`

```glsl
mat4 getInvView()
```
- **Description**: Returns the inverse of the view matrix, allowing transformation from view space back to world coordinates.
- **Return Type**: `mat4`

```glsl
vec3 getCameraPosition()
```
- **Description**: Obtains the position of the camera in world space.
- **Return Type**: `vec3`

```glsl
vec2 getScreenSize()
```
- **Description**: Provides the dimensions of the screen in pixels.
- **Return Type**: `vec2`

```glsl
bool isPointerRingEnabled()
```
- **Description**: Checks if the pointer ring effect is enabled.
- **Return Type**: `bool`

```glsl
vec3 getPointerRayDirection()
```
- **Description**: Retrieves the direction vector of the pointer ray in world space.
- **Return Type**: `vec3`

```glsl
vec3 getPointerRayOrigin()
```
- **Description**: Returns the origin point of the pointer ray in world space.
- **Return Type**: `vec3`

```glsl
float getPointerRingOuterDistThreshold()
```
- **Description**: Provides the outer distance threshold for the pointer ring effect.
- **Return Type**: `float`

```glsl
float getPointerRingInnerDistThreshold()
```
- **Description**: Retrieves the inner distance threshold for the pointer ring effect.
- **Return Type**: `float`

```glsl
float getPointerRingColorMix()
```
- **Description**: Returns the mix factor used to blend the pointer ring color with the fragment color.
- **Return Type**: `float`

```glsl
vec3 getPointerRingColor()
```
- **Description**: Provides the color used for the pointer ring effect.
- **Return Type**: `vec3`

```glsl
float getFragToPointerRayDistance()
```
- **Description**: Calculates the distance from the fragment's world position to the closest point on the pointer ray.
- **Return Type**: `float`

```glsl
bool isInPointerRing()
```
- **Description**: Determines if the fragment is within the pointer ring based on the distance thresholds.
- **Return Type**: `bool`

```glsl
vec4 mixWithPointerRing(in vec4 color)
```
- **Description**: Blends the given color with the pointer ring color if the pointer ring is enabled and the fragment is within the ring.
- **Return Type**: `vec4`
