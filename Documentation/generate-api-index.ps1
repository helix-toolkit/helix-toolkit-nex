#!/usr/bin/env pwsh
# Auto-generates api/index.md and index.md from all non-test, non-sample projects.

$projectsRoot = "$PSScriptRoot\..\Source\HelixToolkit-Nex"

$assemblies = Get-ChildItem -Path $projectsRoot -Recurse -Filter "*.csproj" |
    Where-Object { $_.FullName -notmatch '\\Samples\\' -and $_.BaseName -notlike '*.Tests' } |
    Select-Object -ExpandProperty BaseName |
    Sort-Object

# Generate api/index.md
$apiLines = @(
    "# API Documentation",
    "",
    "Complete API reference for Helix Toolkit NEX.",
    "Browse namespaces and types using the tree on the left.",
    "",
    "## Assemblies",
    ""
)
foreach ($name in $assemblies) {
    $apiLines += "- **$name**"
}
Set-Content -Path "$PSScriptRoot\api\index.md" -Value ($apiLines -join "`n") -Encoding UTF8
Write-Host "Generated api/index.md ($($assemblies.Count) assemblies)" -ForegroundColor Green

# Generate index.md (site homepage)
$homeLines = @(
    "# Helix Toolkit NEX Documentation",
    "",
    "Welcome to Helix Toolkit NEX — a modern, high-performance 3D graphics toolkit for .NET 8.",
    "",
    "## Libraries",
    ""
)
foreach ($name in $assemblies) {
    $homeLines += "- **$name**"
}
$homeLines += @(
    "",
    "## Getting Started",
    "",
    "Browse the [API Documentation](api/index.md) to explore available classes and methods.",
    "",
    "## Requirements",
    "",
    "- .NET 8.0 or later",
    "- Vulkan 1.2 or later compatible GPU"
)
Set-Content -Path "$PSScriptRoot\index.md" -Value ($homeLines -join "`n") -Encoding UTF8
Write-Host "Generated index.md ($($assemblies.Count) assemblies)" -ForegroundColor Green
