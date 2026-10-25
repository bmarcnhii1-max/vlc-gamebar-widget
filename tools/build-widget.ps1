$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent
$msbuild = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'
$launcherProj = Join-Path $root 'src\VlcLauncher\VlcLauncher.csproj'
$launcherOut = Join-Path $root 'src\VlcLauncher\bin\Release\net48\VlcLauncher.exe'
$launcherDst = Join-Path $root 'src\VlcGameBarWidget\Launcher\VlcLauncher.exe'
$proj = Join-Path $root 'src\VlcGameBarWidget\VlcGameBarWidget.csproj'

Write-Host '=== build launcher ==='
dotnet build $launcherProj -c Release -v:minimal
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
New-Item -ItemType Directory -Force -Path (Split-Path $launcherDst) | Out-Null
Copy-Item $launcherOut $launcherDst -Force

Write-Host '=== stop running widget ==='
Get-Process VlcGameBarWidget -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500

Write-Host '=== clean ==='
Remove-Item (Join-Path $root 'src\VlcGameBarWidget\obj') -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $root 'src\VlcGameBarWidget\bin') -Recurse -Force -ErrorAction SilentlyContinue

Write-Host '=== build widget ==='
& $msbuild $proj /restore /t:Build /p:Configuration=Release /p:Platform=x64 /v:minimal
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$manifest = Join-Path $root 'src\VlcGameBarWidget\bin\x64\Release\ilc\AppxManifest.xml'
$launcherInIlc = Join-Path $root 'src\VlcGameBarWidget\bin\x64\Release\ilc\Launcher\VlcLauncher.exe'
if (!(Test-Path $manifest)) { Write-Host 'FAILED: ilc manifest missing'; exit 1 }
if (!(Test-Path $launcherInIlc)) { Write-Host 'FAILED: launcher missing in ilc layout'; exit 1 }

Write-Host ('OK ' + $manifest)
Write-Host ('OK ' + $launcherInIlc)
