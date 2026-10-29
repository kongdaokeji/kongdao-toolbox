@echo off
rem ====================================================================
rem  空岛图吧工具箱 - MSIX 一键安装 + 签名证书信任
rem
rem  做的三件事：
rem    1. 从 .msix / .msixbundle 的签名里导出签名证书
rem    2. 把证书导入「本地计算机 - 受信任人」，自签名证书同时导入
rem       「受信任的根证书颁发机构」，让系统不再报「证书不受信任」
rem    3. 调用 Add-AppxPackage 安装该程序包
rem
rem  用法：
rem    install-msix.cmd                          自动找同目录的 msix / msixbundle
rem    install-msix.cmd "D:\下载\xxx.msix"        指定程序包
rem    install-msix.cmd "signer.cer"             只信任这个证书，不安装
rem    install-msix.cmd --trust-only "xxx.msix"  只信任该包的证书，不安装
rem
rem  写「本地计算机」证书存储需要管理员权限，脚本会自己弹 UAC 提权。
rem ====================================================================
setlocal EnableExtensions

set "TUBA_SELF=%~f0"
set "TUBA_ROOT=%~dp0"
set "TUBA_CER=%TEMP%\TubaWinUi3_MSIX_Signer.cer"
set "TUBA_STATUS=%TEMP%\TubaWinUi3_MSIX_Status.txt"
set "TUBA_TARGET="
set "TUBA_TRUST_ONLY=0"
set "TUBA_IS_CER=0"
set "TUBA_TEMP_CER=0"
set "TUBA_SELF_SIGNED=0"
set "TUBA_ALREADY_TRUSTED=0"
set "TUBA_PAUSE=0"

rem 提权后是新进程，参数靠环境变量带过去（UAC 新进程继承环境）
if not "%~1"=="" set "TUBA_ARG1=%~1"
if not "%~2"=="" set "TUBA_ARG2=%~2"

rem 双击运行时（cmd /c）结束后暂停，方便看结果
echo %CMDCMDLINE% | find /i "/c" >nul 2>&1
if not errorlevel 1 set "TUBA_PAUSE=1"

if /i "%TUBA_ARG1%"=="--help" goto :usage
if /i "%TUBA_ARG1%"=="-h" goto :usage
if /i "%TUBA_ARG1%"=="/?" goto :usage

if /i "%TUBA_ARG1%"=="--trust-only" set "TUBA_TRUST_ONLY=1"
if /i "%TUBA_ARG1%"=="-t" set "TUBA_TRUST_ONLY=1"
if "%TUBA_TRUST_ONLY%"=="1" (set "TUBA_TARGET=%TUBA_ARG2%") else (set "TUBA_TARGET=%TUBA_ARG1%")

rem 没给参数就自己找：只找程序包，不自动挑 .cer，避免误信任无关证书
if not defined TUBA_TARGET (
    for /f "delims=" %%F in ('dir /b /a-d /o-d "%TUBA_ROOT%*.msixbundle" 2^>nul') do if not defined TUBA_TARGET set "TUBA_TARGET=%TUBA_ROOT%%%F"
)
if not defined TUBA_TARGET (
    for /f "delims=" %%F in ('dir /b /a-d /o-d "%TUBA_ROOT%*.msix" 2^>nul') do if not defined TUBA_TARGET set "TUBA_TARGET=%TUBA_ROOT%%%F"
)
if not defined TUBA_TARGET (
    for /f "delims=" %%F in ('dir /b /a-d /o-d "%TUBA_ROOT%StoreOutput\*.msixbundle" 2^>nul') do if not defined TUBA_TARGET set "TUBA_TARGET=%TUBA_ROOT%StoreOutput\%%F"
)
if not defined TUBA_TARGET (
    for /f "delims=" %%F in ('dir /b /a-d /o-d "%TUBA_ROOT%StoreOutput\*.msix" 2^>nul') do if not defined TUBA_TARGET set "TUBA_TARGET=%TUBA_ROOT%StoreOutput\%%F"
)
if not defined TUBA_TARGET goto :nopackage

