@echo off
setlocal
cd /d "%~dp0"

where dotnet >nul 2>&1
if errorlevel 1 (
    echo [AVPC] .NET 8 SDK is required. Please install it first.
    pause
    exit /b 1
)

dotnet run --project "src\AVPC\AVPC.csproj"
if errorlevel 1 (
    echo.
    echo [AVPC] Startup failed. See the errors above.
    pause
    exit /b 1
)
