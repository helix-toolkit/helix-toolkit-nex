# Implementation Plan: texture-ref-wrapper

## Overview

Introduce `TextureRef` as a lightweight wrapper that decouples consumers from the `TextureResource` lifecycle. The implementation proceeds in dependency order: core type first, then repository interface and implementation changes, then the consumer (`PBRMaterialProperties`) update, and finally property-based tests. Each step compiles cleanly before the next begins.

## Tasks

- [x] 1. Create `TextureRef` and `NullTextureRepository` in `HelixToolkit.Nex.Repository`
  - [x] 1.1 Create `TextureRef.cs` in `HelixToolkit.Nex.Repository`
    - Declare `TextureRef` as a `sealed class` with `string Key`, `ITextureRepository Repository`, and a private `Handle<Texture> _cachedHandle` field
    - Implement the constructor `TextureRef(string key, ITextureRepository repository, Handle<Texture> initialHandle)`
    - Implement `GetHandle()`: return `_cachedHandle` if `Valid`; otherwise call `Repository.TryGet(Key, out var entry)` and update `_cachedHandle` from `entry.Texture.Handle` if found; return the (possibly still-invalid) handle in all cases
    - Add the `NullTextureRepository` internal sealed class in the same file — a no-op `ITextureRepository` where `TryGet` always returns `false`, all `GetOrCreate*` / `Replace*` methods return `TextureRef.Null`, `Remove` returns `false`, and all other members are no-ops or return defaults
    - Declare `public static readonly TextureRef Null` using `NullTextureRepository.Instance` and `Handle<Texture>.Null`
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 1.7, 1.8, 1.9_

- [x] 2. Update `ITextureRepository` with new return types and new methods
  - [x] 2.1 Change `GetOrCreate*` return types and add `Replace*` / `Remove` to `ITextureRepository.cs`
    - Change `GetOrCreateFromStream`, `GetOrCreateFromFile`, and `GetOrCreateFromImage` return types from `TextureResource` to `TextureRef`
    - Add `ReplaceFromStream(string name, Stream stream, string? debugName = null) TextureRef`
    - Add `ReplaceFromFile(string filePath, string? debugName = null) TextureRef`
    - Add `ReplaceFromImage(string name, Image image) TextureRef`
    - Add `Remove(string key) bool`
    - Update XML doc comments on changed members to reflect the new return type
    - _Requirements: 2.1, 2.2, 2.3, 3.1, 3.2, 3.3, 4.1, 7.1_

