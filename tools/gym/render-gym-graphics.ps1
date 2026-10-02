# SoccerMod gym: the pictures with lettering (kick boards, scoreboard, the hall's sign, the mural
# on the east wall, the team banners). Fantasy names only.
#
#   powershell -ExecutionPolicy Bypass -File tools/gym/render-gym-graphics.ps1 <outDir>
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
function Poly($g, $brush, $pts) {
  $p = @(); for ($i = 0; $i -lt $pts.Count; $i += 2) { $p += New-Object System.Drawing.PointF ([single]$pts[$i]), ([single]$pts[$i + 1]) }
  $g.FillPolygon($brush, [System.Drawing.PointF[]]$p)
}
function Ball($g, $cx, $cy, $r, $light, $dark) {
  $g.FillEllipse((Solid $light), [single]($cx - $r), [single]($cy - $r), [single](2 * $r), [single](2 * $r))
  $pts = @(); for ($i = 0; $i -lt 5; $i++) { $a = -[Math]::PI / 2 + $i * 2 * [Math]::PI / 5; $pts += $cx + $r * 0.36 * [Math]::Cos($a); $pts += $cy + $r * 0.36 * [Math]::Sin($a) }
  Poly $g (Solid $dark) $pts
  for ($i = 0; $i -lt 5; $i++) {
    $a = -[Math]::PI / 2 + $i * 2 * [Math]::PI / 5; $x = $cx + $r * 0.86 * [Math]::Cos($a); $y = $cy + $r * 0.86 * [Math]::Sin($a)
    $g.FillEllipse((Solid $dark), [single]($x - $r * 0.2), [single]($y - $r * 0.2), [single]($r * 0.4), [single]($r * 0.4))
  }
}
function Save($bmp, $g, $name) { $g.Dispose(); $bmp.Save((Join-Path $Out "$name.png"), [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose(); Write-Host "$name.png" }

# ---- kick board: 1536 x 256 = 240 x 40 units, two panels of 120 -------------------------------------
$bmp, $g = Canvas 1536 256
$g.FillRectangle((Grad 0 0 1536 256 '#262b35' '#1b1f27'), 0, 0, 1536, 256)
$g.FillRectangle((Solid '#f2c21a'), 0, 0, 1536, 14)
$g.FillRectangle((Solid '#11141a'), 0, 236, 1536, 20)
Ball $g 96 132 62 '#eef0f2' '#1b1f27'
FitText $g 'SOCCERMOD' 'Impact' 'Regular' (Solid '#eef0f2') 190 48 520 170 -0.12
Poly $g (Solid '#c8242a') @(820, 44, 1010, 44, 970, 220, 780, 220)
Poly $g (Solid '#2a62e6') @(1030, 44, 1220, 44, 1180, 220, 990, 220)
FitText $g '2' 'Impact' 'Regular' (Solid '#ffffff') 810 56 170 150
FitText $g '2' 'Impact' 'Regular' (Solid '#ffffff') 1020 56 170 150
FitText $g 'vs' 'Bahnschrift SemiBold' 'Regular' (Solid '#f2c21a') 968 96 64 70
FitText $g 'STREET CAGE' 'Bahnschrift SemiBold' 'Regular' (Solid '#aeb6c4') 1240 84 270 96
Save $bmp $g 'gym_kickboard'

# ---- scoreboard: 1024 x 352 -----------------------------------------------------------------------------
$bmp, $g = Canvas 1024 352
$g.FillRectangle((Solid '#07080b'), 0, 0, 1024, 352)
$g.FillRectangle((Grad 0 0 1024 60 '#1a1d25' '#0d0f14'), 0, 0, 1024, 60)
FitText $g 'KA SOCCERMOD  -  2 vs 2' 'Bahnschrift SemiBold' 'Regular' (Solid '#dfe5f0') 30 10 964 40
FitText $g 'RED' 'Bahnschrift SemiBold' 'Regular' (Solid '#ff5a52') 40 76 300 56
FitText $g 'BLUE' 'Bahnschrift SemiBold' 'Regular' (Solid '#5a9cff') 684 76 300 56
FitText $g '0' 'Consolas' 'Bold' (Solid '#ff3a30') 60 130 260 210
FitText $g '0' 'Consolas' 'Bold' (Solid '#3a86ff') 704 130 260 210
FitText $g '10:00' 'Consolas' 'Bold' (Solid '#ffd21a') 352 150 320 130
FitText $g 'TIME' 'Bahnschrift SemiBold' 'Regular' (Solid '#9aa3b2') 412 92 200 40
$g.FillRectangle((Solid '#2a2e38'), 344, 70, 3, 270); $g.FillRectangle((Solid '#2a2e38'), 677, 70, 3, 270)
Save $bmp $g 'gym_scoreboard'

# ---- the hall's sign: 1024 x 352 ------------------------------------------------------------------------
$bmp, $g = Canvas 1024 352
$g.FillRectangle((Grad 0 0 1024 352 '#f4f2ea' '#e6e3d8'), 0, 0, 1024, 352)
$g.FillRectangle((Solid '#1b1f27'), 0, 0, 1024, 16); $g.FillRectangle((Solid '#1b1f27'), 0, 336, 1024, 16)
Ball $g 150 176 104 '#1b1f27' '#f4f2ea'
FitText $g 'SOCCERMOD' 'Impact' 'Regular' (Solid '#1b1f27') 290 44 690 170 -0.12
$g.FillRectangle((Solid '#c8242a'), 290, 218, 340, 12); $g.FillRectangle((Solid '#2a62e6'), 640, 218, 340, 12)
FitText $g 'STREET CAGE  2 vs 2' 'Bahnschrift SemiBold' 'Regular' (Solid '#3a4150') 290 240 690 74
Save $bmp $g 'gym_sign'

# ---- mural for the east wall: 2048 x 400 ------------------------------------------------------------------
$bmp, $g = Canvas 2048 400
$g.FillRectangle((Solid '#e8e7e0'), 0, 0, 2048, 400)
Poly $g (Grad 0 0 2048 400 '#c8242a' '#9c181d' $false) @(0, 60, 760, 60, 640, 340, 0, 340)
Poly $g (Grad 0 0 2048 400 '#1d47c0' '#2a62e6' $false) @(1408, 60, 2048, 60, 2048, 340, 1288, 340)
Poly $g (Solid '#1b1f27') @(760, 60, 800, 60, 680, 340, 640, 340)
Poly $g (Solid '#1b1f27') @(1368, 60, 1408, 60, 1288, 340, 1248, 340)
Ball $g 1024 200 150 '#1b1f27' '#e8e7e0'
FitText $g 'PLAY' 'Impact' 'Regular' (Solid '#ffffff') 60 90 520 220 -0.14
FitText $g 'HARD' 'Impact' 'Regular' (Solid '#ffffff') 1470 90 520 220 -0.14
Save $bmp $g 'gym_mural'

# ---- emblem in the centre circle: 1024 x 1024 (the floor cuts it round) ---------------------------------
function RingText($g, $s, $fontName, $size, $brush, $cx, $cy, $r, $startDeg, $stepDeg) {
  $fmt = [System.Drawing.StringFormat]::GenericTypographic; $f = Font $fontName $size 'Regular'
  for ($i = 0; $i -lt $s.Length; $i++) {
    $ch = $s.Substring($i, 1); if ($ch -eq ' ') { continue }
    $m = $g.MeasureString($ch, $f, 900, $fmt); $state = $g.Save()
    $g.TranslateTransform([single]$cx, [single]$cy); $g.RotateTransform([single]($startDeg + $i * $stepDeg))
    $g.DrawString($ch, $f, $brush, [single](-$m.Width / 2), [single](-$r), $fmt)
    $g.Restore($state)
  }
}
$bmp, $g = Canvas 1024 1024
$g.FillRectangle((Solid '#1b1f27'), 0, 0, 1024, 1024)
$g.FillEllipse((Solid '#f2c21a'), 22, 22, 980, 980); $g.FillEllipse((Solid '#1b1f27'), 40, 40, 944, 944)
$g.FillPie((Solid '#c8242a'), 196, 196, 632, 632, 180, 180); $g.FillPie((Solid '#2a62e6'), 196, 196, 632, 632, 0, 180)
$g.FillEllipse((Solid '#1b1f27'), 226, 226, 572, 572)
Ball $g 512 512 236 '#eef0f2' '#1b1f27'
$top = 'KA  SOCCERMOD'; RingText $g $top 'Impact' 120 (Solid '#eef0f2') 512 512 462 (-($top.Length - 1) * 5.6) 11.2
$low = 'STREET CAGE  2 VS 2'; RingText $g $low 'Bahnschrift SemiBold' 78 (Solid '#f2c21a') 512 512 446 (180 - ($low.Length - 1) * 3.6) 7.2
Save $bmp $g 'gym_emblem'

# ---- team banners: 512 x 280 each ------------------------------------------------------------------------------
foreach ($t in @(@('red', '#c8242a', '#8a1418', 'RED'), @('blue', '#2a62e6', '#16389e', 'BLUE'))) {
  $bmp, $g = Canvas 512 280
  $g.FillRectangle((Grad 0 0 512 280 $t[1] $t[2]), 0, 0, 512, 280)
  $g.FillRectangle((Solid '#f4f4f0'), 0, 0, 512, 12); $g.FillRectangle((Solid '#f4f4f0'), 0, 268, 512, 12)
  Ball $g 96 140 70 '#f6f6f2' $t[2]
  FitText $g $t[3] 'Impact' 'Regular' (Solid '#f6f6f2') 190 34 300 140 -0.12
  FitText $g 'HOME OF THE CAGE' 'Bahnschrift SemiBold' 'Regular' (Solid '#f6f6f2') 190 186 300 50
  Save $bmp $g "gym_banner_$($t[0])"
}
