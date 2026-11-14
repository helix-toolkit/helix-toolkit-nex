# psPBRTemplate.glsl Utility Functions

## Material Access

### `getPBRProperties`

```glsl
PBRProperties getPBRProperties()
```

Retrieves the PBR material properties for the current fragment. It casts `fpConst.materialBufferAddress` to a `MaterialBuffer` and indexes it with `materialId`.

**Return type:** `PBRProperties`

## Frame Constants

### `getTimeMs`

```glsl
uint64_t getTimeMs()
```

Returns the current time in milliseconds from the fragment push constants (`fpConst.timeMs`).

**Return type:** `uint64_t`

## Camera and Transform Accessors

### `getViewProjection`

```glsl
mat4 getViewProjection()
```

Returns the combined view-projection matrix (`fpConst.viewProjection`).

**Return type:** `mat4`

### `getInvViewProjection`

```glsl
mat4 getInvViewProjection()
```

Returns the inverse of the combined view-projection matrix (`fpConst.inverseViewProjection`).

**Return type:** `mat4`

### `getView`

```glsl
mat4 getView()
```

Returns the view matrix (`fpConst.view`).

**Return type:** `mat4`

### `getInvView`

```glsl
mat4 getInvView()
```

Returns the inverse view matrix (`fpConst.inverseView`).

**Return type:** `mat4`

### `getCameraPosition`

```glsl
vec3 getCameraPosition()
```

Returns the camera position in world space (`fpConst.cameraPosition`).

**Return type:** `vec3`

### `getScreenSize`

```glsl
vec2 getScreenSize()
```

Returns the screen dimensions in pixels (`fpConst.screenDimensions`).

**Return type:** `vec2`

### `getViewDirection`

```glsl
vec3 getViewDirection()
```

Returns the normalized direction from the current fragment's world position toward the camera. It is computed as `normalize(getCameraPosition() - fragWorldPos)`.

**Return type:** `vec3`

## Pointer Ring

The pointer ring is a screen-space highlight drawn around the ray defined by the pointer. The accessors below read its settings from `fpConst.pointerRing`.

### `isPointerRingEnabled`

```glsl
bool isPointerRingEnabled()
```

Returns `true` when the pointer ring is enabled (`fpConst.pointerRing.enabled != 0`).

**Return type:** `bool`

### `getPointerRayDirection`

```glsl
vec3 getPointerRayDirection()
```

Returns the pointer ray direction (`fpConst.pointerRing.rayDirection`). The value is not normalized by this function.

**Return type:** `vec3`

### `getPointerRayOrigin`

```glsl
vec3 getPointerRayOrigin()
```

Returns the origin of the pointer ray (`fpConst.pointerRing.rayOrigin`).

**Return type:** `vec3`

### `getPointerRingOuterDistThreshold`

```glsl
float getPointerRingOuterDistThreshold()
```

Returns the outer distance threshold of the ring (`fpConst.pointerRing.outerDistThreshold`). A fragment is inside the ring when its distance to the pointer ray is at or below this value.

**Return type:** `float`

### `getPointerRingInnerDistThreshold`

```glsl
float getPointerRingInnerDistThreshold()
```

Returns the inner distance threshold of the ring (`fpConst.pointerRing.innerDistThreshold`). A fragment is inside the ring when its distance to the pointer ray is at or above this value.

**Return type:** `float`

### `getPointerRingColorMix`

```glsl
float getPointerRingColorMix()
```

Returns the blend factor used to mix the ring color into the surface color (`fpConst.pointerRing.colorMix`). Used by `mixWithPointerRing`.

**Return type:** `float`

### `getPointerRingColor`

```glsl
vec3 getPointerRingColor()
```

Returns the base color of the pointer ring (`fpConst.pointerRing.color`).

**Return type:** `vec3`

### `getFragToPointerRayDistance`

```glsl
float getFragToPointerRayDistance()
```

Computes the shortest distance from the current fragment's world position (`fragWorldPos`) to the pointer ray. The ray is treated as a half-line starting at the ray origin, so the closest point is clamped to the origin when the fragment lies behind it.

**Return type:** `float`

### `isInPointerRing`

```glsl
bool isInPointerRing()
```

Returns `true` when the fragment's distance to the pointer ray lies between the inner and outer distance thresholds, inclusive.

**Return type:** `bool`

### `mixWithPointerRing`

```glsl
vec4 mixWithPointerRing(in vec4 color)
```

Blends the pointer ring color into the input color when the pointer ring is enabled and the fragment lies inside the ring. The alpha channel is unchanged.

When `EXCLUDE_MESH_PROPS` is not defined, the ring color is scaled by `mix(0.2, 1.0, NdotV)`, where `NdotV` is the clamped dot product of the normalized `fragNormal` and the view direction. This shades the ring across curved geometry. When `EXCLUDE_MESH_PROPS` is defined, the unmodulated ring color is used.

**Return type:** `vec4`

## Texture Coordinate Selection

These functions are compiled only when `WIREFRAME_PASS` is not defined and `EXCLUDE_MESH_PROPS` is not defined.

### `selectTexCoord`

```glsl
vec2 selectTexCoord(uint texCoordSets, uint slot)
```

Selects the texture coordinate set for a material texture slot. It returns `fragTexCoord1` (TEXCOORD_1) if the bit `slot` is set in `texCoordSets`, and `fragTexCoord` (TEXCOORD_0) otherwise.

The `slot` argument is one of the following bit constants, which mirror the C# `PBRTextureSlot` enum:

| Constant | Value |
|---|---|
| `UV_SLOT_ALBEDO` | `1u` |
| `UV_SLOT_NORMAL` | `2u` |
| `UV_SLOT_METALLIC_ROUGHNESS` | `4u` |
| `UV_SLOT_AO` | `8u` |
| `UV_SLOT_BUMP` | `16u` |
| `UV_SLOT_EMISSIVE` | `32u` |
| `UV_SLOT_THICKNESS` | `64u` |

**Return type:** `vec2`

<!-- source-hash: 7f3709c090c86893 -->
