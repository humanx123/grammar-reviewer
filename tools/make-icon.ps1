Add-Type -AssemblyName System.Drawing

$outDir = Join-Path $PSScriptRoot "..\App\assets"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

function New-Frame([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    $s = [double]$size
    $radius = [single]($s * 0.22)
    $rect = New-Object System.Drawing.RectangleF(0, 0, [single]$s, [single]$s)

    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $radius * 2
    $path.AddArc(0, 0, $d, $d, 180, 90)
    $path.AddArc($s - $d, 0, $d, $d, 270, 90)
    $path.AddArc($s - $d, $s - $d, $d, $d, 0, 90)
    $path.AddArc(0, $s - $d, $d, $d, 90, 90)
    $path.CloseFigure()

    $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.PointF(0, 0)),
        (New-Object System.Drawing.PointF([single]$s, [single]$s)),
        [System.Drawing.Color]::FromArgb(255, 44, 62, 107),
        [System.Drawing.Color]::FromArgb(255, 74, 95, 160))
    $g.FillPath($bg, $path)

    $bubbleW = $s * 0.66
    $bubbleH = $s * 0.46
    $bx = ($s - $bubbleW) / 2
    $by = $s * 0.20
    $br = $bubbleH * 0.28
    $bd = $br * 2
    $bubble = New-Object System.Drawing.Drawing2D.GraphicsPath
    $bubble.AddArc([single]$bx, [single]$by, [single]$bd, [single]$bd, 180, 90)
    $bubble.AddArc([single]($bx + $bubbleW - $bd), [single]$by, [single]$bd, [single]$bd, 270, 90)
    $bubble.AddArc([single]($bx + $bubbleW - $bd), [single]($by + $bubbleH - $bd), [single]$bd, [single]$bd, 0, 90)
    $tailX = $bx + $bubbleW * 0.30
    $bubble.AddLine([single]($bx + $bubbleW * 0.55), [single]($by + $bubbleH), [single]($bx + $bubbleW * 0.42), [single]($by + $bubbleH + $s * 0.12))
    $bubble.AddLine([single]($bx + $bubbleW * 0.42), [single]($by + $bubbleH + $s * 0.12), [single]$tailX, [single]($by + $bubbleH))
    $bubble.AddArc([single]$bx, [single]($by + $bubbleH - $bd), [single]$bd, [single]$bd, 90, 90)
    $bubble.CloseFigure()
    $g.FillPath([System.Drawing.Brushes]::White, $bubble)

    if ($size -ge 32) {
        $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 44, 62, 107), [single]([Math]::Max(1.5, $s * 0.055)))
        $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
        $cx = $s / 2
        $cy = $by + $bubbleH / 2
        $k = $s * 0.13
        $pts = [System.Drawing.PointF[]]@(
            (New-Object System.Drawing.PointF([single]($cx - $k), [single]$cy)),
            (New-Object System.Drawing.PointF([single]($cx - $k * 0.25), [single]($cy + $k * 0.7))),
            (New-Object System.Drawing.PointF([single]($cx + $k), [single]($cy - $k * 0.6))))
        $g.DrawLines($pen, $pts)
    }

    $g.Dispose()
    return $bmp
}

$sizes = 16, 32, 48, 256
$pngs = @()
foreach ($size in $sizes) {
    $bmp = New-Frame $size
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += ,@{ size = $size; bytes = $ms.ToArray() }
    if ($size -eq 256) { $bmp.Save((Join-Path $outDir "icon.png"), [System.Drawing.Imaging.ImageFormat]::Png) }
    $bmp.Dispose()
}

$icoPath = Join-Path $outDir "icon.ico"
$fs = [System.IO.File]::Create($icoPath)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([uint16]0)
$bw.Write([uint16]1)
$bw.Write([uint16]$pngs.Count)
$offset = 6 + 16 * $pngs.Count
foreach ($p in $pngs) {
    $dim = if ($p.size -ge 256) { 0 } else { $p.size }
    $bw.Write([byte]$dim)
    $bw.Write([byte]$dim)
    $bw.Write([byte]0)
    $bw.Write([byte]0)
    $bw.Write([uint16]1)
    $bw.Write([uint16]32)
    $bw.Write([uint32]$p.bytes.Length)
    $bw.Write([uint32]$offset)
    $offset += $p.bytes.Length
}
foreach ($p in $pngs) { $bw.Write($p.bytes) }
$bw.Close()
$fs.Close()

Write-Output "Icono generado: $icoPath"
