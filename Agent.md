# Agent Guide — HelixToolkit Nex

This file gives an AI agent the context needed to work productively in this repository. Read it before making changes. For human-facing setup see [DEV.md](DEV.md), [CONTRIBUTING.md](CONTRIBUTING.md), and [CODE_FORMATTING.md](CODE_FORMATTING.md).

## What this project is

HelixToolkit Nex is the next-generation 3D graphics engine from HelixToolkit, written in **C# / .NET 9**. It exposes a unified graphics interface designed to support multiple GPU backends, with the current implementation targeting **Vulkan 1.3**. The graphics interface and Vulkan backend are inspired by [LightWeightVk](https://github.com/corporateshark/lightweightvk).

Key characteristics:
- **Bindless descriptor architecture** — a single large descriptor set of resource arrays; shaders index resources by integer handles rather than binding per-draw.
- **Reverse-Z depth** — all projection matrices use reverse-Z for improved depth precision. Depth clears to `0.0`, depth test is "greater".
- **Render-graph driven** — rendering is organized as a graph of nodes with declared input/output resources.
- **ECS-based scene management** — a custom data-oriented Entity Component System underpins the scene graph and render data.
- **AOT-friendly and cross-platform** — builds and runs on Windows and Linux (tested on Ubuntu). Reflection-heavy patterns are avoided.

The project is under active development; APIs change frequently. Each package's `README.md` documents its current key types and recent changes — consult it before assuming an API shape.

## Repository layout

```
/                       Root: README.md, DEV.md, CONTRIBUTING.md, CODE_FORMATTING.md, .editorconfig
Assets/                 Models, textures, environment maps, icons (Git LFS — see below)
Scripts/                PowerShell + bash helper scripts (build, format, git hooks/LFS)
Documentation/          DocFX articles + generated api docs
Source/HelixToolkit-Nex/
  HelixToolkit.Nex.slnx           The solution (new XML .slnx format)
  <one folder per package>        Library projects (see package map below)
  <package>.Tests/                MSTest test project paired with each library
  Samples/                        Runnable sample apps
    GraphicsAPI/    Low-level graphics-API demos (HelloTriangle, MeshCulling, ...)
    Integration/    Engine-level demos (PBRTest, LightCulling, gltfImporter, ...)
    Interop/        WPF / WinUI / Avalonia embedding demos
    SceneSamples/, Application/
```

Every library folder contains a `README.md` with its purpose, a key-types table, usage examples, and a "recent changes" section. **These READMEs are the primary architecture reference — keep them in mind and update them when you change public API.**

## Package architecture (dependency layering)

Packages are layered bottom-up. Lower layers never depend on higher ones.

**Foundation**
- `HelixToolkit.Nex` (folder `HelixToolkit.Nex`) — base package: DI helpers, serialization, tracing, shared `ResultCode`. Deps: `Microsoft.Extensions.Logging`, `ZLinq`.
- `HelixToolkit.Nex.Maths` — vectors, matrices, collections, type converters. **Row-major** matrices (see conventions).
- `HelixToolkit.Nex.ECS` (folder `HelixToolkit.Nex.EntityComponentSystem`) — the Entity Component System: `World`, `Entity`, `Components<T>`, `EntityCollection`, event bus, deferred `CommandBuffer`.

**Graphics abstraction + backend**
- `HelixToolkit.Nex.Graphics` — backend-agnostic GPU interface: `IContext`, `ICommandBuffer`, resource descs (`BufferDesc`, `TextureDesc`, `RenderPipelineDesc`), barriers, `ElementBuffer<T>` / `RingElementBuffer<T>`.
- `HelixToolkit.Nex.Graphics.Vulkan` — Vulkan 1.3 implementation via `Vortice.Vulkan`. `VulkanContext`, `CommandBuffer`, staging device, pipeline builder, barrier planner.
- `HelixToolkit.Nex.Graphics.Mock` — headless/mock backend for tests.

**Resource + shader systems**
- `HelixToolkit.Nex.Shaders` — GLSL shader sources (`.vert`/`.frag`/`.comp` under `Vert/`, `Frag/`, `Compute/`, plus `HxHeaders/`), shader generation, compilation (`ShaderCompiler`), and caching.
- `HelixToolkit.Nex.CodeGen` — **Roslyn source generators**. Generates C# structs from GLSL structs annotated with `@code_gen` (ensuring CPU/GPU layout match) and observable properties from `[Observable]` fields. Do not hand-edit generated structs; edit the GLSL or the generator.
- `HelixToolkit.Nex.Textures` — texture loading/decoding.
- `HelixToolkit.Nex.Repository` — thread-safe LRU caches for textures, shaders, samplers (`TextureRepository`, `ShaderRepository`, `SamplerRepository`; `TextureRef`/`SamplerRef` wrappers).

