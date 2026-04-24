# Requirements Document

## Introduction

This feature introduces `TextureRef`, a lightweight wrapper class for GPU texture resources in the HelixToolkit Nex engine. Currently, `ITextureRepository` returns `TextureResource` directly, requiring callers to manage reference counts and leaving them with stale handles when textures are replaced or removed.

`TextureRef` decouples consumers from the underlying `TextureResource` lifecycle by holding a cache key and a back-reference to the repository. When a consumer needs the current `TextureHandle` (e.g. to read the bindless index), it calls `GetHandle()` on the wrapper. The wrapper checks whether its cached `TextureHandle` is still valid; if not, it re-fetches from the repository using the key. This lazy re-fetch approach is simple and correct because handle reads happen on the render thread.

`ITextureRepository` gains `Remove` and `Replace*` operations to support destruction and hot-swapping of textures at runtime. `TextureResource` lifecycle (reference counting, GPU destruction) remains managed solely by `TextureRepository`; callers never call `AddReference` or `Dispose` on `TextureResource` directly.

## Glossary

- **TextureRef**: The new wrapper class. Holds a `string Key`, an `ITextureRepository Repository` back-reference, and a cached `TextureHandle`. Consumers hold a `TextureRef` instead of a `TextureResource` directly.
- **TextureHandle**: The GPU handle type returned by the repository and cached inside `TextureRef`. Exposes a `Valid` property and a bindless `Index`.
- **TextureResource**: The existing reference-counted GPU texture wrapper (`Resource<Texture>`). Its lifecycle remains managed solely by `TextureRepository`.
- **TextureRepository**: The concrete implementation of `ITextureRepository` that caches and manages GPU textures.
- **ITextureRepository**: The interface through which consumers interact with the texture cache.
- **TextureCacheEntry**: The internal cache entry stored in `TextureRepository`, wrapping a `TextureResource`.
- **Cache Key**: The string identifier used to look up a texture in the repository — either a caller-supplied name (stream/image sources) or a normalized absolute file path (file sources).
- **Bindless Index**: The `uint32_t` index exposed by `TextureHandle.Index`, used by GPU shaders to sample a texture from the bindless descriptor array.
- **Stale Handle**: A `TextureHandle` whose `Valid` property returns `false`, indicating the underlying GPU resource has been replaced or removed.
- **Replace**: A repository operation that swaps the `TextureResource` stored under a given Cache Key. Existing `TextureRef` instances for that key will re-fetch on their next `GetHandle()` call because the old handle becomes stale.
- **Remove**: A repository operation that destroys the `TextureResource` stored under a given Cache Key. Existing `TextureRef` instances for that key will return an invalid handle on their next `GetHandle()` call.
- **PBRMaterialProperties**: An existing consumer class that holds texture maps (albedo, normal, metallic-roughness, etc.) and reads their bindless index values to populate GPU shader data.

---

## Requirements

### Requirement 1: TextureRef Wrapper Type

**User Story:** As an engine consumer, I want to hold a `TextureRef` object instead of a raw `TextureResource`, so that I always get a valid handle on demand without managing resource lifetimes myself.

#### Acceptance Criteria

1. THE `TextureRef` SHALL be a reference type (class).
2. THE `TextureRef` SHALL hold a `string Key` property that returns the Cache Key used to identify the texture in the repository.
3. THE `TextureRef` SHALL hold an `ITextureRepository Repository` property that is the back-reference to the repository from which the texture was obtained.
4. THE `TextureRef` SHALL cache a `TextureHandle` internally for fast repeated access.
5. THE `TextureRef` SHALL expose a `GetHandle()` method that returns the current `TextureHandle`.
6. WHEN `GetHandle()` is called and the cached `TextureHandle.Valid` is `true`, THE `TextureRef` SHALL return the cached handle without querying the repository.
7. WHEN `GetHandle()` is called and the cached `TextureHandle.Valid` is `false`, THE `TextureRef` SHALL re-fetch the handle from the repository using the stored Key and update the cached handle before returning it.
8. WHEN `GetHandle()` is called and the Key no longer exists in the repository, THE `TextureRef` SHALL return an invalid `TextureHandle` (one whose `Valid` property is `false`).
9. THE `TextureRef` SHALL expose a static `TextureRef Null` sentinel instance whose `GetHandle()` always returns an invalid `TextureHandle`.

---

### Requirement 2: ITextureRepository Returns TextureRef

**User Story:** As an engine consumer, I want `ITextureRepository.GetOrCreate*` methods to return `TextureRef` instead of `TextureResource`, so that I hold a wrapper that can lazily re-fetch the handle if the texture is replaced.

#### Acceptance Criteria

1. THE `ITextureRepository` SHALL declare `GetOrCreateFromStream` with return type `TextureRef`.
2. THE `ITextureRepository` SHALL declare `GetOrCreateFromFile` with return type `TextureRef`.
3. THE `ITextureRepository` SHALL declare `GetOrCreateFromImage` with return type `TextureRef`.
4. WHEN a `GetOrCreate*` method is called with a Cache Key that already exists in the repository, THE `TextureRepository` SHALL return a `TextureRef` whose `Key` matches that Cache Key and whose `Repository` is the repository instance.
5. WHEN a `GetOrCreate*` method creates a new texture, THE `TextureRepository` SHALL create a new `TextureRef` wrapping the new `TextureResource` and return it.
6. THE `TextureRepository` SHALL manage `TextureResource` reference counts internally; callers of `GetOrCreate*` SHALL NOT be required to call `AddReference` or `Dispose` on `TextureResource`.

