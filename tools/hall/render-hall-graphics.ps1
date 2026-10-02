# SoccerMod indoor hall: the pictures with lettering (scoreboard screens, the mural on the east
# wall, the lounge sign). Fantasy names only. The advert strips come from the stadium's set
# (tools/arena/render-arena-graphics.ps1: ads_a.png, ads_b.png).
#
#   powershell -ExecutionPolicy Bypass -File tools/hall/render-hall-graphics.ps1 <outDir>
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

# ---- scoreboard screen: 1024 x 384 ------------------------------------------------------------------
$bmp, $g = Canvas 1024 384
$g.FillRectangle((Grad 0 0 1024 384 '#0a1020' '#04060c'), 0, 0, 1024, 384)
$g.FillRectangle((Grad 0 0 512 384 '#b51f24' '#5e0d10' $false), 0, 74, 300, 236)
$g.FillRectangle((Grad 724 0 300 384 '#10307e' '#2458d8' $false), 724, 74, 300, 236)
$g.FillRectangle((Solid '#ffffff'), 0, 70, 1024, 4); $g.FillRectangle((Solid '#ffffff'), 0, 310, 1024, 4)
FitText $g 'KA SOCCERMOD  -  INDOOR ARENA' 'Bahnschrift SemiBold' 'Regular' (Solid '#e8ecf4') 40 14 944 44
FitText $g 'HOME' 'Impact' 'Regular' (Solid '#ffffff') 30 120 240 140
FitText $g 'AWAY' 'Impact' 'Regular' (Solid '#ffffff') 754 120 240 140
FitText $g 'VS' 'Impact' 'Regular' (Solid '#ffd21a') 380 100 264 184
FitText $g 'MATCH DAY' 'Bahnschrift SemiBold' 'Regular' (Solid '#8fd3ff') 380 322 264 46
Ball $g 70 345 20 '#ffffff' '#10141c'; Ball $g 954 345 20 '#ffffff' '#10141c'
Save $bmp $g 'hall_video'

# ---- mural for the east wall: 2048 x 512 ---------------------------------------------------------------
$bmp, $g = Canvas 2048 512
$g.FillRectangle((Grad 0 0 2048 512 '#16181d' '#0d0e12'), 0, 0, 2048, 512)
Poly $g (Grad 0 0 2048 512 '#c8242a' '#7d1216' $false) @(0, 0, 520, 0, 300, 512, 0, 512)
Poly $g (Grad 0 0 2048 512 '#1a3fae' '#2a62e6' $false) @(1748, 0, 2048, 0, 2048, 512, 1528, 512)
Poly $g (Solid '#f4f4f0') @(520, 0, 560, 0, 340, 512, 300, 512)
Poly $g (Solid '#f4f4f0') @(1708, 0, 1748, 0, 1528, 512, 1488, 512)
Ball $g 420 256 150 '#f6f6f2' '#14161b'
FitText $g 'SOCCERMOD' 'Impact' 'Regular' (Solid '#f6f6f2') 620 60 880 250 -0.14
FitText $g 'INDOOR  ARENA' 'Bahnschrift SemiBold' 'Regular' (Solid '#ffd21a') 640 318 840 110
$g.FillRectangle((Solid '#ffd21a'), 640, 300, 840, 8)
Save $bmp $g 'hall_mural'

# ---- lounge sign: 512 x 64 ----------------------------------------------------------------------------------
$bmp, $g = Canvas 512 64
$g.FillRectangle((Solid '#0c0d10'), 0, 0, 512, 64)
FitText $g 'SKY  LOUNGE' 'Bahnschrift SemiBold' 'Regular' (Solid '#ffe6b0') 20 6 472 52
Save $bmp $g 'hall_sign'

# ---- team banners: 256 x 512 each -----------------------------------------------------------------------------
foreach ($t in @(@('red', '#c8242a', '#8a1418', 'HOME'), @('blue', '#2a62e6', '#16389e', 'AWAY'))) {
  $bmp, $g = Canvas 256 512
  $g.FillRectangle((Grad 0 0 256 512 $t[1] $t[2]), 0, 0, 256, 512)
  $g.FillRectangle((Solid '#f4f4f0'), 0, 0, 256, 22); $g.FillRectangle((Solid '#f4f4f0'), 0, 150, 256, 10); $g.FillRectangle((Solid '#f4f4f0'), 0, 372, 256, 10)
  Ball $g 128 262 78 '#f6f6f2' $t[2]
  FitText $g $t[3] 'Impact' 'Regular' (Solid '#f6f6f2') 20 34 216 104
  FitText $g 'SOCCERMOD' 'Bahnschrift SemiBold' 'Regular' (Solid '#f6f6f2') 20 396 216 60
  Poly $g (Solid '#101216') @(0, 512, 128, 470, 256, 512)
  Save $bmp $g "hall_banner_$($t[0])"
}
