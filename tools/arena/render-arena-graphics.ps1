# SoccerMod Arena: the pictures with lettering (printed boards on the wall round the pitch,
# the video wall, the tunnel sign). Fantasy brands only, the same ones as on the LED boards
# (tools/atmo/render-board-pages.ps1), drawn here as vector shapes.
#
#   powershell -ExecutionPolicy Bypass -File tools/arena/render-arena-graphics.ps1 <outDir>
param([Parameter(Mandatory = $true)][string]$Out)
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force $Out | Out-Null

function C($hex) { [System.Drawing.ColorTranslator]::FromHtml($hex) }
function Solid($hex) { New-Object System.Drawing.SolidBrush (C $hex) }
function Grad($x, $y, $w, $h, $a, $b, $vertical = $true) {
  $rect = New-Object System.Drawing.RectangleF ([single]$x), ([single]$y), ([single]$w), ([single]$h)
  $mode = if ($vertical) { [System.Drawing.Drawing2D.LinearGradientMode]::Vertical } else { [System.Drawing.Drawing2D.LinearGradientMode]::Horizontal }
  New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, (C $a), (C $b), $mode
}
function Font($name, $size, $style = 'Regular') { New-Object System.Drawing.Font $name, ([single]$size), ([System.Drawing.FontStyle]$style), ([System.Drawing.GraphicsUnit]::Pixel) }
function Canvas($w, $h) {
  $bmp = New-Object System.Drawing.Bitmap $w, $h, ([System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = 'AntiAlias'; $g.TextRenderingHint = 'AntiAliasGridFit'; $g.InterpolationMode = 'HighQualityBicubic'
  return $bmp, $g
}
# text fitted into a box (centred), returns nothing
function FitText($g, $s, $fontName, $style, $brush, $x, $y, $w, $h, $skew = 0) {
  $fmt = [System.Drawing.StringFormat]::GenericTypographic
  $size = $h
  do { $f = Font $fontName $size $style; $m = $g.MeasureString($s, $f, 9000, $fmt); if ($m.Width -le $w) { break }; $size = $size * 0.94 } while ($size -gt 6)
  $tx = $x + ($w - $m.Width) / 2; $ty = $y + ($h - $m.Height) / 2
  $state = $g.Save()
  if ($skew -ne 0) { $mx = New-Object System.Drawing.Drawing2D.Matrix 1, 0, ([single]$skew), 1, ([single](-$skew * ($ty + $m.Height / 2))), 0; $g.MultiplyTransform($mx) }
  $g.DrawString($s, $f, $brush, [single]$tx, [single]$ty, $fmt)
  $g.Restore($state)
}
function Poly($g, $brush, $pts, $dx, $dy, $s = 1.0) {
  $p = @(); for ($i = 0; $i -lt $pts.Count; $i += 2) { $p += New-Object System.Drawing.PointF ([single]($dx + $pts[$i] * $s)), ([single]($dy + $pts[$i + 1] * $s)) }
  $g.FillPolygon($brush, [System.Drawing.PointF[]]$p)
}
function Star($g, $brush, $cx, $cy, $r) {
  $pts = @(); for ($i = 0; $i -lt 10; $i++) { $a = -[Math]::PI / 2 + $i * [Math]::PI / 5; $rr = if ($i % 2) { $r * 0.42 } else { $r }; $pts += $cx + $rr * [Math]::Cos($a); $pts += $cy + $rr * [Math]::Sin($a) }
  Poly $g $brush $pts 0 0
}
function Ball($g, $cx, $cy, $r, $light, $dark) {
  $g.FillEllipse((Solid $light), [single]($cx - $r), [single]($cy - $r), [single](2 * $r), [single](2 * $r))
  $pts = @(); for ($i = 0; $i -lt 5; $i++) { $a = -[Math]::PI / 2 + $i * 2 * [Math]::PI / 5; $pts += $cx + $r * 0.36 * [Math]::Cos($a); $pts += $cy + $r * 0.36 * [Math]::Sin($a) }
  Poly $g (Solid $dark) $pts 0 0
  for ($i = 0; $i -lt 5; $i++) { $a = -[Math]::PI / 2 + $i * 2 * [Math]::PI / 5; $x = $cx + $r * 0.86 * [Math]::Cos($a); $y = $cy + $r * 0.86 * [Math]::Sin($a); $g.FillEllipse((Solid $dark), [single]($x - $r * 0.2), [single]($y - $r * 0.2), [single]($r * 0.4), [single]($r * 0.4)) }
}

# ---- printed boards: 4 boards of 512 x 128 per strip -----------------------------------------------
$BW = 512; $BH = 128
$boards = @{
  kickfuel = {
    param($g, $x)
    $g.FillRectangle((Grad $x 0 $BW $BH '#151515' '#050505'), $x, 0, $BW, $BH)
    Poly $g (Grad $x 0 100 $BH '#ffd21a' '#ff7a00') @(62, 0, 18, 92, 50, 92, 30, 170, 104, 62, 70, 62, 94, 0) ($x + 30) 12 0.6
    FitText $g 'KICKFUEL' 'Impact' 'Regular' (Solid '#ffffff') ($x + 118) 20 370 88 -0.18
  }
  voltwave = {
    param($g, $x)
    $g.FillRectangle((Grad $x 0 $BW $BH '#1b4fe0' '#0f35b5'), $x, 0, $BW, $BH)
    $pen = New-Object System.Drawing.Pen (C '#ffffff'), 7; $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
    for ($k = 0; $k -lt 3; $k++) { $y = 42 + $k * 22; $g.DrawBezier($pen, [single]($x + 34), $y, [single]($x + 56), ($y - 18), [single]($x + 74), ($y + 18), [single]($x + 96), $y) }
    FitText $g 'voltwave' 'Bahnschrift SemiBold' 'Regular' (Solid '#ffffff') ($x + 118) 22 366 84
  }
  goalcrest = {
    param($g, $x)
    $g.FillRectangle((Grad $x 0 $BW $BH '#0f7a3c' '#085a2b'), $x, 0, $BW, $BH)
    Poly $g (Grad $x 0 100 $BH '#ffe08a' '#c9971c') @(0, 0, 110, 0, 110, 70, 55, 130, 0, 70) ($x + 30) 22 0.66
    Star $g (Solid '#085a2b') ($x + 66) 56 20
    FitText $g 'GoalCrest' 'Segoe UI Black' 'Regular' (Solid '#ffffff') ($x + 122) 24 360 80
  }
  topcorner = {
    param($g, $x)
    $g.FillRectangle((Grad $x 0 $BW $BH '#e2141d' '#b30d15'), $x, 0, $BW, $BH)
    FitText $g 'TopCorner' 'Segoe Script' 'Bold' (Solid '#ffffff') ($x + 40) 10 432 86
    $pen = New-Object System.Drawing.Pen (C '#ffffff'), 4
    $g.DrawBezier($pen, [single]($x + 70), 108, [single]($x + 180), 92, [single]($x + 300), 118, [single]($x + 440), 98)
  }
  pitchline = {
    param($g, $x)
    $g.FillRectangle((Grad $x 0 $BW $BH '#ffffff' '#e6e6ec'), $x, 0, $BW, $BH)
    Poly $g (Solid '#3d1f8f') @(0, 40, 70, 30, 105, 0, 118, 6, 98, 36, 150, 30, 160, 42, 98, 52, 118, 86, 104, 90, 70, 58, 0, 58) ($x + 26) 34 0.62
    $fmt = [System.Drawing.StringFormat]::GenericTypographic; $f = Font 'Arial Black' 62
    $w1 = $g.MeasureString('Pitch', $f, 9000, $fmt).Width
    $g.DrawString('Pitch', $f, (Solid '#3d1f8f'), [single]($x + 140), 22, $fmt)
    $g.DrawString('Line', $f, (Solid '#f26522'), [single]($x + 136 + $w1), 22, $fmt)
  }
  soccermod = {
    param($g, $x)
    $g.FillRectangle((Grad $x 0 $BW $BH '#0a1760' '#1f47c2' $false), $x, 0, $BW, $BH)
    Ball $g ($x + 68) 64 40 '#ffffff' '#0a1760'
    FitText $g 'SOCCERMOD ARENA' 'Bahnschrift SemiBold Condensed' 'Regular' (Solid '#ffffff') ($x + 124) 26 366 76
  }
  fairplay = {
    param($g, $x)
    $g.FillRectangle((Grad $x 0 $BW $BH '#141414' '#050505'), $x, 0, $BW, $BH)
    FitText $g 'PLAY FAIR' 'Bahnschrift SemiBold' 'Regular' (Solid '#ffffff') ($x + 40) 22 390 84
    $g.FillRectangle((Solid '#2fb84a'), [single]($x + 452), 38, 14, 52)
  }
  respect = {
    param($g, $x)
    $g.FillRectangle((Grad $x 0 $BW $BH '#12245c' '#0a1740'), $x, 0, $BW, $BH)
    FitText $g 'RESPECT THE GAME' 'Bahnschrift SemiBold Condensed' 'Regular' (Solid '#ffffff') ($x + 30) 24 452 80
  }
}
function Strip($name, $list) {
  $bmp, $g = Canvas ($BW * 4) $BH
  for ($i = 0; $i -lt 4; $i++) {
    $x = $i * $BW
    $g.SetClip((New-Object System.Drawing.Rectangle $x, 0, $BW, $BH))
    & $boards[$list[$i]] $g $x
    $g.ResetClip()
    # the board's frame
    $pen = New-Object System.Drawing.Pen (C '#1a1c20'), 5
    $g.DrawRectangle($pen, ($x + 2), 2, ($BW - 5), ($BH - 5))
  }
  $bmp.Save((Join-Path $Out "$name.png"), [System.Drawing.Imaging.ImageFormat]::Png); $g.Dispose(); $bmp.Dispose(); "strip $name"
}
Strip 'ads_a' @('kickfuel', 'soccermod', 'goalcrest', 'topcorner')
Strip 'ads_b' @('voltwave', 'pitchline', 'fairplay', 'respect')

# ---- video wall 2048 x 512 (800 x 200 units): logo, score panel for the plugin's numbers, matchday panel ----
$bmp, $g = Canvas 2048 512
$g.FillRectangle((Grad 0 0 2048 512 '#0b1436' '#050a1c'), 0, 0, 2048, 512)
# diagonal light bands
for ($k = 0; $k -lt 9; $k++) { $x = -200 + $k * 290; Poly $g (New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(16, 120, 170, 255))) @(0, 512, 120, 512, 420, 0, 300, 0) $x 0 }
# left: arena logo
Ball $g 150 196 96 '#ffffff' '#0b1436'
FitText $g 'SOCCERMOD' 'Bahnschrift SemiBold Condensed' 'Regular' (Solid '#ffffff') 270 96 390 130
FitText $g 'ARENA' 'Bahnschrift SemiBold Condensed' 'Regular' (Solid '#5fb4ff') 270 214 390 110
$g.FillRectangle((Solid '#5fb4ff'), 60, 366, 600, 6)
FitText $g 'MATCHDAY LIVE' 'Bahnschrift SemiBold' 'Regular' (Solid '#c9d6f2') 60 392 600 60
# centre: two score boxes (red left, blue right) - the plugin writes the numbers into them
foreach ($t in @(@(695, '#e2262e', '#7d0f14', 'HOME'), @(1053, '#2a62f0', '#0f2c86', 'AWAY'))) {
  $x = $t[0]
  $path = New-Object System.Drawing.Drawing2D.GraphicsPath
  $r = 26; $w = 300; $h = 372; $y = 70
  $path.AddArc($x, $y, $r, $r, 180, 90); $path.AddArc($x + $w - $r, $y, $r, $r, 270, 90); $path.AddArc($x + $w - $r, $y + $h - $r, $r, $r, 0, 90); $path.AddArc($x, $y + $h - $r, $r, $r, 90, 90); $path.CloseFigure()
  $g.FillPath((Solid '#04060c'), $path)
  $g.SetClip($path)
  $g.FillRectangle((Grad $x $y $w 78 $t[1] $t[2]), $x, $y, $w, 78)
  $g.ResetClip()
  $g.DrawPath((New-Object System.Drawing.Pen (C $t[1]), 5), $path)
  FitText $g $t[3] 'Bahnschrift SemiBold' 'Regular' (Solid '#ffffff') ($x + 30) ($y + 12) ($w - 60) 56
}
$g.FillEllipse((Solid '#ffffff'), 1013, 257, 22, 22); $g.FillEllipse((Solid '#ffffff'), 1013, 311, 22, 22)
# right: sponsor panel
$g.FillRectangle((Grad 1388 70 600 372 '#161616' '#060606'), 1388, 70, 600, 372)
Poly $g (Grad 1388 70 200 372 '#ffd21a' '#ff7a00') @(62, 0, 18, 92, 50, 92, 30, 170, 104, 62, 70, 62, 94, 0) 1416 118 1.5
FitText $g 'KICKFUEL' 'Impact' 'Regular' (Solid '#ffffff') 1590 150 370 150 -0.18
FitText $g 'OFFICIAL ENERGY PARTNER' 'Bahnschrift SemiBold Condensed' 'Regular' (Solid '#ffd21a') 1590 318 370 44
$g.DrawRectangle((New-Object System.Drawing.Pen (C '#2b3a66'), 8), 4, 4, 2040, 504)
$bmp.Save((Join-Path $Out 'videowall.png'), [System.Drawing.Imaging.ImageFormat]::Png); $g.Dispose(); $bmp.Dispose(); 'videowall'

# ---- tunnel sign 512 x 64 (160 x 16 units) -----------------------------------------------------------
$bmp, $g = Canvas 512 64
$g.FillRectangle((Grad 0 0 512 64 '#1b1e24' '#0e1014'), 0, 0, 512, 64)
$g.FillRectangle((Solid '#e2262e'), 0, 0, 512, 5); $g.FillRectangle((Solid '#2a62f0'), 0, 59, 512, 5)
FitText $g 'PLAYERS  TUNNEL' 'Bahnschrift SemiBold Condensed' 'Regular' (Solid '#ffffff') 60 12 392 42
$bmp.Save((Join-Path $Out 'tunnel_trim.png'), [System.Drawing.Imaging.ImageFormat]::Png); $g.Dispose(); $bmp.Dispose(); 'tunnel_trim'
