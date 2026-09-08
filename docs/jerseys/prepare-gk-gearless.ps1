# Apply the common variant-b gearless goalkeeper candidate to the isolated
# Workshop Tools addon.  This intentionally does not touch the approved
# soccermod_jerseys addon or any server files.
param(
    [string]$CsRoot = "E:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive",
    [string]$AddonName = "soccermod_jerseys_gearless",
    [string]$RepoRoot = ""
)

$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $PSCommandPath
$repo = if ($RepoRoot) { (Resolve-Path -LiteralPath $RepoRoot).Path } else { (Resolve-Path (Join-Path $here "..\..")).Path }
$contentAddon = Join-Path $CsRoot "content/csgo_addons/$AddonName"
$kits = Join-Path $contentAddon "models/soccermod/kits"
$materials = Join-Path $contentAddon "materials/soccermod/kits"
$refs = Join-Path $repo "docs/jerseys/refs/gk-gearless"
$templates = Join-Path $repo "docs/jerseys/gk-gearless"
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$backup = Join-Path $repo "artifacts/gk-gearless-source-backup/$stamp"

foreach ($path in @($contentAddon, $kits, $materials, $refs, $templates)) {
    if (-not (Test-Path -LiteralPath $path)) { throw "Required path missing: $path" }
}
foreach ($file in @(
    "gkhome_gearless_body_color.png",
    "gkhome_gearless_legs_color.png",
    "gkaway_gearless_body_color.png",
    "gkaway_gearless_legs_color.png",
    "gkhome_gearless_gloves_color.png",
    "gkaway_gearless_gloves_color.png"
)) {
    if (-not (Test-Path -LiteralPath (Join-Path $refs $file) -PathType Leaf)) { throw "Generated reference missing: $file" }
}

New-Item -ItemType Directory -Force -Path $backup | Out-Null
foreach ($relative in @(
    "models/soccermod/kits/kit_gkhome.vmdl",
    "models/soccermod/kits/kit_gkaway.vmdl",
    "materials/soccermod/kits/kit_c_body.vmat",
    "materials/soccermod/kits/kit_c_lower_body.vmat",
    "materials/soccermod/kits/kit_d_body.vmat",
    "materials/soccermod/kits/kit_d_lower_body.vmat"
)) {
    $source = Join-Path $contentAddon $relative
    if (Test-Path -LiteralPath $source -PathType Leaf) {
        $destination = Join-Path $backup $relative
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
        Copy-Item -LiteralPath $source -Destination $destination
    }
}

function Copy-Ref([string]$sourceName, [string]$targetName) {
    Copy-Item -LiteralPath (Join-Path $refs $sourceName) -Destination (Join-Path $materials $targetName) -Force
}

Copy-Ref "gkhome_gearless_body_color.png" "tm_leet_v2_body_variantb_gkhome_color.png"
Copy-Ref "gkhome_gearless_legs_color.png" "tm_leet_v2_lower_body_variantb_gkhome_color.png"
Copy-Ref "gkaway_gearless_body_color.png" "tm_leet_v2_body_variantb_gkaway_color.png"
Copy-Ref "gkaway_gearless_legs_color.png" "tm_leet_v2_lower_body_variantb_gkaway_color.png"
Copy-Ref "gkhome_gearless_gloves_color.png" "gkhome_gloves_color.png"
Copy-Ref "gkaway_gearless_gloves_color.png" "gkaway_gloves_color.png"

function Write-Utf8NoBom([string]$path, [string]$text) {
    [IO.File]::WriteAllText($path, $text, [Text.UTF8Encoding]::new($false))
}

function Make-BodyMaterial([string]$name, [string]$colour) {
    $text = Get-Content -LiteralPath (Join-Path $materials "kit_b_body.vmat") -Raw
    $text = $text.Replace("tm_leet_v2_body_variantb_color.png", $colour)
    Write-Utf8NoBom (Join-Path $materials "$name.vmat") $text
}

function Make-LowerMaterial([string]$name, [string]$colour) {
    $text = Get-Content -LiteralPath (Join-Path $materials "kit_b_lower_body.vmat") -Raw
    $text = $text.Replace("tm_leet_v2_lower_body_variantb_color.png", $colour)
    Write-Utf8NoBom (Join-Path $materials "$name.vmat") $text
}

Make-BodyMaterial "kit_gkhome_body" "tm_leet_v2_body_variantb_gkhome_color.png"
Make-LowerMaterial "kit_gkhome_lower_body" "tm_leet_v2_lower_body_variantb_gkhome_color.png"
Make-BodyMaterial "kit_gkaway_body" "tm_leet_v2_body_variantb_gkaway_color.png"
Make-LowerMaterial "kit_gkaway_lower_body" "tm_leet_v2_lower_body_variantb_gkaway_color.png"

Copy-Item -LiteralPath (Join-Path $templates "kit_gkhome_gloves.vmat") -Destination (Join-Path $materials "kit_gkhome_gloves.vmat") -Force
Copy-Item -LiteralPath (Join-Path $templates "kit_gkaway_gloves.vmat") -Destination (Join-Path $materials "kit_gkaway_gloves.vmat") -Force

$kitTemplate = Get-Content -LiteralPath (Join-Path $kits "kit_home.vmdl") -Raw

function Make-GkModel([string]$name, [string]$body, [string]$lower, [string]$gloves) {
    $text = $kitTemplate.Replace("kit_a_body.vmat", "$body.vmat").Replace("kit_a_lower_body.vmat", "$lower.vmat")
    $pattern = '(?s)(from = "characters/models/tm_leet/materials/tm_leet_v2_lower_body_variantb\.vmat"\s+to = "materials/soccermod/kits/' + [Regex]::Escape($lower) + '\.vmat"\s+\},\s*)'
    $remap = @"
							{
								from = "characters/models/shared/arms/glove_fingerless/materials/glove_fingerless.vmat"
								to = "materials/soccermod/kits/$gloves.vmat"
							},
"@
    $text = [Regex]::Replace($text, $pattern, { param($match) $match.Groups[1].Value + $remap }, 1)
    # The source template's whitespace capture ends immediately before the
    # remap-array closing bracket. Keep the generated KV3 readable (and avoid
    # producing the valid-but-opaque `},]` sequence).
    $text = $text.Replace("},]", "},`r`n`t`t`t`t`t`t]")
    if ($text -notmatch [Regex]::Escape("materials/soccermod/kits/$gloves.vmat")) { throw "Glove remap was not inserted into $name" }
    Write-Utf8NoBom (Join-Path $kits "$name.vmdl") $text
}

Make-GkModel "kit_gkhome" "kit_gkhome_body" "kit_gkhome_lower_body" "kit_gkhome_gloves"
Make-GkModel "kit_gkaway" "kit_gkaway_body" "kit_gkaway_lower_body" "kit_gkaway_gloves"

Write-Host "Prepared isolated $AddonName keeper candidate." -ForegroundColor Green
Write-Host "Backup: $backup"
Write-Host "Both GK models now share kit_home.vmdl's validated variant-b gearless mesh; only body/lower/glove materials differ."
