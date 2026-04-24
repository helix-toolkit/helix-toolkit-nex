# Requirements Document

## Introduction

This feature introduces `SamplerRef`, a lightweight wrapper class for GPU sampler resources in the HelixToolkit Nex engine. Currently, `ISamplerRepository` returns `SamplerResource` directly, requiring callers to manage reference counts and leaving them with stale handles when samplers are replaced or removed.

`SamplerRef` decouples consumers from the underlying `SamplerResource` lifecycle by holding a cache key and a back-reference to the repository. When a consumer needs the current `SamplerHandle` (e.g. to read the bindless index), it calls `GetHandle()` on the wrapper. The wrapper checks whether its cached `SamplerHandle` is still valid; if not, it re-fetches from the repository using the key. This lazy re-fetch approach is simple and correct because handle reads happen on the render thread.

`ISamplerRepository` gains a `Remove` operation to support destruction of samplers at runtime. `SamplerResource` lifecycle (reference counting, GPU destruction) remains managed solely by `SamplerRepository`; callers never call `AddReference` or `Dispose` on `SamplerResource` directly.

`PBRMaterialProperties` currently holds `SamplerResource _sampler` and `SamplerResource _displaceSampler` fields — these will become `SamplerRef`, matching the pattern already applied to texture map fields.

This feature mirrors the `TextureRef` pattern introduced in the `texture-ref-wrapper` spec.

## Glossary

- **SamplerRef**: The new wrapper class. Holds a `string Key`, an `ISamplerRepository Repository` back-reference, and a cached `SamplerHandle`. Consumers hold a `SamplerRef` instead of a `SamplerResource` directly.
- **SamplerHandle**: The GPU handle type (`Handle<Sampler>`) returned by the repository and cached inside `SamplerRef`. Exposes a `Valid` property and a bindless `Index`.
- **SamplerResource**: The existing reference-counted GPU sampler wrapper (`Resource<Sampler>`). Its lifecycle remains managed solely by `SamplerRepository`.
- **SamplerRepository**: The concrete implementation of `ISamplerRepository` that caches and manages GPU samplers.
- **ISamplerRepository**: The interface through which consumers interact with the sampler cache.
- **SamplerModuleCacheEntry**: The internal cache entry stored in `SamplerRepository`, wrapping a `SamplerResource`.
- **Cache Key**: The string identifier used to look up a sampler in the repository — derived from the `SamplerStateDesc` fields (excluding `DebugName`) via `SamplerRepository.GenerateCacheKey`.
- **Bindless Index**: The `uint` index exposed by `SamplerHandle.Index`, used by GPU shaders to sample a texture from the bindless descriptor array.
- **Stale Handle**: A `SamplerHandle` whose `Valid` property returns `false`, indicating the underlying GPU resource has been removed.
- **Remove**: A repository operation that destroys the `SamplerResource` stored under a given Cache Key. Existing `SamplerRef` instances for that key will return an invalid handle on their next `GetHandle()` call.
- **PBRMaterialProperties**: An existing consumer class that holds sampler references (`Sampler`, `DisplaceSampler`) and reads their bindless index values to populate GPU shader data.

---

## Requirements

### Requirement 1: SamplerRef Wrapper Type

**User Story:** As an engine consumer, I want to hold a `SamplerRef` object instead of a raw `SamplerResource`, so that I always get a valid handle on demand without managing resource lifetimes myself.

#### Acceptance Criteria

1. THE `SamplerRef` SHALL be a reference type (class).
2. THE `SamplerRef` SHALL hold a `string Key` property that returns the Cache Key used to identify the sampler in the repository.
3. THE `SamplerRef` SHALL hold an `ISamplerRepository Repository` property that is the back-reference to the repository from which the sampler was obtained.
4. THE `SamplerRef` SHALL cache a `SamplerHandle` internally for fast repeated access.
5. THE `SamplerRef` SHALL expose a `GetHandle()` method that returns the current `SamplerHandle`.
6. WHEN `GetHandle()` is called and the cached `SamplerHandle.Valid` is `true`, THE `SamplerRef` SHALL return the cached handle without querying the repository.
7. WHEN `GetHandle()` is called and the cached `SamplerHandle.Valid` is `false`, THE `SamplerRef` SHALL re-fetch the handle from the repository using the stored Key and update the cached handle before returning it.
8. WHEN `GetHandle()` is called and the Key no longer exists in the repository, THE `SamplerRef` SHALL return an invalid `SamplerHandle` (one whose `Valid` property is `false`).
9. THE `SamplerRef` SHALL expose a static `SamplerRef Null` sentinel instance whose `GetHandle()` always returns an invalid `SamplerHandle`.

---