**Geometry + assets**
- `HelixToolkit.Nex.Geometries` (folder `HelixToolkit.Nex.Geometry`) — geometry types, octrees, serialization.
- `HelixToolkit.Nex.Geometries.Builders` — mesh/geometry builders.
- `HelixToolkit.Nex.glTF` — glTF importer.

**Scene + rendering + engine**
- `HelixToolkit.Nex.Scene` — scene graph on top of ECS: `Node`, `Transform`, `WorldTransform`, hierarchy, `SceneCommandBuffer`.
- `HelixToolkit.Nex.Material` — PBR + point/billboard/line materials, material registries, pipeline management, shader building.
- `HelixToolkit.Nex.Rendering` — render graph (`RenderGraph`, `RenderContext`, `Renderer`), render/compute nodes (Forward+ light culling, GPU frustum culling), draw streams, post effects (Bloom, FXAA, SMAA, SSAO, tone mapping, wireframe, border highlight), gizmos, GPU picking.
- `HelixToolkit.Nex.Engine` — top-level coordinator: `Engine`, `EngineBuilder` (fluent config), cameras + camera controllers, lights, `WorldDataProvider`, per-viewport `RenderContext`.

**UI / interop**
- `HelixToolkit.Nex.ImGui` — Dear ImGui integration.
- `HelixToolkit.Nex.Wpf`, `HelixToolkit.Nex.WinUI` — **Windows-only** host controls (D3D11 interop via `VK_KHR_external_memory_win32`).
- `HelixToolkit.Nex.Avalonia` — cross-platform (Windows + Linux) Avalonia control.
- `HelixToolkit.Nex.Interop`, `HelixToolkit.Nex.Interop.DirectX`, `Interop.PlatformShared` — interop plumbing. The D3D11 path is guarded at runtime with `OperatingSystem.IsWindows()`.

When adding a dependency between packages, respect the layering — e.g. `Rendering` may use `Material`/`Graphics`, but `Graphics` must not reference `Rendering`.

## Core architectural concepts

- **ECS (data-oriented):** Components are plain structs stored contiguously per type, per `World`. Behavior lives in systems that iterate `Components<T>` / `EntityCollection`. A `World` is single-threaded — access it from one thread at a time. To build data off-thread, record into a `CommandBuffer` (ECS) or `SceneCommandBuffer` (Scene) and `Flush` on the world's owning thread. Up to 128 distinct component types per process. Tag components (empty structs via `Entity.Tag<T>()`) allocate no storage.
- **Scene graph:** A `Node` wraps an `Entity` plus scene components (`NodeInfo`, `Transform`, `WorldTransform`, `Parent`, `Children`, `Renderable`). `NodeInfo` sorts by hierarchy level so parents update before children; call `world.SortSceneNodes()` then `world.UpdateTransforms()`.
- **Render graph:** Nodes declare input/output `RenderResource`s (textures/buffers). The graph resolves execution order and allocates transient resources. Add resources/passes fluently on `RenderGraph`. Compute + render nodes coexist.
- **Draw streams:** Geometry to be drawn is grouped into draw streams keyed by `DrawStreamType` + `DrawStreamVariants` (instancing / hitability / dynamic state) and material. Registries (`MeshDrawStreamRegistry`, `PointDrawStreamRegistry`, `LineDrawStreamRegistry`) provide zero-allocation enumeration. Dynamic data uses `RingElementBuffer` to rotate buffer slots and avoid GPU stalls.
- **Bindless resources:** Textures/samplers are referenced by integer indices in push constants / storage buffers, not per-draw descriptor binds.
- **Barriers:** Use backend-agnostic `BarrierPreset` / `BarrierDescriptor`, `ImageTransition`, `PipelineStageFlags`, `AccessFlags` on `ICommandBuffer` rather than raw Vulkan barriers.
- **Async upload:** GPU uploads go through the transfer queue / staging device; `context.UploadAsync(...)` returns a handle you can await.
- **Frame loop:** `engine.BeginFrame()` must be called once per frame before rendering (`Submit` throws otherwise), then set viewport size + camera params and call `engine.Render(viewport, worldData)`.

## Conventions that matter for shaders

- **Matrices are row-major in C#** but **column-major in GLSL** — account for this at the CPU/GPU boundary.
- **Reverse-Z** projection everywhere (depth clear `0.0`, greater-equal compare).
- GPU-facing structs are generated from GLSL by `HelixToolkit.Nex.CodeGen` and laid out `[StructLayout(LayoutKind.Sequential, Pack = 16)]`. Keep GLSL and C# layouts in sync by editing the GLSL `@code_gen` struct, not the generated C#.
- GLSL types map to `System.Numerics` types (`vec3` → `Vector3`, `mat4` → `Matrix4x4`, etc.).

