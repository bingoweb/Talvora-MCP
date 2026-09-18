param(
    [string] $RepoRoot = (Join-Path $PSScriptRoot '..')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$assets = Join-Path $RepoRoot 'assets'
New-Item -ItemType Directory -Force -Path $assets | Out-Null

function New-RoundedRectPath {
    param(
        [System.Drawing.RectangleF] $Rect,
        [float] $Radius
    )

    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $d = $Radius * 2
    $path.AddArc($Rect.X, $Rect.Y, $d, $d, 180, 90)
    $path.AddArc($Rect.Right - $d, $Rect.Y, $d, $d, 270, 90)
    $path.AddArc($Rect.Right - $d, $Rect.Bottom - $d, $d, $d, 0, 90)
    $path.AddArc($Rect.X, $Rect.Bottom - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-TalvoraBitmap {
    param([int] $Size)

    $bmp = [System.Drawing.Bitmap]::new($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    $s = [single]$Size
    $margin = [single]($s * 0.055)
    $radius = [single]($s * 0.22)
    $tile = [System.Drawing.RectangleF]::new($margin, $margin, $s - 2*$margin, $s - 2*$margin)
    $tilePath = New-RoundedRectPath -Rect $tile -Radius $radius

    $bgBrush = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
        $tile,
        [System.Drawing.Color]::FromArgb(255, 9, 30, 49),
        [System.Drawing.Color]::FromArgb(255, 3, 13, 26),
        45.0
    )
    $g.FillPath($bgBrush, $tilePath)

    if ($Size -ge 48) {
        $glowPen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(60, 0, 229, 255), [single]($s*0.030))
        $g.DrawPath($glowPen, $tilePath)
        $glowPen.Dispose()
    }

    $edgeBrush = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
        $tile,
        [System.Drawing.Color]::FromArgb(255, 60, 225, 255),
        [System.Drawing.Color]::FromArgb(255, 0, 92, 255),
        15.0
    )
    $edgePen = [System.Drawing.Pen]::new($edgeBrush, [single]([Math]::Max(1, $s*0.010)))
    $g.DrawPath($edgePen, $tilePath)

    # Hexagonal Talvora frame
    $cx = [single]($s * 0.46)
    $cy = [single]($s * 0.47)
    $r = [single]($s * 0.265)
    $hex = [System.Drawing.PointF[]]@(
        ([System.Drawing.PointF]::new($cx, $cy-$r)),
        ([System.Drawing.PointF]::new($cx+$r*0.87, $cy-$r*0.50)),
        ([System.Drawing.PointF]::new($cx+$r*0.87, $cy+$r*0.50)),
        ([System.Drawing.PointF]::new($cx, $cy+$r)),
        ([System.Drawing.PointF]::new($cx-$r*0.87, $cy+$r*0.50)),
        ([System.Drawing.PointF]::new($cx-$r*0.87, $cy-$r*0.50)),
        ([System.Drawing.PointF]::new($cx, $cy-$r))
    )

    $hexRect = [System.Drawing.RectangleF]::new($cx-$r, $cy-$r, 2*$r, 2*$r)
    $brandBrush = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
        $hexRect,
        [System.Drawing.Color]::FromArgb(255, 0, 229, 255),
        [System.Drawing.Color]::FromArgb(255, 55, 99, 255),
        30.0
    )
    $hexPen = [System.Drawing.Pen]::new($brandBrush, [single]([Math]::Max(2, $s*0.070)))
    $hexPen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $g.DrawLines($hexPen, $hex)

    # Geometric T
    $topY = [single]($cy - $r*0.20)
    $barW = [single]($r * 1.25)
    $barH = [single]($r * 0.30)
    $stemW = [single]($r * 0.34)
    $stemBottom = [single]($cy + $r*0.76)
    $tPath = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $tPath.AddPolygon([System.Drawing.PointF[]]@(
        ([System.Drawing.PointF]::new($cx-$barW/2, $topY)),
        ([System.Drawing.PointF]::new($cx+$barW/2, $topY)),
        ([System.Drawing.PointF]::new($cx+$barW*0.37, $topY+$barH)),
        ([System.Drawing.PointF]::new($cx+$stemW/2, $topY+$barH*1.25)),
        ([System.Drawing.PointF]::new($cx+$stemW/2, $stemBottom)),
        ([System.Drawing.PointF]::new($cx, $stemBottom+$r*0.12)),
        ([System.Drawing.PointF]::new($cx-$stemW/2, $stemBottom)),
        ([System.Drawing.PointF]::new($cx-$stemW/2, $topY+$barH*1.25)),
        ([System.Drawing.PointF]::new($cx-$barW*0.37, $topY+$barH))
    ))
    $tBrush = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
        ([System.Drawing.RectangleF]::new($cx-$barW/2, $topY, $barW, $stemBottom-$topY+$r*0.12)),
        [System.Drawing.Color]::FromArgb(255, 94, 245, 255),
        [System.Drawing.Color]::FromArgb(255, 0, 96, 255),
        90.0
    )
    $g.FillPath($tBrush, $tPath)

    foreach($d in @($g,$tilePath,$bgBrush,$edgeBrush,$edgePen,$brandBrush,$hexPen,$tPath,$tBrush)){ $d.Dispose() }
    return $bmp
}

$sizes = @(16,20,24,32,40,48,64,128,256)
$pngEntries = @()

foreach($size in $sizes) {
    $bmp = New-TalvoraBitmap -Size $size
    try {
        $ms = [System.IO.MemoryStream]::new()
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $pngEntries += [pscustomobject]@{ Size=$size; Bytes=$ms.ToArray() }
        $ms.Dispose()

        if($size -eq 256) {
            $preview = Join-Path $assets 'Talvora.png'
            $bmp.Save($preview, [System.Drawing.Imaging.ImageFormat]::Png)
        }
    }
    finally {
        $bmp.Dispose()
    }
}

$icoPath = Join-Path $assets 'Talvora.ico'
$stream = [System.IO.File]::Open($icoPath,[System.IO.FileMode]::Create,[System.IO.FileAccess]::Write)
$writer = [System.IO.BinaryWriter]::new($stream)
try {
    $writer.Write([UInt16]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]$pngEntries.Count)

    $offset = 6 + (16 * $pngEntries.Count)
    foreach($entry in $pngEntries) {
        $dim = if($entry.Size -ge 256){[byte]0}else{[byte]$entry.Size}
        $writer.Write($dim)
        $writer.Write($dim)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([UInt16]1)
        $writer.Write([UInt16]32)
        $writer.Write([UInt32]$entry.Bytes.Length)
        $writer.Write([UInt32]$offset)
        $offset += $entry.Bytes.Length
    }

    foreach($entry in $pngEntries) {
        $writer.Write($entry.Bytes)
    }
}
finally {
    $writer.Dispose()
    $stream.Dispose()
}

$iconBytes=[IO.File]::ReadAllBytes($icoPath)
$count=[BitConverter]::ToUInt16($iconBytes,4)
if($count -ne $sizes.Count){ throw "ICO frame count mismatch: $count" }

[pscustomobject]@{
    Icon=$icoPath
    Preview=(Join-Path $assets 'Talvora.png')
    Frames=$count
    Sizes=($sizes -join ',')
    IconSha256=(Get-FileHash $icoPath -Algorithm SHA256).Hash
    PreviewSha256=(Get-FileHash (Join-Path $assets 'Talvora.png') -Algorithm SHA256).Hash
} | ConvertTo-Json -Compress