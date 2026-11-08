# psLineTemplate.glsl Utility Functions

## Texture and Vertex Attribute Accessors

### `vec2 getUV()`

```glsl
vec2 getUV()
```

Returns the interpolated texture coordinates passed from the vertex stage (`v_uv`).

**Return type:** `vec2`

---

### `vec4 getColor()`

```glsl
vec4 getColor()
```

Returns the interpolated vertex color (`v_color`).

**Return type:** `vec4`

---

### `float getLineWidth()`

```glsl
float getLineWidth()
```

Returns the interpolated screen-space size value (`v_screenSize`) that the vertex stage supplies for the line.

**Return type:** `float`

---

### `uint getTextureId()`

```glsl
uint getTextureId()
```

Returns the texture index (`v_textureIndex`) associated with the fragment.

**Return type:** `uint`

---

### `uint getSamplerId()`

```glsl
uint getSamplerId()
```

Returns the sampler index (`v_samplerIndex`) associated with the fragment.

**Return type:** `uint`

---

## Frame Constant Accessors

These functions read per-frame data from the `fpConst` push constant block.

### `uint64_t getTimeMs()`

```glsl
uint64_t getTimeMs()
```

Returns the frame time in milliseconds (`fpConst.timeMs`).

**Return type:** `uint64_t`

---

### `mat4 getViewProjection()`

```glsl
mat4 getViewProjection()
```

Returns the combined view-projection matrix (`fpConst.viewProjection`).

**Return type:** `mat4`

---

### `mat4 getInvViewProjection()`

```glsl
mat4 getInvViewProjection()
```

Returns the inverse view-projection matrix (`fpConst.inverseViewProjection`).

**Return type:** `mat4`

---

### `mat4 getView()`

```glsl
mat4 getView()
```

Returns the view matrix (`fpConst.view`).

**Return type:** `mat4`

---

### `mat4 getInvView()`

```glsl
mat4 getInvView()
```

Returns the inverse view matrix (`fpConst.inverseView`).

**Return type:** `mat4`

---

### `vec3 getCameraPosition()`

```glsl
vec3 getCameraPosition()
```

Returns the camera position in world space (`fpConst.cameraPosition`).

**Return type:** `vec3`

---

### `vec2 getScreenSize()`

```glsl
vec2 getScreenSize()
```

Returns the render target dimensions in pixels (`fpConst.screenDimensions`).

**Return type:** `vec2`

---

## Pointer Ring Accessors

These functions read the pointer ring settings from `fpConst.pointerRing`.

### `bool isPointerRingEnabled()`

```glsl
bool isPointerRingEnabled()
```

Returns `true` when the pointer ring feature is enabled, that is, when `fpConst.pointerRing.enabled` is non-zero.

**Return type:** `bool`

---

### `vec3 getPointerRayDirection()`

```glsl
vec3 getPointerRayDirection()
```

Returns the world-space direction of the pointer ray (`fpConst.pointerRing.rayDirection`). The value is not normalized by this function.

**Return type:** `vec3`

---

### `vec3 getPointerRayOrigin()`

```glsl
vec3 getPointerRayOrigin()
```

Returns the world-space origin of the pointer ray (`fpConst.pointerRing.rayOrigin`).

**Return type:** `vec3`

---

### `float getPointerRingOuterDistThreshold()`

```glsl
float getPointerRingOuterDistThreshold()
```

Returns the maximum fragment-to-ray distance that still falls inside the ring (`fpConst.pointerRing.outerDistThreshold`).

**Return type:** `float`

---

### `float getPointerRingInnerDistThreshold()`

```glsl
float getPointerRingInnerDistThreshold()
```

Returns the minimum fragment-to-ray distance that falls inside the ring (`fpConst.pointerRing.innerDistThreshold`).

**Return type:** `float`

---

### `float getPointerRingColorMix()`

```glsl
float getPointerRingColorMix()
```

Returns the blend factor used when mixing the ring color into the fragment color (`fpConst.pointerRing.colorMix`).

**Return type:** `float`

---

### `vec3 getPointerRingColor()`

```glsl
vec3 getPointerRingColor()
```

Returns the RGB color of the pointer ring (`fpConst.pointerRing.color`).

**Return type:** `vec3`

---

### `float getFragToPointerRayDistance()`

```glsl
float getFragToPointerRayDistance()
```

