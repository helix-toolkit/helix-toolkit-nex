# Requirements Document

## Introduction

This feature redesigns the resource-ref lifecycle for `TextureRef` and `SamplerRef` in HelixToolkit Nex.
The current design stores a cached `Handle<T>` in the ref and lazily re-fetches it from the repository via `TryGet` when the handle becomes stale.
The new design eliminates the stale-handle problem entirely by embedding the live `TextureResource` / `SamplerResource` directly inside the ref, adds a push-notification `OnDisposed` event so that `PBRMaterialProperties` can zero its bindless indices automatically when a texture or sampler is removed, removes the `Replace*` family of methods in favour of an explicit Remove-then-GetOrCreate pattern, adds async texture creation methods to `ITextureRepository`, and simplifies `EventBus` by removing its background-thread dispatch machinery.

The eight requirements below correspond directly to the eight design decisions listed in the feature brief.

---

## Glossary

- **TextureRef**: A lightweight, non-disposable wrapper that gives consumers access to a GPU texture. After this feature it holds a `TextureResource` internally.
- **SamplerRef**: A lightweight, non-disposable wrapper that gives consumers access to a GPU sampler. After this feature it holds a `SamplerResource` internally.
- **TextureResource**: A reference-counted `Resource<Texture>` that owns a `Handle<Texture>` and destroys it via `IContext` when its reference count reaches zero.
- **SamplerResource**: A reference-counted `Resource<Sampler>` that owns a `Handle<Sampler>` and destroys it via `IContext` when its reference count reaches zero.
- **Handle**: A generation-tracked GPU resource handle (`Handle<T>`) with `Index`, `Gen`, and `Valid` fields.
- **TextureCacheEntry**: A cache entry stored in `TextureRepository`. After this feature it holds a `TextureRef Ref` instead of a `TextureResource Resource`.
- **SamplerCacheEntry** (`SamplerModuleCacheEntry`): A cache entry stored in `SamplerRepository`. After this feature it holds a `SamplerRef Ref` instead of a `SamplerResource Resource`.
- **ITextureRepository**: The interface for the GPU texture cache.
- **ISamplerRepository**: The interface for the GPU sampler cache.
- **TextureRepository**: The concrete implementation of `ITextureRepository`.
- **SamplerRepository**: The concrete implementation of `ISamplerRepository`.
- **PBRMaterialProperties**: Manages PBR material data in a pool entry and publishes change events. Holds `TextureRef` and `SamplerRef` fields for each texture/sampler slot.
- **PBRProperties**: The pooled struct holding bindless indices (`AlbedoTexIndex`, `NormalTexIndex`, `MetallicRoughnessTexIndex`, `AoTexIndex`, `BumpTexIndex`, `DisplaceTexIndex`, `SamplerIndex`, `DisplaceSamplerIndex`) and scalar material parameters.
- **OnDisposed**: An `event Action?` exposed by `TextureRef` and `SamplerRef`. Fires synchronously when `DisposeResource()` is called on the ref.
- **DisposeResource()**: A method on `TextureRef` and `SamplerRef` that disposes the internally held resource and fires `OnDisposed`.
- **EventBus**: A singleton publish/subscribe bus used for material property change notifications.
- **AsyncUploadHandle**: A handle returned by `IContext.UploadAsync` that can be awaited for GPU upload completion.
- **Main thread**: The single thread on which all property changes, `OnDisposed` callbacks, and material index updates occur (WPF-style threading model).

---

## Requirements

### Requirement 1: TextureRef Holds Resource Internally

**User Story:** As a renderer, I want `TextureRef.GetHandle()` to always return the current GPU handle without a repository round-trip, so that bindless index reads are fast and never stale.

#### Acceptance Criteria

