@echo off
setlocal
cd /d "%~dp0"

where dotnet >nul 2>&1
if errorlevel 1 (
    echo [AVPC] .NET 8 SDK is required. Please install it first.
    pause
    exit /b 1
)

powershell.exe -NoProfile -WindowStyle Hidden -Command "Start-Process -FilePath 'dotnet' -ArgumentList 'run','--project','src\AVPC\AVPC.csproj' -WorkingDirectory '%CD%' -WindowStyle Hidden"
if errorlevel 1 (
    echo [AVPC] Startup failed.
    pause
    exit /b 1
)
exit /b 0
