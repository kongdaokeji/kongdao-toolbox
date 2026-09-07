param(
    [string]$ToolsRoot = '',
    [string]$OutputDir = ''
)

$ErrorActionPreference = 'Stop'

if (-not $ToolsRoot) { $ToolsRoot = Join-Path $PSScriptRoot 'TubaWinUi3.WinUI3\Tools' }
if (-not $OutputDir) { $OutputDir = Join-Path $PSScriptRoot 'TubaWinUi3.WinUI3\IconCache' }

if (-not (Test-Path -LiteralPath $ToolsRoot)) {
    Write-Host "ERROR: ToolsRoot not found: $ToolsRoot" -ForegroundColor Red
    exit 1
}

Add-Type -AssemblyName System.Drawing

$toolsRoot = (Resolve-Path -LiteralPath $ToolsRoot).Path
if (-not (Test-Path -LiteralPath $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}
$outputDir = (Resolve-Path -LiteralPath $OutputDir).Path

$sha256 = [System.Security.Cryptography.SHA256]::Create()

function Get-CacheKey {
    param([string]$Path)

    $relative = $Path.Substring($toolsRoot.Length).TrimStart('\').TrimStart('/')
    $keyInput = '{ToolsRoot}\' + $relative
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($keyInput)
    $hash = $sha256.ComputeHash($bytes)
    return ([System.BitConverter]::ToString($hash) -replace '-', '').Substring(0, 16).ToLowerInvariant()
}

$files = Get-ChildItem -LiteralPath $toolsRoot -Recurse -File -ErrorAction SilentlyContinue |
    Where-Object { $_.Extension -in '.exe', '.lnk' }
Write-Host "Found $($files.Count) exe/lnk files in Tools" -ForegroundColor Cyan

$validKeys = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
$extracted = 0
$skipped = 0
$failed = 0

foreach ($file in $files) {
    $key = Get-CacheKey $file.FullName
    $null = $validKeys.Add($key)
    $outPath = Join-Path $outputDir "$key.png"

    if (Test-Path -LiteralPath $outPath) {
        if ((Get-Item -LiteralPath $outPath).LastWriteTimeUtc -ge $file.LastWriteTimeUtc) {
            $skipped++
            continue
        }
    }

    try {
        $icon = [System.Drawing.Icon]::ExtractAssociatedIcon($file.FullName)
        if ($null -eq $icon) {
            $failed++
            continue
        }
        $bitmap = $icon.ToBitmap()
        try {
            $bitmap.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)
        }
        finally {
            $bitmap.Dispose()
            $icon.Dispose()
        }
        $extracted++
    }
    catch {
        $failed++
        Write-Warning "Icon extraction failed for $($file.FullName): $($_.Exception.Message)"
    }
}

foreach ($stale in Get-ChildItem -LiteralPath $outputDir -Filter '*.png' -File -ErrorAction SilentlyContinue) {
    if (-not $validKeys.Contains([System.IO.Path]::GetFileNameWithoutExtension($stale.Name))) {
        Remove-Item -LiteralPath $stale.FullName -Force -ErrorAction SilentlyContinue
        Write-Host "Removed stale cache: $($stale.Name)" -ForegroundColor DarkGray
    }
}

$count = (Get-ChildItem -LiteralPath $outputDir -Filter '*.png' -File).Count
$totalSize = [math]::Round((Get-ChildItem -LiteralPath $outputDir -Filter '*.png' -File | Measure-Object -Property Length -Sum).Sum / 1MB, 2)

Write-Host "Icon cache generated: $extracted extracted, $skipped up-to-date, $failed failed, $count total ($totalSize MB)" -ForegroundColor Green
Write-Host "Output: $outputDir" -ForegroundColor Green
