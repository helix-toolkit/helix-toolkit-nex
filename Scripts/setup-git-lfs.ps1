#!/usr/bin/env pwsh

# Script to set up Git LFS for the repository
# This installs the Git LFS hooks and ensures the large binary asset
# patterns (already declared in .gitattributes) are tracked.

Write-Host "Setting up Git LFS..." -ForegroundColor Green

# Check if .git directory exists (must run from repository root)
if (-not (Test-Path ".git")) {
    Write-Error "This script must be run from the repository root directory."
    exit 1
}

# Verify the git-lfs binary is available
$gitLfs = Get-Command git-lfs -ErrorAction SilentlyContinue
if (-not $gitLfs) {
    Write-Error @"
Git LFS is not installed or not on PATH.
Install it first, then re-run this script:
  Windows : winget install GitHub.GitLFS   (or: choco install git-lfs)
  macOS   : brew install git-lfs
  Linux   : sudo apt-get install git-lfs   (or your distro's package manager)
Download : https://git-lfs.com
"@
    exit 1
}

# Patterns to track. These mirror the entries already in .gitattributes so the
# script is idempotent and keeps the two in sync.
$lfsPatterns = @(
    "Assets/**/*.glb",
    "Assets/**/*.bin",
    "Assets/**/*.dds",
    "Assets/**/*.raw",
    "Assets/**/*.dat",
    "Assets/**/*.tif",
    "Assets/**/*.tiff",
    "Assets/**/*.gif",
    "Source/HelixToolkit-Nex/Samples/**/*.gif"
)

try {
    # Install the LFS pre-push/clean/smudge hooks for this repository
    git lfs install --local
    if ($LASTEXITCODE -ne 0) { throw "git lfs install failed." }

    # Ensure each pattern is tracked (git lfs track is idempotent)
    foreach ($pattern in $lfsPatterns) {
        git lfs track $pattern | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Failed to track pattern: $pattern" }
    }

    Write-Host "Git LFS installed and patterns tracked successfully!" -ForegroundColor Green
    Write-Host ""
    Write-Host "Currently tracked patterns:" -ForegroundColor Cyan
    git lfs track

    Write-Host ""
    Write-Host "If .gitattributes changed, stage and commit it:" -ForegroundColor Yellow
    Write-Host "  git add .gitattributes" -ForegroundColor Yellow
    Write-Host "  git commit -m 'Configure Git LFS'" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "To migrate files already committed as regular blobs, run:" -ForegroundColor Yellow
    Write-Host "  git lfs migrate import --include='Assets/**/*.glb,Assets/**/*.bin'" -ForegroundColor Yellow
}
catch {
    Write-Error "Failed to set up Git LFS: $_"
    exit 1
}
