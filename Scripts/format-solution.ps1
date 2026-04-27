#!/usr/bin/env pwsh

# Script to format the HelixToolkit.Nex solution
# This script runs dotnet format on the entire solution

$solutionPath = Join-Path -Path $PSScriptRoot -ChildPath "../Source/HelixToolkit-Nex/HelixToolkit.Nex.slnx"

Write-Host "Formatting solution: $solutionPath" -ForegroundColor Green

try {
    dotnet format $solutionPath
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "Solution formatted successfully!" -ForegroundColor Green
    } else {
        Write-Host "dotnet format completed with exit code: $LASTEXITCODE" -ForegroundColor Yellow
    }
} catch {
    Write-Error "Failed to format solution: $_"
    exit 1
}
