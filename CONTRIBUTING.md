# Contributing to HelixToolkit Nex

Thank you for your interest in contributing to HelixToolkit Nex! This document provides guidelines and instructions for contributing to the project.

## Development Setup

### Prerequisites

- .NET 8 SDK
- Vulkan SDK 1.3.296.0 or later
- Git
- Visual Studio 2022 or your preferred IDE with .NET support

### Setting Up Git Hooks (Recommended)

To automatically check code formatting before each commit, set up the pre-commit hook:

```powershell
.\Scripts\setup-git-hooks.ps1
```

This will install a pre-commit hook that:
- Runs code formatting verification before each commit
- Prevents commits with formatting issues
- Provides instructions on how to fix formatting problems

Once installed, the hook will automatically run every time you commit. If formatting issues are detected, the commit will be blocked until you run `.\Scripts\format-solution.ps1` to fix them.

**Note:** You can bypass the hook with `git commit --no-verify`, but this is not recommended as it may cause CI failures.

## Code Formatting

This project uses `.editorconfig` to enforce consistent code style across the codebase. Code formatting is automatically checked in the CI pipeline.

### Format Code Locally

Before committing changes, run the formatting script to ensure your code adheres to the style guidelines:

```powershell
.\Scripts\format-solution.ps1
```

This will automatically format all code files in the solution according to the rules defined in `.editorconfig`.

### CI Code Style Check

The CI workflow automatically verifies code formatting on all pull requests and pushes. If your code doesn't match the formatting rules, the build will fail with details about which files need formatting.

To avoid CI failures:
1. Set up git hooks using `.\Scripts\setup-git-hooks.ps1` (recommended)
2. Run `.\Scripts\format-solution.ps1` before committing
3. Ensure your IDE is configured to respect `.editorconfig` settings
4. Review the formatting changes before committing

### IDE Configuration

Most modern IDEs (Visual Studio, VS Code, Rider) automatically recognize and apply `.editorconfig` settings. Make sure this feature is enabled in your IDE.

## Building the Project

To build the solution:

```powershell
dotnet restore Source/HelixToolkit-Nex/HelixToolkit.Nex.sln
dotnet build Source/HelixToolkit-Nex/HelixToolkit.Nex.sln --configuration Debug
```

## Running Tests

To run the test suite:

```powershell
dotnet test Source/HelixToolkit-Nex/HelixToolkit.Nex.sln --configuration Debug
```

Note: Tests tagged with `TestCategory=GPURequired` require GPU access and are skipped in CI environments.

## Submitting Changes

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/your-feature-name`)
3. Set up git hooks: `.\Scripts\setup-git-hooks.ps1` (first time only)
4. Make your changes
5. Commit your changes (formatting will be checked automatically if hooks are set up)
6. Build and test your changes locally
7. Push to your fork
8. Submit a pull request to the `develop` branch

## Pull Request Guidelines

- Provide a clear description of the changes and their purpose
- Reference any related issues
- Ensure all CI checks pass (build, tests, code formatting)
- Keep pull requests focused on a single feature or fix
- Update documentation as needed

## Code Style Guidelines

- Follow the `.editorconfig` rules (enforced automatically)
- Write clear, self-documenting code with meaningful names
- Add XML documentation comments for public APIs
- Keep methods focused and reasonably sized
- Write unit tests for new functionality

## Questions or Issues?

If you have questions or run into issues:
- Check existing [GitHub Issues](https://github.com/helix-toolkit/helix-toolkit-nex/issues)
- Open a new issue with a detailed description
- Join discussions in pull requests and issues

We appreciate your contributions!
