# Copyright 2026 The AutoLayout authors
# SPDX-License-Identifier: Apache-2.0
#
# Draws the AutoLayout icon (a keyboard) from simple shapes and writes a multi-size .ico with
# PNG frames, plus a 256-pixel PNG. Run it again after changing the drawing.

param(
    [string] $OutFile = (Join-Path $PSScriptRoot '..\src\AutoLayout\Assets\AutoLayout.ico')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function New-RoundedRectangle([float] $x, [float] $y, [float] $w, [float] $h, [float] $r) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-IconFrame([int] $size) {
    $bitmap = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    $s = $size / 32.0
    # Keyboard body
    $body = New-RoundedRectangle (1 * $s) (7 * $s) (30 * $s) (19 * $s) (3.5 * $s)
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 0, 103, 192))), $body)

    # Keys: two rows and a space bar
    $keyBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
    foreach ($row in 0, 1) {
        for ($col = 0; $col -lt 6; $col++) {
            $kx = (4 + $col * 4.2) * $s
            $ky = (10 + $row * 4.5) * $s
            $key = New-RoundedRectangle $kx $ky (3 * $s) (3 * $s) ([Math]::Max(0.5, 0.8 * $s))
            $g.FillPath($keyBrush, $key)
        }
    }
    $space = New-RoundedRectangle (9 * $s) (19.5 * $s) (14 * $s) (3 * $s) ([Math]::Max(0.5, 0.8 * $s))
    $g.FillPath($keyBrush, $space)

    $g.Dispose()
    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    # The leading comma keeps PowerShell from sending the bytes down the pipeline one by one
    return , $stream.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 256
$frames = New-Object 'System.Collections.Generic.List[byte[]]'
foreach ($size in $sizes) { $frames.Add((New-IconFrame $size)) }

New-Item -ItemType Directory -Force (Split-Path $OutFile) | Out-Null
$out = New-Object System.IO.BinaryWriter ([System.IO.File]::Create($OutFile))
try {
    # ICONDIR
    $out.Write([UInt16] 0); $out.Write([UInt16] 1); $out.Write([UInt16] $sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $dimension = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
        # ICONDIRENTRY: width, height, colors, reserved, planes, bpp, size, offset
        $out.Write([Byte] $dimension); $out.Write([Byte] $dimension); $out.Write([Byte] 0); $out.Write([Byte] 0)
        $out.Write([UInt16] 1); $out.Write([UInt16] 32)
        $out.Write([UInt32] $frames[$i].Length); $out.Write([UInt32] $offset)
        $offset += $frames[$i].Length
    }
    foreach ($frame in $frames) { $out.Write($frame) }
}
finally {
    $out.Close()
}
Write-Output "Wrote $OutFile"

$png = [System.IO.Path]::ChangeExtension($OutFile, '.png')
[System.IO.File]::WriteAllBytes($png, $frames[$frames.Count - 1])
Write-Output "Wrote $png"

# Images for the MSIX package (Microsoft Store): the icon centered on a transparent canvas
$storeFolder = Join-Path (Split-Path $OutFile) 'Store'
New-Item -ItemType Directory -Force $storeFolder | Out-Null
function Save-Asset([string] $name, [int] $width, [int] $height, [int] $icon) {
    $frameStream = New-Object System.IO.MemoryStream (, (New-IconFrame $icon))
    $frame = [System.Drawing.Image]::FromStream($frameStream)
    $canvas = New-Object System.Drawing.Bitmap $width, $height, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($canvas)
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.DrawImage($frame, [int](($width - $icon) / 2), [int](($height - $icon) / 2), $icon, $icon)
    $g.Dispose()
    $path = Join-Path $storeFolder $name
    $canvas.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $canvas.Dispose(); $frame.Dispose(); $frameStream.Dispose()
    Write-Output "Wrote $path"
}
Save-Asset 'Square44x44Logo.png' 44 44 40
Save-Asset 'Square44x44Logo.targetsize-24_altform-unplated.png' 24 24 24
Save-Asset 'Square44x44Logo.targetsize-48_altform-unplated.png' 48 48 48
Save-Asset 'Square150x150Logo.png' 150 150 96
Save-Asset 'Wide310x150Logo.png' 310 150 96
Save-Asset 'StoreLogo.png' 50 50 48
Save-Asset 'SplashScreen.png' 620 300 160
