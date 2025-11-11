# Helix Toolkit NEX - Complete Documentation System

## Overview

A complete, automated documentation generation system has been set up for the entire Helix Toolkit NEX solution using **DocFX**, the official Microsoft documentation generator for .NET projects.

## What Has Been Created

### 1. Core Documentation Files

| File | Purpose |
|------|---------|
| `docfx.json` | Main DocFX configuration file |
| `index.md` | Documentation homepage |
| `toc.yml` | Main table of contents |
| `api/index.md` | API reference landing page |
| `DOCUMENTATION.md` | Complete documentation guide |

### 2. Article Content

| File | Purpose |
|------|---------|
| `articles/getting-started.md` | Quick start guide |
| `articles/core-concepts.md` | Architecture and design concepts |
| `articles/toc.yml` | Articles table of contents |

### 3. Build Scripts

| File | Platform | Purpose |
|------|----------|---------|
| `build-docs.ps1` | PowerShell (All platforms) | Full-featured build script |
| `build-docs.bat` | Windows CMD | Simple Windows batch script |
| `serve-docs.ps1` | PowerShell (All platforms) | Quick serve script |

### 4. CI/CD Integration

| File | Purpose |
|------|---------|
| `.github/workflows/documentation.yml` | GitHub Actions workflow for auto-deployment |

### 5. Supporting Files

| File | Purpose |
|------|---------|
| `.gitignore.docs` | Ignore documentation build artifacts |

## Features

### ? Automatic XML Documentation Generation

The system automatically configures all library projects to generate XML documentation:

```xml
<GenerateDocumentationFile>true</GenerateDocumentationFile>
```

### ? Complete API Reference

All public APIs are automatically documented:
- Classes and interfaces
- Methods and properties
- Parameters and return values
- Generic type parameters
- Exceptions
- Remarks and examples

### ? Cross-References

- Automatic linking between types
- GitHub integration for source code links
- IntelliSense-friendly documentation

### ? Search Functionality

Built-in search across all documentation

### ? Responsive Design

Modern, mobile-friendly documentation theme

### ? Multiple Output Formats

- HTML (default)
- PDF (with additional configuration)
- Markdown

## Documented Projects

The following projects are included in the documentation:

1. **HelixToolkit.Nex.Graphics** - Core graphics abstraction
2. **HelixToolkit.Nex.Graphics.Vulkan** - Vulkan implementation
3. **HelixToolkit.Nex.Maths** - Mathematics library
4. **HelixToolkit.Nex** - Core utilities
5. **HelixToolkit.Nex.Scene** - Scene management
6. **HelixToolkit.Nex.Rendering** - Rendering components
7. **HelixToolkit.Nex.ImGui** - ImGui integration

Samples and test projects are **excluded** from documentation.

## Usage

### Quick Start

#### Option 1: PowerShell (Recommended)

```powershell
# Build and serve documentation
.\build-docs.ps1 -Serve

# Clean and rebuild
.\build-docs.ps1 -Clean -Serve

# Just build (no serve)
.\build-docs.ps1
```

#### Option 2: Windows Batch File

```cmd
build-docs.bat
```

#### Option 3: Manual

```bash
# Install DocFX (one-time)
dotnet tool install -g docfx

# Build solution
dotnet build --configuration Release

# Generate documentation
docfx docfx.json

# Serve locally
docfx serve _site
```

### Viewing Documentation

After building, documentation is available at:
- Local: `http://localhost:8080` (when serving)
- File: Open `_site/index.html` in browser
- GitHub Pages: Will be at `https://helix-toolkit.github.io/helix-toolkit-nex/` (once deployed)

## Automatic Deployment

### GitHub Pages Setup

The included GitHub Actions workflow automatically:

1. ? Builds documentation on push to `main`
2. ? Deploys to GitHub Pages
3. ? Updates on every commit

**To Enable:**

1. Go to: Repository Settings ? Pages
2. Set Source: **GitHub Actions**
3. Documentation will auto-deploy on next push

### Build Status Badge

Add to README.md:

```markdown
[![Documentation](https://github.com/helix-toolkit/helix-toolkit-nex/workflows/Build%20and%20Deploy%20Documentation/badge.svg)](https://github.com/helix-toolkit/helix-toolkit-nex/actions)
```

## Documentation Best Practices

### 1. XML Documentation Comments

Always document public APIs:

```csharp
/// <summary>
/// Creates a new buffer on the GPU.
/// </summary>
/// <param name="desc">Buffer description including size and usage flags.</param>
/// <returns>A handle to the created buffer.</returns>
/// <remarks>
/// The buffer must be explicitly destroyed by calling <see cref="DestroyBuffer"/>.
/// </remarks>
/// <exception cref="ArgumentException">
/// Thrown when <paramref name="desc"/> contains invalid values.
/// </exception>
public BufferHandle CreateBuffer(in BufferDesc desc);
```

