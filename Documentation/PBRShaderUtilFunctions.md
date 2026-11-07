# psPBRTemplate.glsl Utility Functions

## Material Access

### `getPBRProperties`

```glsl
PBRProperties getPBRProperties()
```

Retrieves the PBR material properties for the current fragment. It casts `fpConst.materialBufferAddress` to a `MaterialBuffer` and returns the entry at `materialId`.

**Return type:** `PBRProperties`

## Timing

### `getTimeMs`

```glsl
uint64_t getTimeMs()
```

Returns the current time in milliseconds from the fragment push constants (`fpConst.timeMs`).

**Return type:** `uint64_t`

## Camera and Transform Matrices

Matrices returned by these functions are GLSL column-major. The C# side of the engine stores matrices row-major, so the values are uploaded to the shader accordingly.

### `getViewProjection`

```glsl
mat4 getViewProjection()
```

Returns the combined view-projection matrix from `fpConst.viewProjection`.

**Return type:** `mat4`

### `getInvViewProjection`

```glsl
mat4 getInvViewProjection()
```

Returns the inverse of the view-projection matrix from `fpConst.inverseViewProjection`. Useful for reconstructing world-space positions from screen-space or depth values.

**Return type:** `mat4`

### `getView`

```glsl
mat4 getView()
```

Returns the view matrix from `fpConst.view`.

**Return type:** `mat4`

### `getInvView`

```glsl
mat4 getInvView()
```

Returns the inverse view matrix from `fpConst.inverseView`.

**Return type:** `mat4`

## Camera and Screen Information

### `getCameraPosition`

```glsl
vec3 getCameraPosition()
```

Returns the camera position in world space from `fpConst.cameraPosition`.

**Return type:** `vec3`

### `getScreenSize`

```glsl
vec2 getScreenSize()
```

Returns the render target dimensions from `fpConst.screenDimensions`.

**Return type:** `vec2`

### `getViewDirection`

```glsl
vec3 getViewDirection()
```

Returns the normalized direction from the current fragment's world position toward the camera, computed as `normalize(getCameraPosition() - fragWorldPos)`.

**Return type:** `vec3`

## Pointer Ring

The pointer ring is a screen-space visual indicator that highlights geometry near the pointer's ray. The functions below read its configuration from `fpConst.pointerRing`.

### `isPointerRingEnabled`

```glsl
bool isPointerRingEnabled()
```

Returns `true` if the pointer ring is enabled (`fpConst.pointerRing.enabled != 0`).

**Return type:** `bool`

### `getPointerRayDirection`

```glsl
vec3 getPointerRayDirection()
```

Returns the pointer ray direction from `fpConst.pointerRing.rayDirection`.

**Return type:** `vec3`

### `getPointerRayOrigin`

```glsl
vec3 getPointerRayOrigin()
```

Returns the pointer ray origin from `fpConst.pointerRing.rayOrigin`.

**Return type:** `vec3`

### `getPointerRingOuterDistThreshold`

```glsl
float getPointerRingOuterDistThreshold()
```

Returns the outer distance threshold, the maximum fragment-to-ray distance that is still inside the ring. Read from `fpConst.pointerRing.outerDistThreshold`.

**Return type:** `float`

### `getPointerRingInnerDistThreshold`

```glsl
float getPointerRingInnerDistThreshold()
```

Returns the inner distance threshold, the minimum fragment-to-ray distance for the ring. Read from `fpConst.pointerRing.innerDistThreshold`.

**Return type:** `float`

### `getPointerRingColorMix`

```glsl
float getPointerRingColorMix()
```

Returns the blend factor used when mixing the ring color with the surface color. Read from `fpConst.pointerRing.colorMix`.

**Return type:** `float`

### `getPointerRingColor`

```glsl
vec3 getPointerRingColor()
```

Returns the base color of the pointer ring from `fpConst.pointerRing.color`.

**Return type:** `vec3`

### `getFragToPointerRayDistance`

```glsl
float getFragToPointerRayDistance()
```

Computes the shortest distance from the current fragment's world position to the pointer ray. The ray is treated as a half-line starting at the ray origin, so the closest point is clamped to the origin when the fragment lies behind it.

**Return type:** `float`

### `isInPointerRing`

```glsl
bool isInPointerRing()
```

Returns `true` if the current fragment's distance to the pointer ray lies between the inner and outer distance thresholds, inclusive.

**Return type:** `bool`

### `mixWithPointerRing`

```glsl
vec4 mixWithPointerRing(in vec4 color)
```

Blends the pointer ring color into the input color when the pointer ring is enabled and the fragment is within the ring. The blend amount is `getPointerRingColorMix()`, and only the RGB channels are modified. The alpha channel is preserved.

When `EXCLUDE_MESH_PROPS` is not defined, the ring brightness is modulated by the surface normal relative to the view direction. The ring color is scaled by `mix(0.2, 1.0, NdotV)`, where `NdotV` is the clamped dot product of the normalized `fragNormal` and the view direction. This lets the ring shade naturally across curved geometry. When `EXCLUDE_MESH_PROPS` is defined, this shading is skipped and the base ring color is used directly.

**Parameters:**

| Name | Type | Qualifier | Description |
|------|------|-----------|-------------|
| `color` | `vec4` | `in` | The input surface color. |

**Return type:** `vec4`

<!-- source-hash: 5d1e502ea557467e -->
