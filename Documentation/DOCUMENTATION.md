# Documentation Generation for Helix Toolkit NEX

This directory contains the configuration and tools for generating API documentation for the Helix Toolkit NEX solution.

## Quick Start

### Prerequisites

- .NET 8.0 SDK
- PowerShell (Windows, Linux, or macOS)

### Building Documentation

#### Windows/Linux/macOS

```powershell
# Build documentation
.\build-docs.ps1

# Build and serve locally
.\build-docs.ps1 -Serve

# Clean and rebuild
.\build-docs.ps1 -Clean -Serve

# Force rebuild (ignores cache)
.\build-docs.ps1 -Force
```

#### Serving Pre-built Documentation

```powershell
.\serve-docs.ps1
```

Then open your browser to `http://localhost:8080`

## Manual Setup

If you prefer to set up manually:

1. Install DocFX:
```bash
dotnet tool install -g docfx
```

2. Build the solution:
```bash
dotnet build --configuration Release
```

3. Generate documentation:
```bash
docfx docfx.json
```

4. Serve locally:
```bash
docfx serve _site
```

## Project Structure

```
.
??? docfx.json     # DocFX configuration
??? index.md    # Documentation homepage
??? toc.yml             # Table of contents
??? api/      # API documentation (generated)
???? index.md         # API reference homepage
??? articles/         # Tutorials and guides
?   ??? toc.yml
?   ??? getting-started.md
?   ??? core-concepts.md
??? images/                 # Documentation images
??? build-docs.ps1     # Build script
??? serve-docs.ps1          # Serve script
??? _site/      # Generated output (gitignored)
```

## Configuration

### docfx.json

The main configuration file controls:

- Which projects to document
- Output location
- Theme and styling
- Global metadata (title, footer, etc.)

### Included Projects

The following projects are documented:

- HelixToolkit.Nex.Graphics
- HelixToolkit.Nex.Graphics.Vulkan
- HelixToolkit.Nex.Maths
- HelixToolkit.Nex (core utilities)
- HelixToolkit.Nex.Scene
- HelixToolkit.Nex.Rendering
- HelixToolkit.Nex.ImGui

Samples and test projects are excluded.

## XML Documentation

The build script automatically configures all projects to generate XML documentation files by adding:

```xml
<PropertyGroup>
  <GenerateDocumentationFile>true</GenerateDocumentationFile>
</PropertyGroup>
```

## Customization

### Adding Custom Articles

1. Create a markdown file in `articles/`
2. Add it to `articles/toc.yml`:

```yaml
- name: My Article
  href: my-article.md
```

### Changing Theme

Edit `docfx.json` and modify the `template` section:

```json
"template": [
  "default",
  "modern"  // or "statictoc", "darkfx", etc.
]
```

### Adding Images

Place images in the `images/` directory and reference them:

```markdown
![Alt text](../images/my-image.png)
```

## Continuous Integration

The repository includes a GitHub Actions workflow (`.github/workflows/documentation.yml`) that:

1. Builds the documentation on every push to main
2. Deploys to GitHub Pages automatically

### Enabling GitHub Pages

1. Go to repository Settings ? Pages
2. Set Source to "GitHub Actions"
3. The documentation will be available at: `https://helix-toolkit.github.io/helix-toolkit-nex/`

## Troubleshooting

### "DocFX not found"

Install it globally:
```bash
dotnet tool install -g docfx
```

Or update it:
```bash
dotnet tool update -g docfx
```

### Build Errors

1. Make sure the solution builds successfully:
```bash
dotnet build --configuration Release
```

2. Check that XML documentation files are being generated in `bin/Release/net8.0/`

3. Clean and rebuild:
```powershell
.\build-docs.ps1 -Clean
```

### Missing API Documentation

Ensure all public members have XML documentation comments:

```csharp
/// <summary>
/// Description of the method.
/// </summary>
/// <param name="parameter">Parameter description</param>
/// <returns>Return value description</returns>
public void MyMethod(string parameter) { }
```

## Additional Resources

- [DocFX Documentation](https://dotnet.github.io/docfx/)
- [DocFX Templates](https://dotnet.github.io/docfx/docs/template.html)
- [Markdown Syntax](https://dotnet.github.io/docfx/docs/markdown.html)

## License

The documentation follows the same license as the main Helix Toolkit NEX project.
