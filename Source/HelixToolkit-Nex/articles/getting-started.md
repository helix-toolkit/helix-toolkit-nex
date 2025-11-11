# Getting Started with Helix Toolkit NEX

This guide will help you get started with Helix Toolkit NEX.

## Prerequisites

- .NET 8.0 SDK or later
- Vulkan 1.2 compatible GPU and drivers
- Visual Studio 2022 or JetBrains Rider (recommended)

## Installation

### NuGet Packages (When Available)

```bash
dotnet add package HelixToolkit.Nex.Graphics
dotnet add package HelixToolkit.Nex.Graphics.Vulkan
dotnet add package HelixToolkit.Nex.Maths
```

### Building from Source

1. Clone the repository:
```bash
git clone https://github.com/helix-toolkit/helix-toolkit-nex.git
cd helix-toolkit-nex
```

2. Build the solution:
```bash
dotnet build Source/HelixToolkit-Nex/HelixToolkit-Nex.sln
```

## Basic Usage

### Creating a Graphics Context

```csharp
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Graphics.Vulkan;

// Create Vulkan context
var builder = new VulkanBuilder();
var context = builder
    .WithDebug(true)
  .WithValidation(true)
    .Build();

// Initialize the context
context.Initialize();
```

### Creating a Simple Triangle

See the HelloTriangle sample for a complete example of rendering a triangle.

## Next Steps

- Explore the [API Reference](../api/index.md)
- Check out the Samples directory for complete examples
- Read about [Core Concepts](core-concepts.md)

## Additional Resources

- [GitHub Repository](https://github.com/helix-toolkit/helix-toolkit-nex)
- [Issue Tracker](https://github.com/helix-toolkit/helix-toolkit-nex/issues)
