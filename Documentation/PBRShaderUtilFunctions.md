# psPBRTemplate.glsl Utility Functions

```glsl
PBRProperties getPBRProperties()
```
- **Description**: Retrieves the PBR (Physically Based Rendering) properties for the current material using the material buffer address.
- **Return Type**: `PBRProperties`

```glsl
uint64_t getTimeMs()
```
- **Description**: Returns the current time in milliseconds from the frame constant buffer.
- **Return Type**: `uint64_t`

```glsl
mat4 getViewProjection()
```
- **Description**: Retrieves the view-projection matrix from the frame constant buffer, used for transforming coordinates from world space to clip space.
- **Return Type**: `mat4`

```glsl
mat4 getInvViewProjection()
```
- **Description**: Retrieves the inverse of the view-projection matrix, useful for transforming coordinates from clip space back to world space.
- **Return Type**: `mat4`

```glsl
mat4 getView()
```
- **Description**: Retrieves the view matrix from the frame constant buffer, used for transforming coordinates from world space to view space.
- **Return Type**: `mat4`

```glsl
mat4 getInvView()
```
- **Description**: Retrieves the inverse view matrix, useful for transforming coordinates from view space back to world space.
- **Return Type**: `mat4`

```glsl
vec3 getCameraPosition()
```
- **Description**: Returns the camera's position in world space from the frame constant buffer.
- **Return Type**: `vec3`

```glsl
vec2 getScreenSize()
```
- **Description**: Retrieves the screen dimensions (width and height) from the frame constant buffer.
- **Return Type**: `vec2`

```glsl
bool isPointerRingEnabled()
```
- **Description**: Checks if the pointer ring effect is enabled by evaluating the enabled flag in the pointer ring structure.
- **Return Type**: `bool`

```glsl
vec3 getPointerRayDirection()
```
- **Description**: Retrieves the direction of the pointer ray from the pointer ring structure.
- **Return Type**: `vec3`

```glsl
vec3 getPointerRayOrigin()
```
- **Description**: Retrieves the origin of the pointer ray from the pointer ring structure.
- **Return Type**: `vec3`

```glsl
float getPointerRingOuterDistThreshold()
```
- **Description**: Returns the outer distance threshold for the pointer ring effect, used to determine the outer boundary of the ring.
- **Return Type**: `float`

```glsl
float getPointerRingInnerDistThreshold()
```
- **Description**: Returns the inner distance threshold for the pointer ring effect, used to determine the inner boundary of the ring.
- **Return Type**: `float`

```glsl
float getPointerRingColorMix()
```
- **Description**: Retrieves the color mix factor for the pointer ring effect, determining how much the ring color should blend with the fragment color.
- **Return Type**: `float`

```glsl
vec3 getPointerRingColor()
```
- **Description**: Retrieves the color of the pointer ring from the pointer ring structure.
- **Return Type**: `vec3`

```glsl
float getFragToPointerRayDistance()
```
- **Description**: Calculates the distance from the fragment's world position to the closest point on the pointer ray, used to determine if the fragment is within the pointer ring.
- **Return Type**: `float`

```glsl
bool isInPointerRing()
```
- **Description**: Determines if the fragment is within the pointer ring by checking if the distance to the pointer ray is between the inner and outer distance thresholds.
- **Return Type**: `bool`

```glsl
vec4 mixWithPointerRing(in vec4 color)
```
- **Description**: Blends the input color with the pointer ring color if the pointer ring effect is enabled and the fragment is within the ring. The blending is modulated by the surface normal and view direction to ensure natural shading.
- **Return Type**: `vec4`
