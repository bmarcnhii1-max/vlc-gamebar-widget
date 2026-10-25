param([string[]]$Files)
$enc = New-Object System.Text.UTF8Encoding($true)
foreach ($file in $Files) {
    if (!(Test-Path $file)) { Write-Host ('skip ' + $file); continue }
    $text = [System.IO.File]::ReadAllText($file, [System.Text.Encoding]::UTF8)
    [System.IO.File]::WriteAllText($file, $text, $enc)
    Write-Host ('bom: ' + $file)
}
