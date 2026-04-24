# Implementation Plan: sampler-ref-wrapper

## Overview

Introduce `SamplerRef` as a lightweight wrapper that decouples consumers from the `SamplerResource` lifecycle. The implementation proceeds in dependency order: core type first, then repository interface and implementation changes, then the consumer (`PBRMaterialProperties`) update, then caller updates, and finally tests. Each step compiles cleanly before the next begins.

## Tasks

- [x] 1. Create `SamplerRef` and `NullSamplerRepository` in `HelixToolkit.Nex.Repository`
  - [x] 1.1 Create `SamplerRef.cs` in `HelixToolkit.Nex.Repository`
    - Declare `NullSamplerRepository` as an `internal sealed class` implementing `ISamplerRepository` — a no-op where `TryGet` always returns `false`, `GetOrCreate` returns `SamplerRef.Null`, `Remove` returns `false`, and all other members are no-ops or return defaults; expose a `static readonly Instance` singleton
    - Declare `SamplerRef` as a `public sealed class` with `string Key`, `ISamplerRepository Repository`, and a private `Handle<Sampler> _cachedHandle` field
    - Implement the constructor `SamplerRef(string key, ISamplerRepository repository, Handle<Sampler> initialHandle)`
    - Implement `GetHandle()`: return `_cachedHandle` if `Valid`; otherwise call `Repository.TryGet(Key, out var entry)` and update `_cachedHandle` from `entry.Sampler.Handle` if found; return the (possibly still-invalid) handle in all cases
    - Declare `public static readonly SamplerRef Null` using `NullSamplerRepository.Instance` and `Handle<Sampler>.Null`
    - Mirror the structure of `TextureRef.cs` exactly, substituting `Sampler`/`ISamplerRepository`/`SamplerModuleCacheEntry` for their texture equivalents
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 1.7, 1.8, 1.9_

- [x] 2. Update `ISamplerRepository` with new return type and new `Remove` method
  - [x] 2.1 Change `GetOrCreate` return type and add `Remove` to `ISamplerRepository.cs`
    - Change `GetOrCreate(SamplerStateDesc desc)` return type from `SamplerResource` to `SamplerRef`
    - Add `bool Remove(string key)` method declaration
    - Update XML doc comments on `GetOrCreate` to reflect the new return type
    - Add XML doc comment for `Remove` describing the key-not-found (`false`) and key-found (`true`, disposes resource) behaviours
    - _Requirements: 2.1, 3.1_

