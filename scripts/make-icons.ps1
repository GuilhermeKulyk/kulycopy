<#
.SYNOPSIS
    Regenerates the application icons (.ico) and the MSIX/Store tile images (.png) from the vector-like
    description below. Run from anywhere: .\scripts\make-icons.ps1
#>
[CmdletBinding()]
param(
    [string]$IcoDir = (Join-Path $PSScriptRoot "..\src\KulyCopy\Assets"),
    [string]$MsixAssetsDir = (Join-Path $PSScriptRoot "..\packaging\msix\Assets")
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

function New-RoundedRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

# Draws the clipboard glyph into a 32x32 design grid scaled to $size, offset so it is centered in a
# $canvasW x $canvasH bitmap. $off = grey variant (clipboard storing disabled).
function Draw-Glyph($g, [float]$size, [float]$offsetX, [float]$offsetY, [bool]$off) {
    $s = $size / 32.0
    if ($off) {
        $accent = [System.Drawing.Color]::FromArgb(255, 128, 132, 142)
        $dark   = [System.Drawing.Color]::FromArgb(255, 92, 96, 106)
        $paper  = [System.Drawing.Color]::FromArgb(255, 226, 228, 233)
        $line   = [System.Drawing.Color]::FromArgb(255, 170, 174, 184)
    } else {
        $accent = [System.Drawing.Color]::FromArgb(255, 45, 127, 249)
        $dark   = [System.Drawing.Color]::FromArgb(255, 24, 80, 180)
        $paper  = [System.Drawing.Color]::FromArgb(255, 255, 255, 255)
        $line   = [System.Drawing.Color]::FromArgb(255, 120, 170, 250)
    }
    $b1 = New-Object System.Drawing.SolidBrush($accent)
    $b2 = New-Object System.Drawing.SolidBrush($dark)
    $b3 = New-Object System.Drawing.SolidBrush($paper)
    $g.FillPath($b1, (New-RoundedRect ($offsetX + 4 * $s) ($offsetY + 4 * $s) (24 * $s) (26 * $s) (4 * $s)))
    $g.FillPath($b2, (New-RoundedRect ($offsetX + 11 * $s) ($offsetY + 1.5 * $s) (10 * $s) (6 * $s) (2 * $s)))
    $g.FillPath($b3, (New-RoundedRect ($offsetX + 8 * $s) ($offsetY + 9.5 * $s) (16 * $s) (17 * $s) (1.5 * $s)))
    $pen = New-Object System.Drawing.Pen($line, [Math]::Max(1.0, 1.7 * $s))
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawLine($pen, [float]($offsetX + 11 * $s), [float]($offsetY + 14 * $s), [float]($offsetX + 21 * $s), [float]($offsetY + 14 * $s))
    $g.DrawLine($pen, [float]($offsetX + 11 * $s), [float]($offsetY + 18 * $s), [float]($offsetX + 21 * $s), [float]($offsetY + 18 * $s))
    $g.DrawLine($pen, [float]($offsetX + 11 * $s), [float]($offsetY + 22 * $s), [float]($offsetX + 17 * $s), [float]($offsetY + 22 * $s))
    $pen.Dispose(); $b1.Dispose(); $b2.Dispose(); $b3.Dispose()
}

function New-Canvas([int]$w, [int]$h) {
    $bmp = New-Object System.Drawing.Bitmap($w, $h, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)
    return @($bmp, $g)
}

function Draw-Icon([int]$size, [bool]$off) {
    $c = New-Canvas $size $size
    Draw-Glyph $c[1] $size 0 0 $off
    $c[1].Dispose()
    return $c[0]
}

# Store tiles: glyph scaled to a fraction of the tile and centered (tiles need breathing room).
function Draw-Tile([int]$w, [int]$h, [float]$fill) {
    $c = New-Canvas $w $h
    $size = [Math]::Min($w, $h) * $fill
    Draw-Glyph $c[1] $size (($w - $size) / 2) (($h - $size) / 2) $false
    $c[1].Dispose()
    return $c[0]
}

function Save-Ico([string]$path, [bool]$off) {
    $sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
    $pngs = New-Object System.Collections.ArrayList
    foreach ($sz in $sizes) {
        $bmp = Draw-Icon $sz $off
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        [void]$pngs.Add($ms.ToArray())
        $ms.Dispose(); $bmp.Dispose()
    }
    $fs = [System.IO.File]::Create($path)
    $bw = New-Object System.IO.BinaryWriter($fs)
    $bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $sz = $sizes[$i]
        $dim = if ($sz -ge 256) { 0 } else { $sz }
        $bw.Write([Byte]$dim); $bw.Write([Byte]$dim); $bw.Write([Byte]0); $bw.Write([Byte]0)
        $bw.Write([UInt16]1); $bw.Write([UInt16]32)
        $bw.Write([UInt32]$pngs[$i].Length); $bw.Write([UInt32]$offset)
        $offset += $pngs[$i].Length
    }
    foreach ($p in $pngs) { $bw.Write([byte[]]$p) }
    $bw.Flush(); $bw.Dispose(); $fs.Dispose()
}

function Save-Png($bmp, [string]$path) {
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

New-Item -ItemType Directory -Force -Path $IcoDir | Out-Null
New-Item -ItemType Directory -Force -Path $MsixAssetsDir | Out-Null

Save-Ico (Join-Path $IcoDir "kulycopy.ico") $false
Save-Ico (Join-Path $IcoDir "kulycopy-off.ico") $true

# Required MSIX / Store images (scale-100) plus scale-200 variants for high-DPI tiles.
$tiles = @(
    @{ Name = "Square44x44Logo.png";               W = 44;  H = 44;  Fill = 1.0  },
    @{ Name = "Square44x44Logo.scale-200.png";     W = 88;  H = 88;  Fill = 1.0  },
    @{ Name = "Square44x44Logo.targetsize-44_altform-unplated.png"; W = 44; H = 44; Fill = 1.0 },
    @{ Name = "Square71x71Logo.png";               W = 71;  H = 71;  Fill = 0.7  },
    @{ Name = "Square150x150Logo.png";             W = 150; H = 150; Fill = 0.6  },
    @{ Name = "Square150x150Logo.scale-200.png";   W = 300; H = 300; Fill = 0.6  },
    @{ Name = "Square310x310Logo.png";             W = 310; H = 310; Fill = 0.6  },
    @{ Name = "Wide310x150Logo.png";               W = 310; H = 150; Fill = 0.6  },
    @{ Name = "Wide310x150Logo.scale-200.png";     W = 620; H = 300; Fill = 0.6  },
    @{ Name = "StoreLogo.png";                     W = 50;  H = 50;  Fill = 0.9  },
    @{ Name = "StoreLogo.scale-200.png";           W = 100; H = 100; Fill = 0.9  }
)
foreach ($t in $tiles) {
    Save-Png (Draw-Tile $t.W $t.H $t.Fill) (Join-Path $MsixAssetsDir $t.Name)
}

Write-Host "Icons written to $IcoDir"
Write-Host "MSIX assets written to $MsixAssetsDir"
