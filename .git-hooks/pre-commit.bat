@echo off
REM Git pre-commit hook to validate C# code formatting (Windows version)
REM This hook runs dotnet format to check for formatting issues before allowing a commit

echo Running code format validation...

REM Check if dotnet is installed
where dotnet >nul 2>nul
if %ERRORLEVEL% NEQ 0 (
    echo Error: dotnet CLI not found. Please install .NET SDK.
    exit /b 1
)

REM Get list of staged .cs files
set STAGED_CS_FILES=
for /f "delims=" %%i in ('git diff --cached --name-only --diff-filter=ACM') do (
    echo %%i | findstr /i "\.cs$" >nul
    if not errorlevel 1 (
        set STAGED_CS_FILES=!STAGED_CS_FILES! %%i
    )
)

if "%STAGED_CS_FILES%"=="" (
    echo No C# files to check.
    exit /b 0
)

echo Checking C# files for formatting issues...

REM Run dotnet format with verify-no-changes flag
dotnet format --verify-no-changes --include %STAGED_CS_FILES%

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo X Code formatting issues detected!
    echo.
    echo Please run 'dotnet format' to fix formatting issues, then stage and commit again.
    echo.
    echo Alternatively, you can run:
    echo   dotnet format --include %STAGED_CS_FILES%
    echo.
    echo To bypass this check (not recommended), use: git commit --no-verify
    exit /b 1
)

echo. Code formatting validation passed!
exit /b 0
