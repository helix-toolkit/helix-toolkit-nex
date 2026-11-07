# psPointTemplate.glsl Utility Functions

The functions below are accessors and helpers for the point-template fragment shader. They read per-fragment varyings (`v_*`), the frame constant block (`fpConst`), and the pointer-ring parameters nested in `fpConst.pointerRing`. The variables they reference are declared elsewhere in the shader and are not part of this extract.

## Per-Fragment Inputs

### getUV

```glsl
vec2 getUV()
```

Returns the fragment's texture coordinates, taken from the `v_uv` varying.

**Return type:** `vec2`

### getColor

```glsl
vec4 getColor()
```

Returns the fragment's vertex color, taken from the `v_color` varying.

**Return type:** `vec4`

### getPointSize

```glsl
float getPointSize()
```

Returns the point's screen-space size, taken from the `v_screenSize` varying.

**Return type:** `float`

### getTextureId

```glsl
uint getTextureId()
```

Returns the texture index for the point, taken from the `v_textureIndex` varying.

**Return type:** `uint`

### getSamplerId

```glsl
uint getSamplerId()
```

Returns the sampler index for the point, taken from the `v_samplerIndex` varying.

**Return type:** `uint`

## Frame Constants

### getTimeMs

```glsl
uint64_t getTimeMs()
```

Returns the frame time value stored in `fpConst.timeMs`.

**Return type:** `uint64_t`

### getViewProjection

```glsl
mat4 getViewProjection()
```

Returns the combined view-projection matrix from `fpConst.viewProjection`.

**Return type:** `mat4`

### getInvViewProjection

```glsl
mat4 getInvViewProjection()
```

Returns the inverse view-projection matrix from `fpConst.inverseViewProjection`.

**Return type:** `mat4`

### getView

```glsl
mat4 getView()
```

Returns the view matrix from `fpConst.view`.

**Return type:** `mat4`

### getInvView

```glsl
mat4 getInvView()
```

Returns the inverse view matrix from `fpConst.inverseView`.

**Return type:** `mat4`

> **Note:** GLSL matrices are column-major. The matrices returned by these functions are passed through from the frame constant block without transformation.

### getCameraPosition

```glsl
vec3 getCameraPosition()
```

Returns the camera position from `fpConst.cameraPosition`.

**Return type:** `vec3`

### getScreenSize

```glsl
vec2 getScreenSize()
```

Returns the screen dimensions from `fpConst.screenDimensions`.

**Return type:** `vec2`

## Pointer Ring

The pointer ring is a highlight drawn around the points that lie near a pointer ray. The ring parameters are read from `fpConst.pointerRing`.

### isPointerRingEnabled

```glsl
bool isPointerRingEnabled()
```

Returns `true` when the pointer ring is enabled, that is, when `fpConst.pointerRing.enabled` is nonzero.

**Return type:** `bool`

### getPointerRayDirection

```glsl
vec3 getPointerRayDirection()
```

Returns the pointer ray direction from `fpConst.pointerRing.rayDirection`. The value is not normalized by this function.

**Return type:** `vec3`

### getPointerRayOrigin

```glsl
vec3 getPointerRayOrigin()
```

Returns the pointer ray origin from `fpConst.pointerRing.rayOrigin`.

**Return type:** `vec3`

### getPointerRingOuterDistThreshold

```glsl
float getPointerRingOuterDistThreshold()
```

Returns the outer distance threshold of the ring from `fpConst.pointerRing.outerDistThreshold`. Fragments at or below this distance from the ray are inside the outer edge.

**Return type:** `float`

### getPointerRingInnerDistThreshold

```glsl
float getPointerRingInnerDistThreshold()
```

Returns the inner distance threshold of the ring from `fpConst.pointerRing.innerDistThreshold`. Fragments at or above this distance from the ray are inside the inner edge.

**Return type:** `float`

### getPointerRingColorMix

```glsl
float getPointerRingColorMix()
```

Returns the blend factor used to mix the ring color with the fragment color, from `fpConst.pointerRing.colorMix`. A value of `0.0` leaves the fragment color unchanged, and `1.0` replaces the RGB channels with the ring color.

**Return type:** `float`

### getPointerRingColor

```glsl
vec3 getPointerRingColor()
```

Returns the ring color from `fpConst.pointerRing.color`.

**Return type:** `vec3`

### getFragToPointerRayDistance

```glsl
float getFragToPointerRayDistance()
```

Computes the distance from the fragment's world position (`v_fragWorldPos`) to the pointer ray. The ray direction is normalized before use. The projection parameter `t` is clamped to zero, so the distance is measured to the ray segment that starts at the ray origin and extends in the ray direction, not to the infinite line.

**Return type:** `float`

### isInPointerRing

```glsl
bool isInPointerRing()
```

Returns `true` when the fragment's distance to the pointer ray, as computed by `getFragToPointerRayDistance()`, lies within the inclusive range from the inner to the outer distance threshold.

**Return type:** `bool`

### mixWithPointerRing

```glsl
vec4 mixWithPointerRing(in vec4 color)
```

Applies the pointer ring highlight to an input color. If the pointer ring is enabled and the fragment lies inside the ring, the RGB channels of `color` are linearly mixed with the ring color using `getPointerRingColorMix()`. The alpha channel is not modified. The function returns the input color unchanged otherwise.

**Return type:** `vec4`

<!-- source-hash: 82f1e8087ad8b4e3 -->
