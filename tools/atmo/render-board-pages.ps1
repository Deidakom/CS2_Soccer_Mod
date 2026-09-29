# "Arena Vision" LED board pages, owner 2026-09-29: "like real advertising boards" (his
# reference: big logo + wordmark on a coloured background, repeated along the board),
# mixed with text boards (PLAY FAIR ...). Fantasy brands only, logos drawn here as vector
# shapes. Renders each page at 1280 x 200 (4 x the 320 x 50 LED grid of one board);
# tools/atmo/generate-boards.mjs turns them into LED-pixel textures and transition frames.
#
#   powershell -ExecutionPolicy Bypass -File tools/atmo/render-board-pages.ps1 <outDir>
param([Parameter(Mandatory = $true)][string]$Out)
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force $Out | Out-Null
$W = 1280; $H = 200

function Brush($a, $b, $vertical = $true) {
  $rect = New-Object System.Drawing.Rectangle 0, 0, $W, $H
  $mode = if ($vertical) { [System.Drawing.Drawing2D.LinearGradientMode]::Vertical } else { [System.Drawing.Drawing2D.LinearGradientMode]::Horizontal }
  New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, $a, $b, $mode
}
function C($hex) { [System.Drawing.ColorTranslator]::FromHtml($hex) }
function Solid($hex) { New-Object System.Drawing.SolidBrush (C $hex) }
function Font($name, $size, $style = 'Regular') { New-Object System.Drawing.Font $name, $size, ([System.Drawing.FontStyle]$style), ([System.Drawing.GraphicsUnit]::Pixel) }
function Text($g, $s, $font, $brush, $x, $y) {
  # measure-free: draw with the top-left at (x, y) - returns the right edge
  $size = $g.MeasureString($s, $font, 5000, [System.Drawing.StringFormat]::GenericTypographic)
  $g.DrawString($s, $font, $brush, [single]$x, [single]$y, [System.Drawing.StringFormat]::GenericTypographic)
  return $x + $size.Width
}
function TextWidth($g, $s, $font) { $g.MeasureString($s, $font, 5000, [System.Drawing.StringFormat]::GenericTypographic).Width }
function Poly($g, $brush, $pts, $dx, $dy, $s = 1.0) {
  $p = @(); for ($i = 0; $i -lt $pts.Count; $i += 2) { $p += New-Object System.Drawing.PointF ([single]($dx + $pts[$i] * $s)), ([single]($dy + $pts[$i + 1] * $s)) }
  $g.FillPolygon($brush, [System.Drawing.PointF[]]$p)
}
function Star($g, $brush, $cx, $cy, $r) {
  $pts = @(); for ($i = 0; $i -lt 10; $i++) { $a = -[Math]::PI / 2 + $i * [Math]::PI / 5; $rr = if ($i % 2) { $r * 0.42 } else { $r }; $pts += $cx + $rr * [Math]::Cos($a); $pts += $cy + $rr * [Math]::Sin($a) }
  Poly $g $brush $pts 0 0
}
function Page($name, $background, [scriptblock]$unit, [int]$repeat = 2) {
  $bmp = New-Object System.Drawing.Bitmap $W, $H
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = 'AntiAlias'; $g.TextRenderingHint = 'AntiAliasGridFit'; $g.InterpolationMode = 'HighQualityBicubic'
  $g.FillRectangle($background, 0, 0, $W, $H)
  $slot = $W / $repeat
  # Draw the unit once on a wide transparent canvas, find its bounding box, then fit it
  # centred into every slot (margins: 8 % left/right, 12 % top/bottom) - no unit overflows.
  $u = New-Object System.Drawing.Bitmap 2400, $H
  $ug = [System.Drawing.Graphics]::FromImage($u)
  $ug.SmoothingMode = "AntiAlias"; $ug.TextRenderingHint = "AntiAliasGridFit"
  & $unit $ug $slot
  $ug.Dispose()
  $minX = $u.Width; $maxX = 0; $minY = $H; $maxY = 0
  for ($y = 0; $y -lt $H; $y += 2) { for ($x = 0; $x -lt $u.Width; $x += 2) { if ($u.GetPixel($x, $y).A -gt 20) { if ($x -lt $minX) { $minX = $x }; if ($x -gt $maxX) { $maxX = $x }; if ($y -lt $minY) { $minY = $y }; if ($y -gt $maxY) { $maxY = $y } } } }
  $bw = [Math]::Max(1, $maxX - $minX + 2); $bh = [Math]::Max(1, $maxY - $minY + 2)
  $scale = [Math]::Min(($slot * 0.84) / $bw, ($H * 0.76) / $bh)
  $dw = $bw * $scale; $dh = $bh * $scale
  for ($i = 0; $i -lt $repeat; $i++) {
    $dest = New-Object System.Drawing.RectangleF ([single]($i * $slot + ($slot - $dw) / 2)), ([single](($H - $dh) / 2)), ([single]$dw), ([single]$dh)
    $src = New-Object System.Drawing.RectangleF ([single]$minX), ([single]$minY), ([single]$bw), ([single]$bh)
    $g.DrawImage($u, $dest, $src, [System.Drawing.GraphicsUnit]::Pixel)
  }
  $u.Dispose()
  $bmp.Save((Join-Path $Out "$name.png"), [System.Drawing.Imaging.ImageFormat]::Png)
  $g.Dispose(); $bmp.Dispose(); "page $name"
}