- [x] 3. Update `TextureRepository` to implement the new interface
  - [x] 3.1 Change `GetOrCreate*` implementations to return `TextureRef`
    - In each `GetOrCreate*` method, remove the `AddResourceReference(cached!.Texture)` call on cache hits (the repository's stored reference is sufficient)
    - Change `StoreEntry` return type from `TextureResource` to `TextureRef`; update its return statement to `return new TextureRef(cacheKey, this, texture.Handle)`
    - Update all three `GetOrCreate*` methods to return the `TextureRef` from `StoreEntry` (or construct one on cache hit: `new TextureRef(cacheKey, this, cached!.Texture.Handle)`)
    - _Requirements: 2.4, 2.5, 2.6, 5.1, 5.3_

  - [x] 3.2 Add `ReplaceEntry` helper and implement `Replace*` methods
    - Add a private `ReplaceEntry(string cacheKey, TextureResource newTexture, string debugName) TextureRef` helper that: retrieves the old entry via `TryGet`, calls `Set` with the new entry, then disposes the old entry (using `_evictionLock` or a dedicated `_replaceLock` to guard the read-then-write sequence)
    - Implement `ReplaceFromStream`, `ReplaceFromFile`, and `ReplaceFromImage` — each creates a new `TextureResource` via `TextureCreator`, then delegates to `ReplaceEntry`; if the key does not exist, fall through to `StoreEntry` (same as `GetOrCreate*`)
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7_

  - [x] 3.3 Implement `Remove`
    - Add `public bool Remove(string key)` that calls `_cache.TryRemove(key, out var entry)` and, if successful, calls `DisposeEntry(entry)` and returns `true`; otherwise returns `false`
    - _Requirements: 4.1, 4.2, 4.3, 5.2_

- [x] 4. Checkpoint — build the solution
  - Ensure `HelixToolkit.Nex.Repository` compiles without errors before proceeding to consumer changes.

- [x] 5. Update `PBRMaterialProperties` to use `TextureRef`
  - [x] 5.1 Replace `TextureResource` fields with `TextureRef` fields
    - Change the six backing fields (`_albedoMap`, `_normalMap`, `_metallicRoughnessMap`, `_aoMap`, `_bumpMap`, `_displaceMap`) from `TextureResource` to `TextureRef`, initialised to `TextureRef.Null`
    - Change the corresponding public property types (`AlbedoMap`, `NormalMap`, `MetallicRoughnessMap`, `AoMap`, `BumpMap`, `DisplaceMap`) from `TextureResource` to `TextureRef`
    - _Requirements: 6.1, 6.4_

  - [x] 5.2 Update shader index writes to call `GetHandle()`
    - In each texture map setter, change `Properties.<Slot>TexIndex = value.Index` to `Properties.<Slot>TexIndex = value.GetHandle().Index`
    - Affected setters: `AlbedoMap`, `NormalMap`, `MetallicRoughnessMap`, `AoMap`, `BumpMap`, `DisplaceMap`
    - _Requirements: 6.2_

  - [x] 5.3 Remove `TextureResource.Dispose()` calls from `PBRMaterialProperties.Dispose()`
    - In `Dispose(bool disposing)`, remove the six `<Map>.Dispose()` calls (`AlbedoMap.Dispose()`, `NormalMap.Dispose()`, `MetallicRoughnessMap.Dispose()`, `AoMap.Dispose()` (if present), `BumpMap.Dispose()` (if present), `DisplaceMap.Dispose()`)
    - Keep `_pool?.Destroy(_handle)` and the `MaterialPropsUpdatedEvent` publish
    - _Requirements: 6.3, 5.3_

- [x] 6. Checkpoint — build the full solution
  - Ensure the entire solution (`HelixToolkit.Nex.sln`) compiles without errors or warnings before writing tests.

- [x] 7. Create `HelixToolkit.Nex.Repository.Tests` project and write unit tests
  - [x] 7.1 Create the test project `HelixToolkit.Nex.Repository.Tests`
    - Add `HelixToolkit.Nex.Repository.Tests.csproj` using `MSTest.Sdk/3.6.4`, targeting `net8.0`, with project references to `HelixToolkit.Nex.Repository`, `HelixToolkit.Nex.Graphics.Mock`, and `HelixToolkit.Nex.Material`
    - Add `FsCheck` version `3.3.2` as a `PackageReference`
    - Add `GlobalUsings.cs` with `global using HelixToolkit.Nex.Graphics;`, `global using HelixToolkit.Nex.Repository;`, and `global using TextureHandle = HelixToolkit.Nex.Handle<HelixToolkit.Nex.Graphics.Texture>;`
    - Add `MSTestSettings.cs` with `[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]`
    - Add the project to `HelixToolkit.Nex.sln`
    - _Requirements: 1.1 through 7.3_

  - [x] 7.2 Write unit tests for `TextureRef` API contracts and error paths
    - Verify `typeof(TextureRef).IsClass` is `true`
    - Verify `TextureRef.Null.GetHandle()` returns a handle with `Valid == false`
    - Verify `TextureRef.Null.GetHandle()` does not throw when called multiple times
    - Verify `Remove` returns `false` for a non-existent key (using a mock or stub repository)
    - Verify `PBRMaterialProperties.Null` has all six texture map fields equal to `TextureRef.Null`
    - Verify `PBRMaterialProperties.Dispose()` does not throw (i.e. no `NullReferenceException` from removed `Dispose` calls)
    - _Requirements: 1.1, 1.9, 4.3, 6.4_

- [x] 8. Write property-based tests using FsCheck
  - [x] 8.1 Create `TextureRefPropertyTests.cs` and write a mock `ITextureRepository` helper
    - Add a `MockTextureRepository` test helper class implementing `ITextureRepository` that: stores a configurable `TryGet` result, counts `TryGet` call invocations, and returns `TextureRef.Null` for all mutating methods
    - _Requirements: 1.2, 1.3, 1.6, 1.7, 1.8, 1.9_

  - [x] 8.2 Write property test for Property 1 — Key and Repository round-trip
    - `// Feature: texture-ref-wrapper, Property 1: For any non-null string key and any ITextureRepository instance, constructing a TextureRef with that key and repository returns the same key from Key and the same repository instance from Repository`
    - Generate random non-null strings; construct `new TextureRef(key, mockRepo, Handle<Texture>.Null)`; assert `ref.Key == key && ref.Repository == mockRepo`
    - **Validates: Requirements 1.2, 1.3**

  - [x] 8.3 Write property test for Property 2 — Valid cached handle skips repository
    - `// Feature: texture-ref-wrapper, Property 2: For any TextureRef whose cached handle is valid, calling GetHandle() any number of times returns the same handle value and makes zero calls to Repository.TryGet`
    - Generate a valid `Handle<Texture>` (non-zero index, gen > 0) and a call count N ∈ [1, 50]; construct `TextureRef` with that handle; call `GetHandle()` N times; assert all results equal the initial handle and `mockRepo.TryGetCallCount == 0`
    - **Validates: Requirements 1.6**

  - [x] 8.4 Write property test for Property 3 — Stale handle triggers re-fetch
    - `// Feature: texture-ref-wrapper, Property 3: For any TextureRef whose cached handle has become invalid, calling GetHandle() calls Repository.TryGet with the stored key and, if the key exists, returns the handle from the repository entry and updates the internal cache`
    - Construct `TextureRef` with `Handle<Texture>.Null` (invalid); configure `MockTextureRepository.TryGet` to return a new valid `TextureCacheEntry`; call `GetHandle()`; assert `TryGetCallCount == 1` and returned handle matches the entry's handle
    - **Validates: Requirements 1.7, 1.8**

  - [x] 8.5 Write property test for Property 4 — Null sentinel always returns invalid handle
    - `// Feature: texture-ref-wrapper, Property 4: For any number of GetHandle() calls on TextureRef.Null, the returned handle always has Valid == false`
    - Generate N ∈ [1, 200]; call `TextureRef.Null.GetHandle()` N times; assert all results have `Valid == false`
    - **Validates: Requirements 1.9**

  - [x] 8.6 Write property test for Property 5 — GetOrCreate* returns TextureRef with matching key
    - `// Feature: texture-ref-wrapper, Property 5: For any cache key, calling GetOrCreate* returns a TextureRef whose Key matches the cache key and whose Repository is the repository instance`
    - Use a `MockTextureRepository` that returns a pre-built `TextureRef` keyed to the input; generate random non-empty strings; assert `result.Key == key && result.Repository == repo`
    - **Validates: Requirements 2.1, 2.2, 2.3, 2.4, 2.5**

  - [x] 8.7 Write property test for Property 7 — Remove causes existing TextureRef to return invalid handle
    - `// Feature: texture-ref-wrapper, Property 7: For any TextureRef previously returned for a given key, after Remove(key) is called, calling GetHandle() on that TextureRef returns a handle with Valid == false`
    - Use a `MockTextureRepository` that starts with a valid entry for key K, then after `Remove(K)` returns `false` from `TryGet`; construct `TextureRef` with a valid handle; simulate staleness by constructing with invalid handle and configuring `TryGet` to return `false`; assert `GetHandle().Valid == false`
    - **Validates: Requirements 4.2, 4.4**

  - [x] 8.8 Write property test for Property 9 — PBRMaterialProperties shader index matches GetHandle().Index
    - `// Feature: texture-ref-wrapper, Property 9: For any TextureRef assigned to a texture map field on PBRMaterialProperties, the corresponding shader index field equals wrapper.GetHandle().Index at the time of assignment`
    - Generate a random valid handle index; construct a `TextureRef` with that handle; assign to `AlbedoMap` on a `PBRMaterialProperties` instance (using the internal constructor via a test pool); assert `Properties.AlbedoTexIndex == wrapper.GetHandle().Index`
    - **Validates: Requirements 6.2**

- [x] 9. Final checkpoint — run all tests
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- The build compiles cleanly after task 3 (repository changes) before touching `PBRMaterialProperties`
- `NullTextureRepository` must be defined before `TextureRef.Null` is initialised (same file, above the static field)
- Property tests P6 and P8 require a real `TextureRepository` with a `MockContext`; they are omitted from the optional tasks above because they depend on `TextureCreator` which requires image data — integration-level coverage is deferred to manual testing
- FsCheck version `3.3.2` matches the version already used in `HelixToolkit.Nex.Textures.Tests`
- All property tests use `Prop.ForAll(...).QuickCheckThrowOnFailure()` with the default 100-iteration minimum, matching the pattern in `MipMapCountTests`
