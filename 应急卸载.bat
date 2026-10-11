@echo off
rem Emergency uninstall for AutoDateShift (use when the exe is lost).
rem Stops the background service, removes the autostart task, and guides cleanup.
setlocal

echo [1/3] Stopping background service...
taskkill /f /im AutoDateShift.exe >nul 2>&1
schtasks /end /tn AutoDateShiftWorker >nul 2>&1

echo [2/3] Removing autostart task...
schtasks /delete /f /tn AutoDateShiftWorker

echo [3/3] Done. Manual follow-up:
echo.
echo   - If the system DATE looks wrong, open:
echo       C:\ProgramData\AutoDateShift\state.json
echo     Read "appliedOffsetSeconds" (in seconds).
echo     Real date = current system date MINUS that offset.
echo     Fix the date via Control Panel - Date and Time.
echo.
echo   - To fully clean up, delete the folder:
echo       C:\ProgramData\AutoDateShift
echo.
pause