### Requirement 2: ISamplerRepository Returns SamplerRef

**User Story:** As an engine consumer, I want `ISamplerRepository.GetOrCreate` to return `SamplerRef` instead of `SamplerResource`, so that I hold a wrapper that can lazily re-fetch the handle if the sampler is replaced.

#### Acceptance Criteria

1. THE `ISamplerRepository` SHALL declare `GetOrCreate` with return type `SamplerRef`.
2. WHEN `GetOrCreate` is called with a `SamplerStateDesc` whose Cache Key already exists in the repository, THE `SamplerRepository` SHALL return a `SamplerRef` whose `Key` matches that Cache Key and whose `Repository` is the repository instance.
3. WHEN `GetOrCreate` creates a new sampler, THE `SamplerRepository` SHALL create a new `SamplerRef` wrapping the new `SamplerResource` and return it.
4. THE `SamplerRepository` SHALL manage `SamplerResource` reference counts internally; callers of `GetOrCreate` SHALL NOT be required to call `AddReference` or `Dispose` on `SamplerResource`.

---

### Requirement 3: Remove Sampler by Key

**User Story:** As an engine developer, I want to remove and destroy a sampler by its key so that the GPU resource is freed and all existing `SamplerRef` holders for that key return an invalid handle on their next access.

#### Acceptance Criteria

1. THE `ISamplerRepository` SHALL declare a `Remove(string key)` method that returns `bool`.
2. WHEN `Remove` is called with a Cache Key that exists in the repository, THE `SamplerRepository` SHALL remove the entry from the cache, dispose the associated `SamplerResource`, and return `true`.
3. WHEN `Remove` is called with a Cache Key that does not exist in the repository, THE `SamplerRepository` SHALL return `false` and make no other changes.
4. AFTER `Remove` completes for a given key, all `SamplerRef` instances previously returned for that key SHALL return an invalid `SamplerHandle` on the next `GetHandle()` call, because the cached handle is stale and the key no longer exists in the repository.

---

### Requirement 4: SamplerResource Lifecycle Managed by Repository

**User Story:** As an engine consumer, I want the repository to manage all `SamplerResource` reference counting and disposal, so that I do not need to call `AddReference` or `Dispose` on `SamplerResource` myself.

#### Acceptance Criteria

1. THE `SamplerRepository` SHALL call `AddReference` on a `SamplerResource` only when storing it internally.
2. THE `SamplerRepository` SHALL call `Dispose` on a `SamplerResource` when the entry is removed, or when the repository itself is disposed.
3. THE `SamplerRepository` SHALL NOT require callers to call `AddReference` or `Dispose` on any `SamplerResource` obtained through `ISamplerRepository`.
4. WHEN the repository is disposed, THE `SamplerRepository` SHALL dispose all cached `SamplerResource` instances, causing subsequent `GetHandle()` calls on any outstanding `SamplerRef` to return an invalid handle.

---

### Requirement 5: PBRMaterialProperties Uses SamplerRef

**User Story:** As an engine developer, I want `PBRMaterialProperties` to store `SamplerRef` wrappers instead of `SamplerResource` instances, so that sampler lifecycle is managed by the repository without manual updates.

#### Acceptance Criteria

1. THE `PBRMaterialProperties` SHALL store sampler fields (`Sampler`, `DisplaceSampler`) as `SamplerRef` instead of `SamplerResource`.
2. WHEN GPU shader data is written (e.g. populating `SamplerIndex` or `DisplaceSamplerIndex`), THE `PBRMaterialProperties` SHALL call `wrapper.GetHandle()` on the relevant `SamplerRef` and read the returned handle's `Index`.
3. THE `PBRMaterialProperties` SHALL NOT call `Dispose` on `SamplerResource` directly when disposing sampler fields; disposal of the underlying `SamplerResource` is the responsibility of `SamplerRepository`.
4. THE `PBRMaterialProperties` SHALL use `SamplerRef.Null` as the default initial value for each sampler field.

---

### Requirement 6: Existing Cache Query API Compatibility

**User Story:** As an engine developer, I want the existing `TryGet` method to remain available, so that code that queries the cache by key without creating a sampler continues to work.

#### Acceptance Criteria

1. THE `ISamplerRepository` SHALL retain the `TryGet(string cacheKey, out SamplerModuleCacheEntry? entry)` method signature.
2. WHEN `TryGet` is called with a Cache Key that exists in the repository, THE `SamplerRepository` SHALL populate `entry` with the current `SamplerModuleCacheEntry` and return `true`.
3. WHEN `TryGet` is called with a Cache Key that does not exist in the repository, THE `SamplerRepository` SHALL set `entry` to `null` and return `false`.
