@echo off
REM Build documentation for Helix Toolkit NEX

echo Building Helix Toolkit NEX Documentation
echo ========================================
echo.

REM Check if DocFX is installed
where docfx >nul 2>nul
if %ERRORLEVEL% NEQ 0 (
    echo DocFX not found. Installing...
    dotnet tool install -g docfx
    if %ERRORLEVEL% NEQ 0 (
        echo Failed to install DocFX
        exit /b 1
    )
)

REM Build solution
echo Building solution...
dotnet build --configuration Release
if %ERRORLEVEL% NEQ 0 (
    echo Build failed
    exit /b 1
)

REM Build documentation
echo Building documentation...
docfx docfx.json
if %ERRORLEVEL% NEQ 0 (
    echo Documentation build failed
    exit /b 1
)

echo.
echo Documentation built successfully!
echo Output location: _site
echo.
echo To view: docfx serve _site
echo Or open: _site\index.html

pause
