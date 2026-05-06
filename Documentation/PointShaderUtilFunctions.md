# psPointTemplate.glsl Utility Functions

```glsl
uint64_t getTimeMs()
```
- **Purpose**: Retrieves the current time in milliseconds.
- **Return Type**: `uint64_t`

```glsl
mat4 getViewProjection()
```
- **Purpose**: Obtains the view-projection matrix used for transforming coordinates from world space to clip space.
- **Return Type**: `mat4`

```glsl
mat4 getInvViewProjection()
```
- **Purpose**: Retrieves the inverse of the view-projection matrix, useful for transforming coordinates from clip space back to world space.
- **Return Type**: `mat4`

```glsl
mat4 getView()
```
- **Purpose**: Returns the view matrix, which transforms coordinates from world space to camera space.
- **Return Type**: `mat4`

```glsl
mat4 getInvView()
```
- **Purpose**: Provides the inverse view matrix, allowing transformation from camera space back to world space.
- **Return Type**: `mat4`

```glsl
vec3 getCameraPosition()
```
- **Purpose**: Retrieves the position of the camera in world space.
- **Return Type**: `vec3`

```glsl
vec2 getScreenSize()
```
- **Purpose**: Returns the dimensions of the screen or viewport.
- **Return Type**: `vec2`

```glsl
bool isPointerRingEnabled()
```
- **Purpose**: Checks if the pointer ring feature is enabled.
- **Return Type**: `bool`

```glsl
vec3 getPointerRayDirection()
```
- **Purpose**: Retrieves the direction of the pointer ray in world space.
- **Return Type**: `vec3`

```glsl
vec3 getPointerRayOrigin()
```
- **Purpose**: Provides the origin point of the pointer ray in world space.
- **Return Type**: `vec3`

```glsl
float getPointerRingOuterDistThreshold()
```
- **Purpose**: Returns the outer distance threshold for the pointer ring, used to determine the ring's boundary.
- **Return Type**: `float`

```glsl
float getPointerRingInnerDistThreshold()
```
- **Purpose**: Retrieves the inner distance threshold for the pointer ring, defining the inner boundary of the ring.
- **Return Type**: `float`

```glsl
float getPointerRingColorMix()
```
- **Purpose**: Provides the mix factor used to blend the pointer ring color with the fragment color.
- **Return Type**: `float`

```glsl
vec3 getPointerRingColor()
```
- **Purpose**: Returns the color of the pointer ring.
- **Return Type**: `vec3`

```glsl
float getFragToPointerRayDistance()
```
- **Purpose**: Calculates the distance from the current fragment position to the closest point on the pointer ray.
- **Return Type**: `float`

```glsl
bool isInPointerRing()
```
- **Purpose**: Determines if the current fragment is within the bounds of the pointer ring.
- **Return Type**: `bool`

```glsl
vec4 mixWithPointerRing(in vec4 color)
```
- **Purpose**: Blends the input color with the pointer ring color if the pointer ring is enabled and the fragment is within the ring.
- **Return Type**: `vec4`