1. THE `TextureRef` SHALL hold a `TextureResource _resource` field internally.
2. WHEN `TextureRef.GetHandle()` is called, THE `TextureRef` SHALL return `_resource.Handle` directly without calling `ITextureRepository.TryGet`.
3. THE `TextureRef` SHALL expose `event Action? OnDisposed`.
4. THE `TextureRef` SHALL expose a `DisposeResource()` method that calls `_resource.Dispose()` and then invokes `OnDisposed` synchronously before returning.
5. WHEN `DisposeResource()` is called on a `TextureRef` whose `_resource` is already disposed, THE `TextureRef` SHALL invoke `OnDisposed` exactly once and SHALL NOT throw.
6. THE `TextureRef.Null` sentinel SHALL hold `TextureResource.Null` as its internal resource, and `GetHandle()` on `TextureRef.Null` SHALL return an invalid handle.
7. FOR ALL valid `TextureRef` instances `r`, `r.GetHandle().Index` SHALL equal `r._resource.Handle.Index` at all times (handle-identity invariant).

#### Correctness Properties

- **Handle-identity invariant** (property): For any `TextureRef` constructed with a valid `TextureResource`, `GetHandle().Index == _resource.Handle.Index` holds before and after any number of `GetHandle()` calls.
- **OnDisposed fires exactly once** (example): Given a `TextureRef` with one subscriber on `OnDisposed`, calling `DisposeResource()` invokes the subscriber exactly once.
- **OnDisposed fires before return** (example): Given a `TextureRef` with a subscriber that sets a flag, the flag is set to `true` before `DisposeResource()` returns.

---

### Requirement 2: Repository Holds Canonical Ref

**User Story:** As a material system, I want all callers that request the same cache key to receive the exact same `TextureRef` object reference, so that `OnDisposed` subscriptions are shared and there is no ref proliferation.

#### Acceptance Criteria

1. THE `TextureCacheEntry` SHALL store a `TextureRef Ref` field instead of a `TextureResource Resource` field.
2. WHEN `ITextureRepository.GetOrCreateFromStream`, `GetOrCreateFromFile`, or `GetOrCreateFromImage` is called with a key that already exists in the cache, THE `TextureRepository` SHALL return the same `TextureRef` object reference that was stored at creation time (reference-identity guarantee).
3. THE `TextureRepository.AddResourceReference` override SHALL be a no-op (the repository is the sole owner; callers do not increment the reference count).
4. WHEN `TextureRepository.DisposeEntry` is called on a `TextureCacheEntry`, THE `TextureRepository` SHALL call `entry.Ref.DisposeResource()`.
5. THE `SamplerModuleCacheEntry` SHALL store a `SamplerRef Ref` field instead of a `SamplerResource Resource` field.
6. WHEN `ISamplerRepository.GetOrCreate` is called with a description whose cache key already exists, THE `SamplerRepository` SHALL return the same `SamplerRef` object reference that was stored at creation time.
7. WHEN `SamplerRepository.DisposeEntry` is called on a `SamplerModuleCacheEntry`, THE `SamplerRepository` SHALL call `entry.Ref.DisposeResource()`.

#### Correctness Properties

- **Ref identity on cache hit** (property): For any cache key `K`, two successive calls to `GetOrCreateFrom*(K, ...)` on the same repository instance SHALL return `object.ReferenceEquals(ref1, ref2) == true`.
- **Dispose propagates to ref** (example): After `TextureRepository.Remove(key)` is called, the `TextureRef` previously returned for that key SHALL have its `OnDisposed` event fired.

---

### Requirement 3: OnDisposed Push Notification

**User Story:** As a material property manager, I want to be notified synchronously when a texture or sampler is disposed, so that I can zero the corresponding bindless index before the next render frame reads stale data.

#### Acceptance Criteria

