# psPointTemplate.glsl Utility Functions

```glsl
uint64_t getTimeMs()
```
- **Purpose**: Retrieves the current time in milliseconds.
- **Return Type**: `uint64_t`

```glsl
mat4 getViewProjection()
```
- **Purpose**: Returns the view-projection matrix used for transforming world coordinates to clip space.
- **Return Type**: `mat4`

```glsl
mat4 getInvViewProjection()
```
- **Purpose**: Provides the inverse of the view-projection matrix, useful for transforming clip space coordinates back to world coordinates.
- **Return Type**: `mat4`

```glsl
mat4 getView()
```
- **Purpose**: Retrieves the view matrix, which transforms world coordinates to view space.
- **Return Type**: `mat4`

```glsl
mat4 getInvView()
```
- **Purpose**: Returns the inverse of the view matrix, allowing transformation from view space back to world coordinates.
- **Return Type**: `mat4`

```glsl
vec3 getCameraPosition()
```
- **Purpose**: Provides the current position of the camera in world space.
- **Return Type**: `vec3`

```glsl
vec2 getScreenSize()
```
- **Purpose**: Retrieves the dimensions of the screen in pixels.
- **Return Type**: `vec2`

```glsl
bool isPointerRingEnabled()
```
- **Purpose**: Checks if the pointer ring feature is enabled.
- **Return Type**: `bool`

```glsl
vec3 getPointerRayDirection()
```
- **Purpose**: Returns the direction vector of the pointer ray in world space.
- **Return Type**: `vec3`

```glsl
vec3 getPointerRayOrigin()
```
- **Purpose**: Provides the origin point of the pointer ray in world space.
- **Return Type**: `vec3`

```glsl
float getPointerRingOuterDistThreshold()
```
- **Purpose**: Retrieves the outer distance threshold for the pointer ring effect.
- **Return Type**: `float`

```glsl
float getPointerRingInnerDistThreshold()
```
- **Purpose**: Returns the inner distance threshold for the pointer ring effect.
- **Return Type**: `float`

```glsl
float getPointerRingColorMix()
```
- **Purpose**: Provides the mix factor for blending the pointer ring color with the fragment color.
- **Return Type**: `float`

```glsl
vec3 getPointerRingColor()
```
- **Purpose**: Retrieves the color used for the pointer ring effect.
- **Return Type**: `vec3`

```glsl
float getFragToPointerRayDistance()
```
- **Purpose**: Calculates the distance from the fragment position to the closest point on the pointer ray.
- **Return Type**: `float`

```glsl
bool isInPointerRing()
```
- **Purpose**: Determines if the current fragment is within the pointer ring's distance thresholds.
- **Return Type**: `bool`

```glsl
vec4 mixWithPointerRing(in vec4 color)
```
- **Purpose**: Blends the input color with the pointer ring color if the pointer ring is enabled and the fragment is within the ring.
- **Return Type**: `vec4`
