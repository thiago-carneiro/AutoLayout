# Copyright 2026 The AutoLayout authors
# SPDX-License-Identifier: Apache-2.0
#
# Draws the AutoLayout icon (a keyboard with a small "A") from simple shapes and writes a
# multi-size .ico with PNG frames. Run it again after changing the drawing.

param(
    [string] $OutFile = (Join-Path $PSScriptRoot '..\src\AutoLayout\Resources\AutoLayout.ico')
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
