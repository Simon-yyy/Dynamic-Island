@echo off
chcp 65001 >nul
setlocal enableextensions
cd /d "%~dp0"
echo 正在关闭可能正在运行的旧版「灵动岛」进程...
taskkill /F /IM 灵动岛.exe >nul 2>nul
echo 正在编译最新「灵动岛」源码 (2026.11.17.batt-multi13) ...
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1"
if errorlevel 1 (
    echo.
    echo [错误] 编译未成功，请查看上方提示。
    pause
    exit /b 1
)
echo.
echo 编译完成，正在启动最新 灵动岛.exe ...
start "" "%~dp0灵动岛.exe"
timeout /t 2 >nul
