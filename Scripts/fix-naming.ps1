# Script to help identify and fix naming violations
$solution = "Source/HelixToolkit-Nex/HelixToolkit.Nex.sln"

# Get all naming violations with file paths
$violations = dotnet format $solution --verify-no-changes --verbosity diagnostic 2>&1 | 
Select-String "warning IDE1006" | 
ForEach-Object {
    if ($_ -match "([^:]+):(\d+):(\d+): warning IDE1006: (.+)") {
        [PSCustomObject]@{
            File    = $matches[1]
            Line    = $matches[2]
            Column  = $matches[3]
            Message = $matches[4]
        }
    }
}

# Group by file to see which files need the most work
$byFile = $violations | Group-Object File | Sort-Object Count -Descending

Write-Host "Top 20 files with most violations:"
$byFile | Select-Object -First 20 | ForEach-Object {
    Write-Host "$($_.Count) - $($_.Name)"
}

Write-Host "`nTotal violations: $($violations.Count)"