- [x] 3. Update `SamplerRepository` to implement the new interface
  - [x] 3.1 Extract `StoreEntry` helper and change `GetOrCreate` to return `SamplerRef`
    - Extract a private `StoreEntry(string cacheKey, SamplerResource sampler, string? debugName) SamplerRef` helper that: creates a `SamplerModuleCacheEntry`, calls `Set(cacheKey, entry)`, calls `AddResourceReference(sampler)`, asserts `sampler.Valid`, and returns `new SamplerRef(cacheKey, this, sampler.Handle)`
    - In `GetOrCreate`, remove the `AddResourceReference(cached!.Sampler)` call on cache hits (the repository's stored reference is sufficient; callers no longer hold `SamplerResource` directly)
    - Change the cache-hit return from `cached!.Sampler` to `new SamplerRef(cacheKey, this, cached!.Sampler.Handle)`
    - Change the cache-miss path to delegate to `StoreEntry` instead of inlining the store logic
    - Change the method return type from `SamplerResource` to `SamplerRef`
    - _Requirements: 2.2, 2.3, 2.4, 4.1, 4.3_

  - [x] 3.2 Implement `Remove`
    - Add `public bool Remove(string key)` that calls `TryRemoveFromCache(key, out var removed)` and, if successful, calls `DisposeEntry(removed)` and returns `true`; otherwise returns `false`
    - `TryRemoveFromCache` is the protected helper already present in the `Repository<>` base class from the `texture-ref-wrapper` implementation
    - _Requirements: 3.2, 3.3, 4.2_

- [x] 4. Checkpoint — build `HelixToolkit.Nex.Repository`
  - Ensure `HelixToolkit.Nex.Repository` compiles without errors before proceeding to consumer changes.

- [x] 5. Update `PBRMaterialProperties` to use `SamplerRef`
  - [x] 5.1 Replace `SamplerResource` fields with `SamplerRef` fields in `PBRMaterialProperties.cs`
    - Change `private SamplerResource _sampler = SamplerResource.Null` to `private SamplerRef _sampler = SamplerRef.Null`
    - Change `private SamplerResource _displaceSampler = SamplerResource.Null` to `private SamplerRef _displaceSampler = SamplerRef.Null`
    - Change the public property types `Sampler` and `DisplaceSampler` from `SamplerResource` to `SamplerRef`
    - _Requirements: 5.1, 5.4_

  - [x] 5.2 Update shader index writes to call `GetHandle()`
    - In the `Sampler` setter, change `Properties.SamplerIndex = value.Index` to `Properties.SamplerIndex = value.GetHandle().Index`
    - In the `DisplaceSampler` setter, change `Properties.DisplaceSamplerIndex = value.Index` to `Properties.DisplaceSamplerIndex = value.GetHandle().Index`
    - _Requirements: 5.2_

  - [x] 5.3 Remove `SamplerResource.Dispose()` calls from `PBRMaterialProperties.Dispose()`
    - In `Dispose(bool disposing)`, remove `Sampler.Dispose()` and `DisplaceSampler.Dispose()`
    - Keep `_pool?.Destroy(_handle)` and the `MaterialPropsUpdatedEvent` publish unchanged
    - _Requirements: 5.3_

- [x] 6. Update `TextureDemo` sample caller
  - [x] 6.1 Update `TextureDemo.cs` to store and assign `SamplerRef` instead of `SamplerResource`
    - Change `private SamplerResource _sampler = SamplerResource.Null` to `private SamplerRef _sampler = SamplerRef.Null`
    - Change `private SamplerResource _displaceSampler = SamplerResource.Null` to `private SamplerRef _displaceSampler = SamplerRef.Null`
    - The assignments `_sampler = samplerRepo.GetOrCreate(...)` and `_displaceSampler = samplerRepo.GetOrCreate(...)` already receive the new `SamplerRef` return type — no change needed to those lines
    - The assignments `_material.Sampler = _sampler` and `_material.DisplaceSampler = _displaceSampler` already pass the correct type — no change needed
    - Remove any `_sampler.Dispose()` or `_displaceSampler.Dispose()` calls if present (the repository owns the resource)
    - _Requirements: 2.4, 4.3_

- [x] 7. Checkpoint — build the full solution
  - Ensure the entire solution (`HelixToolkit.Nex.sln`) compiles without errors or warnings before writing tests.

- [x] 8. Write unit tests for `SamplerRef` in `HelixToolkit.Nex.Repository.Tests`
  - [x] 8.1 Create `SamplerRefTests.cs` in `HelixToolkit.Nex.Repository.Tests`
    - Verify `typeof(SamplerRef).IsClass` is `true`
    - Verify `SamplerRef.Null.GetHandle()` returns a handle with `Valid == false`
    - Verify `SamplerRef.Null.GetHandle()` does not throw when called multiple times
    - Verify `NullSamplerRepository.Remove` returns `false` for any key (accessed via `SamplerRef.Null.Repository.Remove(...)`)
    - Verify `PBRMaterialProperties.Null.Sampler == SamplerRef.Null` and `PBRMaterialProperties.Null.DisplaceSampler == SamplerRef.Null`
    - Verify `PBRMaterialProperties.Null.Dispose()` does not throw
    - _Requirements: 1.1, 1.9, 3.3, 5.4_

- [x] 9. Write property-based tests using FsCheck
  - [x] 9.1 Create `SamplerRefPropertyTests.cs` and write a `MockSamplerRepository` helper
    - Add a `MockSamplerRepository` test helper class implementing `ISamplerRepository` that: stores a configurable `TryGet` result (found/not-found), counts `TryGet` call invocations, and returns `SamplerRef.Null` for `GetOrCreate`
    - Implement `SetTryGetNotFound()` to configure the mock to return `false` from `TryGet`
    - Implement `SetTryGetFound()` to configure the mock to return `true` with a real `SamplerModuleCacheEntry` backed by a `MockContext` sampler — mirror the `MockTextureRepository.SetTryGetFound()` pattern from `TextureRefPropertyTests.cs`
    - Mirror the structure of `MockTextureRepository` in `TextureRefPropertyTests.cs`, substituting sampler types
    - _Requirements: 1.2, 1.3, 1.6, 1.7, 1.8, 1.9_

  - [x]* 9.2 Write property test for Property 1 — Key and Repository round-trip
    - `// Feature: sampler-ref-wrapper, Property 1: For any non-null string key and any ISamplerRepository instance, constructing a SamplerRef with that key and repository returns the same key from Key and the same repository instance from Repository`
    - Generate random non-null strings from a fixed set; construct `new SamplerRef(key, mockRepo, Handle<Sampler>.Null)`; assert `ref.Key == key && ReferenceEquals(ref.Repository, mockRepo)`
    - **Property 1: Key and Repository round-trip**
    - **Validates: Requirements 1.2, 1.3**

  - [x]* 9.3 Write property test for Property 2 — Valid cached handle skips repository
    - `// Feature: sampler-ref-wrapper, Property 2: For any SamplerRef whose cached handle is valid, calling GetHandle() any number of times returns the same handle value and makes zero calls to Repository.TryGet`
    - Generate a call count N ∈ [1, 50]; construct `SamplerRef` with a valid `Handle<Sampler>` (non-zero index, gen > 0); call `GetHandle()` N times; assert all results equal the initial handle and `mockRepo.TryGetCallCount == 0`
    - **Property 2: Valid cached handle skips repository**
    - **Validates: Requirements 1.6**

  - [x]* 9.4 Write property test for Property 3 — Stale handle triggers re-fetch
    - `// Feature: sampler-ref-wrapper, Property 3: For any SamplerRef whose cached handle has become invalid, calling GetHandle() calls Repository.TryGet with the stored key and, if the key exists, returns the handle from the repository entry and updates the internal cache`
    - Construct `SamplerRef` with `Handle<Sampler>.Null` (invalid); configure `MockSamplerRepository.SetTryGetFound()` to return a valid entry; call `GetHandle()`; assert `TryGetCallCount == 1` and returned handle matches the entry's sampler handle
    - **Property 3: Stale handle triggers re-fetch**
    - **Validates: Requirements 1.7, 1.8**

  - [x]* 9.5 Write property test for Property 4 — Null sentinel always returns invalid handle
    - `// Feature: sampler-ref-wrapper, Property 4: For any number of GetHandle() calls on SamplerRef.Null, the returned handle always has Valid == false`
    - Generate N ∈ [1, 200]; call `SamplerRef.Null.GetHandle()` N times; assert all results have `Valid == false`
    - **Property 4: Null sentinel always returns invalid handle**
    - **Validates: Requirements 1.9**

  - [x]* 9.6 Write property test for Property 5 — GetOrCreate returns SamplerRef with matching key and repository
    - `// Feature: sampler-ref-wrapper, Property 5: For any SamplerStateDesc, calling GetOrCreate returns a SamplerRef whose Key equals SamplerRepository.GenerateCacheKey(desc) and whose Repository is the repository instance`
    - Use a `MockSamplerRepository` whose `GetOrCreate` returns `new SamplerRef(SamplerRepository.GenerateCacheKey(desc), this, Handle<Sampler>.Null)`; generate random `SamplerStateDesc` field combinations; assert `result.Key == SamplerRepository.GenerateCacheKey(desc) && ReferenceEquals(result.Repository, mockRepo)`
    - **Property 5: GetOrCreate returns SamplerRef with matching key and repository**
    - **Validates: Requirements 2.1, 2.2, 2.3**

  - [x]* 9.7 Write property test for Property 6 — Remove causes existing SamplerRef to return invalid handle
    - `// Feature: sampler-ref-wrapper, Property 6: For any SamplerRef previously returned for a given key, after Remove(key) is called, calling GetHandle() on that SamplerRef returns a handle with Valid == false`
    - Construct `SamplerRef` with `Handle<Sampler>.Null` (stale) and configure `MockSamplerRepository.SetTryGetNotFound()` to simulate post-remove state; call `GetHandle()`; assert `!result.Valid`
    - **Property 6: Remove causes existing SamplerRef to return invalid handle**
    - **Validates: Requirements 3.2, 3.4**

  - [x]* 9.8 Write property test for Property 7 — Repository disposal causes SamplerRef to return invalid handle
    - `// Feature: sampler-ref-wrapper, Property 7: For any SamplerRef obtained from a repository, after the repository is disposed, calling GetHandle() returns a handle with Valid == false`
    - Construct `SamplerRef` with `Handle<Sampler>.Null` and configure `MockSamplerRepository.SetTryGetNotFound()` to simulate post-dispose state (TryGet returns false); call `GetHandle()`; assert `!result.Valid`
    - **Property 7: Repository disposal causes SamplerRef to return invalid handle**
    - **Validates: Requirements 4.4**

  - [x]* 9.9 Write property test for Property 8 — PBRMaterialProperties shader index matches GetHandle().Index
    - `// Feature: sampler-ref-wrapper, Property 8: For any SamplerRef assigned to a sampler field on PBRMaterialProperties, the corresponding shader index field equals wrapper.GetHandle().Index at the time of assignment`
    - Generate a random valid handle index ∈ [1, 1000]; construct a `SamplerRef` with that handle (gen=1 for validity); verify `GetHandle().Index == (uint)idx` and `mockRepo.TryGetCallCount == 0`
    - **Property 8: PBRMaterialProperties shader index matches GetHandle().Index**
    - **Validates: Requirements 5.2**

- [x] 10. Final checkpoint — run all tests
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- The build compiles cleanly after task 3 (repository changes) before touching `PBRMaterialProperties`
- `NullSamplerRepository` must be defined before `SamplerRef.Null` is initialised (same file, above the static field)
- Rendering nodes (`Fxaa`, `ToneMapping`, `Smaa`, `Bloom`, `BorderHighlightPostEffect`, `WBOITCompositeNode`, `SampleTextureNode`, `ForwardPlusLightCullingNode`) are **not** updated — they store `SamplerResource` for the node's own lifetime and call `.Index`/`.Valid`/`.Dispose()` directly; this is correct for internal render-graph resources that are not shared across material instances
- `TextureDemo` is the only sample caller that feeds `SamplerResource` into `PBRMaterialProperties` and must be updated (task 6)
- Replace is not supported for samplers — sampler descriptors are immutable, so no `ReplaceEntry` helper is needed (unlike `TextureRepository`)
- Property tests P6 and P7 are covered by the mock approach (no real `SamplerRepository` needed); P5 uses a mock `GetOrCreate` that constructs the `SamplerRef` directly
- FsCheck version `3.3.2` is already a dependency of `HelixToolkit.Nex.Repository.Tests`
- All property tests use `Prop.ForAll(...).QuickCheckThrowOnFailure()` with the default 100-iteration minimum
