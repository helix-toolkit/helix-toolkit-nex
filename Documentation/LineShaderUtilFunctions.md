# psLineTemplate.glsl Utility Functions

### Vertex Attribute Accessors

#### `getUV`

```glsl
vec2 getUV()
```

Returns the interpolated texture coordinates (`v_uv`) for the current fragment.

**Return type:** `vec2`

#### `getColor`

```glsl
vec4 getColor()
```

Returns the interpolated vertex color (`v_color`) for the current fragment.

**Return type:** `vec4`

#### `getLineWidth`

```glsl
float getLineWidth()
```

Returns the screen-size value (`v_screenSize`) passed from the vertex stage for the current line primitive.

**Return type:** `float`

#### `getTextureId`

```glsl
uint getTextureId()
```

Returns the texture index (`v_textureIndex`) associated with the current fragment, used to identify the texture to sample.

**Return type:** `uint`

#### `getSamplerId`

```glsl
uint getSamplerId()
```

Returns the sampler index (`v_samplerIndex`) associated with the current fragment, used to identify the sampler to use.

**Return type:** `uint`

### Frame Constant Accessors

These functions read from the `fpConst` frame constant data. Matrix values are column-major, as is standard for GLSL. The C# side of the engine stores matrices in row-major order, so layout conversion is handled at the boundary.

#### `getTimeMs`

```glsl
uint64_t getTimeMs()
```

Returns the current time in milliseconds from `fpConst.timeMs`.

**Return type:** `uint64_t`

#### `getViewProjection`

```glsl
mat4 getViewProjection()
```

Returns the combined view-projection matrix from `fpConst.viewProjection`.

**Return type:** `mat4`

#### `getInvViewProjection`

```glsl
mat4 getInvViewProjection()
```

Returns the inverse view-projection matrix from `fpConst.inverseViewProjection`. Useful for reconstructing world-space positions from screen-space coordinates.

**Return type:** `mat4`

#### `getView`

```glsl
mat4 getView()
```

Returns the view matrix from `fpConst.view`.

**Return type:** `mat4`

#### `getInvView`

```glsl
mat4 getInvView()
```

Returns the inverse view matrix from `fpConst.inverseView`.

**Return type:** `mat4`

#### `getCameraPosition`

```glsl
vec3 getCameraPosition()
```

Returns the camera position from `fpConst.cameraPosition`.

**Return type:** `vec3`

#### `getScreenSize`

```glsl
vec2 getScreenSize()
```

Returns the screen dimensions from `fpConst.screenDimensions`.

**Return type:** `vec2`

### Pointer Ring Accessors

These functions read pointer ring configuration from `fpConst.pointerRing`.

#### `isPointerRingEnabled`

```glsl
bool isPointerRingEnabled()
```

Returns `true` when the pointer ring feature is enabled, that is, when `fpConst.pointerRing.enabled` is nonzero.

**Return type:** `bool`

#### `getPointerRayDirection`

```glsl
vec3 getPointerRayDirection()
```

Returns the pointer ray direction from `fpConst.pointerRing.rayDirection`.

**Return type:** `vec3`

#### `getPointerRayOrigin`

```glsl
vec3 getPointerRayOrigin()
```

Returns the pointer ray origin from `fpConst.pointerRing.rayOrigin`.

**Return type:** `vec3`

#### `getPointerRingOuterDistThreshold`

```glsl
float getPointerRingOuterDistThreshold()
```

Returns the outer distance threshold from `fpConst.pointerRing.outerDistThreshold`. Fragments farther from the pointer ray than this value are outside the ring.

**Return type:** `float`

#### `getPointerRingInnerDistThreshold`

```glsl
float getPointerRingInnerDistThreshold()
```

Returns the inner distance threshold from `fpConst.pointerRing.innerDistThreshold`. Fragments closer to the pointer ray than this value are inside the inner edge of the ring.

**Return type:** `float`

#### `getPointerRingColorMix`

```glsl
float getPointerRingColorMix()
```

Returns the blend factor from `fpConst.pointerRing.colorMix` that controls how strongly the ring color is blended into the fragment color.

**Return type:** `float`

#### `getPointerRingColor`

```glsl
vec3 getPointerRingColor()
```

Returns the pointer ring color from `fpConst.pointerRing.color`.

**Return type:** `vec3`

### Pointer Ring Computation

#### `getFragToPointerRayDistance`

```glsl
float getFragToPointerRayDistance()
```

Computes the shortest distance from the current fragment's world position (`v_fragWorldPos`) to the pointer ray. The projection parameter is clamped to zero, so the ray is treated as a half-line that starts at the ray origin and extends in the ray direction. Points behind the origin are measured to the origin itself.

**Return type:** `float`

#### `isInPointerRing`

```glsl
bool isInPointerRing()
```

Returns `true` when the fragment's distance to the pointer ray lies within the closed interval `[innerDistThreshold, outerDistThreshold]`, meaning the fragment falls inside the ring band.

**Return type:** `bool`

#### `mixWithPointerRing`

```glsl
vec4 mixWithPointerRing(in vec4 color)
```

Applies the pointer ring highlight to an input color. When the pointer ring is enabled and the fragment is inside the ring band, the RGB channels are linearly interpolated toward the ring color by the color mix factor. The alpha channel is left unchanged. If either condition is false, the input color is returned unmodified.

**Parameters:**
- `color` (`in vec4`): The input fragment color.

**Return type:** `vec4`

<!-- source-hash: e38a66d55a91bcda -->
