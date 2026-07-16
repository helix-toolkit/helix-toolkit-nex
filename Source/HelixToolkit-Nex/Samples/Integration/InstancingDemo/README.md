# Instancing Integration Demo

## Purpose

This Integration sample demonstrates GPU instancing through the HelixToolkit.Nex engine-level
instancing API (`Instancing`, `MeshNode.Instancing`, and the `InstancingManager` exposed via
`ResourceManager.InstancingManager`).

The scene renders two visually distinct mesh types, both drawn with GPU instancing:

- A **static** instanced mesh whose per-instance transforms are computed once during scene
  construction and never change.
- A **dynamic** instanced mesh whose per-instance transforms are recomputed every frame and
  re-marked dirty, so the `InstancingManager` re-uploads its instance-transform GPU buffer during
  the engine's frame-begin step, producing a visible animation.

The scene is rendered to an offscreen target that is composited into an ImGui `Viewport`. The
viewport drives an `OrbitCameraController` from mouse input and supports GPU picking: clicking an
instance highlights it with a wireframe box drawn by the `BoundingBoxPostEffect`. Because picking
reports the per-instance id, the highlight is attached to the exact instance that was clicked
(via `BoundingBoxOverlay.InstanceIndex`), so it follows a moving dynamic instance.

The sample follows the established Integration sample structure used by `PBRTest` and
`DepthPrepassRenderer`: a dedicated project folder with its own `.csproj`, a `Program.cs` hosting
the SDL-driven application loop (and forwarding mouse/keyboard input into ImGui), a demo class that
owns the engine and render state, and this `README.md`.

## Interaction

- **Left click**: pick an instance; a green bounding box is drawn around the clicked instance.
- **Right drag**: orbit the camera.
- **Middle drag**: pan the camera.
- **Mouse wheel**: zoom.
- **Control panel** (left): shows instance counts, a pause/resume toggle for the animation, and a
  readout of the currently picked instance (mesh, instance index, world position).

## Build steps

From the repository root, build the sample as part of the Integration samples solution:

```sh
dotnet build Source/HelixToolkit-Nex/HelixToolkit.Nex.slnx
```

Or build just this project:

```sh
dotnet build Source/HelixToolkit-Nex/Samples/Integration/InstancingDemo/InstancingDemo.csproj
```

## Run steps

Run the sample executable:

```sh
dotnet run --project Source/HelixToolkit-Nex/Samples/Integration/InstancingDemo/InstancingDemo.csproj
```

A window opens showing both instanced mesh types inside the 3D viewport. The dynamic mesh animates
while the window is open. Use the mouse to orbit/pan/zoom and click instances to highlight them;
close the window to exit.