1. THE `TextureRef` SHALL expose `event Action? OnDisposed` as a public event.
2. THE `SamplerRef` SHALL expose `event Action? OnDisposed` as a public event.
3. WHEN `TextureRef.DisposeResource()` is called, THE `TextureRef` SHALL invoke all `OnDisposed` subscribers synchronously on the calling thread before the method returns.
4. WHEN `SamplerRef.DisposeResource()` is called, THE `SamplerRef` SHALL invoke all `OnDisposed` subscribers synchronously on the calling thread before the method returns.
5. WHEN `TextureRepository.Remove(key)` or `TextureRepository.Clear()` triggers `DisposeEntry`, THE `TextureRepository` SHALL call `entry.Ref.DisposeResource()`, which SHALL fire `OnDisposed` on the ref.
6. WHEN `SamplerRepository.Remove(key)` or `SamplerRepository.Clear()` triggers `DisposeEntry`, THE `SamplerRepository` SHALL call `entry.Ref.DisposeResource()`, which SHALL fire `OnDisposed` on the ref.
7. IF `OnDisposed` has no subscribers, THEN THE `TextureRef` and `SamplerRef` SHALL complete `DisposeResource()` without throwing.

#### Correctness Properties

- **Synchronous delivery** (example): Given a subscriber that records the thread ID inside `OnDisposed`, the recorded thread ID SHALL equal the thread ID of the `DisposeResource()` caller.
- **Multi-subscriber delivery** (example): Given N subscribers on `OnDisposed`, all N handlers SHALL be invoked when `DisposeResource()` is called.

---

### Requirement 4: PBRMaterialProperties Manages OnDisposed Subscriptions

**User Story:** As a PBR material, I want my bindless texture and sampler indices to be zeroed automatically when the underlying resource is disposed, so that the GPU never reads a dangling bindless index.

#### Acceptance Criteria

1. THE `PBRMaterialProperties` SHALL store one named `Action` delegate field per texture/sampler property (e.g. `_onAlbedoMapDisposed`, `_onNormalMapDisposed`, etc.) rather than inline lambdas, so that unsubscription is possible.
2. WHEN a texture property setter (e.g. `AlbedoMap`) is called with a new `TextureRef` value, THE `PBRMaterialProperties` SHALL unsubscribe the stored delegate from the old ref's `OnDisposed` before assigning the new ref.
3. WHEN a texture property setter is called with a new `TextureRef` value, THE `PBRMaterialProperties` SHALL subscribe the stored delegate to the new ref's `OnDisposed` after assigning the new ref.
4. WHEN a sampler property setter (e.g. `Sampler`, `DisplaceSampler`) is called with a new `SamplerRef` value, THE `PBRMaterialProperties` SHALL unsubscribe the stored delegate from the old ref's `OnDisposed` before assigning the new ref.
5. WHEN a sampler property setter is called with a new `SamplerRef` value, THE `PBRMaterialProperties` SHALL subscribe the stored delegate to the new ref's `OnDisposed` after assigning the new ref.
6. WHEN the `OnDisposed` callback fires for a texture field, THE `PBRMaterialProperties` SHALL set the corresponding `Properties.xxxTexIndex` to `0` and call `NotifyUpdated()`.
7. WHEN the `OnDisposed` callback fires for a sampler field, THE `PBRMaterialProperties` SHALL set the corresponding `Properties.xxxSamplerIndex` to `0` and call `NotifyUpdated()`.
8. WHEN `PBRMaterialProperties.Dispose()` is called, THE `PBRMaterialProperties` SHALL unsubscribe all stored delegates from their respective refs' `OnDisposed` events before releasing the pool entry.

#### Correctness Properties

- **Index zeroed on dispose** (example): Given a `PBRMaterialProperties` with `AlbedoMap` set to a valid `TextureRef`, calling `DisposeResource()` on that ref SHALL result in `Properties.AlbedoTexIndex == 0`.
- **Old ref not affected after replacement** (example): Given `AlbedoMap` set to ref A then replaced with ref B, calling `DisposeResource()` on ref A SHALL NOT change `Properties.AlbedoTexIndex` (unsubscription was effective).
- **NotifyUpdated called on dispose** (example): Given a subscriber on the `EventBus` for `MaterialPropsUpdatedEvent`, disposing a texture ref held by a material SHALL cause a `MaterialPropertyOp.Update` event to be published.

