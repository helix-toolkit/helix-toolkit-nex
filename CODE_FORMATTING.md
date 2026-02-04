# Code Formatting Guide

This document describes the code formatting rules and automatic validation setup for the helix-toolkit-nex project.

## Overview

The project uses:
- **EditorConfig** (`.editorconfig`) - Defines coding style rules
- **Git pre-commit hooks** - Automatically validates formatting before commits
- **dotnet format** - CLI tool to check and fix formatting issues

## Naming Conventions

### Private Fields
Private fields **must** follow the `_camelCase` naming convention:

```csharp
// ✅ Correct
private int _myField;
private readonly string _myString;
private bool _isEnabled;

// ❌ Incorrect
private int myField;      // Missing underscore prefix
private int myField_;     // Underscore as suffix (old convention)
private int _MyField;     // Should be camelCase, not PascalCase
```

### Other Naming Rules
- **Interfaces**: Start with `I` (e.g., `IContext`, `IRenderer`)
- **Public/Internal fields**: PascalCase (e.g., `MaxValue`, `DefaultSize`)
- **Constants**: PascalCase (e.g., `DefaultTimeout`)
- **Static readonly fields**: PascalCase (e.g., `Empty`, `DefaultValue`)
- **Classes, Structs, Enums**: PascalCase
- **Methods, Properties, Events**: PascalCase

## Setting Up Git Pre-Commit Hooks

### Prerequisites
- Git installed
- .NET SDK installed (verify with `dotnet --version`)

### Installation Steps

#### Option 1: Automatic Setup (Recommended)

**On Windows (PowerShell):**
```powershell
# Run from repository root
.\setup-git-hooks.ps1
```

**On Linux/macOS (Bash):**
```bash
# Run from repository root
chmod +x setup-git-hooks.sh
./setup-git-hooks.sh
```

#### Option 2: Manual Setup

**On Windows:**
1. Open Command Prompt or PowerShell
2. Navigate to repository root
3. Run:
   ```cmd
   copy .git-hooks\pre-commit.bat .git\hooks\pre-commit
   ```

**On Linux/macOS:**
1. Open Terminal
2. Navigate to repository root
3. Run:
   ```bash
   cp .git-hooks/pre-commit .git/hooks/pre-commit
   chmod +x .git/hooks/pre-commit
   ```

### Verifying Installation

Try making a commit with improperly formatted code:
```bash
git add .
git commit -m "Test commit"
```

If the hook is working, you'll see:
```
Running code format validation...
Checking C# files for formatting issues...
```

## Using dotnet format

### Check for formatting issues
Check all files in the solution:
```bash
dotnet format --verify-no-changes
```

Check specific files:
```bash
dotnet format --verify-no-changes --include File1.cs File2.cs
```

### Fix formatting issues automatically
Fix all files:
```bash
dotnet format
```

Fix specific files:
```bash
dotnet format --include File1.cs File2.cs
```

### Format specific projects
```bash
dotnet format Source/HelixToolkit-Nex/HelixTookit.Nex/HelixToolkit.Nex.csproj
```

## IDE Integration

### Visual Studio 2022
1. The `.editorconfig` file is automatically detected
2. Format on save: Go to **Tools > Options > Text Editor > C# > Code Style > Formatting**
3. Enable "Format document on save"
4. Code analysis warnings will appear for naming violations

### Visual Studio Code
1. Install the **C# Dev Kit** extension
2. Install the **EditorConfig for VS Code** extension
3. Add to `settings.json`:
   ```json
   {
     "editor.formatOnSave": true,
     "omnisharp.enableEditorConfigSupport": true,
     "omnisharp.enableRoslynAnalyzers": true
   }
   ```

### JetBrains Rider
1. EditorConfig is automatically supported
2. Enable format on save: **Settings > Tools > Actions on Save**
3. Check "Reformat code"

## Bypassing Pre-Commit Hook (Not Recommended)

If you need to commit without formatting validation:
```bash
git commit --no-verify -m "Your commit message"
```

**Note:** This should only be used in exceptional circumstances.

## Troubleshooting

### Hook not running
- **Windows**: Ensure the file doesn't have `.bat` extension in `.git/hooks/`
- **Linux/macOS**: Verify execute permissions: `chmod +x .git/hooks/pre-commit`
- Check Git hooks are enabled: `git config core.hooksPath` (should be empty or `.git/hooks`)

### dotnet format not found
Install the latest .NET SDK from: https://dotnet.microsoft.com/download

Verify installation:
```bash
dotnet --version
```

### False positives
If the hook reports issues but you believe the code is correct:
1. Run `dotnet format` to see detailed issues
2. Check the `.editorconfig` rules
3. Verify your IDE is using the correct settings

### Performance issues
For large commits, you can temporarily disable the hook:
```bash
git commit --no-verify -m "Large refactoring"
```

Then run formatting separately:
```bash
dotnet format
git add .
git commit --amend --no-edit
```

## Continuous Integration

For CI/CD pipelines, add this step to validate formatting:

```yaml
# GitHub Actions example
- name: Check code formatting
  run: dotnet format --verify-no-changes

# Azure Pipelines example
- script: dotnet format --verify-no-changes
  displayName: 'Check code formatting'
```

## Additional Resources

- [EditorConfig Documentation](https://editorconfig.org/)
- [dotnet format Documentation](https://docs.microsoft.com/en-us/dotnet/core/tools/dotnet-format)
- [.NET Code Style Rules](https://docs.microsoft.com/en-us/dotnet/fundamentals/code-analysis/style-rules/)
- [C# Coding Conventions](https://docs.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions)

## Contributing

When contributing to this project:
1. Ensure your IDE respects the `.editorconfig` settings
2. Run `dotnet format` before committing
3. Let the pre-commit hook validate your changes
4. Follow the naming conventions outlined above

For questions or issues with formatting rules, please open an issue on GitHub.
