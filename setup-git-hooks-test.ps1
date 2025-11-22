# PowerShell script to setup git pre-commit hooks for code formatting validation
# Run this from the repository root: .\setup-git-hooks.ps1

Write-Host "Setting up git pre-commit hooks..." -ForegroundColor Cyan

# Check if we're in a git repository
if (-not (Test-Path ".git")) {
    Write-Host "Error: Not in a git repository root directory." -ForegroundColor Red
    Write-Host "Please run this script from the repository root." -ForegroundColor Yellow
    exit 1
}

# Check if dotnet is installed
try {
    $dotnetVersion = dotnet --version
    Write-Host "âœ“ .NET SDK found: $dotnetVersion" -ForegroundColor Green
}
catch {
    Write-Host "âœ— .NET SDK not found." -ForegroundColor Red
    Write-Host "Please install .NET SDK from: https://dotnet.microsoft.com/download" -ForegroundColor Yellow
    exit 1
}

# Create .git/hooks directory if it doesn't exist
$hooksDir = ".git\hooks"
if (-not (Test-Path $hooksDir)) {
    New-Item -ItemType Directory -Path $hooksDir | Out-Null
    Write-Host "âœ“ Created hooks directory" -ForegroundColor Green
}

# Copy the pre-commit hook
$sourceHook = ".git-hooks\pre-commit.bat"
$targetHook = ".git\hooks\pre-commit"

if (-not (Test-Path $sourceHook)) {
    Write-Host "âœ— Source hook not found: $sourceHook" -ForegroundColor Red
    exit 1
}

try {
    Copy-Item -Path $sourceHook -Destination $targetHook -Force
    Write-Host "âœ“ Installed pre-commit hook" -ForegroundColor Green
}
catch {
    Write-Host "âœ— Failed to copy hook: $_" -ForegroundColor Red
    exit 1
}

# Verify EditorConfig exists
if (Test-Path ".editorconfig") {
    Write-Host "âœ“ EditorConfig found" -ForegroundColor Green
}
else {
    Write-Host "âš  Warning: .editorconfig not found" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•" -ForegroundColor Cyan
Write-Host "Git pre-commit hook setup complete!" -ForegroundColor Green
Write-Host "â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•" -ForegroundColor Cyan
Write-Host ""
Write-Host "The hook will now automatically check code formatting before each commit." -ForegroundColor White
Write-Host ""
Write-Host "To test the hook, try making a commit:" -ForegroundColor White
Write-Host "  git add ." -ForegroundColor Gray
Write-Host "  git commit -m 'Test commit'" -ForegroundColor Gray
Write-Host ""
Write-Host "To manually format code, run:" -ForegroundColor White
Write-Host "  dotnet format" -ForegroundColor Gray
Write-Host ""
Write-Host "For more information, see: CODE_FORMATTING.md" -ForegroundColor White
Write-Host ""

