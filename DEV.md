# Development Setup

This guide covers everything you need to build, run, and contribute to **HelixToolkit Nex** from a fresh clone.

For contribution workflow and code-style expectations, see [CONTRIBUTING.md](CONTRIBUTING.md).

## 1. Prerequisites

| Requirement | Notes |
| ----------- | ----- |
| **.NET 8 SDK** or later | https://dotnet.microsoft.com/download |
| **Vulkan SDK** 1.3.296.0 or later | https://vulkan.lunarg.com/ — required for the Vulkan backend |
| **Git** | https://git-scm.com/ |
| **Git LFS** | https://git-lfs.com/ — required for large binary assets (models, textures, GIFs) |
| **PowerShell** (`pwsh`) | Used by the setup/build scripts under [Scripts/](Scripts/) |
| **IDE** | Visual Studio 2022, VS Code, or Rider with .NET support |

### Platform support

- **Windows 10 or later** — full solution, including the WPF / WinUI host projects.
- **Linux** (tested on Ubuntu 26.04) — builds via the `LinuxDebug` / `LinuxRelease` configurations, which skip the Windows-only host projects (WPF / WinUI). The Avalonia control and the DirectX interop assembly are cross-platform and build everywhere; the Windows-specific D3D11 path is guarded at runtime via `OperatingSystem.IsWindows()`.
- A **Vulkan 1.3 compatible GPU and drivers** are required to run the samples.

## 2. Clone the repository

```powershell
git clone https://github.com/helix-toolkit/helix-toolkit-nex.git
cd helix-toolkit-nex
```

## 3. Set up Git LFS

Binary assets (`.glb`, `.dds`, `.gif`, etc.) are stored with [Git LFS](https://git-lfs.com/). Install the Git LFS binary first (see prerequisites), then run:

```powershell
.\Scripts\setup-git-lfs.ps1
```

This installs the LFS hooks for the repository and ensures the asset patterns declared in [.gitattributes](.gitattributes) are tracked. If you cloned **before** installing Git LFS, pull the actual asset contents with:

```powershell
git lfs pull
```

## 4. Set up Git hooks (recommended)

Install the pre-commit hook that verifies code formatting before each commit:

```powershell
.\Scripts\setup-git-hooks.ps1
```

The hook runs `dotnet format` in verify mode against staged C# files and blocks the commit if formatting issues are found. You can bypass it with `git commit --no-verify`, but this may cause CI failures.

## 5. Build

### Windows

```powershell
dotnet restore Source/HelixToolkit-Nex/HelixToolkit.Nex.slnx
dotnet build Source/HelixToolkit-Nex/HelixToolkit.Nex.slnx --configuration Debug
```

### Linux

Use the helper script, which selects the Linux-only configurations automatically:

```bash
Scripts/build-linux.sh                 # LinuxDebug build
Scripts/build-linux.sh -c Release      # LinuxRelease build
Scripts/build-linux.sh --clean         # clean before building
Scripts/build-linux.sh -- -v minimal   # pass extra args through to `dotnet build`
```

## 6. Run tests

```powershell
dotnet test Source/HelixToolkit-Nex/HelixToolkit.Nex.slnx --configuration Debug
```

> Tests tagged with `TestCategory=GPURequired` require GPU access and are skipped in CI environments.

## 7. Format code

The project uses [.editorconfig](.editorconfig) to enforce a consistent style, verified in CI. Format the whole solution before committing:

```powershell
.\Scripts\format-solution.ps1
```

## Scripts reference

| Script | Purpose |
| ------ | ------- |
| [Scripts/setup-git-lfs.ps1](Scripts/setup-git-lfs.ps1) | Install Git LFS hooks and track binary asset patterns |
| [Scripts/setup-git-hooks.ps1](Scripts/setup-git-hooks.ps1) | Install the pre-commit formatting hook |
| [Scripts/format-solution.ps1](Scripts/format-solution.ps1) | Run `dotnet format` across the solution |
| [Scripts/build-linux.sh](Scripts/build-linux.sh) | Build the solution on Linux |
| [Scripts/fix-naming.ps1](Scripts/fix-naming.ps1) | Apply naming-convention fixes |

## Troubleshooting

- **Asset files appear as small text pointers** — Git LFS was not installed before cloning. Install it, then run `git lfs pull`.
- **CI formatting failures** — run `.\Scripts\format-solution.ps1` and commit the changes.
- **Vulkan initialization errors** — verify your GPU/drivers support Vulkan 1.3 and that the Vulkan SDK is installed.
