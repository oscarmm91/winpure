# Generates src/WinPure/Resources/app.ico — blue shield with white star.
# Renders vector art with WPF at 256/64/48/32/16 px and packs PNG-compressed ICO entries.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase

$repo = Split-Path $PSScriptRoot -Parent
$outIco = Join-Path $repo 'src\WinPure\Resources\app.ico'

# ---- vector art in a 256x256 design space -------------------------------
$shield = [System.Windows.Media.Geometry]::Parse(
    'M 128,18 C 168,38 206,46 234,48 C 234,118 226,182 128,238 C 30,182 22,118 22,48 C 50,46 88,38 128,18 Z')

# 5-point star
$cx = 128.0; $cy = 124.0; $outerR = 60.0; $innerR = 24.0
$pts = for ($k = 0; $k -lt 10; $k++) {
    $rad = if ($k % 2 -eq 0) { $outerR } else { $innerR }
    $ang = (-90 + $k * 36) * [math]::PI / 180
    New-Object System.Windows.Point (($cx + $rad * [math]::Cos($ang)), ($cy + $rad * [math]::Sin($ang)))
}
$starFig = New-Object System.Windows.Media.PathFigure
$starFig.StartPoint = $pts[0]; $starFig.IsClosed = $true
foreach ($p in $pts[1..9]) {
    $seg = New-Object System.Windows.Media.LineSegment ($p, $true)
    $starFig.Segments.Add($seg)
}
$star = New-Object System.Windows.Media.PathGeometry
$star.Figures.Add($starFig)

$fill = New-Object System.Windows.Media.LinearGradientBrush
$fill.StartPoint = '0.5,0'; $fill.EndPoint = '0.5,1'
$fill.GradientStops.Add((New-Object System.Windows.Media.GradientStop ([System.Windows.Media.Color]::FromRgb(0x35, 0x9D, 0xE8), 0)))
$fill.GradientStops.Add((New-Object System.Windows.Media.GradientStop ([System.Windows.Media.Color]::FromRgb(0x00, 0x5A, 0x9E), 1)))
$borderBrush = New-Object System.Windows.Media.SolidColorBrush ([System.Windows.Media.Color]::FromRgb(0x10, 0x2A, 0x4A))
$white = [System.Windows.Media.Brushes]::White

# ---- render each size ----------------------------------------------------
$sizes = 256, 64, 48, 32, 16
$pngs = foreach ($s in $sizes) {
    $visual = New-Object System.Windows.Media.DrawingVisual
    $dc = $visual.RenderOpen()
    $scale = New-Object System.Windows.Media.ScaleTransform (($s / 256.0), ($s / 256.0))
    $dc.PushTransform($scale)
    $pen = New-Object System.Windows.Media.Pen ($borderBrush, 12)
    $pen.LineJoin = 'Round'
    $dc.DrawGeometry($fill, $pen, $shield)
    $dc.DrawGeometry($white, $null, $star)
    $dc.Pop()
    $dc.Close()

    $bmp = New-Object System.Windows.Media.Imaging.RenderTargetBitmap ($s, $s, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $bmp.Render($visual)
    $enc = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bmp))
    $ms = New-Object System.IO.MemoryStream
    $enc.Save($ms)
    , $ms.ToArray()
}

# ---- pack ICO (PNG entries) ----------------------------------------------
$stream = [System.IO.File]::Create($outIco)
$w = New-Object System.IO.BinaryWriter ($stream)
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $w.Write([byte]($(if ($s -eq 256) { 0 } else { $s })))  # width
    $w.Write([byte]($(if ($s -eq 256) { 0 } else { $s })))  # height
    $w.Write([byte]0); $w.Write([byte]0)                    # colors, reserved
    $w.Write([uint16]1); $w.Write([uint16]32)               # planes, bpp
    $w.Write([uint32]$pngs[$i].Length)
    $w.Write([uint32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($png in $pngs) { $w.Write($png) }
$w.Close()
Write-Output "Icon written: $outIco ($((Get-Item $outIco).Length) bytes, sizes: $($sizes -join ', '))"
