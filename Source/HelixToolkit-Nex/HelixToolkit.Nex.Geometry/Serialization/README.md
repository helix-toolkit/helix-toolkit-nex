# JSON Serialization for Geometry

This directory contains JSON converters for serializing and deserializing `Geometry` objects using `System.Text.Json`.

## Overview

The following types support JSON serialization:

- `Vertex` - Vertex data structure with position, normal, texture coordinates, and color
- `BiNormal` - Bitangent and tangent vectors for advanced lighting
- `Geometry` - Complete geometry data including vertices, indices, and bi-normals

## Usage

### Basic Serialization

```csharp
using System.Text.Json;
using HelixToolkit.Nex.Geometries;

// Create a geometry
var geometry = new Geometry(
    new[] {
        new Vertex(new Vector3(0, 0, 0), new Vector3(0, 1, 0), new Vector2(0, 0)),
        new Vertex(new Vector3(1, 0, 0), new Vector3(0, 1, 0), new Vector2(1, 0)),
        new Vertex(new Vector3(0, 1, 0), new Vector3(0, 1, 0), new Vector2(0, 1))
    },
    new uint[] { 0, 1, 2 },
    topology: Topology.Triangle
);

// Serialize to JSON
string json = JsonSerializer.Serialize(geometry, new JsonSerializerOptions 
{ 
    WriteIndented = true 
});

Console.WriteLine(json);

// Deserialize from JSON
var deserialized = JsonSerializer.Deserialize<Geometry>(json);
```

### Serialization Options

You can customize the JSON output using `JsonSerializerOptions`:

```csharp
var options = new JsonSerializerOptions
{
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
};

string json = JsonSerializer.Serialize(geometry, options);
```

### JSON Format

The JSON format for a `Geometry` object looks like this:

```json
{
  "Id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "Topology": "Triangle",
  "Vertices": [
    {
      "Position": { "X": 0.0, "Y": 0.0, "Z": 0.0 },
      "Normal": { "X": 0.0, "Y": 1.0, "Z": 0.0 },
      "TexCoord": { "X": 0.0, "Y": 0.0 },
      "Color": { "X": 1.0, "Y": 1.0, "Z": 1.0, "W": 1.0 }
    },
    ...
  ],
  "Indices": [0, 1, 2],
  "BiNormals": [],
  "IsDynamic": false
}
```

## Custom Converters

The serialization is handled by the following custom converters:

- `VertexJsonConverter` - Handles `Vertex` struct serialization
- `BiNormalJsonConverter` - Handles `BiNormal` struct serialization
- `GeometryJsonConverter` - Handles `Geometry` class serialization

These converters are automatically applied via the `[JsonConverter]` attribute on the types.

## Important Notes

1. **GPU Buffers Not Serialized**: The `VertexBuffer`, `IndexBuffer`, and `BiNormalBuffer` properties are not serialized as they represent GPU resources that cannot be meaningfully serialized.

2. **Topology Preservation**: The geometry topology (Point, Line, Triangle, etc.) is preserved during serialization.

3. **BiNormals Are Optional**: BiNormal data is only serialized if it exists (count > 0).

4. **ID Preservation**: The geometry's `Guid` ID is preserved during serialization, allowing geometry identification across serialization boundaries.

## File I/O Examples

### Save to File

```csharp
var geometry = new Geometry(/* ... */);
string json = JsonSerializer.Serialize(geometry, new JsonSerializerOptions { WriteIndented = true });
await File.WriteAllTextAsync("geometry.json", json);
```

### Load from File

```csharp
string json = await File.ReadAllTextAsync("geometry.json");
var geometry = JsonSerializer.Deserialize<Geometry>(json);
```

## Performance Considerations

- Use `WriteIndented = false` for smaller file sizes in production
- Consider using `JsonSerializerOptions` caching to avoid recreating options on every call
