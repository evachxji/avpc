@echo off
setlocal
cd /d "%~dp0"
title AVPC Startup

where dotnet >nul 2>&1
if errorlevel 1 (
    echo [AVPC] .NET 8 SDK is required. Please install it first.
    pause
    exit /b 1
)

set "APP_EXE=%CD%\src\AVPC\bin\Debug\net8.0-windows10.0.19041.0\win-x64\AVPC.exe"
echo [1/2] Building AVPC...
dotnet build "src\AVPC\AVPC.csproj" --nologo
if errorlevel 1 (
    echo [AVPC] Build failed. See the errors above.
    pause
    exit /b 1
)

echo [2/2] Starting AVPC. This window will close when ready...
powershell.exe -NoProfile -Command "$p = Start-Process -FilePath '%APP_EXE%' -WorkingDirectory '%CD%' -PassThru; for ($i = 0; $i -lt 120; $i++) { if ($p.HasExited) { exit 1 }; if ($p.MainWindowHandle -ne 0) { exit 0 }; Start-Sleep -Milliseconds 250 }; exit 2"
if errorlevel 1 (
    echo [AVPC] Startup failed or timed out.
    pause
    exit /b 1
)

echo [AVPC] Ready.
exit /b 0
