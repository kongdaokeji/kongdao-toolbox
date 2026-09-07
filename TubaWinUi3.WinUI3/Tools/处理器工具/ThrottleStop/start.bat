@echo off
chcp 65001 >nul
setlocal EnableExtensions
cd /d "%~dp0"

set "EXE=ThrottleStop.exe"

if exist "%EXE%" (
    echo 已找到 ThrottleStop，正在启动...
    start "" "%EXE%"
    exit /b 0
)

echo ThrottleStop 尚未下载，正在从社区工具仓库下载，请稍候...
echo.

set "ZIP=%TEMP%\ThrottleStop_9.7.3.zip"
if exist "%ZIP%" del /f /q "%ZIP%" >nul 2>&1

echo [1/2] 正在下载（GitCode 镜像）...
curl.exe -sS -L --connect-timeout 30 --max-time 300 -o "%ZIP%" "https://gitcode.com/luolangaga/tubatoolsPlugin/-/raw/main/plugins/%%E5%%A4%%84%%E7%%90%%86%%E5%%99%%A8%%E5%%B7%%A5%%E5%%85%%B7/throttlestop/ThrottleStop_9.7.3.zip"

if not exist "%ZIP%" goto :github
for %%F in ("%ZIP%") do if %%~zF LSS 1000000 goto :github

echo 正在解压...
call :extract
if exist "%EXE%" goto :done

:github
echo.
echo GitCode 下载失败，尝试 GitHub 源...
if exist "%ZIP%" del /f /q "%ZIP%" >nul 2>&1
curl.exe -sS -L --connect-timeout 30 --max-time 300 -o "%ZIP%" "https://raw.githubusercontent.com/luolangaga/tubatoolsPlugin/main/plugins/%%E5%%A4%%84%%E7%%90%%86%%E5%%99%%A8%%E5%%B7%%A5%%E5%%85%%B7/throttlestop/ThrottleStop_9.7.3.zip"
if not exist "%ZIP%" goto :fail
for %%F in ("%ZIP%") do if %%~zF LSS 1000000 goto :fail
echo 正在解压...
call :extract
if exist "%EXE%" goto :done

:fail
echo.
echo 下载失败，请检查网络连接后重试。
if exist "%ZIP%" del /f /q "%ZIP%" >nul 2>&1
echo 5 秒后自动关闭...
timeout /t 5 /nobreak >nul 2>&1
exit /b 1

:done
echo.
echo 下载并解压完成！正在启动...
start "" "%EXE%"
if exist "%ZIP%" del /f /q "%ZIP%" >nul 2>&1
exit /b 0

:extract
tar.exe -xf "%ZIP%" -C "%~dp0" >nul 2>&1
if exist "%EXE%" exit /b 0
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "Expand-Archive -LiteralPath '%ZIP%' -DestinationPath '%~dp0' -Force" >nul 2>&1
exit /b 0