---

### Requirement 5: Replace Methods Removed

**User Story:** As an API consumer, I want a clear, explicit update pattern (Remove then GetOrCreate) instead of implicit Replace semantics, so that `OnDisposed` fires predictably and there is no ambiguity about ref identity after an update.

#### Acceptance Criteria

1. THE `ITextureRepository` SHALL NOT expose `ReplaceFromStream`, `ReplaceFromFile`, or `ReplaceFromImage` methods.
2. THE `TextureRepository` SHALL NOT implement `ReplaceFromStream`, `ReplaceFromFile`, or `ReplaceFromImage`.
3. THE `NullTextureRepository` SHALL NOT implement `ReplaceFromStream`, `ReplaceFromFile`, or `ReplaceFromImage`.
4. WHEN a caller wants to update a texture, THE caller SHALL call `ITextureRepository.Remove(key)` followed by the appropriate `GetOrCreateFrom*` method.
5. WHEN `Remove(key)` is called, THE `TextureRepository` SHALL fire `OnDisposed` on the old ref (via `DisposeEntry`), zeroing any material indices subscribed to it, before the new ref is created.
6. WHEN `GetOrCreateFrom*(key, ...)` is called after `Remove(key)`, THE `TextureRepository` SHALL create and cache a new `TextureRef` object for that key.

#### Correctness Properties

- **Remove-then-create produces new ref** (example): After `Remove(key)` followed by `GetOrCreateFromStream(key, ...)`, the returned `TextureRef` SHALL NOT be reference-equal to the ref returned before `Remove`.
- **OnDisposed fires on Remove** (example): A subscriber on the old ref's `OnDisposed` SHALL be invoked when `Remove(key)` is called.

---

### Requirement 6: Async Texture Creation

**User Story:** As a scene loader, I want to create GPU textures asynchronously so that pixel data upload does not block the main thread while the scene is loading.

#### Acceptance Criteria

1. THE `ITextureRepository` SHALL expose `GetOrCreateFromStreamAsync(string name, Stream stream, string? debugName = null)` returning `Task<TextureRef>`.
2. THE `ITextureRepository` SHALL expose `GetOrCreateFromFileAsync(string filePath, string? debugName = null)` returning `Task<TextureRef>`.
3. THE `ITextureRepository` SHALL expose `GetOrCreateFromImageAsync(string name, Image image)` returning `Task<TextureRef>`.
4. WHEN `GetOrCreateFromStreamAsync` is called with a key that already exists in the cache, THE `TextureRepository` SHALL return a completed `Task<TextureRef>` wrapping the existing cached ref without re-uploading pixel data.
5. WHEN `GetOrCreateFromStreamAsync` is called with a new key, THE `TextureRepository` SHALL allocate the `TextureResource` (GPU memory) synchronously, store the new `TextureRef` in the cache immediately, and upload pixel data asynchronously via `TextureCreator.CreateTextureFromStreamAsync`.
6. WHEN `GetOrCreateFromFileAsync` is called with a new key, THE `TextureRepository` SHALL allocate the `TextureResource` synchronously, store the new `TextureRef` in the cache immediately, and upload pixel data asynchronously via `TextureCreator.CreateTextureFromStreamAsync`.
7. WHEN `GetOrCreateFromImageAsync` is called with a new key, THE `TextureRepository` SHALL allocate the `TextureResource` synchronously, store the new `TextureRef` in the cache immediately, and upload pixel data asynchronously via `TextureCreator.CreateTextureAsync`.
8. WHILE an async upload is in progress for a key, WHEN a second caller calls any `GetOrCreateFrom*` method with the same key, THE `TextureRepository` SHALL return the same `TextureRef` object reference that was stored during the first call.
9. WHEN the `Task<TextureRef>` returned by an async method completes, THE caller SHALL await the task on the main thread before assigning the returned `TextureRef` to a `PBRMaterialProperties` field.
10. IF `GetOrCreateFromFileAsync` is called with a path that does not exist on disk, THEN THE `TextureRepository` SHALL return a faulted `Task<TextureRef>` containing a `FileNotFoundException`.

