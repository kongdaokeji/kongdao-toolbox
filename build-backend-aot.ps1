# 主动拦截后端 NativeAOT 发布辅助脚本。
# 用途：定位 MSVC vcvars64.bat（VS2022 各版本/目录），在其环境下以
#   IlcUseEnvironmentalTools=true 发布 NativeAOT 单文件 exe，
#   兼容 vswhere 元数据缺失（仅装 BuildTools）的机器与 CI。
# 用法：
#   powershell -NoProfile -ExecutionPolicy Bypass -File build-backend-aot.ps1 -Rid win-x64 -OutputDir <dir>
param(
    [Parameter(Mandatory = $true)]
    [string]$Rid,
    [Parameter(Mandatory = $true)]
    [string]$OutputDir,
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$backendProj = Join-Path $repoRoot "TubaWinUI3.BackEnd\TubaWinUi3.BackEnd.csproj"

# 从 RID 解析目标架构（win-x64 -> x64，win-x86 -> x86，win-arm64 -> arm64）。
$targetArch = if ($Rid -match '^win-(x64|x86|arm64)$') { $Matches[1] } else { 'x64' }

# 清除可能残留的 Platform 环境变量（外层构建/命令提示符泄漏的 Platform 会被 MSBuild
# 当作属性导入并覆盖平台目标，导致 -r win-arm64 与 x64 平台不匹配 NETSDK1032）。
Remove-Item Env:Platform -ErrorAction SilentlyContinue

# 定位 vcvars：优先 vcvarsall.bat（按目标架构选择本机/交叉工具链；arm64 必须用
# arm64 的 LIB/link.exe，vcvars64 的 x64 库无法链接 arm64 AOT 产物），
# 找不到则退回 vcvars64.bat（仅 x64）。覆盖 VS2022 各版本（Enterprise/Professional/Community/BuildTools）
# 与自定义目录。都找不到则直接发布（依赖机器上已配置的 link.exe/lib.exe 环境）。
$vcvarsBases = @()
foreach ($vsEdition in @("BuildTools", "Enterprise", "Professional", "Community")) {
    foreach ($base in @("${env:ProgramFiles(x86)}\Microsoft Visual Studio\2022",
                        "${env:ProgramFiles}\Microsoft Visual Studio\2022")) {
        if ($base -and (Test-Path $base)) {
            $vcvarsBases += (Join-Path $base (Join-Path $vsEdition "VC\Auxiliary\Build"))
        }
    }
}
$vcvarsAll = $vcvarsBases | ForEach-Object { Join-Path $_ "vcvarsall.bat" } | Where-Object { Test-Path $_ } | Select-Object -First 1
$vcvars64 = $vcvarsBases | ForEach-Object { Join-Path $_ "vcvars64.bat" } | Where-Object { Test-Path $_ } | Select-Object -First 1
# 也允许通过环境变量显式指定（指向 vcvarsall.bat 或 vcvars64.bat）
if ($env:VCVARS64_PATH) {
    if ([IO.Path]::GetFileName($env:VCVARS64_PATH) -eq "vcvarsall.bat") { $vcvarsAll = $env:VCVARS64_PATH }
    else { $vcvars64 = $env:VCVARS64_PATH }
}

# 显式固定 Platform/PlatformTarget 与 RID 一致，防止环境/外部属性覆盖。
# 注意用分号合并为单个 -p: 参数：某些 CI runner 的 cmd/参数解析会把相邻的
# 两个 -p:xxx 拼成一个（值变成 "x64 -p:PlatformTarget=x64"），分号形式不可能被拼接。
$baseSwitches = "-p:Platform=$targetArch;PlatformTarget=$targetArch"
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

if ($vcvarsAll) {
    Write-Host "使用 vcvarsall ($targetArch): $vcvarsAll"
    $cmd = "call `"$vcvarsAll`" $targetArch >nul 2>&1 && dotnet publish `"$backendProj`" -c $Configuration -r $Rid $baseSwitches -p:IlcUseEnvironmentalTools=true -o `"$OutputDir`""
    cmd /c $cmd
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失败 (exit $LASTEXITCODE)" }
}
elseif ($vcvars64) {
    Write-Host "使用 vcvars64: $vcvars64"
    $cmd = "call `"$vcvars64`" >nul 2>&1 && dotnet publish `"$backendProj`" -c $Configuration -r $Rid $baseSwitches -p:IlcUseEnvironmentalTools=true -o `"$OutputDir`""
    cmd /c $cmd
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失败 (exit $LASTEXITCODE)" }
}
else {
    Write-Host "未找到 vcvars，尝试直接发布（依赖现有环境）"
    & dotnet publish $backendProj -c $Configuration -r $Rid $baseSwitches -o $OutputDir
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失败 (exit $LASTEXITCODE)" }
}

$exe = Join-Path $OutputDir "TubaWinUI3.BackEnd.exe"
if (-not (Test-Path $exe)) { throw "后端产物不存在: $exe" }
Write-Host "后端 AOT 产物: $exe ($((Get-Item $exe).Length) bytes)"
