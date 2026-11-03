# Copyright 2026 The AutoLayout authors
# SPDX-License-Identifier: Apache-2.0
#
# Makes the Microsoft Store screenshots (PNG, at least 1366 x 768) from the captures that
# `AutoLayout.exe --demo --capture <folder>` writes: each window on a 1920 x 1080 canvas with a
# soft background and a shadow. Windows with transparent corners keep them.
#
#   .\tools\make-store-screenshots.ps1 -Captures <folder> [-OutDir docs\images\store]

param(
    [Parameter(Mandatory)] [string] $Captures,
    [string] $OutDir = (Join-Path $PSScriptRoot '..\docs\images\store'),
    [int] $Width = 1920,
    [int] $Height = 1080
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

New-Item -ItemType Directory -Force $OutDir | Out-Null
$files = Get-ChildItem $Captures -Filter *.png | Sort-Object Name
if (-not $files) { throw "No PNG files in $Captures" }

foreach ($file in $files) {
    $shot = [System.Drawing.Image]::FromFile($file.FullName)
    try {
        # Fit the window into 80% of the canvas, never enlarging it (that would blur it)
        $scale = [Math]::Min(1.0, [Math]::Min($Width * 0.8 / $shot.Width, $Height * 0.8 / $shot.Height))
        $w = [int]($shot.Width * $scale); $h = [int]($shot.Height * $scale)
        $x = [int](($Width - $w) / 2); $y = [int](($Height - $h) / 2)

        $canvas = New-Object System.Drawing.Bitmap $Width, $Height, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $g = [System.Drawing.Graphics]::FromImage($canvas)
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic

        # Background: the icon's blue, light, top-left to bottom-right
        $rect = New-Object System.Drawing.Rectangle 0, 0, $Width, $Height
        $background = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect,
            ([System.Drawing.Color]::FromArgb(255, 222, 236, 250)), ([System.Drawing.Color]::FromArgb(255, 186, 211, 240)), 35.0
        $g.FillRectangle($background, $rect)

        # Shadow: a few widening, fading rounded rectangles under the window
        for ($i = 12; $i -ge 1; $i--) {
            $alpha = [int](6 + (12 - $i) * 1.2)
            $brush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb($alpha, 0, 30, 70))
            $g.FillPath($brush, (New-RoundedRectangle ($x - $i) ($y - $i + 8) ($w + 2 * $i) ($h + 2 * $i) (8 + $i)))
            $brush.Dispose()
        }

        # The window, clipped to rounded corners like Windows 11 windows
        $g.SetClip((New-RoundedRectangle $x $y $w $h 8))
        $g.DrawImage($shot, $x, $y, $w, $h)
        $g.ResetClip()
        $g.Dispose()

        $out = Join-Path $OutDir $file.Name
        $canvas.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
        $canvas.Dispose()
        Write-Output "Wrote $out ($Width x $Height)"
    }
    finally {
        $shot.Dispose()
    }
}