# ---- brands -----------------------------------------------------------------------------
Page "kickfuel" (Brush (C '#111111') (C '#050505')) {
  param($g, $slot)
  $bolt = Brush (C '#ffd21a') (C '#ff7a00')
  Poly $g $bolt @(62, 0, 18, 92, 50, 92, 30, 170, 104, 62, 70, 62, 94, 0) 60 15
  $f = Font 'Impact' 132
  $g.TranslateTransform(0, 0); $m = New-Object System.Drawing.Drawing2D.Matrix 1, 0, -0.18, 1, 30, 0; $g.MultiplyTransform($m)
  $null = Text $g 'KICKFUEL' $f (Solid '#ffffff') 205 22
}
Page "voltwave" (Brush (C '#1b4fe0') (C '#0f35b5')) {
  param($g, $slot)
  $pen = New-Object System.Drawing.Pen (C '#ffffff'), 13; $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
  for ($k = 0; $k -lt 3; $k++) { $y = 62 + $k * 36; $g.DrawBezier($pen, 70, $y, 105, $y - 30, 135, $y + 30, 170, $y) ; $g.DrawBezier($pen, 170, $y, 190, $y - 18, 205, $y - 12, 218, $y - 4) }
  $null = Text $g 'voltwave' (Font 'Bahnschrift SemiBold' 128) (Solid '#ffffff') 250 22
}
Page "goalcrest" (Brush (C '#0f7a3c') (C '#085a2b')) {
  param($g, $slot)
  $gold = Brush (C '#ffe08a') (C '#c9971c')
  Poly $g $gold @(0, 0, 110, 0, 110, 70, 55, 130, 0, 70) 70 30
  Star $g (Solid '#085a2b') 125 80 34
  $right = Text $g 'GoalCrest' (Font 'Segoe UI Black' 116) (Solid '#ffffff') 205 18
  Star $g (Solid '#e3262e') ($right + 36) 70 30
}
Page "topcorner" (Brush (C '#e2141d') (C '#b30d15')) {
  param($g, $slot)
  $null = Text $g 'TopCorner' (Font 'Segoe Script' 118 'Bold') (Solid '#ffffff') 70 8
  $pen = New-Object System.Drawing.Pen (C '#ffffff'), 7
  $g.DrawBezier($pen, 90, 170, 250, 140, 420, 190, 590, 150)
}
Page "pitchline" (Brush (C '#ffffff') (C '#e9e9ee')) {
  param($g, $slot)
  $navy = Solid '#3d1f8f'
  Poly $g $navy @(0, 40, 70, 30, 105, 0, 118, 6, 98, 36, 150, 30, 160, 42, 98, 52, 118, 86, 104, 90, 70, 58, 0, 58) 50 60
  $f = Font 'Arial Black' 116
  $right = Text $g 'Pitch' $f $navy 235 20
  $null = Text $g 'Line' $f (Solid '#f26522') ($right - 8) 20
}
Page "soccermod" (Brush (C '#0a1760') (C '#1f47c2') $false) {
  param($g, $slot)
  $g.FillEllipse((Solid '#ffffff'), 70, 40, 120, 120)
  Poly $g (Solid '#0a1760') @(130, 72, 154, 90, 145, 118, 115, 118, 106, 90) 0 0
  foreach ($a in 0..4) { $r = [Math]::PI * 2 * $a / 5 - [Math]::PI / 2; $cx = 130 + 50 * [Math]::Cos($r); $cy = 100 + 50 * [Math]::Sin($r); $g.FillEllipse((Solid '#0a1760'), [single]($cx - 13), [single]($cy - 13), 26, 26) }
  $null = Text $g 'SOCCERMOD ARENA' (Font 'Bahnschrift SemiBold Condensed' 104) (Solid '#ffffff') 220 44
}
# ---- text boards (owner: mixed with the brands, banner - text - banner - text) -----------
Page "fairplay" (Brush (C '#101010') (C '#050505')) {
  param($g, $slot)
  $right = Text $g 'PLAY FAIR' (Font 'Bahnschrift SemiBold' 118) (Solid '#ffffff') 90 30
  $g.FillRectangle((Solid '#2fb84a'), [single]($right + 28), 70, 18, 60)
}
Page "respect" (Brush (C '#12245c') (C '#0a1740')) {
  param($g, $slot)
  $null = Text $g 'RESPECT THE GAME' (Font 'Bahnschrift SemiBold Condensed' 112) (Solid '#ffffff') 60 36
}
# ---- goal takeovers --------------------------------------------------------------------------
foreach ($t in @(@('goal_red', '#ef2b22', '#a8110c'), @('goal_blue', '#2f68ff', '#1034b8'))) {
  Page $t[0] (Brush (C $t[1]) (C $t[2])) {
    param($g, $slot)
    $null = Text $g 'GOAL!' (Font 'Impact' 150) (Solid '#ffffff') 50 12
  } 3
}
