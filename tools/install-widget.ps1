$ErrorActionPreference = 'Continue'

$root = Split-Path $PSScriptRoot -Parent
$manifest = Join-Path $root 'src\VlcGameBarWidget\bin\x64\Release\ilc\AppxManifest.xml'

if (!(Test-Path $manifest)) {
    Write-Host ('manifest not found: ' + $manifest)
    exit 1
}

Write-Host '=== stop widget and game bar ==='
Get-Process VlcGameBarWidget, GameBar, GameBarFTServer -ErrorAction SilentlyContinue |
    Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 1

Write-Host '=== remove previous package ==='
Get-AppxPackage -Name VlcGameBarWidget | ForEach-Object {
    Write-Host ('removing ' + $_.PackageFullName)
    Remove-AppxPackage -Package $_.PackageFullName -ErrorAction Continue
}
Start-Sleep -Seconds 1

Write-Host '=== register package ==='
try {
    Add-AppxPackage -Register $manifest -ErrorAction Stop
    Write-Host 'REGISTER OK'
} catch {
    Write-Host ('REGISTER FAILED: ' + $_.Exception.Message)
    exit 1
}

$pkg = Get-AppxPackage -Name VlcGameBarWidget
if (-not $pkg) {
    Write-Host 'package not found after registration'
    exit 1
}

$pkg | Format-List Name, PackageFullName, PackageFamilyName, Status, InstallLocation

Write-Host '=== loopback exemption ==='
CheckNetIsolation.exe LoopbackExempt -a -n=$pkg.PackageFamilyName | Out-Null
CheckNetIsolation.exe LoopbackExempt -s | Select-String -SimpleMatch $pkg.PackageFamilyName

Write-Host '=== restart game bar ==='
Start-Process 'ms-gamebar:'

Write-Host ('PKF=' + $pkg.PackageFamilyName)
