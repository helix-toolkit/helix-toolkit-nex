```markdown
# HelixToolkit.Nex.glTF

The `HelixToolkit.Nex.glTF` package is a component of the HelixToolkit-Nex 3D graphics engine, designed to import glTF 2.0 files into the engine's scene graph. It provides functionality to parse, convert, and integrate glTF assets, including meshes, materials, textures, and nodes, into the HelixToolkit-Nex rendering pipeline.

## Overview

The `HelixToolkit.Nex.glTF` package is responsible for:
- Importing glTF 2.0 files and converting them into the HelixToolkit-Nex scene graph.
- Handling the conversion of glTF materials to the engine's PBR material properties.
- Managing GPU resources such as textures, samplers, and geometries created during the import process.
- Providing diagnostics information about the import process, including warnings and errors.

This package fits into the HelixToolkit-Nex engine by enabling the integration of glTF assets, which are widely used in 3D graphics applications, into the engine's ECS-based architecture and rendering pipeline.

## Key Types

| Type | Description |
|------|-------------|
| `Importer` | Main class for importing glTF files into the HelixToolkit-Nex scene graph. |
| `ImportResult` | Contains the root node of the imported scene, diagnostics, and resource manifest. Implements `IDisposable` for cleanup. |
| `ImportDiagnostic` | Represents a diagnostic entry with severity, message, and reference to the glTF element. |
| `DiagnosticSeverity` | Enum indicating the severity level of an import diagnostic (Warning, Error). |
| `ImporterConfig` | Configuration options for the glTF importer, including default shading mode. |

## Usage Examples

### Importing a glTF File

```csharp
using HelixToolkit.Nex.glTF;
using HelixToolkit.Nex.Engine;

// Initialize the importer
var importer = new Importer();

// Provide the world data provider
var worldData = new WorldDataProvider(/* ECS world and resource managers */);

// Import a glTF file
string filePath = "path/to/model.gltf";
ImportResult result = importer.Import(filePath, worldData);

// Check for success
if (result.Success)
{
    // Access the root node of the imported scene
    var rootNode = result.RootNode;

    // Process diagnostics
    foreach (var diagnostic in result.Diagnostics)
    {
        Console.WriteLine($"{diagnostic.Severity}: {diagnostic.Message}");
    }
}

// Dispose resources when done
result.Dispose();
```

### Asynchronous Import

```csharp
using System.Threading;
using System.Threading.Tasks;
using HelixToolkit.Nex.glTF;

// Initialize the importer
var importer = new Importer();

// Provide the world data provider
var worldData = new WorldDataProvider(/* ECS world and resource managers */);

// Import a glTF file asynchronously
string filePath = "path/to/model.gltf";
CancellationToken cancellationToken = new CancellationToken();
Task<ImportResult> importTask = importer.ImportAsync(filePath, worldData, null, cancellationToken);

// Await the result
ImportResult asyncResult = await importTask;

// Check for success
if (asyncResult.Success)
{
    // Access the root node of the imported scene
    var rootNode = asyncResult.RootNode;

    // Process diagnostics
    foreach (var diagnostic in asyncResult.Diagnostics)
    {
        Console.WriteLine($"{diagnostic.Severity}: {diagnostic.Message}");
    }
}

// Dispose resources when done
asyncResult.Dispose();
```

## Architecture Notes

- **Design Patterns**: The package uses the Factory pattern for creating materials and textures, and the Builder pattern for constructing the scene graph.
- **Dependencies**: Relies on the `HelixToolkit.Nex.Scene` for node and mesh management, `HelixToolkit.Nex.Material` for material properties, and `HelixToolkit.Nex.Repository` for managing GPU resources.
- **Resource Management**: Utilizes `ResourceManifest` to track and dispose of GPU resources created during the import process.
- **ECS Integration**: The package integrates with the HelixToolkit-Nex ECS architecture, allowing imported assets to be managed within the engine's entity-component system.
```
