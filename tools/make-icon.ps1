Add-Type -AssemblyName System.Drawing

$out = 'C:\VlcWidgetRepo\VlcControl\app.ico'
$sizes = @(16, 24, 32, 48, 64, 128, 256)
$pngs = @()

function New-IconPng([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.Clear([System.Drawing.Color]::Transparent)

    $pad = [Math]::Max(0.5, $s * 0.03)
    $rect = New-Object System.Drawing.RectangleF $pad, $pad, ($s - 2 * $pad), ($s - 2 * $pad)
    $radius = $s * 0.24
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $radius * 2
    $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
    $path.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
    $path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
    $path.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
    $path.CloseFigure()

    $orange = [System.Drawing.Color]::FromArgb(255, 255, 138, 0)
    $deep = [System.Drawing.Color]::FromArgb(255, 214, 96, 0)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, $orange, $deep, 90.0
    $g.FillPath($brush, $path)

    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(60, 255, 255, 255)), ([Math]::Max(1, $s * 0.02))
    $g.DrawPath($pen, $path)

    $cx = $s / 2.0
    $cy = $s / 2.0
    $triW = $s * 0.30
    $triH = $s * 0.38
    $off = $s * 0.035
    $pts = @(
        (New-Object System.Drawing.PointF ($cx - $triW / 2 + $off), ($cy - $triH / 2)),
        (New-Object System.Drawing.PointF ($cx - $triW / 2 + $off), ($cy + $triH / 2)),
        (New-Object System.Drawing.PointF ($cx + $triW / 2 + $off), $cy)
    )
    $dark = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 24, 24, 24))
    $g.FillPolygon($dark, $pts)

    $g.Dispose(); $brush.Dispose(); $pen.Dispose(); $dark.Dispose(); $path.Dispose()

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return ,$ms.ToArray()
}

foreach ($s in $sizes) { $pngs += ,(New-IconPng $s) }

$fs = [System.IO.File]::Create($out)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([UInt16]0)
$bw.Write([UInt16]1)
$bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $data = $pngs[$i]
    $bw.Write([Byte]($(if ($s -ge 256) { 0 } else { $s })))
    $bw.Write([Byte]($(if ($s -ge 256) { 0 } else { $s })))
    $bw.Write([Byte]0)
    $bw.Write([Byte]0)
    $bw.Write([UInt16]1)
    $bw.Write([UInt16]32)
    $bw.Write([UInt32]$data.Length)
    $bw.Write([UInt32]$offset)
    $offset += $data.Length
}
foreach ($data in $pngs) { $bw.Write($data) }
$bw.Flush(); $bw.Dispose(); $fs.Dispose()

Write-Output ('icon written: ' + $out + ' size=' + (Get-Item $out).Length)
[System.IO.File]::WriteAllBytes('C:\Users\User\Documents\Qoder\2026-10-25\38ef574f\artifacts\shots\app-icon-256.png', $pngs[$pngs.Count - 1])
