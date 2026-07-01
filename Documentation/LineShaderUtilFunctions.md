# psLineTemplate.glsl Utility Functions

```glsl
vec2 getUV()
```
- **Purpose**: Retrieves the UV coordinates for the current fragment.
- **Return Type**: `vec2`

```glsl
vec4 getColor()
```
- **Purpose**: Retrieves the color associated with the current fragment.
- **Return Type**: `vec4`

```glsl
float getLineWidth()
```
- **Purpose**: Retrieves the line width in screen space for the current fragment.
- **Return Type**: `float`

```glsl
uint getTextureId()
```
- **Purpose**: Retrieves the texture ID for the current fragment.
- **Return Type**: `uint`

```glsl
uint getSamplerId()
```
- **Purpose**: Retrieves the sampler ID for the current fragment.
- **Return Type**: `uint`

```glsl
uint64_t getTimeMs()
```
- **Purpose**: Retrieves the current time in milliseconds.
- **Return Type**: `uint64_t`

```glsl
mat4 getViewProjection()
```
- **Purpose**: Retrieves the view-projection matrix.
- **Return Type**: `mat4`

```glsl
mat4 getInvViewProjection()
```
- **Purpose**: Retrieves the inverse of the view-projection matrix.
- **Return Type**: `mat4`

```glsl
mat4 getView()
```
- **Purpose**: Retrieves the view matrix.
- **Return Type**: `mat4`

```glsl
mat4 getInvView()
```
- **Purpose**: Retrieves the inverse of the view matrix.
- **Return Type**: `mat4`

```glsl
vec3 getCameraPosition()
```
- **Purpose**: Retrieves the camera's position in world space.
- **Return Type**: `vec3`

```glsl
vec2 getScreenSize()
```
- **Purpose**: Retrieves the dimensions of the screen.
- **Return Type**: `vec2`

```glsl
bool isPointerRingEnabled()
```
- **Purpose**: Checks if the pointer ring effect is enabled.
- **Return Type**: `bool`

```glsl
vec3 getPointerRayDirection()
```
- **Purpose**: Retrieves the direction of the pointer ray.
- **Return Type**: `vec3`

```glsl
vec3 getPointerRayOrigin()
```
- **Purpose**: Retrieves the origin of the pointer ray.
- **Return Type**: `vec3`

```glsl
float getPointerRingOuterDistThreshold()
```
- **Purpose**: Retrieves the outer distance threshold for the pointer ring effect.
- **Return Type**: `float`

```glsl
float getPointerRingInnerDistThreshold()
```
- **Purpose**: Retrieves the inner distance threshold for the pointer ring effect.
- **Return Type**: `float`

```glsl
float getPointerRingColorMix()
```
- **Purpose**: Retrieves the mix factor for blending the pointer ring color with the fragment color.
- **Return Type**: `float`

```glsl
vec3 getPointerRingColor()
```
- **Purpose**: Retrieves the color of the pointer ring.
- **Return Type**: `vec3`

```glsl
float getFragToPointerRayDistance()
```
- **Purpose**: Calculates the distance from the fragment to the closest point on the pointer ray.
- **Return Type**: `float`

```glsl
bool isInPointerRing()
```
- **Purpose**: Determines if the fragment is within the pointer ring's distance thresholds.
- **Return Type**: `bool`

```glsl
vec4 mixWithPointerRing(in vec4 color)
```
- **Purpose**: Blends the fragment color with the pointer ring color if the pointer ring effect is enabled and the fragment is within the ring.
- **Return Type**: `vec4`
