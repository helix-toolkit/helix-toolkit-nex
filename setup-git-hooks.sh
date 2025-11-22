#!/bin/bash
# Bash script to setup git pre-commit hooks for code formatting validation
# Run this from the repository root: ./setup-git-hooks.sh

set -e

echo "Setting up git pre-commit hooks..."

# Check if we're in a git repository
if [ ! -d ".git" ]; then
    echo "Error: Not in a git repository root directory."
    echo "Please run this script from the repository root."
    exit 1
fi

# Check if dotnet is installed
if ! command -v dotnet &> /dev/null; then
    echo "✗ .NET SDK not found."
    echo "Please install .NET SDK from: https://dotnet.microsoft.com/download"
    exit 1
fi

DOTNET_VERSION=$(dotnet --version)
echo "✓ .NET SDK found: $DOTNET_VERSION"

# Create .git/hooks directory if it doesn't exist
HOOKS_DIR=".git/hooks"
if [ ! -d "$HOOKS_DIR" ]; then
    mkdir -p "$HOOKS_DIR"
    echo "✓ Created hooks directory"
fi

# Copy the pre-commit hook
SOURCE_HOOK=".git-hooks/pre-commit"
TARGET_HOOK=".git/hooks/pre-commit"

if [ ! -f "$SOURCE_HOOK" ]; then
    echo "✗ Source hook not found: $SOURCE_HOOK"
    exit 1
fi

cp "$SOURCE_HOOK" "$TARGET_HOOK"
chmod +x "$TARGET_HOOK"
echo "✓ Installed pre-commit hook"

# Verify EditorConfig exists
if [ -f ".editorconfig" ]; then
    echo "✓ EditorConfig found"
else
    echo "⚠ Warning: .editorconfig not found"
fi

echo ""
echo "════════════════════════════════════════════════"
echo "Git pre-commit hook setup complete!"
echo "════════════════════════════════════════════════"
echo ""
echo "The hook will now automatically check code formatting before each commit."
echo ""
echo "To test the hook, try making a commit:"
echo "  git add ."
echo "  git commit -m 'Test commit'"
echo ""
echo "To manually format code, run:"
echo "  dotnet format"
echo ""
echo "For more information, see: CODE_FORMATTING.md"
echo ""