#### Correctness Properties

- **Cache-hit returns completed task** (property): For any key `K` already present in the cache, `GetOrCreateFromStreamAsync(K, ...)` SHALL return a `Task<TextureRef>` where `IsCompleted == true` immediately upon return.
- **Concurrent callers get same ref** (property): Given N concurrent calls to `GetOrCreateFromStreamAsync` with the same key on the same repository, all N tasks SHALL complete with `object.ReferenceEquals` equal refs.
- **Ref available before upload completes** (example): After calling `GetOrCreateFromStreamAsync` with a new key, `TryGet(key, out _)` SHALL return `true` before the returned task completes.

---

### Requirement 7: EventBus Simplified to Synchronous Dispatch

**User Story:** As a maintainer, I want `EventBus` to dispatch events synchronously on the calling thread without a background thread or queue, so that the implementation is simpler and event delivery is deterministic.

#### Acceptance Criteria

1. THE `EventBus` SHALL remove the `_publishThread` background thread and its `ProcessEventQueue` loop.
2. THE `EventBus` SHALL remove the `_eventQueue` (`ConcurrentQueue<Action>`) field.
3. THE `EventBus` SHALL remove the `_cancellationTokenSource` field.
4. THE `EventBus` SHALL remove the `_mainThreadContext` (`SynchronizationContext?`) field.
5. THE `EventBus` SHALL remove the `PublishAsync<TEvent>` method.
6. THE `EventBus.Subscribe` method SHALL remove the `dispatchOnMainThread` parameter.
7. THE `EventBus.Publish<TEvent>` method SHALL invoke all registered handlers synchronously on the calling thread before returning.
8. WHEN `EventBus.Publish<TEvent>` is called, THE `EventBus` SHALL invoke each handler in subscription order before the method returns.
9. IF a handler throws during `EventBus.Publish`, THEN THE `EventBus` SHALL log the exception and continue invoking remaining handlers without re-throwing.

#### Correctness Properties

- **Synchronous delivery** (example): Given a subscriber that sets a flag, calling `Publish(event)` SHALL result in the flag being set before `Publish` returns.
- **Handler order preserved** (example): Given two subscribers A and B registered in that order, `Publish` SHALL invoke A before B.
- **Exception isolation** (example): Given two subscribers where the first throws, the second subscriber SHALL still be invoked.

---

### Requirement 8: Thread Safety Assumption

**User Story:** As a system architect, I want the resource-ref lifecycle to document its threading model explicitly, so that implementors do not add unnecessary synchronization.

#### Acceptance Criteria

1. THE `TextureRef`, `SamplerRef`, and `PBRMaterialProperties` SHALL assume that all calls to `GetHandle()`, `DisposeResource()`, property setters, and `OnDisposed` callbacks occur on the main thread.
2. THE `TextureRef` and `SamplerRef` SHALL NOT use locks, `Interlocked` operations, or `volatile` fields to protect `OnDisposed` invocation or `_resource` access.
3. THE `PBRMaterialProperties` SHALL NOT use locks or `Interlocked` operations to protect texture/sampler index writes triggered by `OnDisposed` callbacks.
4. WHERE async texture creation is used (`GetOrCreateFromStreamAsync`, `GetOrCreateFromFileAsync`, `GetOrCreateFromImageAsync`), THE `TextureRepository` SHALL protect the cache insertion of the new `TextureRef` with the existing repository-level lock so that concurrent background tasks do not create duplicate entries.
5. WHEN the `Task<TextureRef>` returned by an async creation method completes, THE caller SHALL assign the result to `PBRMaterialProperties` fields only on the main thread.
