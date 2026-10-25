param(
    [string]$Configuration = 'Release',
    [switch]$SkipGameBarRestart
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$manifest = Join-Path $root "src\VlcGameBarWidget\bin\x64\$Configuration\ilc\AppxManifest.xml"

if (-not (Test-Path $manifest)) {
    Write-Host "Package manifest not found: $manifest"
    Write-Host 'Run tools\build.ps1 first.'
    exit 1
}

Write-Host '=== stop widget / Game Bar ==='
Get-Process -Name VlcGameBarWidget, GameBar, GameBarFTServer -ErrorAction SilentlyContinue |
    ForEach-Object {
        Write-Host ('stopping ' + $_.ProcessName + ' (' + $_.Id + ')')
        Stop-Process -Id $_.Id -Force
    }
Start-Sleep -Milliseconds 800

Write-Host '=== remove previous package ==='
Get-AppxPackage -Name VlcGameBarWidget | ForEach-Object {
    Write-Host ('removing ' + $_.PackageFullName)
    Remove-AppxPackage -Package $_.PackageFullName -ErrorAction Continue
}

Write-Host '=== register ==='
Add-AppxPackage -Register $manifest
$pkg = Get-AppxPackage -Name VlcGameBarWidget
if (-not $pkg) {
    Write-Host 'REGISTER FAILED'
    exit 1
}
Write-Host ('registered: ' + $pkg.PackageFullName)
Write-Host ('install: ' + $pkg.InstallLocation)

Write-Host '=== loopback exemption (VLC HTTP on 127.0.0.1) ==='
& CheckNetIsolation.exe LoopbackExempt -a "-n=$($pkg.PackageFamilyName)" | Out-Null
$exempt = & CheckNetIsolation.exe LoopbackExempt -s | Select-String $pkg.PackageFamilyName
if ($exempt) { Write-Host 'loopback exemption: OK' } else { Write-Host 'loopback exemption: NOT FOUND (VLC control may not connect)' }

if (-not $SkipGameBarRestart) {
    Write-Host '=== start Game Bar ==='
    Start-Process 'ms-gamebar:'
    Start-Sleep -Seconds 2
}

Write-Host ''
Write-Host 'Done. Press Win+G, open the widget menu (grid icon) and add "VLC Control".'
