# Tasks: resource-ref-lifecycle

## Task List

- [x] 1. Simplify EventBus to synchronous-only dispatch
  - [x] 1.1 Remove `_publishThread`, `ProcessEventQueue`, `_eventQueue`, `_cancellationTokenSource`, and `_mainThreadContext` fields from `EventBus`
  - [x] 1.2 Remove `PublishAsync<TEvent>` method from `EventBus`
  - [x] 1.3 Remove `dispatchOnMainThread` parameter from `EventBus.Subscribe<TEvent>`
  - [x] 1.4 Remove `DispatchOnMainThread` property from `EventSubscription<TEvent>`
  - [x] 1.5 Simplify `PublishInternal` to call handlers directly without `SynchronizationContext.Post`
  - [x] 1.6 Simplify `EventBus.Dispose` to remove thread join and `CancellationTokenSource` disposal
  - [x] 1.7 Remove `EventBus(bool captureMainThreadContext)` and `EventBus(SynchronizationContext? context)` constructors; replace with a single no-arg constructor
  - [x] 1.8 Update `EventBusTests.cs`: remove tests for `PublishAsync`, `dispatchOnMainThread`, `captureMainThreadContext`, and `SynchronizationContext` dispatch; update `Dispose_ShouldThrowOnSubsequentCalls` to remove `PublishAsync` assertion

- [x] 2. Redesign TextureRef to hold TextureResource internally
  - [x] 2.1 Change `TextureRef` constructor to accept `TextureResource resource` instead of `Handle<Texture> initialHandle`
  - [x] 2.2 Replace `_cachedHandle` field with `internal TextureResource _resource` field
  - [x] 2.3 Rewrite `GetHandle()` to return `_resource.Handle` directly (remove `TryGet` re-fetch logic)
  - [x] 2.4 Add `event Action? OnDisposed` to `TextureRef`
  - [x] 2.5 Add `internal void DisposeResource()` that calls `_resource.Dispose()` then `OnDisposed?.Invoke()`
  - [x] 2.6 Add `IDisposable` implementation to `TextureRef` where `Dispose()` calls `DisposeResource()` (satisfies `CacheEntry<TResource>` constraint)
  - [x] 2.7 Update `TextureRef.Null` sentinel to use `TextureResource.Null` as the internal resource
  - [x] 2.8 Update `NullTextureRepository` to remove `ReplaceFrom*` method implementations

- [x] 3. Redesign SamplerRef to hold SamplerResource internally
  - [x] 3.1 Change `SamplerRef` constructor to accept `SamplerResource resource` instead of `Handle<Sampler> initialHandle`
  - [x] 3.2 Replace `_cachedHandle` field with `internal SamplerResource _resource` field
  - [x] 3.3 Rewrite `GetHandle()` to return `_resource.Handle` directly (remove `TryGet` re-fetch logic)
  - [x] 3.4 Add `event Action? OnDisposed` to `SamplerRef`
  - [x] 3.5 Add `internal void DisposeResource()` that calls `_resource.Dispose()` then `OnDisposed?.Invoke()`
  - [x] 3.6 Add `IDisposable` implementation to `SamplerRef` where `Dispose()` calls `DisposeResource()`
  - [x] 3.7 Update `SamplerRef.Null` sentinel to use `SamplerResource.Null` as the internal resource
  - [x] 3.8 Update `NullSamplerRepository` to remove any stale `TryGet` logic that references `Sampler` property

- [x] 4. Update ITextureRepository interface
  - [x] 4.1 Remove `ReplaceFromStream`, `ReplaceFromFile`, `ReplaceFromImage` method declarations
  - [x] 4.2 Add `Task<TextureRef> GetOrCreateFromStreamAsync(string name, Stream stream, string? debugName = null)` declaration
  - [x] 4.3 Add `Task<TextureRef> GetOrCreateFromFileAsync(string filePath, string? debugName = null)` declaration
  - [x] 4.4 Add `Task<TextureRef> GetOrCreateFromImageAsync(string name, Image image)` declaration
  - [x] 4.5 Update `TryGet` XML doc to reference `TextureCacheEntry.Ref` instead of `TextureCacheEntry.Texture`

- [x] 5. Update ISamplerRepository interface
  - [x] 5.1 Update `TryGet` XML doc to reference `SamplerModuleCacheEntry.Ref` instead of `SamplerModuleCacheEntry.Sampler`
  - [x] 5.2 Verify no other interface changes are needed (no async methods for samplers)

