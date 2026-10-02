# Brand boards (owner 2026-10-02: "can we add real brands as well, such as Coca Cola,
# Nike, Adidas ... mix them in"; see tools/brands/README.md). Board pages in the format of
# tools/atmo/render-board-pages.ps1 (1280 x 200, the unit twice per page): each brand's NAME in its
# colours and a similar lettering, drawn here with fonts that are on every Windows PC - not the
# companies' logo drawings.
#
#   powershell -ExecutionPolicy Bypass -File tools/brands/render-brand-pages.ps1 <outDir>
param([Parameter(Mandatory = $true)][string]$Out)
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force $Out | Out-Null
$W = 1280; $H = 200

function C($hex) { [System.Drawing.ColorTranslator]::FromHtml($hex) }
function Solid($hex) { New-Object System.Drawing.SolidBrush (C $hex) }
function Grad($a, $b) { New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.Rectangle 0, 0, $W, $H), (C $a), (C $b), ([System.Drawing.Drawing2D.LinearGradientMode]::Vertical) }
function Font($name, $size, $style = 'Regular') { New-Object System.Drawing.Font $name, $size, ([System.Drawing.FontStyle]$style), ([System.Drawing.GraphicsUnit]::Pixel) }
function Text($g, $s, $font, $brush, $x, $y) {
  $size = $g.MeasureString($s, $font, 5000, [System.Drawing.StringFormat]::GenericTypographic)
  $g.DrawString($s, $font, $brush, [single]$x, [single]$y, [System.Drawing.StringFormat]::GenericTypographic)
  return $x + $size.Width
}
function Skew($g, $k) { $m = New-Object System.Drawing.Drawing2D.Matrix 1, 0, ([single]$k), 1, 0, 0; $g.MultiplyTransform($m) }
function Poly($g, $brush, $pts) {
  $p = @(); for ($i = 0; $i -lt $pts.Count; $i += 2) { $p += New-Object System.Drawing.PointF ([single]$pts[$i]), ([single]$pts[$i + 1]) }
  $g.FillPolygon($brush, [System.Drawing.PointF[]]$p)
}
# The unit is drawn once on a wide transparent canvas, its bounding box found, then fitted centred
# into each of the two slots (margins 8 % left / right, 12 % top / bottom).
function Page($name, $background, [scriptblock]$unit, [int]$repeat = 2) {
  $bmp = New-Object System.Drawing.Bitmap $W, $H
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = 'AntiAlias'; $g.InterpolationMode = 'HighQualityBicubic'
  $g.FillRectangle($background, 0, 0, $W, $H)
  $u = New-Object System.Drawing.Bitmap 2400, 400
  $ug = [System.Drawing.Graphics]::FromImage($u)
  $ug.SmoothingMode = 'AntiAlias'; $ug.TextRenderingHint = 'AntiAliasGridFit'
  & $unit $ug
  $ug.Dispose()
  $minX = $u.Width; $maxX = 0; $minY = $u.Height; $maxY = 0
  $rect = New-Object System.Drawing.Rectangle 0, 0, $u.Width, $u.Height
  $data = $u.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $bytes = New-Object byte[] ($data.Stride * $u.Height)
  [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $bytes, 0, $bytes.Length)
  $u.UnlockBits($data)
  for ($y = 0; $y -lt $u.Height; $y += 2) { $row = $y * $data.Stride; for ($x = 0; $x -lt $u.Width; $x += 2) { if ($bytes[$row + $x * 4 + 3] -gt 20) { if ($x -lt $minX) { $minX = $x }; if ($x -gt $maxX) { $maxX = $x }; if ($y -lt $minY) { $minY = $y }; if ($y -gt $maxY) { $maxY = $y } } } }
  $bw = [Math]::Max(1, $maxX - $minX + 2); $bh = [Math]::Max(1, $maxY - $minY + 2)
  $slot = $W / $repeat
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

# white script on red, with the sweeping underline
Page "cocacola" (Grad '#e8111f' '#c40812') {
  param($g)
  $null = Text $g 'Coca-Cola' (Font 'Segoe Script' 190 'Bold') (Solid '#ffffff') 40 20
  $pen = New-Object System.Drawing.Pen (C '#ffffff'), 12; $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
  $g.DrawBezier($pen, 60, 300, 380, 250, 700, 330, 1040, 262)
}
# white heavy slanted capitals on black, a sweeping tick underneath
Page "nike" (Grad '#141414' '#050505') {
  param($g)
  $state = $g.Save(); Skew $g -0.22
  $null = Text $g 'NIKE' (Font 'Impact' 230) (Solid '#ffffff') 150 20
  $g.Restore($state)
  Poly $g (Solid '#ffffff') @(40, 300, 150, 372, 760, 258, 190, 334, 96, 282)
}
# white lower-case on black, three slanted bars in front
Page "adidas" (Grad '#101010' '#000000') {
  param($g)
  $white = Solid '#ffffff'
  for ($k = 0; $k -lt 3; $k++) { $x = 40 + $k * 78; $top = 250 - $k * 62; Poly $g $white @($x, 300, ($x + 46), 300, ($x + 46 + (300 - $top) * 0.42), $top, ($x + (300 - $top) * 0.42), $top) }
  $null = Text $g 'adidas' (Font 'Segoe UI Black' 230) $white 380 50
}
# black capitals on white, a red bar
Page "puma" (Grad '#ffffff' '#ececec') {
  param($g)
  $state = $g.Save(); Skew $g -0.08
  $right = Text $g 'PUMA' (Font 'Arial Black' 220) (Solid '#111111') 80 30
  $g.Restore($state)
  $g.FillRectangle((Solid '#d8141c'), 70, 300, [single]($right - 60), 22)
}
# white lower-case on blue, a red-white-blue ball
Page "pepsi" (Grad '#0a4fc4' '#06348a') {
  param($g)
  $g.FillEllipse((Solid '#ffffff'), 40, 60, 250, 250)
  $g.FillPie((Solid '#e11b2b'), 52, 72, 226, 226, 190, 180)
  $g.FillPie((Solid '#0a3fa8'), 52, 72, 226, 226, 10, 180)
  $pen = New-Object System.Drawing.Pen (C '#ffffff'), 34
  $g.DrawBezier($pen, 46, 205, 120, 150, 210, 230, 286, 165)
  $null = Text $g 'pepsi' (Font 'Bahnschrift SemiBold' 230) (Solid '#ffffff') 340 55
}
# white serif italics on red
Page "emirates" (Grad '#d71a21' '#b0121a') {
  param($g)
  $null = Text $g 'Fly Emirates' (Font 'Georgia' 190 'Bold, Italic') (Solid '#ffffff') 40 60
}
