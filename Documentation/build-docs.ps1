#!/usr/bin/env pwsh
# Script to build documentation for Helix Toolkit NEX

param(
    [switch]$Serve,
    [switch]$Clean,
    [switch]$Force
)

$ErrorActionPreference = "Stop"

Write-Host "Helix Toolkit NEX Documentation Builder" -ForegroundColor Cyan
Write-Host "=======================================" -ForegroundColor Cyan
Write-Host ""

# Check if DocFX is installed
$docfxInstalled = $null -ne (Get-Command docfx -ErrorAction SilentlyContinue)

if (-not $docfxInstalled) {
    Write-Host "DocFX not found. Installing DocFX..." -ForegroundColor Yellow
    dotnet tool install -g docfx
    
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to install DocFX. Please install it manually: dotnet tool install -g docfx"
        exit 1
    }
    
    Write-Host "DocFX installed successfully!" -ForegroundColor Green
}

# Clean previous build if requested
if ($Clean) {
    Write-Host "Cleaning previous documentation build..." -ForegroundColor Yellow
    
    if (Test-Path "_site") {
        Remove-Item "_site" -Recurse -Force
        Write-Host "Cleaned _site directory" -ForegroundColor Green
  }
    
    if (Test-Path "api") {
   Remove-Item "api" -Recurse -Force -Exclude "index.md"
        Write-Host "Cleaned api directory" -ForegroundColor Green
    }
    
    if (Test-Path "obj") {
      Remove-Item "obj" -Recurse -Force
    Write-Host "Cleaned obj directory" -ForegroundColor Green
    }
}

# Ensure XML documentation is generated for all projects
Write-Host "Configuring projects for XML documentation generation..." -ForegroundColor Yellow

$projects = @(
  "..\Source\HelixToolkit-Nex\HelixToolkit.Nex.Graphics\HelixToolkit.Nex.Graphics.csproj",
    "..\Source\HelixToolkit-Nex\HelixToolkit.Nex.Graphics.Vulkan\HelixToolkit.Nex.Graphics.Vulkan.csproj",
    "..\Source\HelixToolkit-Nex\HelixToolkit.Nex.Maths\HelixToolkit.Nex.Maths.csproj",
    "..\Source\HelixToolkit-Nex\HelixTookit.Nex\HelixToolkit.Nex.csproj",
    "..\Source\HelixToolkit-Nex\HelixToolkit.Nex.Scene\HelixToolkit.Nex.Scene.csproj",
    "..\Source\HelixToolkit-Nex\HelixToolkit.Nex.Rendering\HelixToolkit.Nex.Rendering.csproj",
    "..\Source\HelixToolkit-Nex\HelixToolkit.Nex.ImGui\HelixToolkit.Nex.ImGui.csproj"
)

foreach ($project in $projects) {
    if (Test-Path $project) {
        Write-Host "  Processing $project" -ForegroundColor Gray

    [xml]$projectXml = Get-Content $project
      $propertyGroup = $projectXml.Project.PropertyGroup | Where-Object { $null -eq $_.Condition } | Select-Object -First 1
 
      if ($null -eq $propertyGroup) {
       $propertyGroup = $projectXml.CreateElement("PropertyGroup")
  $projectXml.Project.AppendChild($propertyGroup) | Out-Null
        }
      
        # Check if GenerateDocumentationFile already exists
        $docFileNode = $propertyGroup.SelectSingleNode("GenerateDocumentationFile")
        if ($null -eq $docFileNode) {
 $docFileNode = $projectXml.CreateElement("GenerateDocumentationFile")
         $docFileNode.InnerText = "true"
      $propertyGroup.AppendChild($docFileNode) | Out-Null
            $projectXml.Save((Resolve-Path $project))
   Write-Host "    Added GenerateDocumentationFile=true" -ForegroundColor Green
        }
        elseif ($docFileNode.InnerText -ne "true") {
  $docFileNode.InnerText = "true"
        $projectXml.Save((Resolve-Path $project))
         Write-Host "    Updated GenerateDocumentationFile=true" -ForegroundColor Green
        }
    }
}

Write-Host ""

# Build the solution first to generate XML documentation
Write-Host "Building solution to generate XML documentation..." -ForegroundColor Yellow

# Find the solution file
$solutionFile = "..\Source\HelixToolkit-Nex\HelixToolkit-Nex.sln"

if (Test-Path $solutionFile) {
    Write-Host "Building solution: $solutionFile" -ForegroundColor Gray
    dotnet build $solutionFile --configuration Release
}
else {
    Write-Host "Solution file not found at $solutionFile, building projects individually..." -ForegroundColor Yellow
 foreach ($project in $projects) {
        if (Test-Path $project) {
            dotnet build $project --configuration Release
     }
    }
}

if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed. Please fix compilation errors first."
    exit 1
}

Write-Host "Build completed successfully!" -ForegroundColor Green
Write-Host ""

# Build documentation with DocFX
Write-Host "Building documentation with DocFX..." -ForegroundColor Yellow

$docfxArgs = @()
if ($Force) {
  $docfxArgs += "--force"
}

docfx docfx.json @docfxArgs

if ($LASTEXITCODE -ne 0) {
    Write-Error "DocFX build failed. Check the errors above."
    exit 1
}

Write-Host ""
Write-Host "Documentation built successfully!" -ForegroundColor Green
Write-Host "Output location: _site" -ForegroundColor Cyan

# Serve the documentation if requested
if ($Serve) {
    Write-Host ""
    Write-Host "Starting documentation server..." -ForegroundColor Yellow
    Write-Host "Press Ctrl+C to stop the server" -ForegroundColor Gray
    Write-Host ""
    
    docfx serve _site
}
else {
    Write-Host ""
    Write-Host "To view the documentation:" -ForegroundColor Cyan
    Write-Host "  1. Run: .\build-docs.ps1 -Serve" -ForegroundColor White
    Write-Host "  2. Or open: _site\index.html in your browser" -ForegroundColor White
}

Write-Host ""
Write-Host "Done!" -ForegroundColor Green
