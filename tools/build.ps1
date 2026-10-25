param(
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$projDir = Join-Path $root 'src\VlcGameBarWidget'
$csproj = Join-Path $projDir 'VlcGameBarWidget.csproj'
$msbuild = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'

if (-not (Test-Path $msbuild)) {
    Write-Host "MSBuild not found: $msbuild"
    exit 1
}

if (Get-Process -Name VlcGameBarWidget -ErrorAction SilentlyContinue) {
    Write-Host 'Widget is running. Close it (and the Game Bar overlay) before building.'
    exit 1
}

# .NET Native incremental builds silently produce broken packages - always build from scratch.
foreach ($d in @('obj', 'bin')) {
    $path = Join-Path $projDir $d
    if (Test-Path $path) {
        Write-Host ("cleaning $path")
        Remove-Item $path -Recurse -Force
    }
}

& $msbuild $csproj /restore /t:Build /p:Configuration=$Configuration /p:Platform=x64 /v:minimal
if ($LASTEXITCODE -ne 0) {
    Write-Host ("BUILD FAILED (exit $LASTEXITCODE)")
    exit $LASTEXITCODE
}

$manifest = Join-Path $projDir ("bin\x64\$Configuration\ilc\AppxManifest.xml")
if (-not (Test-Path $manifest)) {
    Write-Host "BUILD SUCCEEDED but $manifest is missing"
    exit 1
}

Write-Host ''
Write-Host "BUILD OK: $manifest"
