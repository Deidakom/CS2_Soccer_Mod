# SoccerMod street arena: everything that is painted, sprayed or written - the graffiti pieces for
# the walls, the paintings on the ground, the tags, the shop fronts and signs. Fantasy names only.
# Pictures with a transparent background (PNG with alpha) are sprayed onto walls or laid on the
# ground by tools/street/generate-street-textures.mjs.
#
#   powershell -ExecutionPolicy Bypass -File tools/street/render-street-art.ps1 <outDir>
param([Parameter(Mandatory = $true)][string]$Out)
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force $Out | Out-Null
$script:rng = New-Object System.Random 20261002
function Rnd([double]$a, [double]$b) { $a + ($b - $a) * $script:rng.NextDouble() }
function C($hex, $alpha = 255) { $c = [System.Drawing.ColorTranslator]::FromHtml($hex); [System.Drawing.Color]::FromArgb([int]$alpha, $c.R, $c.G, $c.B) }
function Solid($hex, $alpha = 255) { New-Object System.Drawing.SolidBrush (C $hex $alpha) }
function PenOf($hex, $w, $alpha = 255) { $p = New-Object System.Drawing.Pen (C $hex $alpha), ([single]$w); $p.LineJoin = 'Round'; $p.StartCap = 'Round'; $p.EndCap = 'Round'; $p }
function Canvas($w, $h, $bg = $null) {
  $bmp = New-Object System.Drawing.Bitmap ([int]$w), ([int]$h), ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = 'AntiAlias'; $g.TextRenderingHint = 'AntiAlias'; $g.InterpolationMode = 'HighQualityBicubic'
  if ($bg) { $g.Clear((C $bg)) } else { $g.Clear([System.Drawing.Color]::FromArgb(0, 0, 0, 0)) }
  return $bmp, $g
}
function Save($bmp, $g, $name) { $g.Dispose(); $bmp.Save((Join-Path $Out "$name.png"), [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose(); Write-Host -NoNewline "$name " }
function Pts($list) { $p = @(); for ($i = 0; $i -lt $list.Count; $i += 2) { $p += New-Object System.Drawing.PointF ([single]$list[$i]), ([single]$list[$i + 1]) }; , [System.Drawing.PointF[]]$p }
function Poly($g, $brush, $list) { $g.FillPolygon($brush, (Pts $list)) }
function Star($g, $brush, $cx, $cy, $r, $n = 4, $inner = 0.32) { $l = @(); for ($i = 0; $i -lt 2 * $n; $i++) { $a = -[Math]::PI / 2 + $i * [Math]::PI / $n; $rr = if ($i % 2) { $r * $inner } else { $r }; $l += $cx + $rr * [Math]::Cos($a); $l += $cy + $rr * [Math]::Sin($a) }; Poly $g $brush $l }
function Spray($g, $hex, $cx, $cy, $rx, $ry, $n, $alpha = 70) { for ($i = 0; $i -lt $n; $i++) { $a = Rnd 0 6.283; $d = [Math]::Sqrt((Rnd 0 1)); $s = Rnd 1.5 6; $g.FillEllipse((Solid $hex $alpha), [single]($cx + [Math]::Cos($a) * $rx * $d - $s / 2), [single]($cy + [Math]::Sin($a) * $ry * $d - $s / 2), [single]$s, [single]$s) } }
function Ball($g, $cx, $cy, $r, $light = '#f4f4ee', $dark = '#16181c') {
  $g.FillEllipse((Solid $dark), [single]($cx - $r - 5), [single]($cy - $r - 5), [single](2 * $r + 10), [single](2 * $r + 10))
  $g.FillEllipse((Solid $light), [single]($cx - $r), [single]($cy - $r), [single](2 * $r), [single](2 * $r))
  $l = @(); for ($i = 0; $i -lt 5; $i++) { $a = -[Math]::PI / 2 + $i * 2 * [Math]::PI / 5; $l += $cx + $r * 0.36 * [Math]::Cos($a); $l += $cy + $r * 0.36 * [Math]::Sin($a) }; Poly $g (Solid $dark) $l
  for ($i = 0; $i -lt 5; $i++) { $a = -[Math]::PI / 2 + $i * 2 * [Math]::PI / 5; $x = $cx + $r * 0.84 * [Math]::Cos($a); $y = $cy + $r * 0.84 * [Math]::Sin($a); $g.FillEllipse((Solid $dark), [single]($x - $r * 0.21), [single]($y - $r * 0.21), [single]($r * 0.42), [single]($r * 0.42)); $g.DrawLine((PenOf $dark ($r * 0.05)), [single]($cx + $r * 0.36 * [Math]::Cos($a)), [single]($cy + $r * 0.36 * [Math]::Sin($a)), [single]$x, [single]$y) }
}
function Crown($g, $cx, $cy, $w, $fill = '#ffd21a', $line = '#16181c') {
  $h = $w * 0.6; $l = @(($cx - $w / 2), ($cy + $h / 2), ($cx - $w / 2), ($cy - $h * 0.2), ($cx - $w / 4), ($cy + $h * 0.1), $cx, ($cy - $h / 2), ($cx + $w / 4), ($cy + $h * 0.1), ($cx + $w / 2), ($cy - $h * 0.2), ($cx + $w / 2), ($cy + $h / 2))
  $g.DrawPolygon((PenOf $line ($w * 0.09)), (Pts $l)); Poly $g (Solid $fill) $l
  foreach ($p in @(@(($cx - $w / 2), ($cy - $h * 0.2)), @($cx, ($cy - $h / 2)), @(($cx + $w / 2), ($cy - $h * 0.2)))) { $g.FillEllipse((Solid $line), [single]($p[0] - $w * 0.07), [single]($p[1] - $w * 0.07), [single]($w * 0.14), [single]($w * 0.14)); $g.FillEllipse((Solid '#ff4a3a'), [single]($p[0] - $w * 0.04), [single]($p[1] - $w * 0.04), [single]($w * 0.08), [single]($w * 0.08)) }
}
# A graffiti piece: fat letters, each a little turned and overlapping the one before, with a block
# shadow, a thick outline, a colour fade, a shine, stars and drips.
function Piece($g, $text, $x, $y, $size, $o) {
  $font = if ($o.font) { $o.font } else { 'Impact' }; $style = if ($o.style) { [int][System.Drawing.FontStyle]$o.style } else { 0 }
  $family = New-Object System.Drawing.FontFamily $font
  $c1 = $o.c1; $c2 = $o.c2; $line = if ($o.line) { $o.line } else { '#101216' }; $block = if ($o.block) { $o.block } else { '#101216' }
  $depth = if ($o.depth) { $o.depth } else { [int]($size * 0.09) }; $ow = if ($o.outline) { $o.outline } else { $size * 0.075 }
  $overlap = if ($null -ne $o.overlap) { $o.overlap } else { 0.12 }; $tilt = if ($null -ne $o.tilt) { $o.tilt } else { 7 }
  $paths = @(); $cx = $x
  foreach ($ch in $text.ToCharArray()) {
    if ($ch -eq ' ') { $cx += $size * 0.3; continue }
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $p.AddString([string]$ch, $family, $style, [single]$size, (New-Object System.Drawing.PointF 0, 0), [System.Drawing.StringFormat]::GenericTypographic)
    $b = $p.GetBounds(); $m = New-Object System.Drawing.Drawing2D.Matrix
    $sc = Rnd 0.94 1.08
    $m.Translate([single]($cx + $b.Width / 2), [single]($y + $size * 0.55 + (Rnd (-$size * 0.05) ($size * 0.05))))
    $m.Rotate([single](Rnd (-$tilt) $tilt)); $m.Shear([single](Rnd (-0.12) 0.02), 0); $m.Scale([single]($sc * $(if ($o.wide) { $o.wide } else { 1.0 })), [single]$sc)
    $m.Translate([single](-$b.X - $b.Width / 2), [single](-$b.Y - $b.Height / 2))
    $p.Transform($m); $paths += $p
    $cx += $b.Width * $(if ($o.wide) { $o.wide } else { 1.0 }) * (1 - $overlap)
  }
  $all = New-Object System.Drawing.Drawing2D.GraphicsPath; foreach ($p in $paths) { $all.AddPath($p, $false) }; $bb = $all.GetBounds()
  if ($o.cloud) { for ($i = 0; $i -lt 16; $i++) { $ex = $bb.X + $bb.Width * (Rnd (-0.03) 1.0); $ey = $bb.Y + $bb.Height * (Rnd (-0.2) 0.9); $er = $size * (Rnd 0.3 0.62); $g.FillEllipse((Solid $o.cloud 235), [single]($ex - $er), [single]($ey - $er * 0.8), [single](2 * $er), [single](1.6 * $er)) } }
  if ($o.halo) { $g.DrawPath((PenOf $o.halo ($ow * 2.6)), $all) }
  foreach ($p in $paths) {
    for ($i = $depth; $i -ge 1; $i--) { $t = New-Object System.Drawing.Drawing2D.Matrix; $t.Translate([single]$i, [single]$i); $q = $p.Clone(); $q.Transform($t); $g.FillPath((Solid $block), $q); $g.DrawPath((PenOf $block ($ow * 0.8)), $q); $q.Dispose() }
    $g.DrawPath((PenOf $line $ow), $p)
    $pb = $p.GetBounds(); $rect = New-Object System.Drawing.RectangleF $pb.X, ($pb.Y - 2), $pb.Width, ($pb.Height + 4)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, (C $c1), (C $c2), ([System.Drawing.Drawing2D.LinearGradientMode]::Vertical)
    $g.FillPath($brush, $p)
    # shine: a light band in the upper part, a few bubbles
    $state = $g.Save(); $g.SetClip($p)
    $g.FillEllipse((Solid '#ffffff' 80), [single]($pb.X - $pb.Width * 0.2), [single]($pb.Y - $pb.Height * 0.42), [single]($pb.Width * 1.4), [single]($pb.Height * 0.66))
    if ($o.third) { $g.FillRectangle((Solid $o.third 200), [single]$pb.X, [single]($pb.Y + $pb.Height * 0.72), [single]$pb.Width, [single]($pb.Height * 0.3)) }
    for ($i = 0; $i -lt 3; $i++) { $br = $size * (Rnd 0.02 0.045); $g.FillEllipse((Solid '#ffffff' 150), [single]($pb.X + $pb.Width * (Rnd 0.15 0.85)), [single]($pb.Y + $pb.Height * (Rnd 0.45 0.9)), [single]$br, [single]$br) }
    $g.Restore($state)
    $g.DrawPath((PenOf '#ffffff' ($ow * 0.16) 150), $p)
  }
  if ($o.drips) { for ($i = 0; $i -lt $o.drips; $i++) { $dx = $bb.X + $bb.Width * (Rnd 0.05 0.95); $dy = $bb.Bottom - $size * 0.08; $dl = $size * (Rnd 0.1 0.34); $g.DrawLine((PenOf $line ($ow * 1.3)), [single]$dx, [single]$dy, [single]$dx, [single]($dy + $dl)); $g.DrawLine((PenOf $c2 ($ow * 0.7)), [single]$dx, [single]$dy, [single]$dx, [single]($dy + $dl)) } }
  if ($o.stars) { for ($i = 0; $i -lt $o.stars; $i++) { Star $g (Solid '#ffffff') ($bb.X + $bb.Width * (Rnd 0 1)) ($bb.Y + $bb.Height * (Rnd (-0.05) 0.5)) ($size * (Rnd 0.05 0.11)) } }
  return $bb
}
# a tag: quick marker handwriting, a little turned
function Tag($g, $text, $x, $y, $size, $hex, $angle = 0, $font = 'Segoe Script') {
  $state = $g.Save(); $g.TranslateTransform([single]$x, [single]$y); $g.RotateTransform([single]$angle)
  $f = New-Object System.Drawing.Font $font, ([single]$size), ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
  $g.DrawString($text, $f, (Solid $hex 235), 0, 0, [System.Drawing.StringFormat]::GenericTypographic)
  $w = $g.MeasureString($text, $f, 4000, [System.Drawing.StringFormat]::GenericTypographic).Width
  $g.DrawBezier((PenOf $hex ($size * 0.07) 235), 0, [single]($size * 1.25), [single]($w * 0.3), [single]($size * 1.45), [single]($w * 0.7), [single]($size * 1.1), [single]($w * 1.04), [single]($size * 1.3))
  $g.Restore($state)
}
function Txt($g, $text, $font, $size, $hex, $x, $y, $style = 'Regular', $alpha = 255) { $f = New-Object System.Drawing.Font $font, ([single]$size), ([System.Drawing.FontStyle]$style), ([System.Drawing.GraphicsUnit]::Pixel); $g.DrawString($text, $f, (Solid $hex $alpha), [single]$x, [single]$y, [System.Drawing.StringFormat]::GenericTypographic); $g.MeasureString($text, $f, 4000, [System.Drawing.StringFormat]::GenericTypographic).Width }
function Fit($g, $text, $font, $hex, $x, $y, $w, $h, $style = 'Regular') { $size = $h; do { $f = New-Object System.Drawing.Font $font, ([single]$size), ([System.Drawing.FontStyle]$style), ([System.Drawing.GraphicsUnit]::Pixel); $m = $g.MeasureString($text, $f, 9000, [System.Drawing.StringFormat]::GenericTypographic); if ($m.Width -le $w) { break }; $size *= 0.94 } while ($size -gt 6); $g.DrawString($text, $f, (Solid $hex), [single]($x + ($w - $m.Width) / 2), [single]($y + ($h - $m.Height) / 2), [System.Drawing.StringFormat]::GenericTypographic) }

# ---- the big pieces for the walls ----------------------------------------------------------------------
$pieces = @(
  @{ n = 'p_soccermod'; t = 'SOCCERMOD'; w = 1500; h = 520; s = 250; o = @{ c1 = '#ffe14a'; c2 = '#ff6a1a'; block = '#5a1a8a'; halo = '#ffffff'; cloud = '#3fc1ff'; drips = 6; stars = 5; third = '#ff2e63' } },
  @{ n = 'p_rua10'; t = 'RUA 10'; w = 1200; h = 540; s = 300; o = @{ c1 = '#9cff57'; c2 = '#0c9b4a'; block = '#0b2a6b'; halo = '#ffe14a'; cloud = '#1f5bd8'; drips = 5; stars = 4 } },
  @{ n = 'p_streetkings'; t = 'REIS'; w = 1400; h = 560; s = 330; o = @{ c1 = '#ff8ad4'; c2 = '#c2168f'; block = '#16181c'; halo = '#ffffff'; cloud = '#ffd21a'; drips = 7; stars = 6; third = '#6a1ad4' } },
  @{ n = 'p_joga'; t = 'JOGA'; w = 1100; h = 560; s = 330; o = @{ c1 = '#ffffff'; c2 = '#39c5ff'; block = '#0b3a9a'; halo = '#ffe14a'; cloud = '#0c9b4a'; drips = 4; stars = 4 } },
  @{ n = 'p_rio'; t = 'RIO'; w = 1000; h = 520; s = 330; o = @{ c1 = '#ffe14a'; c2 = '#0c9b4a'; block = '#0b2a6b'; halo = '#ffffff'; cloud = '#1f5bd8'; drips = 6; stars = 4; font = 'Arial Black' } },
  @{ n = 'p_kacrew'; t = 'KA CREW'; w = 1200; h = 420; s = 220; o = @{ c1 = '#cfd6e0'; c2 = '#5e6b7d'; block = '#16181c'; halo = '#ff5a4a'; drips = 5; stars = 3 } },
  @{ n = 'p_favela'; t = 'FAVELA FC'; w = 1500; h = 460; s = 230; o = @{ c1 = '#ffe14a'; c2 = '#f7941d'; block = '#0b3a9a'; halo = '#ffffff'; drips = 4; stars = 5; third = '#0c9b4a' } },
  @{ n = 'p_gol'; t = 'GOL!'; w = 1000; h = 520; s = 320; o = @{ c1 = '#ffe14a'; c2 = '#0c9b4a'; block = '#0b3a9a'; halo = '#ffffff'; cloud = '#ff6a1a'; drips = 5; stars = 5 } }
)
foreach ($p in $pieces) {
  $bmp, $g = Canvas $p.w $p.h
  # measure on a scratch pass, then draw centred
  $tb, $tg = Canvas $p.w $p.h; $seedState = $script:rng; $script:rng = New-Object System.Random ($p.n.GetHashCode()); $bb = Piece $tg $p.t 0 0 $p.s $p.o; $tg.Dispose(); $tb.Dispose()
  $script:rng = New-Object System.Random ($p.n.GetHashCode())
  Spray $g '#ffffff' ($p.w / 2) ($p.h / 2) ($p.w * 0.48) ($p.h * 0.44) 260 60
  $null = Piece $g $p.t (($p.w - $bb.Width) / 2 - $bb.X) (($p.h - $bb.Height) / 2 - $bb.Y - $p.s * 0.04) $p.s $p.o
  $script:rng = $seedState
  Save $bmp $g $p.n
}

# ---- characters and murals ---------------------------------------------------------------------------
$bmp, $g = Canvas 640 640   # the ball with a crown
Spray $g '#ffd21a' 320 330 300 290 300 70
$g.FillEllipse((Solid '#ff6a1a' 230), 60, 90, 520, 520); $g.FillEllipse((Solid '#ffd21a' 240), 90, 120, 460, 460)
Ball $g 320 360 190; Crown $g 320 140 250
Save $bmp $g 'c_ball'

$bmp, $g = Canvas 1100 620  # the hill with the statue in front of a low sun
$g.FillEllipse((Solid '#ff8a1a' 235), 330, 40, 520, 520); $g.FillEllipse((Solid '#ffd21a' 240), 400, 110, 380, 380)
Poly $g (Solid '#14213d' 245) @(0, 600, 0, 470, 120, 430, 260, 380, 380, 250, 470, 170, 540, 150, 610, 190, 700, 330, 800, 410, 900, 360, 980, 380, 1100, 450, 1100, 600)
$g.FillRectangle((Solid '#14213d' 245), 532, 96, 14, 60); $g.FillRectangle((Solid '#14213d' 245), 500, 104, 78, 10); $g.FillEllipse((Solid '#14213d' 245), 531, 82, 16, 18)
$rnd2 = New-Object System.Random 7; for ($i = 0; $i -lt 90; $i++) { $wx = 20 + $rnd2.Next(1060); $wy = 470 + $rnd2.Next(120); $g.FillRectangle((Solid '#ffe38a' 235), $wx, $wy, 9, 9) }
Save $bmp $g 'c_morro'

$bmp, $g = Canvas 1000 600  # wings to stand in front of
for ($side = -1; $side -le 1; $side += 2) { for ($row = 0; $row -lt 4; $row++) { for ($k = 0; $k -lt 9 - $row; $k++) {
  $state = $g.Save(); $g.TranslateTransform([single](500 + $side * (70 + $k * 44 + $row * 10)), [single](170 + $row * 86 + $k * $k * 2.2)); $g.RotateTransform([single]($side * (18 + $k * 7)))
  $col = @('#ffffff', '#bfe9ff', '#7fd0ff', '#3fa9f5')[$row]
  $g.FillEllipse((Solid '#16181c' 240), -24, -8, 48, 150); $g.FillEllipse((Solid $col 245), -18, -2, 36, 138); $g.Restore($state) } } }
Save $bmp $g 'c_wings'

$bmp, $g = Canvas 2048 551  # rays in the colours of Brazil, behind the lettering
for ($i = 0; $i -lt 26; $i++) { $a0 = [Math]::PI * (1 + $i / 26.0); $a1 = [Math]::PI * (1 + ($i + 1) / 26.0); $col = @('#0c9b4a', '#ffd21a', '#1f5bd8', '#ffd21a')[$i % 4]
  Poly $g (Solid $col 215) @(1024, 640, (1024 + 1700 * [Math]::Cos($a0)), (640 + 1700 * [Math]::Sin($a0)), (1024 + 1700 * [Math]::Cos($a1)), (640 + 1700 * [Math]::Sin($a1))) }
Save $bmp $g 'c_rays'

$bmp, $g = Canvas 860 551   # green field, yellow diamond, blue disc with a white band and stars
$g.FillRectangle((Solid '#0c9b4a' 240), 20, 20, 820, 511); Poly $g (Solid '#ffd21a' 245) @(430, 60, 790, 275, 430, 491, 70, 275)
$g.FillEllipse((Solid '#1f3f9a' 250), 290, 135, 280, 280); $g.DrawArc((PenOf '#ffffff' 22), 250, 205, 380, 300, 205, 110)
$rnd3 = New-Object System.Random 11; for ($i = 0; $i -lt 14; $i++) { Star $g (Solid '#ffffff') (320 + $rnd3.Next(220)) (290 + $rnd3.Next(100)) (5 + $rnd3.Next(5)) 5 0.45 }
Save $bmp $g 'c_flag'

# tags: three sheets of quick names
$tagWords = @('KA', 'rua', 'Zico', 'ginga', 'jogo', 'bola', 'Rio', 'gol', '10', 'crew', 'fera', 'morro', 'samba', 'paz', 'rei', 'craque')
foreach ($k in 1..3) {
  $bmp, $g = Canvas 700 360; $script:rng = New-Object System.Random (100 + $k)
  for ($i = 0; $i -lt 7; $i++) { Tag $g $tagWords[($i * 3 + $k * 5) % $tagWords.Count] (Rnd 10 430) (Rnd 10 250) (Rnd 44 84) (@('#16181c', '#ffffff', '#ff2e63', '#1f5bd8', '#ffd21a', '#0c9b4a')[($i + $k) % 6]) (Rnd (-14) 10) }
  Crown $g (Rnd 500 620) (Rnd 60 280) 90 '#ffd21a'
  Save $bmp $g "t_tags_$k"
}

# ---- paintings on the ground -------------------------------------------------------------------------
$bmp, $g = Canvas 1024 1024  # the centre circle: a ball in a ring of colour, lettering round it
$script:rng = New-Object System.Random 5
Spray $g '#ffffff' 512 512 500 500 500 60
$g.FillEllipse((Solid '#16181c' 235), 22, 22, 980, 980); $g.FillPie((Solid '#ffd21a' 235), 60, 60, 904, 904, 0, 120); $g.FillPie((Solid '#0c9b4a' 235), 60, 60, 904, 904, 120, 120); $g.FillPie((Solid '#ff2e63' 235), 60, 60, 904, 904, 240, 120)
$g.FillEllipse((Solid '#16181c' 240), 190, 190, 644, 644); Ball $g 512 512 250
$ring = 'STREET ARENA   -   KA SOCCERMOD   -   '; $fam = New-Object System.Drawing.Font 'Impact', 96, ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)
for ($i = 0; $i -lt $ring.Length; $i++) { $ch = $ring.Substring($i, 1); if ($ch -eq ' ') { continue }; $m = $g.MeasureString($ch, $fam, 900, [System.Drawing.StringFormat]::GenericTypographic); $state = $g.Save(); $g.TranslateTransform(512, 512); $g.RotateTransform([single]($i * 360.0 / $ring.Length)); $g.DrawString($ch, $fam, (Solid '#16181c' 240), [single](-$m.Width / 2), -446, [System.Drawing.StringFormat]::GenericTypographic); $g.Restore($state) }
Save $bmp $g 'g_centre'

$bmp, $g = Canvas 1024 512   # an arrow towards the goal with a word
Poly $g (Solid '#16181c' 230) @(60, 200, 660, 200, 660, 90, 980, 256, 660, 422, 660, 312, 60, 312); Poly $g (Solid '#ffd21a' 235) @(84, 222, 684, 222, 684, 140, 930, 256, 684, 372, 684, 290, 84, 290)
Fit $g 'ATAQUE' 'Impact' '#16181c' 110 226 540 62
Save $bmp $g 'g_arrow'
foreach ($num in @(@('g_ten', '10', '#ff2e63'), @('g_seven', '7', '#1f5bd8'))) { $bmp, $g = Canvas 512 512; $script:rng = New-Object System.Random 9; Spray $g '#ffffff' 256 256 240 240 160 60; $null = Piece $g $num[1] $(if ($num[1].Length -gt 1) { 60 } else { 160 }) 60 380 @{ c1 = '#ffffff'; c2 = $num[2]; block = '#16181c'; halo = '#ffd21a'; tilt = 4 }; Save $bmp $g $num[0] }
$bmp, $g = Canvas 1024 341; $script:rng = New-Object System.Random 21; $null = Piece $g 'RUA' 250 40 250 @{ c1 = '#ffe14a'; c2 = '#ff6a1a'; block = '#16181c'; halo = '#ffffff'; drips = 3 }; Save $bmp $g 'g_rua'
$bmp, $g = Canvas 1024 341; $script:rng = New-Object System.Random 22; $null = Piece $g 'KINGS' 150 50 230 @{ c1 = '#9cff57'; c2 = '#0c9b4a'; block = '#16181c'; halo = '#ffffff'; drips = 3 }; Crown $g 880 110 150; Save $bmp $g 'g_kings'
$bmp, $g = Canvas 256 712    # chalk hopscotch
$chalk = PenOf '#f4f0e4' 7 225; $rows = @(1, 1, 2, 1, 2, 1); $yy = 700; $nn = 1
foreach ($r in $rows) { $yy -= 100; if ($r -eq 1) { $g.DrawRectangle($chalk, 78, $yy, 100, 100); Fit $g "$nn" 'Segoe Script' '#f4f0e4' 78 $yy 100 100 'Bold'; $nn++ } else { $g.DrawRectangle($chalk, 28, $yy, 100, 100); $g.DrawRectangle($chalk, 128, $yy, 100, 100); Fit $g "$nn" 'Segoe Script' '#f4f0e4' 28 $yy 100 100 'Bold'; $nn++; Fit $g "$nn" 'Segoe Script' '#f4f0e4' 128 $yy 100 100 'Bold'; $nn++ } }
$g.DrawArc($chalk, 58, 20, 140, 160, 180, 180); Save $bmp $g 'g_hopscotch'
foreach ($k in @('a', 'b')) { $bmp, $g = Canvas 512 512; $script:rng = New-Object System.Random $(if ($k -eq 'a') { 31 } else { 32 }); for ($i = 0; $i -lt 6; $i++) { Tag $g $tagWords[($i * 5 + $(if ($k -eq 'a') { 2 } else { 7 })) % $tagWords.Count] (Rnd 20 260) (Rnd 20 400) (Rnd 40 72) (@('#ffffff', '#ffd21a', '#ff2e63', '#39c5ff')[$i % 4]) (Rnd (-30) 30) }; Save $bmp $g "g_tags_$k" }
$bmp, $g = Canvas 640 512; Spray $g '#ffd21a' 320 256 300 240 200 70; Crown $g 320 260 460; Save $bmp $g 'g_crown'
$bmp, $g = Canvas 512 512; Spray $g '#39c5ff' 256 256 240 240 200 70; Star $g (Solid '#16181c' 235) 256 262 240 5 0.42; Star $g (Solid '#39c5ff' 240) 256 262 200 5 0.42; Star $g (Solid '#ffffff' 240) 256 262 96 5 0.42; Save $bmp $g 'g_star'
$bmp, $g = Canvas 256 256   # a manhole cover
$g.FillEllipse((Solid '#2a2a2e'), 6, 6, 244, 244); $g.FillEllipse((Solid '#55565a'), 16, 16, 224, 224); for ($i = -4; $i -le 4; $i++) { $g.DrawLine((PenOf '#2f3034' 9), [single](128 + $i * 24), 30, [single](128 + $i * 24), 226); $g.DrawLine((PenOf '#2f3034' 9), 30, [single](128 + $i * 24), 226, [single](128 + $i * 24)) }; $g.DrawEllipse((PenOf '#2a2a2e' 12), 16, 16, 224, 224)
Save $bmp $g 'g_manhole'
$bmp, $g = Canvas 1024 512  # cracks in the asphalt
$script:rng = New-Object System.Random 41; foreach ($c in 1..4) { $px = Rnd 60 300; $py = Rnd 100 400; for ($i = 0; $i -lt 16; $i++) { $nx = $px + (Rnd 20 60); $ny = $py + (Rnd (-34) 34); $g.DrawLine((PenOf '#1c1c20' (Rnd 2 5) 210), [single]$px, [single]$py, [single]$nx, [single]$ny); if ((Rnd 0 1) -lt 0.3) { $g.DrawLine((PenOf '#1c1c20' 2 200), [single]$px, [single]$py, [single]($px + (Rnd (-40) 40)), [single]($py + (Rnd (-60) 60))) }; $px = $nx; $py = $ny } }
Save $bmp $g 'g_crack'

# ---- abstract pieces for the court (no words: arrows, bubbles, a wide stroke, a splash, a scribble) ----------
function Outline($g, $list, $fill, $line = '#101216', $w = 16) { $g.DrawPolygon((PenOf $line $w), (Pts $list)); Poly $g (Solid $fill) $list }
$bmp, $g = Canvas 1024 1024; $script:rng = New-Object System.Random 301   # arrows that lock into each other
Spray $g '#ffffff' 512 512 470 470 320 50
foreach ($a in @(@(200, 760, -38, '#3fc1c9', 1.0), @(380, 420, 18, '#ff6a5a', 0.86), @(620, 700, -70, '#ffd21a', 0.8), @(700, 330, 52, '#e85aa8', 0.7))) {
  $state = $g.Save(); $g.TranslateTransform([single]$a[0], [single]$a[1]); $g.RotateTransform([single]$a[2]); $g.ScaleTransform([single]$a[4], [single]$a[4])
  $shape = @(-220, -46, 60, -46, 60, -120, 250, 0, 60, 120, 60, 46, -220, 46, -160, 0)
  $sh = @(); for ($i = 0; $i -lt $shape.Count; $i += 2) { $sh += $shape[$i] + 16; $sh += $shape[$i + 1] + 16 }; Poly $g (Solid '#101216') $sh
  Outline $g $shape $a[3]; $g.FillRectangle((Solid '#ffffff' 90), -200, -36, 250, 22)
  for ($i = 0; $i -lt 3; $i++) { $dx = Rnd (-180) 40; $dl = Rnd 30 90; $g.DrawLine((PenOf '#101216' 12), [single]$dx, 44, [single]$dx, [single](44 + $dl)); $g.DrawLine((PenOf $a[3] 6), [single]$dx, 44, [single]$dx, [single](44 + $dl)) }
  $g.Restore($state) }
for ($i = 0; $i -lt 6; $i++) { Star $g (Solid '#ffffff') (Rnd 120 900) (Rnd 120 900) (Rnd 16 34) }
Save $bmp $g 'a_arrows'

$bmp, $g = Canvas 1024 1024; $script:rng = New-Object System.Random 302   # bubbles with a shine
Spray $g '#3fc1c9' 512 512 460 460 300 50
foreach ($b in @(@(360, 420, 250, '#e85aa8'), @(650, 560, 210, '#3fc1c9'), @(430, 720, 170, '#ffd21a'), @(700, 300, 130, '#ff6a5a'), @(250, 640, 110, '#f4f4ee'))) {
  $g.FillEllipse((Solid '#101216'), [single]($b[0] - $b[2] + 14), [single]($b[1] - $b[2] + 14), [single](2 * $b[2]), [single](2 * $b[2]))
  $g.FillEllipse((Solid '#101216'), [single]($b[0] - $b[2] - 9), [single]($b[1] - $b[2] - 9), [single](2 * $b[2] + 18), [single](2 * $b[2] + 18))
  $g.FillEllipse((Solid $b[3]), [single]($b[0] - $b[2]), [single]($b[1] - $b[2]), [single](2 * $b[2]), [single](2 * $b[2]))
  $g.FillEllipse((Solid '#ffffff' 150), [single]($b[0] - $b[2] * 0.62), [single]($b[1] - $b[2] * 0.7), [single]($b[2] * 0.5), [single]($b[2] * 0.32)) }
for ($i = 0; $i -lt 5; $i++) { Star $g (Solid '#101216') (Rnd 140 880) (Rnd 140 880) (Rnd 22 40) }
Save $bmp $g 'a_bubbles'

$bmp, $g = Canvas 2048 1024; $script:rng = New-Object System.Random 303   # one wide stroke with a second colour on it
foreach ($pass in @(@('#101216', 150, 255), @('#3fc1c9', 122, 255), @('#f4f4ee', 26, 190))) { $pen = PenOf $pass[0] $pass[1] $pass[2]; $g.DrawBezier($pen, 150, 760, 600, 120, 1300, 980, 1900, 300) }
$g.DrawBezier((PenOf '#101216' 46), 260, 420, 800, 760, 1300, 180, 1780, 700); $g.DrawBezier((PenOf '#ff6a5a' 30), 260, 420, 800, 760, 1300, 180, 1780, 700)
Spray $g '#3fc1c9' 1024 512 900 420 500 60; Spray $g '#ffd21a' 1500 420 300 260 220 80
for ($i = 0; $i -lt 7; $i++) { $dx = Rnd 300 1800; $dy = Rnd 420 700; $dl = Rnd 60 190; $g.DrawLine((PenOf '#101216' 13), [single]$dx, [single]$dy, [single]$dx, [single]($dy + $dl)); $g.DrawLine((PenOf '#3fc1c9' 7), [single]$dx, [single]$dy, [single]$dx, [single]($dy + $dl)) }
Save $bmp $g 'a_swoosh'

$bmp, $g = Canvas 1024 1024; $script:rng = New-Object System.Random 304   # a splash: a blot with rays and drops
for ($i = 0; $i -lt 26; $i++) { $a = Rnd 0 6.283; $len = Rnd 220 470; $w = Rnd 10 34; $g.DrawLine((PenOf '#ffd21a' $w 235), 512, 512, [single](512 + [Math]::Cos($a) * $len), [single](512 + [Math]::Sin($a) * $len)); $r = Rnd 12 34; $g.FillEllipse((Solid '#ffd21a' 235), [single](512 + [Math]::Cos($a) * ($len + 30) - $r), [single](512 + [Math]::Sin($a) * ($len + 30) - $r), [single](2 * $r), [single](2 * $r)) }
$g.FillEllipse((Solid '#ffd21a' 240), 322, 322, 380, 380); $g.FillEllipse((Solid '#ff6a5a' 235), 402, 402, 220, 220); $g.FillEllipse((Solid '#ffffff' 150), 380, 370, 110, 70)
Save $bmp $g 'a_splat'

$bmp, $g = Canvas 2048 1024; $script:rng = New-Object System.Random 305   # a marker scribble: loops, an underline, a crown
$pts = @(); for ($i = 0; $i -lt 26; $i++) { $pts += New-Object System.Drawing.PointF ([single](160 + $i * 66 + (Rnd (-30) 30))), ([single](512 + [Math]::Sin($i * 1.25) * 300 + (Rnd (-40) 40))) }
$g.DrawCurve((PenOf '#101216' 40), [System.Drawing.PointF[]]$pts, 0.7); $g.DrawCurve((PenOf '#f4f4ee' 24), [System.Drawing.PointF[]]$pts, 0.7)
$g.DrawBezier((PenOf '#e85aa8' 26), 200, 900, 700, 960, 1300, 840, 1880, 930); Crown $g 1760 200 240 '#ffd21a'
Save $bmp $g 'a_scribble'

$bmp, $g = Canvas 2048 1024; $script:rng = New-Object System.Random 306   # signs instead of letters, as a piece
$tb, $tg = Canvas 2048 1024; $bb = Piece $tg '&%?!' 0 0 620 @{ c1 = '#e85aa8'; c2 = '#7a2ad0'; block = '#101216'; halo = '#f4f4ee'; cloud = '#3fc1c9'; drips = 7; stars = 6; tilt = 14; overlap = 0.2 }; $tg.Dispose(); $tb.Dispose(); $script:rng = New-Object System.Random 306
$null = Piece $g '&%?!' ((2048 - $bb.Width) / 2 - $bb.X) ((1024 - $bb.Height) / 2 - $bb.Y - 20) 620 @{ c1 = '#e85aa8'; c2 = '#7a2ad0'; block = '#101216'; halo = '#f4f4ee'; cloud = '#3fc1c9'; drips = 7; stars = 6; tilt = 14; overlap = 0.2 }
Save $bmp $g 'a_wild'

# pillars of the elevated line, house walls, the container lintels
foreach ($k in @('a', 'b', 'c')) { $bmp, $g = Canvas 96 512; $script:rng = New-Object System.Random (50 + [int][char]$k); $col = @{ a = '#ffd21a'; b = '#ff2e63'; c = '#39c5ff' }[$k]
  $state = $g.Save(); $g.TranslateTransform(86, 20); $g.RotateTransform(90); $null = Txt $g (@{ a = 'KA CREW'; b = 'RUA 10'; c = 'GINGA' }[$k]) 'Impact' 78 '#16181c' 4 4; $null = Txt $g (@{ a = 'KA CREW'; b = 'RUA 10'; c = 'GINGA' }[$k]) 'Impact' 78 $col 0 0; $g.Restore($state)
  for ($i = 0; $i -lt 3; $i++) { Star $g (Solid '#ffffff') (Rnd 20 76) (Rnd 330 490) (Rnd 8 16) }; Save $bmp $g "g_pillar_$k" }
$fav = @(@('g_fav_a', 'PAZ', '#ffffff', '#0c9b4a'), @('g_fav_b', 'AMOR', '#ffe14a', '#ff2e63'), @('g_fav_c', 'JOGA BONITO', '#ffffff', '#1f5bd8'), @('g_fav_d', 'GOL', '#ffe14a', '#ff6a1a'))
foreach ($f in $fav) { $bmp, $g = Canvas 512 186; $script:rng = New-Object System.Random ($f[1].Length * 13); $tb, $tg = Canvas 2048 400; $bb = Piece $tg $f[1] 0 0 130 @{ c1 = $f[2]; c2 = $f[3]; halo = '#ffffff' }; $tg.Dispose(); $tb.Dispose(); $script:rng = New-Object System.Random ($f[1].Length * 13)
  $sc = [Math]::Min(1.0, 480 / $bb.Width); $g.ScaleTransform([single]$sc, [single]$sc); $null = Piece $g $f[1] ((512 / $sc - $bb.Width) / 2 - $bb.X) ((186 / $sc - $bb.Height) / 2 - $bb.Y) 130 @{ c1 = $f[2]; c2 = $f[3]; halo = '#ffffff' }; Save $bmp $g $f[0] }
foreach ($t in @(@('g_goal_red', 'KASU 190 248 7     MAX GROSS 30 480 KG', '#ffffff'), @('g_goal_blue', 'RUAU 100 716 2     TARE 2 250 KG', '#ffffff'))) { $bmp, $g = Canvas 1024 176; Fit $g $t[1] 'Bahnschrift SemiBold Condensed' $t[2] 30 30 964 60; $g.FillRectangle((Solid '#ffd21a' 230), 30, 110, 964, 10); for ($i = 0; $i -lt 22; $i++) { Poly $g (Solid '#16181c' 230) @((30 + $i * 44), 120, (52 + $i * 44), 120, (42 + $i * 44), 110, (20 + $i * 44), 110) }; Save $bmp $g $t[0] }
$bmp, $g = Canvas 768 640   # an old painted advert high on a side wall
$g.FillRectangle((Solid '#f1e6c8' 225), 10, 10, 748, 620); $g.FillRectangle((Solid '#0c5a8a' 225), 30, 30, 708, 190); Fit $g 'REFRESCOS' 'Impact' '#f1e6c8' 50 50 668 150
Ball $g 384 400 130 '#f1e6c8' '#8a1c1c'; Fit $g 'DESDE 1958' 'Bahnschrift SemiBold' '#8a1c1c' 60 540 648 70
Save $bmp $g 'g_wall_ad'
$bmp, $g = Canvas 1200 256   # the plaque over the tunnel into the hill
$g.FillRectangle((Solid '#d8d2c0' 235), 6, 6, 1188, 244); $g.DrawRectangle((PenOf '#3a3a3e' 10), 16, 16, 1168, 224); Fit $g 'TUNEL DO MORRO  -  1958' 'Bahnschrift SemiBold Condensed' '#3a3a3e' 50 60 1100 130
$script:rng = New-Object System.Random 71; Tag $g 'KA' 920 150 90 '#ff2e63' -8
Save $bmp $g 'g_tunnel'

# ---- shop fronts and signs (opaque) -------------------------------------------------------------------
function ShopBase($w, $h, $wall, $band) { $bmp, $g = Canvas $w $h $wall; $g.FillRectangle((Solid $band), 0, 0, $w, 96); $g.FillRectangle((Solid '#2a2a2e'), 0, ($h - 26), $w, 26); return $bmp, $g }
function Glass($g, $x, $y, $w, $h, $glow = '#ffcf7a') { $g.FillRectangle((Solid '#2a2420'), $x, $y, $w, $h); $rect = New-Object System.Drawing.RectangleF ([single]($x + 8)), ([single]($y + 8)), ([single]($w - 16)), ([single]($h - 16)); $b = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, (C $glow), (C '#6a4a2a'), ([System.Drawing.Drawing2D.LinearGradientMode]::Vertical); $g.FillRectangle($b, $rect) }
function DoorPic($g, $x, $y, $w, $h, $hex) { $g.FillRectangle((Solid '#1e1a18'), $x, $y, $w, $h); $g.FillRectangle((Solid $hex), ($x + 6), ($y + 6), ($w - 12), ($h - 6)); $g.FillRectangle((Solid "#000000" 64), ($x + 16), ($y + 20), ($w - 32), ($h * 0.4)); $g.FillEllipse((Solid '#d8c070'), ($x + $w - 24), ($y + $h * 0.55), 10, 10) }
function Shutter($g, $x, $y, $w, $h, $hex = '#8a8d90') { $g.FillRectangle((Solid $hex), $x, $y, $w, $h); for ($yy = $y; $yy -lt $y + $h; $yy += 14) { $g.FillRectangle((Solid '#000000' 60), $x, $yy, $w, 4); $g.FillRectangle((Solid '#ffffff' 40), $x, ($yy + 6), $w, 3) }; $g.DrawRectangle((PenOf '#2a2a2e' 6), $x, $y, $w, $h) }

$bmp, $g = ShopBase 2048 450 '#e2b84a' '#1f7a4a'   # mercado
Fit $g 'MERCADO BOA VISTA' 'Impact' '#fff3c8' 60 8 1500 80; Fit $g 'frutas - bebidas - gelo' 'Segoe Script' '#fff3c8' 1560 22 440 52 'Bold'
Glass $g 80 130 520 250; DoorPic $g 660 130 170 294 '#1f7a4a'; Glass $g 890 130 520 250; Shutter $g 1480 120 500 304 '#9aa39a'
$script:rng = New-Object System.Random 61; Tag $g 'rua' 1540 180 90 '#16181c' -8; Tag $g 'KA' 1740 280 80 '#ff2e63' 6
for ($i = 0; $i -lt 5; $i++) { $g.FillRectangle((Solid @('#ff6a1a', '#ffd21a', '#0c9b4a', '#ff2e63', '#39c5ff')[$i]), (110 + $i * 94), 300, 70, 60) }; Fit $g 'ABERTO' 'Bahnschrift SemiBold' '#16181c' 920 150 220 50
Save $bmp $g 'shop_mercado'

$bmp, $g = ShopBase 1400 450 '#e08a48' '#3a3230'   # garage
Fit $g 'OFICINA DO BETO  -  MECANICA E BORRACHARIA' 'Bahnschrift SemiBold Condensed' '#e8d8b0' 40 14 1320 68
Shutter $g 60 116 620 310 '#8d9296'; Shutter $g 730 116 620 310 '#7d8a94'
$script:rng = New-Object System.Random 62; $null = Piece $g 'KA' 150 150 230 @{ c1 = '#ffe14a'; c2 = '#ff6a1a'; halo = '#ffffff'; drips = 3 }; $null = Piece $g 'REI' 800 160 210 @{ c1 = '#9cff57'; c2 = '#0c9b4a'; halo = '#ffffff'; drips = 3 }; Tag $g 'crew' 470 330 64 '#16181c' -6
Save $bmp $g 'shop_garage'

$bmp, $g = ShopBase 1700 450 '#5a8ed2' '#16181c'   # barber
Fit $g 'BARBEARIA DO ZE' 'Impact' '#f4f4ee' 60 6 760 84; Fit $g 'corte - barba - desde 1987' 'Segoe Script' '#ffd21a' 900 20 740 56 'Bold'
Glass $g 90 130 640 256 '#fff0c8'; DoorPic $g 790 130 180 294 '#16181c'; Glass $g 1030 130 560 256 '#fff0c8'
for ($i = 0; $i -lt 12; $i++) { Poly $g (Solid @('#d8262e', '#f4f4ee', '#1f5bd8')[$i % 3]) @(1620, (140 + $i * 22), 1660, (128 + $i * 22), 1660, (150 + $i * 22), 1620, (162 + $i * 22)) }; $g.DrawRectangle((PenOf '#16181c' 6), 1620, 128, 40, 290)
Fit $g 'CORTE  15' 'Bahnschrift SemiBold' '#16181c' 130 170 300 60; Fit $g 'BARBA  10' 'Bahnschrift SemiBold' '#16181c' 1080 170 400 60
Save $bmp $g 'shop_barber'

$bmp, $g = ShopBase 1432 450 '#d9d2c0' '#b3261e'   # deli
Fit $g 'PADARIA PAO QUENTE' 'Impact' '#fff3c8' 40 8 900 80; Fit $g 'ABERTO 24 H' 'Bahnschrift SemiBold' '#ffd21a' 980 22 420 52
Glass $g 70 130 560 250 '#ffe2a8'; DoorPic $g 690 130 170 294 '#3a3230'; Glass $g 920 130 440 250 '#ffe2a8'
Fit $g 'SUCOS - CAFE' 'Bahnschrift SemiBold' '#b3261e' 100 150 320 44; Fit $g 'SALGADOS' 'Bahnschrift SemiBold' '#16181c' 100 200 320 44; Fit $g 'PAO' 'Impact' '#1f5bd8' 950 160 160 70
$g.FillRectangle((Solid '#ff2e63'), 1180, 150, 150, 60); Fit $g 'ABERTO' 'Impact' '#ffffff' 1186 154 138 52
Save $bmp $g 'shop_deli'

$bmp, $g = ShopBase 1876 450 '#e07c8c' '#3fa9a9'   # the pink house
DoorPic $g 150 130 170 294 '#2c6f8a'; Glass $g 420 150 300 200 '#ffd9a0'; Glass $g 860 150 300 200 '#ffd9a0'; DoorPic $g 1320 130 170 294 '#7a3b2e'; Glass $g 1560 150 240 200 '#ffd9a0'
for ($i = 0; $i -lt 30; $i++) { $g.FillRectangle((Solid @('#ffd21a', '#0c9b4a', '#1f5bd8', '#f4f4ee')[$i % 4]), ($i * 63), 100, 61, 26) }
$script:rng = New-Object System.Random 63; Tag $g 'samba' 1180 300 70 '#16181c' -6
Save $bmp $g 'shop_house'

$bmp, $g = Canvas 1792 448 '#cfc4a8'               # ground floors under the viaduct
$g.FillRectangle((Solid '#1f5e8a'), 0, 0, 1792, 60); Shutter $g 60 90 520 330 '#6f767c'; DoorPic $g 640 110 170 312 '#2a3a34'; Shutter $g 870 90 420 330 '#7a6f66'; Glass $g 1350 120 380 240 '#ffbf6a'
$script:rng = New-Object System.Random 64; $null = Piece $g 'GOL' 100 140 210 @{ c1 = '#39c5ff'; c2 = '#1f5bd8'; halo = '#ffffff'; drips = 3 }; Tag $g 'ginga' 900 150 96 '#ffd21a' -8; Tag $g 'KA crew' 930 280 70 '#ffffff' 4
Fit $g 'LAVANDERIA' 'Bahnschrift SemiBold Condensed' '#f4ecd0' 1340 8 400 46; Fit $g 'LOTERIA  -  ACAI  -  LANCHES' 'Bahnschrift SemiBold Condensed' '#f4ecd0' 60 8 760 46
Save $bmp $g 'shop_east'

$bmp, $g = Canvas 1260 450 '#6e4a3c'               # brownstone ground floor with the stoop's door
$g.FillRectangle((Solid '#5a3c30'), 0, 0, 1260, 40); for ($yy = 60; $yy -lt 430; $yy += 46) { $g.FillRectangle((Solid '#000000' 40), 0, $yy, 1260, 5) }
Glass $g 110 110 220 250 '#ffd08a'; Glass $g 420 110 220 250 '#3a4252'; DoorPic $g 720 70 200 354 '#2a2420'; Glass $g 1000 110 200 250 '#ffd08a'
foreach ($wx in @(110, 420, 1000)) { $g.FillRectangle((Solid '#8a6a58'), ($wx - 14), 96, 250, 18); $g.FillRectangle((Solid '#8a6a58'), ($wx - 14), 356, 250, 16) }
Fit $g '127' 'Georgia' '#e8d8b0' 760 20 120 44 'Bold'
Save $bmp $g 'stoop_front'

$bmp, $g = Canvas 560 384 '#f1ead6'                # the kiosk-bar
$g.FillRectangle((Solid '#d8262e'), 0, 0, 560, 80); Fit $g 'BAR DO CAMPINHO' 'Impact' '#fff3c8' 20 10 520 60
$g.FillRectangle((Solid '#2a2420'), 40, 110, 480, 170); $g.FillRectangle((Solid '#ffcf7a'), 50, 120, 460, 150); $g.FillRectangle((Solid '#7a4a2a'), 30, 280, 500, 26)
Fit $g 'agua de coco  -  acai  -  pastel' 'Segoe Script' '#16181c' 30 320 500 44 'Bold'
Save $bmp $g 'bar_front'

$bmp, $g = Canvas 960 240 '#0b3a9a'                # the sign at the stairs to the line: the metro
$g.DrawRectangle((PenOf '#f4f4ee' 6), 8, 8, 944, 224)
$g.FillEllipse((Solid '#f4f4ee'), 36, 40, 160, 160); Fit $g 'M' 'Arial Black' '#0b3a9a' 36 40 160 160
Fit $g 'METRO' 'Arial Black' '#f4f4ee' 230 30 400 96; Fit $g 'Linha 10  -  Largo da Bola' 'Arial' '#f4f4ee' 230 136 690 64 'Bold'
$g.FillRectangle((Solid '#ffd21a'), 660, 56, 262, 12); $g.FillRectangle((Solid '#0c9b4a'), 660, 76, 262, 12)
Save $bmp $g 'sign_subway'
Write-Host ''
