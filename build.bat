@echo off
setlocal
cd /d "%~dp0"
set LOG=build.log
echo Build started %date% %time% > %LOG%

set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
    echo [ERROR] csc.exe not found. Please install .NET Framework 4.x
    echo [ERROR] csc.exe not found >> %LOG%
    pause
    exit /b 1
)

echo Using compiler: %CSC%
echo Using compiler: %CSC% >> %LOG%

rem Kill running instances first, otherwise the exe is locked and compile fails (CS0016)
taskkill /f /im AutoDateShift.exe >nul 2>&1

"%CSC%" /nologo /target:winexe /platform:anycpu /out:AutoDateShift.exe /win32icon:app.ico /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Runtime.Serialization.dll /r:System.ServiceModel.Web.dll src\Models.cs src\JsonStore.cs src\CsvLogger.cs src\Lang.cs src\Ui.cs src\TimeShifter.cs src\NtpClient.cs src\IconFactory.cs src\Worker.cs src\SelfTest.cs src\Autostart.cs src\MainForm.cs src\Program.cs >> %LOG% 2>&1

if errorlevel 1 (
    echo.
    echo [FAIL] Compile failed. Error details:
    echo ----------------------------------------
    type %LOG%
    echo ----------------------------------------
    echo Please send build.log back for fixing.
    pause
    exit /b 1
)

echo [OK] AutoDateShift.exe created successfully.
echo [OK] Build succeeded. >> %LOG%
pause
