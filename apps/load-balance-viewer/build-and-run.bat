@echo off
setlocal
cd /d "%~dp0"

where dotnet >nul 2>&1
if errorlevel 1 (
  echo.
  echo .NET 8 SDK was not found.
  echo Install the .NET 8 SDK and run this file again.
  echo https://dotnet.microsoft.com/download/dotnet/8.0
  echo.
  pause
  exit /b 1
)

echo.
echo ==========================================
echo Therabby Load Balance Viewer v0.3
echo Build + Run
echo ==========================================
echo.

dotnet restore
if errorlevel 1 goto ERROR

dotnet build -c Release
if errorlevel 1 goto ERROR

start "" "bin\Release\net8.0-windows\Therabby.LoadBalanceViewer.exe"
exit /b 0

:ERROR
echo.
echo Build failed.
pause
exit /b 1
