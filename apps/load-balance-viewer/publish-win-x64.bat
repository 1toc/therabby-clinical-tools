@echo off
setlocal
cd /d "%~dp0"

where dotnet >nul 2>&1
if errorlevel 1 (
  echo .NET 8 SDK was not found.
  pause
  exit /b 1
)

dotnet publish -c Release -r win-x64 --self-contained true ^
  /p:PublishSingleFile=true ^
  /p:IncludeNativeLibrariesForSelfExtract=true ^
  -o publish

if errorlevel 1 (
  echo Publish failed.
  pause
  exit /b 1
)

echo.
echo Published:
echo %CD%\publish\Therabby.LoadBalanceViewer.exe
echo.
pause