- [x] 6. Update TextureRepository implementation
  - [x] 6.1 Change `TextureCacheEntry` to extend `CacheEntry<TextureRef>` and rename `Texture` property to `Ref`
  - [x] 6.2 Change `TextureRepository` base class type parameter from `TextureResource` to `TextureRef`
  - [x] 6.3 Update `AddResourceReference` override to be a no-op (remove body)
  - [x] 6.4 Update `DisposeEntry` override to call `entry.Ref.DisposeResource()`
  - [x] 6.5 Rewrite `StoreEntry` to create `TextureRef(cacheKey, this, texture)` and store it as `TextureCacheEntry.Resource`
  - [x] 6.6 Update all `GetOrCreateFrom*` cache-hit paths to return `cached!.Ref` instead of constructing a new `TextureRef`
  - [x] 6.7 Remove `ReplaceFromStream`, `ReplaceFromFile`, `ReplaceFromImage` methods and the `_replaceLock` field
  - [x] 6.8 Add `TextureCreator.CreateTextureAsyncWithResource` helper method (returns `(TextureResource, AsyncUploadHandle<TextureHandle>)`) to `TextureCreator`
  - [x] 6.9 Implement `GetOrCreateFromStreamAsync`: check cache, decode image synchronously, create GPU resource synchronously via new helper, call `StoreEntry`, upload asynchronously, return ref
  - [x] 6.10 Implement `GetOrCreateFromFileAsync`: same pattern as stream async, using file path normalization
  - [x] 6.11 Implement `GetOrCreateFromImageAsync`: same pattern, using `TextureCreator.CreateTextureAsync` directly
  - [x] 6.12 Update `NullTextureRepository` to add async method stubs returning `Task.FromResult(TextureRef.Null)` and remove `Replace*` implementations

- [x] 7. Update SamplerRepository implementation
  - [x] 7.1 Change `SamplerModuleCacheEntry` to extend `CacheEntry<SamplerRef>` and rename `Sampler` property to `Ref`
  - [x] 7.2 Change `SamplerRepository` base class type parameter from `SamplerResource` to `SamplerRef`
  - [x] 7.3 Update `AddResourceReference` override to be a no-op (remove `resource.AddReference()` call)
  - [x] 7.4 Update `DisposeEntry` override to call `entry.Ref.DisposeResource()`
  - [x] 7.5 Rewrite `StoreEntry` to create `SamplerRef(cacheKey, this, sampler)` and store it as `SamplerModuleCacheEntry.Resource`; remove `AddResourceReference(sampler)` call
  - [x] 7.6 Update `GetOrCreate` cache-hit path to return `cached!.Ref` instead of constructing a new `SamplerRef`

- [x] 8. Checkpoint: build Repository project
  - [x] 8.1 Run `dotnet build Source/HelixToolkit-Nex/HelixToolkit.Nex.Repository/HelixToolkit.Nex.Repository.csproj` and resolve all errors
  - [x] 8.2 Run `dotnet build Source/HelixToolkit-Nex/HelixTookit.Nex/HelixTookit.Nex.csproj` and resolve all errors

- [x] 9. Update PBRMaterialProperties
  - [x] 9.1 Add eight named `Action` delegate fields: `_onAlbedoMapDisposed`, `_onNormalMapDisposed`, `_onMetallicRoughnessMapDisposed`, `_onAoMapDisposed`, `_onBumpMapDisposed`, `_onDisplaceMapDisposed`, `_onSamplerDisposed`, `_onDisplaceSamplerDisposed`
  - [x] 9.2 Initialize all eight delegates in the `internal PBRMaterialProperties(...)` constructor with lambdas that check `Valid`, zero the corresponding `Properties.xxxTexIndex` or `Properties.xxxSamplerIndex`, and call `NotifyUpdated()`
  - [x] 9.3 Initialize all eight delegates to no-op lambdas in the private `PBRMaterialProperties()` constructor (used for `Null`)
  - [x] 9.4 Update `AlbedoMap` setter: unsubscribe `_onAlbedoMapDisposed` from old ref, assign new ref, subscribe to new ref, update index, notify
  - [x] 9.5 Update `NormalMap` setter: same pattern
  - [x] 9.6 Update `MetallicRoughnessMap` setter: same pattern
  - [x] 9.7 Update `AoMap` setter: same pattern
  - [x] 9.8 Update `BumpMap` setter: same pattern
  - [x] 9.9 Update `DisplaceMap` setter: same pattern
  - [x] 9.10 Update `Sampler` setter: unsubscribe `_onSamplerDisposed` from old ref, assign new ref, subscribe to new ref, update index, notify
  - [x] 9.11 Update `DisplaceSampler` setter: same pattern
  - [x] 9.12 Update `Dispose(bool disposing)`: unsubscribe all eight delegates from their respective refs before calling `_pool?.Destroy(_handle)`