---

### Requirement 3: Replace Texture by Key

**User Story:** As an engine developer, I want to replace the GPU texture resource stored under a given key so that existing `TextureRef` holders automatically see the new texture on their next `GetHandle()` call, enabling hot-swap at runtime.

#### Acceptance Criteria

1. THE `ITextureRepository` SHALL declare a `ReplaceFromStream(string name, Stream stream, string? debugName = null)` method that returns `TextureRef`.
2. THE `ITextureRepository` SHALL declare a `ReplaceFromFile(string filePath, string? debugName = null)` method that returns `TextureRef`.
3. THE `ITextureRepository` SHALL declare a `ReplaceFromImage(string name, Image image)` method that returns `TextureRef`.
4. WHEN a `Replace*` method is called with a Cache Key that exists in the repository, THE `TextureRepository` SHALL create a new `TextureResource` from the provided source, store it under that key, and dispose the old `TextureResource`.
5. WHEN a `Replace*` method is called with a Cache Key that does not exist in the repository, THE `TextureRepository` SHALL behave identically to the corresponding `GetOrCreate*` method and return a new `TextureRef`.
6. AFTER a `Replace*` call completes, THE `TextureRef` previously returned for that key SHALL return a valid `TextureHandle` with the new resource's bindless index on the next `GetHandle()` call, because the old handle is stale and triggers a re-fetch.
7. THE `TextureRepository` SHALL dispose the old `TextureResource` after storing the replacement, ensuring the GPU resource is released when its reference count reaches zero.

---

### Requirement 4: Remove Texture by Key

**User Story:** As an engine developer, I want to remove and destroy a texture by its key so that the GPU resource is freed and all existing `TextureRef` holders for that key return an invalid handle on their next access.

#### Acceptance Criteria

1. THE `ITextureRepository` SHALL declare a `Remove(string key)` method that returns `bool`.
2. WHEN `Remove` is called with a Cache Key that exists in the repository, THE `TextureRepository` SHALL remove the entry from the cache, dispose the associated `TextureResource`, and return `true`.
3. WHEN `Remove` is called with a Cache Key that does not exist in the repository, THE `TextureRepository` SHALL return `false` and make no other changes.
4. AFTER `Remove` completes for a given key, all `TextureRef` instances previously returned for that key SHALL return an invalid `TextureHandle` on the next `GetHandle()` call, because the cached handle is stale and the key no longer exists in the repository.

---

### Requirement 5: TextureResource Lifecycle Managed by Repository

**User Story:** As an engine consumer, I want the repository to manage all `TextureResource` reference counting and disposal, so that I do not need to call `AddReference` or `Dispose` on `TextureResource` myself.

#### Acceptance Criteria

1. THE `TextureRepository` SHALL call `AddReference` on a `TextureResource` only when storing it internally.
2. THE `TextureRepository` SHALL call `Dispose` on a `TextureResource` when the entry is replaced, removed, or when the repository itself is disposed.
3. THE `TextureRepository` SHALL NOT require callers to call `AddReference` or `Dispose` on any `TextureResource` obtained through `ITextureRepository`.
4. WHEN the repository is disposed, THE `TextureRepository` SHALL dispose all cached `TextureResource` instances, causing subsequent `GetHandle()` calls on any outstanding `TextureRef` to return an invalid handle.

---

### Requirement 6: PBRMaterialProperties Uses TextureRef

**User Story:** As an engine developer, I want `PBRMaterialProperties` to store `TextureRef` wrappers instead of `TextureResource` instances, so that texture replacements are automatically reflected in GPU shader data on the next render without manual updates.

#### Acceptance Criteria

1. THE `PBRMaterialProperties` SHALL store texture map fields (`AlbedoMap`, `NormalMap`, `MetallicRoughnessMap`, `AoMap`, `BumpMap`, `DisplaceMap`) as `TextureRef` instead of `TextureResource`.
2. WHEN GPU shader data is written (e.g. populating `AlbedoTexIndex`), THE `PBRMaterialProperties` SHALL call `wrapper.GetHandle()` on the relevant `TextureRef` and read the returned handle's `Index`.
3. THE `PBRMaterialProperties` SHALL NOT call `Dispose` on `TextureResource` directly when disposing texture map fields; disposal of the underlying `TextureResource` is the responsibility of `TextureRepository`.
4. THE `PBRMaterialProperties` SHALL use `TextureRef.Null` as the default initial value for each texture map field.

---

### Requirement 7: Existing Cache Query API Compatibility

**User Story:** As an engine developer, I want the existing `TryGet` method to remain available, so that code that queries the cache by key without creating a texture continues to work.

#### Acceptance Criteria

1. THE `ITextureRepository` SHALL retain the `TryGet(string cacheKey, out TextureCacheEntry? entry)` method signature.
2. WHEN `TryGet` is called with a Cache Key that exists in the repository, THE `TextureRepository` SHALL populate `entry` with the current `TextureCacheEntry` and return `true`.
3. WHEN `TryGet` is called with a Cache Key that does not exist in the repository, THE `TextureRepository` SHALL set `entry` to `null` and return `false`.
