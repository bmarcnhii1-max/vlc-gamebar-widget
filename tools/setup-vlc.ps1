param(
    [string]$Password = 'gamebar',
    [int]$Port = 8080
)

$ErrorActionPreference = 'Stop'

$vlc = Get-Process -Name vlc -ErrorAction SilentlyContinue
if ($vlc) {
    Write-Host 'VLC is running. Close it first (the config is rewritten on exit), then rerun this script.'
    exit 1
}

$dir = Join-Path $env:APPDATA 'vlc'
$cfg = Join-Path $dir 'vlcrc'
if (-not (Test-Path $cfg)) {
    Write-Host ("VLC config not found: $cfg")
    Write-Host 'Start VLC once to create it, close it, then rerun this script.'
    exit 1
}

$backup = Join-Path $dir ('vlcrc.bak-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
Copy-Item $cfg $backup
Write-Host ("backup: $backup")

$text = [System.IO.File]::ReadAllText($cfg)
$newline = if ($text.Contains("`r`n")) { "`r`n" } else { "`n" }

$wanted = [ordered]@{
    'extraintf'     = 'http'
    'http-password' = $Password
    'http-port'     = "$Port"
    'http-host'     = '127.0.0.1'
}

foreach ($key in $wanted.Keys) {
    $value = $wanted[$key]
    $pattern = '(?m)^#?' + [regex]::Escape($key) + '=.*$'
    $replacement = $key + '=' + $value
    if ([regex]::IsMatch($text, $pattern)) {
        $text = [regex]::Replace($text, $pattern, $replacement, 1)
    } else {
        $text = $text.TrimEnd() + $newline + $replacement + $newline
    }
}

[System.IO.File]::WriteAllText($cfg, $text, (New-Object System.Text.UTF8Encoding($true)))

Write-Host 'configured:'
foreach ($key in $wanted.Keys) {
    $line = Select-String -Path $cfg -Pattern ('^' + [regex]::Escape($key) + '=') | Select-Object -First 1
    Write-Host ('  ' + $line.Line)
}

Write-Host ''
Write-Host 'Start VLC and check: http://127.0.0.1:' + $Port + '/requests/status.xml (login: empty user, password above)'
