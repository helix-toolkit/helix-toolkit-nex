# psPointTemplate.glsl Utility Functions

```glsl
vec2 getUV()
```
Returns the UV coordinates of the current fragment.  
**Return Type:** `vec2`

```glsl
vec4 getColor()
```
Returns the color associated with the current fragment.  
**Return Type:** `vec4`

```glsl
float getPointSize()
```
Returns the screen size of the point being rendered.  
**Return Type:** `float`

```glsl
uint getTextureId()
```
Returns the texture ID used for the current fragment.  
**Return Type:** `uint`

```glsl
uint getSamplerId()
```
Returns the sampler ID used for the current fragment.  
**Return Type:** `uint`

```glsl
uint64_t getTimeMs()
```
Returns the current time in milliseconds from a constant buffer.  
**Return Type:** `uint64_t`

```glsl
mat4 getViewProjection()
```
Returns the view-projection matrix from a constant buffer.  
**Return Type:** `mat4`

```glsl
mat4 getInvViewProjection()
```
Returns the inverse of the view-projection matrix from a constant buffer.  
**Return Type:** `mat4`

```glsl
mat4 getView()
```
Returns the view matrix from a constant buffer.  
**Return Type:** `mat4`

```glsl
mat4 getInvView()
```
Returns the inverse of the view matrix from a constant buffer.  
**Return Type:** `mat4`

```glsl
vec3 getCameraPosition()
```
Returns the camera position from a constant buffer.  
**Return Type:** `vec3`

```glsl
vec2 getScreenSize()
```
Returns the screen dimensions from a constant buffer.  
**Return Type:** `vec2`

```glsl
bool isPointerRingEnabled()
```
Checks if the pointer ring effect is enabled.  
**Return Type:** `bool`

```glsl
vec3 getPointerRayDirection()
```
Returns the direction of the pointer ray from a constant buffer.  
**Return Type:** `vec3`

```glsl
vec3 getPointerRayOrigin()
```
Returns the origin of the pointer ray from a constant buffer.  
**Return Type:** `vec3`

```glsl
float getPointerRingOuterDistThreshold()
```
Returns the outer distance threshold for the pointer ring effect.  
**Return Type:** `float`

```glsl
float getPointerRingInnerDistThreshold()
```
Returns the inner distance threshold for the pointer ring effect.  
**Return Type:** `float`

```glsl
float getPointerRingColorMix()
```
Returns the color mix factor for the pointer ring effect.  
**Return Type:** `float`

```glsl
vec3 getPointerRingColor()
```
Returns the color of the pointer ring effect.  
**Return Type:** `vec3`

```glsl
float getFragToPointerRayDistance()
```
Calculates the distance from the fragment to the closest point on the pointer ray.  
**Return Type:** `float`

```glsl
bool isInPointerRing()
```
Determines if the fragment is within the pointer ring effect's thresholds.  
**Return Type:** `bool`

```glsl
vec4 mixWithPointerRing(in vec4 color)
```
Mixes the input color with the pointer ring color if the effect is enabled and the fragment is within the ring.  
**Return Type:** `vec4`
