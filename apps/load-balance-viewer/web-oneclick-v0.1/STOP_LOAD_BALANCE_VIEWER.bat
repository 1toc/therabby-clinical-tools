@echo off
setlocal
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "$f='%~dp0_app\.server.pid'; if(Test-Path -LiteralPath $f){$p=Get-Content -LiteralPath $f -ErrorAction SilentlyContinue; if($p){Stop-Process -Id ([int]$p) -Force -ErrorAction SilentlyContinue}; Remove-Item -LiteralPath $f -Force -ErrorAction SilentlyContinue}"
exit /b 0