- [x] 10. Update callers
  - [x] 10.1 Search for any call sites using `entry.Texture` on `TextureCacheEntry` and update to `entry.Ref`
  - [x] 10.2 Search for any call sites using `entry.Sampler` on `SamplerModuleCacheEntry` and update to `entry.Ref`
  - [x] 10.3 Search for any `ReplaceFrom*` call sites and replace with `Remove` + `GetOrCreateFrom*`
  - [x] 10.4 Search for any `Subscribe(..., dispatchOnMainThread: ...)` call sites and remove the parameter
  - [x] 10.5 Search for any `PublishAsync` call sites and change to `Publish`
  - [x] 10.6 Search for any `new EventBus(captureMainThreadContext: ...)` or `new EventBus(context)` call sites and update to `new EventBus()`
  - [x] 10.7 Search for any `new TextureRef(key, repo, handle)` call sites (old constructor) and update to the new constructor signature
  - [x] 10.8 Search for any `new SamplerRef(key, repo, handle)` call sites (old constructor) and update to the new constructor signature

- [x] 11. Checkpoint: build full solution
  - [x] 11.1 Run `dotnet build Source/HelixToolkit-Nex/HelixToolkit.Nex.sln` and resolve all remaining errors

- [x] 12. Update and add tests
  - [x] 12.1 Update `TextureRefPropertyTests.cs`: remove `MockTextureRepository.ReplaceFrom*` methods; update `TextureCacheEntry` construction to use `Resource = textureRef` (a `TextureRef`) instead of `Resource = tex` (a `TextureResource`); remove `Property3_StaleHandle_TriggersReFetch` (lazy-fetch behavior removed)
  - [x] 12.2 Update `TextureRefPropertyTests.cs`: add Property 1 test — handle-identity invariant (for any TextureRef, `GetHandle().Index == _resource.Handle.Index` across N calls)
  - [x] 12.3 Update `TextureRefPropertyTests.cs`: add Property 2 test — ref identity on cache hit (two successive `GetOrCreate` calls with same key return `ReferenceEquals` true)
  - [x] 12.4 Add `TextureRefTests.cs` tests: `DisposeResource_FiresOnDisposed`, `DisposeResource_WithNoSubscribers_DoesNotThrow`, `DisposeResource_FiresOnDisposed_Synchronously`
  - [x] 12.5 Update `SamplerRefPropertyTests.cs`: same structural updates as `TextureRefPropertyTests.cs`
  - [x] 12.6 Add `SamplerRefTests.cs` tests: `DisposeResource_FiresOnDisposed`, `DisposeResource_WithNoSubscribers_DoesNotThrow`
  - [x] 12.7 Add `PBRMaterialPropertiesTests.cs` (new file in `HelixToolkit.Nex.Material.Tests`):
    - Property 3 test: for any texture slot, assigning a valid ref then disposing it zeros the index
    - Example: assigning ref A then ref B, disposing ref A does NOT zero the index (unsubscription effective)
    - Example: `PBRMaterialProperties.Dispose()` then disposing a held ref does not fire callbacks
    - Example: `NotifyUpdated` is called when `OnDisposed` fires
  - [x] 12.8 Add `TextureRepositoryTests.cs` (new file in `HelixToolkit.Nex.Repository.Tests`):
    - Example: `Remove(key)` fires `OnDisposed` on the previously returned ref
    - Property 4 test: cache-hit `GetOrCreateFromStreamAsync` returns completed task
    - Example: `GetOrCreateFromFileAsync` with missing file returns faulted task with `FileNotFoundException`
    - Example: ref stored in cache before async upload completes (`TryGet` returns true immediately)
  - [x] 12.9 Update `EventBusTests.cs`: remove `PublishAsync_ShouldInvokeHandler_Eventually`, `PublishAsync_ShouldRunOnBackgroundThread_WhenNotDispatchingToMain`, `PublishAsync_ShouldDispatchToSynchronizationContext`, `Publish_ShouldDispatchToSynchronizationContext_WhenConfigured` tests; update `Dispose_ShouldThrowOnSubsequentCalls` to remove `PublishAsync` assertion; update `ConcurrentPublish_ShouldBeThreadSafe` to remove `dispatchOnMainThread: false` parameter

- [x] 13. Final checkpoint: run all tests
  - [x] 13.1 Run `dotnet test Source/HelixToolkit-Nex/HelixToolkit.Nex.sln` and verify all tests pass