## Coding standards

Enforced by [.editorconfig](.editorconfig) and verified in CI via `dotnet format`. Key rules:

- **Language/runtime:** C# `LangVersion=latest`, `net9.0`, `Nullable=enable`, `ImplicitUsings=enable`, `AllowUnsafeBlocks=true`.
- ****Namespaces:** file-scoped (`namespace X;`) is used throughout the codebase.
- **`var`:** preferred everywhere (built-in types, apparent types, elsewhere).
- **Indentation:** 4 spaces for C#; line endings **CRLF** for `.cs`; final newline required; UTF-8. (YAML/JSON use 2-space indent.)
- **`using` directives:** system directives sorted first.
- **Naming:**
  - Private fields: `_camelCase` (leading underscore, camelCase). Not `myField`, not `_MyField`.
  - Interfaces: `IPascalCase`.
  - Public/internal fields, constants, static readonly, types, methods, properties, events: `PascalCase`.
- Add XML doc comments for public APIs. Prefer clear self-documenting names; keep methods focused.
- Prefer **`ZLinq`** over System.Linq in hot paths — the codebase uses it for allocation-free enumeration. Watch for allocations in per-frame / per-draw code generally (the render path is designed to be zero-alloc in steady state).
- Logging goes through `Microsoft.Extensions.Logging`.
- Operations that can fail commonly return the shared `ResultCode` enum (`Ok`, `InvalidState`, `NotFound`, `WorldNotValid`, ...) rather than throwing.

Run formatting before committing:
```bash
# from repo root
pwsh Scripts/format-solution.ps1        # or: dotnet format
dotnet format --verify-no-changes       # what CI checks
```

## Build, test, run

Solution: `Source/HelixToolkit-Nex/HelixToolkit.Nex.slnx`. Configurations: `Debug`, `Release`, `LinuxDebug`, `LinuxRelease`. The Linux configs exclude the Windows-only WPF/WinUI host projects.

**Windows:**
```powershell
dotnet restore Source/HelixToolkit-Nex/HelixToolkit.Nex.slnx
dotnet build   Source/HelixToolkit-Nex/HelixToolkit.Nex.slnx --configuration Debug
dotnet test    Source/HelixToolkit-Nex/HelixToolkit.Nex.slnx --configuration Debug
```

**Linux** (use the helper — it selects the Linux configs automatically):
```bash
Scripts/build-linux.sh                 # LinuxDebug
Scripts/build-linux.sh -c Release      # LinuxRelease
Scripts/build-linux.sh --clean
```
> On Linux, build/run the interop sample as `LinuxDebug`, not `Debug`.

**Tests:** MSTest. Tests tagged `TestCategory=GPURequired` need a real GPU and are skipped in CI. Each library has a paired `*.Tests` project; add tests there. The `Graphics.Mock` backend enables GPU-free testing of higher layers.

**Prerequisites:** .NET 9 SDK, Vulkan SDK ≥ 1.3.296.0, a Vulkan 1.3 GPU/drivers to run samples, `pwsh`, Git, and **Git LFS**.

## Git LFS & assets

Binary assets (`.glb`, `.dds`, `.gif`, textures, etc.) are stored via **Git LFS** (patterns in [.gitattributes](.gitattributes)). If asset files appear as tiny text pointers, LFS wasn't installed before cloning — run `git lfs pull`. Set up once with `Scripts/setup-git-lfs.ps1`.

## Contribution workflow

- Branch from and PR into **`develop`** (the default branch).
- A pre-commit hook (`Scripts/setup-git-hooks.ps1`) runs `dotnet format --verify-no-changes` on staged C# and blocks unformatted commits; CI enforces the same. Don't bypass with `--no-verify` unless necessary.
- Keep PRs focused. Update the relevant package `README.md` when you change public API, and add/adjust tests.
- CI (`.github/workflows/ci.yml`) runs build, tests, and format verification.

## Practical tips for agents

- **Start from the package `README.md`** nearest the code you're touching — it lists current key types and recent renames (APIs churn; don't trust memory of an old shape).
- When editing anything GPU-facing, check whether a struct is **source-generated** from GLSL before editing the C#.
- Respect **package layering** when adding references; respect **row-major/column-major** and **reverse-Z** conventions when touching math or shaders.
- Watch for **per-frame allocations**; prefer `ZLinq`, ring buffers, and the zero-alloc stream/registry enumeration paths already in place.
- Windows-only code (D3D11 interop, WPF, WinUI) must stay guarded by `OperatingSystem.IsWindows()` and excluded from the Linux configs.
- Run `dotnet format` before finishing; CRLF + block-scoped namespaces + `_camelCase` fields are the easy things to get wrong.
