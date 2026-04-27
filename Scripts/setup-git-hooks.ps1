#!/usr/bin/env pwsh

# Script to set up Git hooks for the repository
# This script creates a pre-commit hook that runs code formatting checks

Write-Host "Setting up Git hooks..." -ForegroundColor Green

$gitHooksDir = ".git/hooks"
$preCommitHook = "$gitHooksDir/pre-commit"

# Check if .git directory exists
if (-not (Test-Path ".git")) {
    Write-Error "This script must be run from the repository root directory."
    exit 1
}

# Create the pre-commit hook
$hookContent = @'
#!/bin/sh
# Pre-commit hook: Check code formatting before commit

echo "Checking code formatting..."

# Run dotnet format in verify mode
dotnet format Source/HelixToolkit-Nex/HelixToolkit.Nex.slnx --verify-no-changes --verbosity quiet

if [ $? -ne 0 ]; then
    echo ""
    echo "Code formatting issues detected!"
    echo "Please run: .\Scripts\format-solution.ps1"
    echo "Then stage and commit your changes again."
    exit 1
fi

echo "Code formatting check passed!"
exit 0
'@

try {
    # Write the pre-commit hook using UTF-8 without BOM (BOM breaks git hooks on Windows)
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($preCommitHook, $hookContent, $utf8NoBom)
    
    # Make the hook executable (on Unix-like systems)
    if ($IsLinux -or $IsMacOS) {
        chmod +x $preCommitHook
    }
    
    Write-Host "Git hooks installed successfully!" -ForegroundColor Green
    Write-Host "Pre-commit hook will now check code formatting before each commit." -ForegroundColor Cyan
    Write-Host ""
    Write-Host "To bypass the hook (not recommended), use: git commit --no-verify" -ForegroundColor Yellow
}
catch {
    Write-Error "Failed to set up Git hooks: $_"
    exit 1
}