for %%F in ("%TUBA_TARGET%") do set "TUBA_TARGET=%%~fF"
for %%F in ("%TUBA_TARGET%") do set "TUBA_EXT=%%~xF"
if not exist "%TUBA_TARGET%" goto :notfound
if /i "%TUBA_EXT%"==".cer" set "TUBA_IS_CER=1"
if /i "%TUBA_EXT%"==".crt" set "TUBA_IS_CER=1"
if /i "%TUBA_EXT%"==".p7b" set "TUBA_IS_CER=1"
if "%TUBA_IS_CER%"=="1" set "TUBA_TRUST_ONLY=1"

rem ---------------------------------------------------------------- 提权
net session >nul 2>&1
if errorlevel 1 (
    echo.
    echo   需要管理员权限才能写「本地计算机」证书存储，正在申请提权...
    echo.
    powershell -NoProfile -Command "try { if ($env:TUBA_ARG1) { Start-Process -FilePath $env:ComSpec -ArgumentList @('/c', ('\"' + $env:TUBA_SELF + '\"'), $env:TUBA_ARG1) -Verb RunAs -ErrorAction Stop } else { Start-Process -FilePath $env:ComSpec -ArgumentList @('/c', ('\"' + $env:TUBA_SELF + '\"')) -Verb RunAs -ErrorAction Stop } } catch { exit 5 }"
    if errorlevel 1 (
        echo   提权被取消，或者当前账号不是管理员。
        echo   也可以右键本文件，选择「以管理员身份运行」。
        if "%TUBA_PAUSE%"=="1" pause
    )
    exit /b 1
)

rem ------------------------------------------------------------ 开始干活
echo.
echo ====================================================================
echo   空岛图吧工具箱 —— MSIX 安装与证书信任
echo ====================================================================
echo.
echo   目标: %TUBA_TARGET%
echo.

if "%TUBA_IS_CER%"=="1" goto :have_cert

echo   [1/3] 正在从程序包签名里提取证书...
set "TUBA_PKG=%TUBA_TARGET%"
powershell -NoProfile -ExecutionPolicy Bypass -Command "$ErrorActionPreference='Stop'; try { $s = Get-AuthenticodeSignature -LiteralPath $env:TUBA_PKG; if (-not $s -or -not $s.SignerCertificate) { Write-Host '        这个程序包没有数字签名，无法安装。'; exit 2 }; Export-Certificate -Cert $s.SignerCertificate -FilePath $env:TUBA_CER -Force | Out-Null; $c = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2 $env:TUBA_CER; Write-Host ('        签名者: ' + $c.Subject); Write-Host ('        指纹:   ' + $c.Thumbprint); $self = [int]($c.Subject -eq $c.Issuer); $ok = [int]($s.Status -eq 'Valid'); [System.IO.File]::WriteAllText($env:TUBA_STATUS, ('SELFSIGNED=' + $self + [Environment]::NewLine + 'TRUSTED=' + $ok), (New-Object System.Text.UTF8Encoding($false))); exit 0 } catch { Write-Host ('        提取证书失败: ' + $_.Exception.Message); exit 3 }"
if errorlevel 1 goto :fail
set "TUBA_TEMP_CER=1"
goto :trust

:have_cert
echo   [1/3] 使用指定的证书文件
set "TUBA_CER=%TUBA_TARGET%"

:trust
if not exist "%TUBA_CER%" goto :notfound
for /f "usebackq tokens=1,* delims==" %%A in ("%TUBA_STATUS%") do if /i "%%A"=="SELFSIGNED" set "TUBA_SELF_SIGNED=%%B"
for /f "usebackq tokens=1,* delims==" %%A in ("%TUBA_STATUS%") do if /i "%%A"=="TRUSTED" set "TUBA_ALREADY_TRUSTED=%%B"

if "%TUBA_ALREADY_TRUSTED%"=="1" (
    echo   [2/3] 系统已经信任这个签名，跳过证书导入
    goto :instal
)

echo   [2/3] 导入「本地计算机 - 受信任人」证书存储...
certutil -addstore -f "TrustedPeople" "%TUBA_CER%" >nul 2>&1
if errorlevel 1 (
    echo        导入失败，certutil 的输出如下：
    certutil -addstore -f "TrustedPeople" "%TUBA_CER%"
    goto :fail
)
echo        完成

