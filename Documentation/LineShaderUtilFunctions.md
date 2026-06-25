# psLineTemplate.glsl Utility Functions

```glsl
vec2 getUV()
```
- **Description**: Retrieves the UV coordinates for the current fragment.
- **Return Type**: `vec2`

```glsl
vec4 getColor()
```
- **Description**: Retrieves the color associated with the current fragment.
- **Return Type**: `vec4`

```glsl
float getLineWidth()
```
- **Description**: Retrieves the line width in screen space for the current fragment.
- **Return Type**: `float`

```glsl
uint getTextureId()
```
- **Description**: Retrieves the texture ID for the current fragment.
- **Return Type**: `uint`

```glsl
uint getSamplerId()
```
- **Description**: Retrieves the sampler ID for the current fragment.
- **Return Type**: `uint`

```glsl
uint64_t getTimeMs()
```
- **Description**: Retrieves the current time in milliseconds from the frame parameters.
- **Return Type**: `uint64_t`

```glsl
mat4 getViewProjection()
```
- **Description**: Retrieves the view-projection matrix from the frame parameters.
- **Return Type**: `mat4`

```glsl
mat4 getInvViewProjection()
```
- **Description**: Retrieves the inverse view-projection matrix from the frame parameters.
- **Return Type**: `mat4`

```glsl
mat4 getView()
```
- **Description**: Retrieves the view matrix from the frame parameters.
- **Return Type**: `mat4`

```glsl
mat4 getInvView()
```
- **Description**: Retrieves the inverse view matrix from the frame parameters.
- **Return Type**: `mat4`

```glsl
vec3 getCameraPosition()
```
- **Description**: Retrieves the camera position from the frame parameters.
- **Return Type**: `vec3`

```glsl
vec2 getScreenSize()
```
- **Description**: Retrieves the screen dimensions from the frame parameters.
- **Return Type**: `vec2`

```glsl
bool isPointerRingEnabled()
```
- **Description**: Checks if the pointer ring effect is enabled.
- **Return Type**: `bool`

```glsl
vec3 getPointerRayDirection()
```
- **Description**: Retrieves the direction of the pointer ray from the frame parameters.
- **Return Type**: `vec3`

```glsl
vec3 getPointerRayOrigin()
```
- **Description**: Retrieves the origin of the pointer ray from the frame parameters.
- **Return Type**: `vec3`

```glsl
float getPointerRingOuterDistThreshold()
```
- **Description**: Retrieves the outer distance threshold for the pointer ring effect.
- **Return Type**: `float`

```glsl
float getPointerRingInnerDistThreshold()
```
- **Description**: Retrieves the inner distance threshold for the pointer ring effect.
- **Return Type**: `float`

```glsl
float getPointerRingColorMix()
```
- **Description**: Retrieves the color mix factor for the pointer ring effect.
- **Return Type**: `float`

```glsl
vec3 getPointerRingColor()
```
- **Description**: Retrieves the color of the pointer ring effect.
- **Return Type**: `vec3`

```glsl
float getFragToPointerRayDistance()
```
- **Description**: Calculates the distance from the fragment to the closest point on the pointer ray.
- **Return Type**: `float`

```glsl
bool isInPointerRing()
```
- **Description**: Determines if the fragment is within the pointer ring's distance thresholds.
- **Return Type**: `bool`

```glsl
vec4 mixWithPointerRing(in vec4 color)
```
- **Description**: Mixes the fragment color with the pointer ring color if the pointer ring effect is enabled and the fragment is within the ring.
- **Return Type**: `vec4`
