# psLineTemplate.glsl Utility Functions

```glsl
vec2 getUV()
```
Returns the UV coordinates for the current fragment.  
**Return Type:** `vec2`

```glsl
vec4 getColor()
```
Returns the color associated with the current fragment.  
**Return Type:** `vec4`

```glsl
float getLineWidth()
```
Returns the line width in screen space for the current fragment.  
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
Returns the current time in milliseconds.  
**Return Type:** `uint64_t`

```glsl
mat4 getViewProjection()
```
Returns the view-projection matrix. This matrix transforms coordinates from world space to clip space.  
**Return Type:** `mat4`

```glsl
mat4 getInvViewProjection()
```
Returns the inverse of the view-projection matrix. This matrix transforms coordinates from clip space back to world space.  
**Return Type:** `mat4`

```glsl
mat4 getView()
```
Returns the view matrix, which transforms coordinates from world space to view space.  
**Return Type:** `mat4`

```glsl
mat4 getInvView()
```
Returns the inverse of the view matrix, which transforms coordinates from view space back to world space.  
**Return Type:** `mat4`

```glsl
vec3 getCameraPosition()
```
Returns the position of the camera in world space.  
**Return Type:** `vec3`

```glsl
vec2 getScreenSize()
```
Returns the dimensions of the screen.  
**Return Type:** `vec2`

```glsl
bool isPointerRingEnabled()
```
Checks if the pointer ring effect is enabled.  
**Return Type:** `bool`

```glsl
vec3 getPointerRayDirection()
```
Returns the direction of the pointer ray in world space.  
**Return Type:** `vec3`

```glsl
vec3 getPointerRayOrigin()
```
Returns the origin of the pointer ray in world space.  
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
Returns the mix factor for blending the pointer ring color with the fragment color.  
**Return Type:** `float`

```glsl
vec3 getPointerRingColor()
```
Returns the color of the pointer ring.  
**Return Type:** `vec3`

```glsl
float getFragToPointerRayDistance()
```
Calculates the distance from the fragment to the closest point on the pointer ray.  
**Return Type:** `float`

```glsl
bool isInPointerRing()
```
Determines if the fragment is within the pointer ring based on the distance thresholds.  
**Return Type:** `bool`

```glsl
vec4 mixWithPointerRing(in vec4 color)
```
Mixes the fragment color with the pointer ring color if the pointer ring effect is enabled and the fragment is within the ring.  
**Return Type:** `vec4`
