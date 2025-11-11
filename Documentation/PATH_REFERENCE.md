# Documentation Directory Structure - Path Reference

## Directory Layout

```
helix-toolkit-nex/
??? .github/
?   ??? workflows/
?       ??? documentation.yml  # GitHub Actions workflow
??? Documentation/    # Documentation root
?   ??? docfx.json          # DocFX configuration
?   ??? build-docs.ps1      # Build script
?   ??? index.md          # Documentation homepage
?   ??? toc.yml    # Table of contents
?   ??? api/               # Generated API docs
?   ??? articles/             # Articles and guides
?   ??? images/       # Documentation images
?   ??? _site/       # Generated output
??? Source/
    ??? HelixToolkit-Nex/   # Source root
??? HelixToolkit-Nex.sln       # Solution file
        ??? HelixToolkit.Nex.Graphics/
        ??? HelixToolkit.Nex.Graphics.Vulkan/
        ??? HelixToolkit.Nex.Maths/
        ??? HelixTookit.Nex/
     ??? HelixToolkit.Nex.Scene/
        ??? HelixToolkit.Nex.Rendering/
        ??? HelixToolkit.Nex.ImGui/
```

## Path Corrections Made

### 1. docfx.json

**Before:**
```json
"files": [
  "../Source/HelixToolkit-Nex/HelixToolkit.Nex.Graphics/**.csproj",
  ...
]
```

**After:**
```json
"files": [
  "../HelixToolkit.Nex.Graphics/**.csproj",
  ...
]
```

**Reason:** The Documentation folder is at `helix-toolkit-nex/Documentation/`, so to reach the source projects at `helix-toolkit-nex/Source/HelixToolkit-Nex/HelixToolkit.Nex.Graphics/`, we only need `../HelixToolkit.Nex.Graphics/` (not the full path).

**WAIT - Let me reconsider this based on the actual structure!**

Looking at the workspace info, the actual structure is:
- Workspace: `E:\holan\Documents\GitHub\helix-toolkit-nex\Source\HelixToolkit-Nex\`
- Documentation: Should be at repository root level

Let me check the correct structure...

## Correct Path Analysis

Given:
- Repository root: `E:\holan\Documents\GitHub\helix-toolkit-nex\`
- Source location: `E:\holan\Documents\GitHub\helix-toolkit-nex\Source\HelixToolkit-Nex\`
- Documentation location: `E:\holan\Documents\GitHub\helix-toolkit-nex\Documentation\`

From `Documentation/docfx.json`:
- To reach `Source/HelixToolkit-Nex/HelixToolkit.Nex.Graphics/`
- Path: `../Source/HelixToolkit-Nex/HelixToolkit.Nex.Graphics/`

## Final Corrected Paths

### docfx.json (from Documentation/ folder)

```json
{
  "metadata": [
    {
    "src": [
        {
        "files": [
    "../Source/HelixToolkit-Nex/HelixToolkit.Nex.Graphics/**.csproj",
            "../Source/HelixToolkit-Nex/HelixToolkit.Nex.Graphics.Vulkan/**.csproj",
            "../Source/HelixToolkit-Nex/HelixToolkit.Nex.Maths/**.csproj",
         "../Source/HelixToolkit-Nex/HelixTookit.Nex/**.csproj",
            "../Source/HelixToolkit-Nex/HelixToolkit.Nex.Scene/**.csproj",
        "../Source/HelixToolkit-Nex/HelixToolkit.Nex.Rendering/**.csproj",
     "../Source/HelixToolkit-Nex/HelixToolkit.Nex.ImGui/**.csproj"
          ]
        }
      ]
    }
  ]
}
```

### build-docs.ps1 (from Documentation/ folder)

```powershell
$projects = @(
    "../Source/HelixToolkit-Nex/HelixToolkit.Nex.Graphics/HelixToolkit.Nex.Graphics.csproj",
    "../Source/HelixToolkit-Nex/HelixToolkit.Nex.Graphics.Vulkan/HelixToolkit.Nex.Graphics.Vulkan.csproj",
    "../Source/HelixToolkit-Nex/HelixToolkit.Nex.Maths/HelixToolkit.Nex.Maths.csproj",
    "../Source/HelixToolkit-Nex/HelixTookit.Nex/HelixToolkit.Nex.csproj",
    "../Source/HelixToolkit-Nex/HelixToolkit.Nex.Scene/HelixToolkit.Nex.Scene.csproj",
    "../Source/HelixToolkit-Nex/HelixToolkit.Nex.Rendering/HelixToolkit.Nex.Rendering.csproj",
    "../Source/HelixToolkit-Nex/HelixToolkit.Nex.ImGui/HelixToolkit.Nex.ImGui.csproj"
)
```

### GitHub Actions (from repository root)

```yaml
- name: Build Solution
  run: dotnet build Source/HelixToolkit-Nex/HelixToolkit-Nex.sln --configuration Release

- name: Build Documentation
run: |
    cd Documentation
    docfx docfx.json
```

## Usage from Documentation Folder

```powershell
# Navigate to Documentation folder
cd Documentation

# Run the build script
.\build-docs.ps1 -Serve
```

## Verification

To verify paths are correct:

```powershell
# From Documentation folder
Test-Path "../Source/HelixToolkit-Nex/HelixToolkit.Nex.Graphics/HelixToolkit.Nex.Graphics.csproj"
# Should return: True

Test-Path "../Source/HelixToolkit-Nex/HelixToolkit-Nex.sln"
# Should return: True
```
