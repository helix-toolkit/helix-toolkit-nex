# psPBRTemplate.glsl Utility Functions

```glsl
PBRProperties getPBRProperties()
```
- **Description**: Retrieves the PBR (Physically Based Rendering) properties for the current material using the material buffer.
- **Return Type**: `PBRProperties`

```glsl
uint64_t getTimeMs()
```
- **Description**: Returns the current time in milliseconds from the frame constant buffer.
- **Return Type**: `uint64_t`

```glsl
mat4 getViewProjection()
```
- **Description**: Retrieves the view-projection matrix from the frame constant buffer, used for transforming vertices from world space to clip space.
- **Return Type**: `mat4`

```glsl
mat4 getInvViewProjection()
```
- **Description**: Retrieves the inverse of the view-projection matrix, useful for transforming coordinates from clip space back to world space.
- **Return Type**: `mat4`

```glsl
mat4 getView()
```
- **Description**: Retrieves the view matrix from the frame constant buffer, used for transforming vertices from world space to view space.
- **Return Type**: `mat4`

```glsl
mat4 getInvView()
```
- **Description**: Retrieves the inverse of the view matrix, which can be used to transform coordinates from view space back to world space.
- **Return Type**: `mat4`

```glsl
vec3 getCameraPosition()
```
- **Description**: Returns the camera position in world space from the frame constant buffer.
- **Return Type**: `vec3`

```glsl
vec2 getScreenSize()
```
- **Description**: Retrieves the dimensions of the screen from the frame constant buffer.
- **Return Type**: `vec2`

```glsl
bool isPointerRingEnabled()
```
- **Description**: Checks if the pointer ring effect is enabled.
- **Return Type**: `bool`

```glsl
vec3 getPointerRayDirection()
```
- **Description**: Returns the direction of the pointer ray, used for pointer-based interactions.
- **Return Type**: `vec3`

```glsl
vec3 getPointerRayOrigin()
```
- **Description**: Retrieves the origin of the pointer ray in world space.
- **Return Type**: `vec3`

```glsl
float getPointerRingOuterDistThreshold()
```
- **Description**: Returns the outer distance threshold for the pointer ring effect.
- **Return Type**: `float`

```glsl
float getPointerRingInnerDistThreshold()
```
- **Description**: Returns the inner distance threshold for the pointer ring effect.
- **Return Type**: `float`

```glsl
float getPointerRingColorMix()
```
- **Description**: Retrieves the mix factor for blending the pointer ring color with the fragment color.
- **Return Type**: `float`

```glsl
vec3 getPointerRingColor()
```
- **Description**: Returns the color of the pointer ring effect.
- **Return Type**: `vec3`

```glsl
float getFragToPointerRayDistance()
```
- **Description**: Calculates the distance from the fragment position to the closest point on the pointer ray.
- **Return Type**: `float`

```glsl
bool isInPointerRing()
```
- **Description**: Determines if the fragment is within the pointer ring based on distance thresholds.
- **Return Type**: `bool`

```glsl
vec3 getViewDirection()
```
- **Description**: Computes the normalized direction vector from the fragment position to the camera.
- **Return Type**: `vec3`

```glsl
vec4 mixWithPointerRing(in vec4 color)
```
- **Description**: Blends the input color with the pointer ring color if the pointer ring is enabled and the fragment is within the ring.
- **Return Type**: `vec4`