if "%TUBA_SELF_SIGNED%"=="1" (
    echo   [3/3] 自签名证书：同时导入「受信任的根证书颁发机构」...
    certutil -addstore -f "Root" "%TUBA_CER%" >nul 2>&1
    if errorlevel 1 (
        echo        导入失败，certutil 的输出如下：
        certutil -addstore -f "Root" "%TUBA_CER%"
        goto :fail
    )
    echo        完成
) else (
    echo   [3/3] 证书由权威 CA 签发，不需要写入根存储
)

:instal
if "%TUBA_TRUST_ONLY%"=="1" goto :done

echo   [ ] 正在安装程序包，第一次安装会比较慢，请耐心等待...
powershell -NoProfile -ExecutionPolicy Bypass -Command "try { Add-AppxPackage -Path $env:TUBA_PKG -ForceApplicationShutdown -ErrorAction Stop; exit 0 } catch { Write-Host ('        安装失败: ' + $_.Exception.Message); $code = ('{0:X8}' -f $_.Exception.HResult); if ($_.Exception.Message -match '(0x[0-9A-Fa-f]{8})') { $code = $matches[1].Substring(2).ToUpper() }; [System.IO.File]::WriteAllText($env:TUBA_STATUS, ('HRESULT=' + $code), (New-Object System.Text.UTF8Encoding($false))); exit 1 }"
if errorlevel 1 goto :install_failed
echo        安装成功

:done
if "%TUBA_TEMP_CER%"=="1" del /f /q "%TUBA_CER%" >nul 2>&1
del /f /q "%TUBA_STATUS%" >nul 2>&1
echo.
echo ====================================================================
if "%TUBA_TRUST_ONLY%"=="1" (
    echo   完成：证书已信任。以后双击 msix 就能直接安装。
) else (
    echo   完成：证书已信任，程序包已安装。
    echo   在开始菜单里搜索「空岛图吧工具箱」即可启动。
)
echo ====================================================================
echo.
if "%TUBA_PAUSE%"=="1" pause
exit /b 0

rem ------------------------------------------------------------ 出错处理
:install_failed
set "TUBA_HR="
for /f "usebackq tokens=1,* delims==" %%A in ("%TUBA_STATUS%") do if /i "%%A"=="HRESULT" set "TUBA_HR=%%B"
echo.
if "%TUBA_HR%"=="80073CFB" (
    echo   系统里已经装了同一个版本，需要先卸载再安装：
    echo     powershell -Command "Get-AppxPackage DA3D64F4.winui3 ^| Remove-AppxPackage"
    echo   注意：卸载会一并删掉应用保存的数据。
    goto :fail
)
if "%TUBA_HR%"=="80073CF3" goto :dep_hint
if "%TUBA_HR%"=="80073CF9" goto :dep_hint
if "%TUBA_HR%"=="80073CFD" goto :dep_hint
goto :fail

:dep_hint
echo   看起来是缺少依赖的框架包。这个应用依赖 Microsoft WebView2 框架包，
echo   从没装过它的系统上，请先安装 WebView2 Runtime 的 MSIX 版本再运行本脚本。
goto :fail

:fail
if "%TUBA_TEMP_CER%"=="1" del /f /q "%TUBA_CER%" >nul 2>&1
del /f /q "%TUBA_STATUS%" >nul 2>&1
echo.
echo   没能完成，请把上面的输出截图反馈。
echo.
if "%TUBA_PAUSE%"=="1" pause
exit /b 1

:notfound
echo.
echo   找不到文件: %TUBA_TARGET%
echo.
if "%TUBA_PAUSE%"=="1" pause
exit /b 1

:nopackage
echo.
echo   脚本目录里没有找到 .msix / .msixbundle。
echo   用法: install-msix.cmd "D:\下载\空岛图吧工具箱.msix"
echo.
if "%TUBA_PAUSE%"=="1" pause
exit /b 1

:usage
echo.
echo   用法:
echo     install-msix.cmd                          自动查找同目录的 msix / msixbundle
echo     install-msix.cmd "D:\下载\xxx.msix"        指定程序包，信任其签名证书并安装
echo     install-msix.cmd "signer.cer"             只信任这个证书，不安装
echo     install-msix.cmd --trust-only "xxx.msix"  只信任该包的证书，不安装
echo.
if "%TUBA_PAUSE%"=="1" pause
exit /b 0
