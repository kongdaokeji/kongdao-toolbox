@echo off
rem 图吧工具箱 FPS 诊断工具启动器（自动申请管理员权限）
rem 用法：先打开游戏并让它处于前台，然后双击本文件，等 12 秒输出完
net session >nul 2>&1
if %errorlevel% neq 0 (
    powershell -Command "Start-Process cmd -ArgumentList '/c title DxTraceDiag && cd /d ""%~dp0"" && dotnet run --project DxTraceDiag && pause' -Verb RunAs"
    exit /b
)
cd /d %~dp0
dotnet run --project DxTraceDiag
pause