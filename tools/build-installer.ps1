$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$stage = Join-Path $root 'artifacts\stage'
$payload = Join-Path $root 'installer\VlcControl.Setup\payload.zip'
$dist = Join-Path $root 'artifacts\dist'

function Say($text) { Write-Host ('==> ' + $text) }

Say 'VlcControl (self-contained single file)'
$publishDir = Join-Path $root 'VlcControl\publish'
$publishExe = Join-Path $publishDir 'VlcControl.exe'
Push-Location (Join-Path $root 'VlcControl')
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -o publish | Out-Host
Pop-Location

Say 'Виджет Game Bar'
$ilc = Join-Path $root 'src\VlcGameBarWidget\bin\x64\Release\ilc'
if (!(Test-Path (Join-Path $ilc 'AppxManifest.xml')) -or !(Test-Path (Join-Path $ilc 'Launcher\VlcLauncher.exe'))) {
    & (Join-Path $root 'tools\build-widget.ps1') | Out-Host
} else {
    Say 'используем готовую сборку виджета'
}

Say 'Деинсталлятор'
Push-Location (Join-Path $root 'installer\VlcControl.Uninstall')
dotnet build -c Release | Out-Host
Pop-Location
$uninstallExe = Join-Path $root 'installer\VlcControl.Uninstall\bin\Release\net48\Uninstall.exe'
if (!(Test-Path $uninstallExe)) { throw 'не найден Uninstall.exe' }

Say 'Сборка payload'
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path (Join-Path $stage 'app') | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $stage 'widget') | Out-Null
Copy-Item $publishExe (Join-Path $stage 'app\VlcControl.exe') -Force
Copy-Item (Join-Path $ilc '*') (Join-Path $stage 'widget') -Recurse -Force
Copy-Item $uninstallExe (Join-Path $stage 'Uninstall.exe') -Force

if (Test-Path $payload) { Remove-Item $payload -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($stage, $payload, [System.IO.Compression.CompressionLevel]::Optimal, $false)
Say ('payload.zip ' + [Math]::Round((Get-Item $payload).Length / 1MB, 1) + ' МБ')

Say 'Установщик'
Push-Location (Join-Path $root 'installer\VlcControl.Setup')
dotnet build -c Release | Out-Host
Pop-Location
$setupExe = Join-Path $root 'installer\VlcControl.Setup\bin\Release\net48\VLCControl-Setup.exe'
if (!(Test-Path $setupExe)) { throw 'не найден VLCControl-Setup.exe' }

New-Item -ItemType Directory -Force -Path $dist | Out-Null
Copy-Item $setupExe (Join-Path $dist 'VLCControl-Setup.exe') -Force
Say ('готово: ' + (Join-Path $dist 'VLCControl-Setup.exe') + ' (' + [Math]::Round((Get-Item (Join-Path $dist 'VLCControl-Setup.exe')).Length / 1MB, 1) + ' МБ)')
