# HelixToolkit.Nex.glTF

The `HelixToolkit.Nex.glTF` package imports glTF 2.0 files into the HelixToolkit-Nex scene graph. It parses, converts, and integrates glTF assets — meshes, materials, textures, lights, and nodes — into the engine's ECS-based scene and rendering pipeline.

## Overview

The `HelixToolkit.Nex.glTF` package is responsible for:
- Importing glTF 2.0 / GLB files and converting them into the HelixToolkit-Nex scene graph.
- Converting glTF materials to the engine's PBR material properties.
- Managing GPU resources (textures, samplers, geometries) created during import.
- Providing diagnostics (warnings and errors) about the import process.
- Supporting `KHR_draco_mesh_compression` and `KHR_lights_punctual`.

Internally the importer builds the scene graph through the Scene module's **`SceneCommandBuffer`**
rather than mutating the ECS `World` directly. This splits an import into two phases: an off-thread
**prepare/record** phase and an owning-thread **complete/flush** phase (see Threading Model below).

## Key Types

| Type                   | Description                                                                                                                                                                        |
| ---------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Importer`             | Main entry point. Provides `Import`/`ImportAsync` (one-shot) and `PrepareImport`/`PrepareImportAsync` (two-phase).                                                                 |
| `PreparedImport`       | The result of the prepare phase: holds the recorded `SceneCommandBuffer`, diagnostics, and created GPU resources. Materialized by calling `Complete` on the world's owning thread. |
| `ImportResult`         | Contains the root node of the imported scene, diagnostics, and the resource manifest. Implements `IDisposable`.                                                                    |
| `ImportDiagnostic`     | A diagnostic entry with severity, message, and reference to the glTF element.                                                                                                      |
| `DiagnosticSeverity`   | Severity level of a diagnostic (`Warning`, `Error`).                                                                                                                               |
| `ImporterConfig`       | Configuration: default shading mode, Draco decompression, point-light mesh options, etc.                                                                                           |
| `ResourceManifest`     | Tracks all GPU resources created during import for disposal / readiness tracking.                                                                                                  |
| `DirectionalLightInfo` | Directional light component attached to a node's entity (from `KHR_lights_punctual`).                                                                                              |
| `RangeLightInfo`       | Point/spot light component attached to a node's entity (from `KHR_lights_punctual`).                                                                                               |

## Usage Examples

### Importing a glTF File (synchronous)

`Import` runs the whole pipeline on the calling thread, which **must be the world's owning thread**.

```csharp
using HelixToolkit.Nex.glTF;
using HelixToolkit.Nex.Engine;

var importer = new Importer();
var worldData = engine.CreateWorldDataProvider(); // owns the ECS world + resource managers

using ImportResult result = importer.Import("path/to/model.gltf", worldData);

if (result.Success)
{
    var rootNode = result.RootNode;
    foreach (var diagnostic in result.Diagnostics)
    {
        Console.WriteLine($"{diagnostic.Severity}: {diagnostic.Message}");
    }
}
```

### Two-Phase Import (load off-thread, materialize on the render thread)

This is the recommended pattern for avoiding render-thread stalls on large assets: run the heavy
parse/convert/record work on a background thread, then flush onto the world on the owning thread.

```csharp
var importer = new Importer();

// 1. Prepare off the render thread: parses, loads buffers, converts meshes/materials/textures
//    (async GPU-upload path), and records the scene graph into a SceneCommandBuffer.
//    No World is touched here.
PreparedImport prepared = await importer
    .PrepareImportAsync("path/to/model.gltf", worldData)
    .ConfigureAwait(false);

// 2. Complete on the world's owning (render) thread: this only flushes the recorded buffer,
//    constructing the engine Nodes and setting their (already-uploaded) GPU resources.
//    Marshal back to the render thread however your app does it (dispatcher, queue, etc.).
ImportResult result = renderThread.Invoke(() => prepared.Complete(worldData));

// If you never complete a prepared import, dispose it to release the GPU resources it created.
```

### One-Shot Async Import

`ImportAsync` is a convenience that prepares and completes in one call. Because the completing flush
mutates the world, **it must be awaited on the world's owning thread**; only the internal load phase
runs off-thread.

```csharp
using ImportResult result = await importer.ImportAsync("path/to/model.gltf", worldData, null, ct);
```

## Threading Model

A glTF import is deliberately split so the expensive work can run off the render thread:

| Phase                                                   | Method                                    | Thread                         | Touches `World`?                          |
| ------------------------------------------------------- | ----------------------------------------- | ------------------------------ | ----------------------------------------- |
| Parse + load buffers + convert + **record scene graph** | `PrepareImportAsync` / `RecordSceneAsync` | Any (background)               | No — recorded into a `SceneCommandBuffer` |
| **Materialize** (construct `Node`s, set ECS components) | `Complete` / `SceneCommandBuffer.Flush`   | World's owning (render) thread | Yes                                       |

- During recording, `SceneBuilder` walks the glTF node tree and records deferred operations:
  `RecordCreateNode` (including factory overloads for `MeshNode`s and light-bearing nodes),
  `RecordName`, `RecordLocalTransform`, and `RecordAddChild`. Mesh/material/texture/light
  *conversion* operates on the resource managers (not the ECS world) and, in the async path, uses
  the asynchronous GPU-upload path (`AddAsync` / `LoadTextureAsync`), so it is safe off-thread.
- `Complete` flushes the buffer with `SceneCommandBuffer.Flush(world)`, which replays the commands
  through the real `Node` API on the owning thread. The root is then read from
  `MaterializedNodes` keyed by the recorded root handle.
- The synchronous `Import` performs both phases on the calling thread, which must be the owning
  thread.

This mirrors the deferred record-then-flush pattern documented in the `HelixToolkit.Nex.Scene`
README; the glTF importer is its primary consumer.

## Architecture Notes

- **Deferred scene building**: The scene graph is constructed via `SceneCommandBuffer` (record on any thread, flush on the owning thread), keeping world mutation single-threaded.
- **Dependencies**: `HelixToolkit.Nex.Scene` (nodes), `HelixToolkit.Nex.Engine` (`WorldDataProvider`, mesh/light components), `HelixToolkit.Nex.Material` (material properties), and `HelixToolkit.Nex.Repository` (GPU resources).
- **Resource management**: `ResourceManifest` tracks GPU resources created during import. Ownership transfers to `ImportResult` on successful completion; otherwise dispose the `PreparedImport`.
- **ECS integration**: Imported nodes are ECS entities; lights are attached as `DirectionalLightInfo` / `RangeLightInfo` components on the referencing node's own entity.
- **Draco compression**: Supports `KHR_draco_mesh_compression`; decode severity depends on whether the extension is listed in `extensionsRequired`.
- **Lighting**: `KHR_lights_punctual` lights are resolved at record time (with diagnostics) and materialized during flush.
