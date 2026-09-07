[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

$WingetId = '9N7F2SM5D1LR'
$AppName = 'Windows HDR Calibration'
$AppAumid = 'MicrosoftCorporationII.WindowsHDRCalibration_8wekyb3d8bbwe!App'

Write-Host "========================================"
Write-Host "  $AppName"
Write-Host "========================================"
Write-Host ""

$installed = $false
try {
    $listOutput = winget list --name $AppName --accept-source-agreements 2>$null
    $installed = $listOutput -match [regex]::Escape($WingetId)
} catch {
    $installed = $false
}

if ($installed) {
    Write-Host "[OK] $AppName already installed, launching..."
    Start-Process "shell:AppsFolder\$AppAumid"
} else {
    Write-Host "[INFO] $AppName not found, installing from Microsoft Store..."
    winget install --id $WingetId --accept-package-agreements --accept-source-agreements
    if ($LASTEXITCODE -eq 0) {
        Write-Host ""
        Write-Host "[OK] $AppName installed, launching..."
        Start-Process "shell:AppsFolder\$AppAumid"
    } else {
        Write-Host ""
        Write-Host "[FAIL] Install failed. Try manually from Microsoft Store."
    }
}

Write-Host ""
Read-Host "Press Enter to exit"