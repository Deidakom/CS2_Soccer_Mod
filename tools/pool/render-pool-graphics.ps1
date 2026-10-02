# SoccerMod 1v1 cage (the drained pool): the pictures with lettering - the old bathhouse's signs, the
# row of changing cabins, the name over the mosaic, the clock, the players' chalk scoreboard, the
# starting blocks' numbers. Fantasy names only.
#
#   powershell -ExecutionPolicy Bypass -File tools/pool/render-pool-graphics.ps1 <outDir>
param([Parameter(Mandatory = $true)][string]$Out)
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force $Out | Out-Null

function C($hex, $alpha = 255) { $c = [System.Drawing.ColorTranslator]::FromHtml($hex); [System.Drawing.Color]::FromArgb([int]$alpha, $c.R, $c.G, $c.B) }
function Solid($hex, $alpha = 255) { New-Object System.Drawing.SolidBrush (C $hex $alpha) }
function PenOf($hex, $w, $alpha = 255) { $p = New-Object System.Drawing.Pen (C $hex $alpha), ([single]$w); $p.LineJoin = 'Round'; $p.StartCap = 'Round'; $p.EndCap = 'Round'; $p }
function Font($name, $size, $style = 'Regular') { New-Object System.Drawing.Font $name, ([single]$size), ([System.Drawing.FontStyle]$style), ([System.Drawing.GraphicsUnit]::Pixel) }
function Canvas($w, $h, $bg = $null) {
  $bmp = New-Object System.Drawing.Bitmap ([int]$w), ([int]$h), ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = 'AntiAlias'; $g.TextRenderingHint = 'AntiAlias'; $g.InterpolationMode = 'HighQualityBicubic'
  if ($bg) { $g.Clear((C $bg)) } else { $g.Clear([System.Drawing.Color]::FromArgb(0, 0, 0, 0)) }
  return $bmp, $g
}
function FitText($g, $s, $fontName, $style, $brush, $x, $y, $w, $h) {
  $fmt = [System.Drawing.StringFormat]::GenericTypographic; $size = $h
  do { $f = Font $fontName $size $style; $m = $g.MeasureString($s, $f, 9000, $fmt); if ($m.Width -le $w) { break }; $size = $size * 0.94 } while ($size -gt 6)
  $g.DrawString($s, $f, $brush, [single]($x + ($w - $m.Width) / 2), [single]($y + ($h - $m.Height) / 2), $fmt)
}
function Save($bmp, $g, $name) { $g.Dispose(); $bmp.Save((Join-Path $Out "$name.png"), [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose(); Write-Host -NoNewline "$name " }
# an enamel sign: a plate with a rim, rust at the screws
function Plate($w, $h, $bg, $rim) { $bmp, $g = Canvas $w $h $bg; $g.DrawRectangle((PenOf $rim 10), 8, 8, ($w - 16), ($h - 16)); foreach ($p in @(@(22, 22), @(($w - 22), 22), @(22, ($h - 22)), @(($w - 22), ($h - 22)))) { $g.FillEllipse((Solid '#7a4a2a' 150), ($p[0] - 12), ($p[1] - 9), 24, 30); $g.FillEllipse((Solid '#b8b0a0'), ($p[0] - 6), ($p[1] - 6), 12, 12) }; return $bmp, $g }

# ---- depth markings and warnings on the band under the pool's edge (4 : 1) -------------------------------
$bmp, $g = Plate 768 192 '#f2efe4' '#1d3f8a'; FitText $g '3.8 m' 'Arial Black' 'Regular' (Solid '#1d3f8a') 30 20 340 150; FitText $g 'DEEP END' 'Bahnschrift SemiBold' 'Regular' (Solid '#c8242a') 390 54 350 90; Save $bmp $g 'sign_depth_deep'
$bmp, $g = Plate 768 192 '#f2efe4' '#1d3f8a'; FitText $g '3.2 m' 'Arial Black' 'Regular' (Solid '#1d3f8a') 30 20 340 150; FitText $g 'LANE 1 - 5' 'Bahnschrift SemiBold' 'Regular' (Solid '#1d3f8a') 390 54 350 90; Save $bmp $g 'sign_depth_shallow'
$bmp, $g = Plate 768 192 '#f2efe4' '#c8242a'
$g.DrawEllipse((PenOf '#c8242a' 14), 40, 26, 140, 140); $g.DrawLine((PenOf '#c8242a' 14), 62, 48, 158, 144); $g.FillEllipse((Solid '#16181c'), 88, 50, 26, 26); $g.DrawLine((PenOf '#16181c' 14), 104, 82, 128, 126); $g.DrawLine((PenOf '#16181c' 12), 128, 126, 150, 104)
FitText $g 'NO DIVING' 'Arial Black' 'Regular' (Solid '#c8242a') 210 40 530 110; Save $bmp $g 'sign_nodiving'

# ---- the big old signs on the end wall ----------------------------------------------------------------
$bmp, $g = Plate 1408 448 '#1d3f8a' '#f2efe4'; FitText $g 'DEEP END' 'Arial Black' 'Regular' (Solid '#f2efe4') 60 40 1288 230; FitText $g 'DEPTH 3.8 m  -  SWIMMERS ONLY' 'Bahnschrift SemiBold' 'Regular' (Solid '#ffd21a') 60 290 1288 110; Save $bmp $g 'sign_deep'
$bmp, $g = Plate 768 576 '#f2efe4' '#1d3f8a'; FitText $g 'POOL RULES' 'Arial Black' 'Regular' (Solid '#1d3f8a') 50 34 668 96
$rules = @('1  shower before bathing', '2  no running on the deck', '3  no ball games', '4  no jumping from the side', '5  children with adults only'); for ($i = 0; $i -lt $rules.Count; $i++) { FitText $g $rules[$i] 'Bahnschrift SemiBold' 'Regular' (Solid '#16181c') 70 (150 + $i * 78) 628 60 }
$g.DrawLine((PenOf '#c8242a' 12), 60, 338, 520, 330); $pen = PenOf '#c8242a' 9; $g.DrawString('says who?', (Font 'Segoe Script' 62 'Bold'), (Solid '#c8242a'), 420, 356)
Save $bmp $g 'sign_rules'

# ---- the bathhouse's name over the mosaic: letters of gold tiles on blue (the texture script cuts it into tiles)
$bmp, $g = Canvas 2080 200 '#16407a'; FitText $g 'CITY BATHS   -   EST. 1926' 'Georgia' 'Bold' (Solid '#e8c05a') 60 30 1960 140; Save $bmp $g 'sign_name'

# ---- the clock: 512 x 512, round (transparent outside) -------------------------------------------------
$bmp, $g = Canvas 512 512
$g.FillEllipse((Solid '#2a2e36'), 6, 6, 500, 500); $g.FillEllipse((Solid '#f2efe4'), 30, 30, 452, 452)
for ($i = 0; $i -lt 60; $i++) { $a = $i * [Math]::PI / 30; $r0 = if ($i % 5) { 206 } else { 184 }; $g.DrawLine((PenOf '#16181c' $(if ($i % 5) { 3 } else { 9 })), [single](256 + $r0 * [Math]::Sin($a)), [single](256 - $r0 * [Math]::Cos($a)), [single](256 + 218 * [Math]::Sin($a)), [single](256 - 218 * [Math]::Cos($a))) }
foreach ($n in @(12, 3, 6, 9)) { $a = $n * [Math]::PI / 6; FitText $g "$n" 'Georgia' 'Bold' (Solid '#16181c') (256 + 150 * [Math]::Sin($a) - 40) (256 - 150 * [Math]::Cos($a) - 34) 80 68 }
$g.DrawLine((PenOf '#16181c' 14), 256, 256, 330, 180); $g.DrawLine((PenOf '#16181c' 9), 256, 256, 196, 96); $g.DrawLine((PenOf '#c8242a' 4), 256, 256, 300, 420); $g.FillEllipse((Solid '#16181c'), 242, 242, 28, 28)
Save $bmp $g 'clock'

# ---- the changing cabins: six doors, 2048 x 320 = 720 x 112 units ---------------------------------------
$bmp, $g = Canvas 2048 320 '#e9e6da'
for ($i = 0; $i -lt 6; $i++) {
  $x = $i * 341.33 + 20
  $g.FillRectangle((Solid '#c9c4b2'), [single]($x - 20), 0, 14, 320); $g.FillRectangle((Solid '#3f8f84'), [single]$x, 26, 300, 294); $g.FillRectangle((Solid '#000000' 40), [single]$x, 26, 300, 10)
  $g.DrawRectangle((PenOf '#2f6f66' 6), [single]($x + 26), 60, 248, 110); $g.DrawRectangle((PenOf '#2f6f66' 6), [single]($x + 26), 190, 248, 110)
  for ($k = 0; $k -lt 5; $k++) { $g.FillRectangle((Solid '#1f4f48'), [single]($x + 60), [single](74 + $k * 9), 180, 4) }
  $g.FillEllipse((Solid '#f2efe4'), [single]($x + 118), 96, 64, 64); FitText $g "$($i + 1)" 'Georgia' 'Bold' (Solid '#16181c') ($x + 118) 100 64 56
  $g.FillEllipse((Solid '#d8c070'), [single]($x + 262), 176, 16, 16)
  if ($i -eq 1) { $g.DrawString('KA', (Font 'Segoe Script' 70 'Bold'), (Solid '#ff2e63' 230), [single]($x + 60), 200) }
  if ($i -eq 4) { $g.DrawString('1v1', (Font 'Segoe Script' 64 'Bold'), (Solid '#16181c' 220), [single]($x + 70), 206) }
}
$g.FillRectangle((Solid '#b8b29e'), 0, 0, 2048, 26)
Save $bmp $g 'cabins'

# ---- the players' scoreboard: a board with chalk writing ------------------------------------------------
$bmp, $g = Canvas 608 384 '#1f2a26'
$g.DrawRectangle((PenOf '#8a6a44' 22), 11, 11, 586, 362)
$chalk = Solid '#f2efe4' 235; $g.DrawString('HOME', (Font 'Segoe Script' 62 'Bold'), $chalk, 44, 40); $g.DrawString('AWAY', (Font 'Segoe Script' 62 'Bold'), $chalk, 340, 40)
$g.DrawLine((PenOf '#f2efe4' 6 235), 304, 40, 300, 340); $g.DrawLine((PenOf '#f2efe4' 5 235), 40, 124, 568, 118)
foreach ($t in @(@(70, 4), @(366, 3))) { for ($k = 0; $k -lt $t[1]; $k++) { $g.DrawLine((PenOf '#f2efe4' 7 235), [single]($t[0] + $k * 30), 160, [single]($t[0] + $k * 30 + 6), 250) } }
$g.DrawString('first to 5', (Font 'Segoe Script' 46 'Bold'), (Solid '#ffd21a' 230), 150, 280)
Save $bmp $g 'scoreboard'

# ---- the starting blocks' tops: five numbers in a row --------------------------------------------------
$bmp, $g = Canvas 640 128 '#1d3f8a'
for ($i = 0; $i -lt 5; $i++) { $g.FillRectangle((Solid '#f2efe4'), ($i * 128 + 14), 14, 100, 100); FitText $g "$($i + 1)" 'Arial Black' 'Regular' (Solid '#1d3f8a') ($i * 128 + 14) 18 100 92 }
Save $bmp $g 'block_top'
Write-Host ''
