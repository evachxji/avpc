@echo off
setlocal
title AVPC Stop

powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "$processes = @(Get-Process -Name 'AVPC' -ErrorAction SilentlyContinue); if ($processes.Count -eq 0) { Write-Host '[AVPC] AVPC is not running.'; exit 0 }; $live = @($processes | Where-Object { $_.Threads.Count -gt 0 }); if ($live.Count -eq 0) { Write-Host '[AVPC] AVPC already crashed; only a residual process record remains.'; Write-Host '[AVPC] Sign out or restart Windows to clear it.'; exit 3 }; foreach ($process in $live) { try { Stop-Process -Id $process.Id -Force -ErrorAction Stop; Write-Host ('[AVPC] Stopped PID ' + $process.Id + '.') } catch { Write-Host ('[AVPC] Failed to stop PID ' + $process.Id + ': ' + $_.Exception.Message); exit 1 } }"
if errorlevel 3 (
    pause
    exit /b 3
)
if errorlevel 1 (
    echo [AVPC] Failed to stop AVPC.
    pause
    exit /b 1
)

exit /b 0