Computes the distance from the fragment's world position (`v_fragWorldPos`) to the pointer ray. The ray direction is normalized internally. The closest point is clamped to the ray origin (`t >= 0`), so the function measures distance to a ray rather than an infinite line.

**Return type:** `float`

---

### `bool isInPointerRing()`

```glsl
bool isInPointerRing()
```

Returns `true` when the fragment's distance to the pointer ray lies within the inclusive range `[innerDistThreshold, outerDistThreshold]`.

**Return type:** `bool`

---

### `vec4 mixWithPointerRing(in vec4 color)`

```glsl
vec4 mixWithPointerRing(in vec4 color)
```

Blends the pointer ring color into the RGB channels of `color` when the ring is enabled and the fragment lies within it. The blend uses `getPointerRingColorMix()`. The alpha channel is unchanged. If the condition is not met, the input color is returned unmodified.

**Return type:** `vec4`

---

## Line Rendering Functions

### `const float LINE_OUTSIDE`

```glsl
const float LINE_OUTSIDE = 1e30;
```

Sentinel distance value returned by `getLineShapeDistance()` when a join assigns the fragment to the neighboring segment. This is a constant, not a function.

---

### `vec2 getFragLinePx()`

```glsl
vec2 getFragLinePx()
```

Converts the fragment position from `gl_FragCoord` into line pixel space. The result is centered on the viewport origin, with +Y pointing up, which matches the coordinate system used by `vsLine.glsl`. The conversion inverts the Y-flipped viewport. Because both segments of a joint derive their position from `gl_FragCoord`, they compute bit-identical positions for the shared bisector test.

**Return type:** `vec2`

---

### `bool isBeyondJoin(uint mode, vec2 q, vec2 u, vec2 v, bool isSegmentEnd)`

```glsl
bool isBeyondJoin(uint mode, vec2 q, vec2 u, vec2 v, bool isSegmentEnd)
```

Determines whether the fragment lies on the neighboring segment's side of a join's angle bisector.

- `mode`: the end mode of this segment at the joint.
- `q`: the fragment position relative to the joint.
- `u`: this segment's direction out of the joint.
- `v`: the neighbor's direction away from the joint.
- `isSegmentEnd`: selects the comparison strictness.

The function returns `false` if `mode` is not a line join mode. Otherwise it evaluates `dot(q, u + v)`. The segment end uses `>= 0.0`, and the segment start uses `> 0.0`. This opposite strictness assigns a fragment that lies exactly on the bisector to exactly one of the two segments.

**Return type:** `bool`

---

### `float shapeLineEnd(float d, uint mode, vec2 q, vec2 u, vec2 v, float halfWidth)`

```glsl
float shapeLineEnd(float d, uint mode, vec2 q, vec2 u, vec2 v, float halfWidth)
```

Applies one end's cap or join shape to the body distance `d`. The parameters `q`, `u`, and `v` follow the same meaning as in `isBeyondJoin`. The `mode` parameter selects the shape:

| Mode | Behavior |
|---|---|
| `LINE_END_BUTT` | Flat cut at the endpoint. |
| `LINE_END_SQUARE` | Flat cut extended by `halfWidth`. |
| `LINE_END_ROUND`, `LINE_END_ROUND_JOIN` | Semicircular cap or round join. |
| `LINE_END_BEVEL_JOIN` | Straight bevel across the outer corners. A straight continuation (`u == v`) returns `d` unchanged. |
| Any other value (miter) | Returns `d` unchanged. The body strip extended to the bisector forms the miter tip. |

**Return type:** `float`

---

### `float getLineShapeDistance()`

```glsl
float getLineShapeDistance()
```

Computes the signed distance, in pixels, from the fragment to the edge of this segment's share of the stroke. The result includes caps and joins. Negative values are inside the stroke.

The function reads the segment geometry from `v_segPx`, `v_dirHw`, `v_adjDir`, and `v_endModes`. It returns `LINE_OUTSIDE` when a join assigns the fragment to a neighboring segment. The half width is interpolated along the segment. This allows the two ends to differ in world render space.

**Return type:** `float`

---

### `float getLineCoverage()`

```glsl
float getLineCoverage()
```

Returns anti-aliased coverage for the stroke shape. The function computes `clamp(0.5 - getLineShapeDistance(), 0.0, 1.0)`, which gives a 1-pixel ramp centered on the edge. The result is 1.0 inside the stroke and 0.0 outside it.

**Return type:** `float`

<!-- source-hash: ce0bb5d0929b2d1a -->