### 2. Use Proper Tags

- `<summary>` - Brief description
- `<param>` - Parameter descriptions
- `<returns>` - Return value description
- `<remarks>` - Additional details
- `<example>` - Code examples
- `<exception>` - Exceptions thrown
- `<see cref=""/>` - Cross-references
- `<inheritdoc/>` - Inherit from interface/base class

### 3. Interface Inheritance

For implementations, use `<inheritdoc/>`:

```csharp
/// <inheritdoc/>
public void Draw(uint vertexCount, uint instanceCount)
{
    // Implementation
}
```

This pulls documentation from the interface automatically.

## Project Structure

```
E:\holan\Documents\GitHub\helix-toolkit-nex\Source\HelixToolkit-Nex\
??? docfx.json              # DocFX configuration
??? index.md       # Homepage
??? toc.yml  # Main TOC
??? DOCUMENTATION.md        # This guide
??? build-docs.ps1          # PowerShell build script
??? build-docs.bat # Windows batch script
??? serve-docs.ps1          # Quick serve script
??? .gitignore.docs     # Documentation artifacts ignore
?
??? api/            # API Reference
?   ??? index.md       # API homepage (generated by DocFX)
?
??? articles/    # Tutorials & Guides
?   ??? toc.yml
?   ??? getting-started.md
?   ??? core-concepts.md
?
??? images/        # Documentation images (create as needed)
?
??? _site/           # Generated output (gitignored)
?   ??? ...         # HTML documentation
?
??? .github/
    ??? workflows/
        ??? documentation.yml  # CI/CD pipeline
```

## Customization

### Adding New Articles

1. Create markdown file in `articles/`
2. Add to `articles/toc.yml`
3. Rebuild documentation

### Changing Appearance

Edit `docfx.json`:

```json
"template": [
  "default",    // Base template
  "modern" // Modern theme
]
```

Available themes:
- `default` - Classic DocFX theme
- `modern` - Modern responsive theme
- `statictoc` - Static table of contents
- Custom themes (add to `templates/` directory)

### Customizing Metadata

Edit `docfx.json` ? `globalMetadata`:

```json
"globalMetadata": {
  "_appTitle": "Your Title",
  "_appName": "Your Name",
  "_appFooter": "© 2024 Your Company",
  "_enableSearch": true
}
```

## Troubleshooting

### Common Issues

| Issue | Solution |
|-------|----------|
| "DocFX not found" | Run: `dotnet tool install -g docfx` |
| Build errors | Ensure solution builds: `dotnet build` |
| Missing documentation | Add XML comments to public APIs |
| Broken links | Check cross-references use correct syntax |
| Serve fails | Port 8080 in use, specify different port: `docfx serve _site -p 8081` |

### Getting Help

1. Check `DOCUMENTATION.md` for detailed instructions
2. Review [DocFX Documentation](https://dotnet.github.io/docfx/)
3. Check build logs in `obj/` directory
4. Enable verbose mode: `docfx docfx.json --log verbose`

## Maintenance

### Regular Tasks

- [ ] Review and update articles quarterly
- [ ] Ensure all new public APIs are documented
- [ ] Update getting-started guide with new features
- [ ] Add code examples to complex APIs
- [ ] Keep core-concepts.md current with architecture changes

### Quality Checks

Run before committing:

```powershell
# Build with warnings treated as errors
docfx docfx.json --warningsAsErrors
```

## Benefits

### For Developers

? IntelliSense support in IDEs
? Clear API contracts and usage
? Reduced time to understand codebase
? Better code discoverability

### For Users

? Professional documentation portal
? Searchable API reference
? Tutorials and guides
? Always up-to-date with code

### For Project

? Increased adoption
? Reduced support burden
? Professional appearance
? Better code quality (forces thinking about API design)

## Next Steps

1. **Review Generated Documentation**
   ```powershell
   .\build-docs.ps1 -Serve
   ```

2. **Enable GitHub Pages**
   - Go to repository settings
   - Enable GitHub Pages with GitHub Actions

3. **Improve XML Comments**
   - Add documentation to undocumented APIs
   - Add code examples
   - Improve descriptions

4. **Expand Articles**
   - Add more tutorials
   - Create architecture diagrams
   - Document common patterns

5. **Setup Automated Checks**
   - Add documentation coverage checks to CI
   - Enforce documentation on new public APIs

## Resources

- **DocFX**: https://dotnet.github.io/docfx/
- **XML Documentation**: https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/xmldoc/
- **Markdown Guide**: https://www.markdownguide.org/
- **GitHub Pages**: https://docs.github.com/en/pages

## License

Documentation follows the same license as the Helix Toolkit NEX project.

---

**Created**: 2024
**Last Updated**: 2024
**Maintained By**: Helix Toolkit Contributors
