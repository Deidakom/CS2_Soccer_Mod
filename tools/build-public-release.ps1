[CmdletBinding()]
param(
    [string]$Version = "v1.0-beta",
    [string]$Cs2Game = "E:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive\game"
)

$ErrorActionPreference = "Stop"
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot "artifacts\releases"))
$packageName = "CS2-SoccerMod-$($Version.TrimStart('v'))-server"
$stageRoot = [IO.Path]::GetFullPath((Join-Path $artifactsRoot $packageName))
$zipPath = Join-Path $artifactsRoot "$packageName.zip"
$zipHashPath = "$zipPath.sha256"

if (-not $stageRoot.StartsWith($artifactsRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to stage outside the release artifacts directory: $stageRoot"
}

$dll = Join-Path $repoRoot "src\server-plugin\SoccerModMvp\bin\Release\net10.0\SoccerModNativeHull.dll"
$compiled = @(
    @{ Source = $dll; Target = "game\csgo\addons\counterstrikesharp\plugins\SoccerModNativeHull\SoccerModNativeHull.dll" },
    @{ Source = Join-Path $Cs2Game "csgo_addons\soccermod_phase1\models\soccermod\ball_large_1850.vmdl_c"; Target = "game\csgo\models\soccermod\ball_large_1850.vmdl_c" },
    @{ Source = Join-Path $Cs2Game "csgo_addons\soccermod_classic_ui\maps\scripts\soccermod_classic_menu.vjs_c"; Target = "game\csgo\maps\scripts\soccermod_classic_menu.vjs_c" },
    @{ Source = Join-Path $Cs2Game "csgo_addons\soccermod_classic_ui\panorama\layout\custom_game\soccermod_classic_menu.vxml_c"; Target = "game\csgo\panorama\layout\custom_game\soccermod_classic_menu.vxml_c" },
    @{ Source = Join-Path $Cs2Game "csgo_addons\soccermod_classic_ui\panorama\styles\custom_game\soccermod_classic_menu.vcss_c"; Target = "game\csgo\panorama\styles\custom_game\soccermod_classic_menu.vcss_c" },
    @{ Source = Join-Path $Cs2Game "csgo_addons\soccermod_stadium_radar\panorama\images\overheadmaps\soccer_cssl_stadium_v8_radar_psd.vtex_c"; Target = "game\csgo\panorama\images\overheadmaps\soccer_cssl_stadium_v8_radar_psd.vtex_c" },
    @{ Source = Join-Path $Cs2Game "csgo_addons\soccermod_stadium_radar\panorama\images\map_icons\screenshots\1080p\soccer_cssl_stadium_v8_png.vtex_c"; Target = "game\csgo\panorama\images\map_icons\screenshots\1080p\soccer_cssl_stadium_v8_png.vtex_c" },
    @{ Source = Join-Path $repoRoot "src\workshop-addon\soccermod_stadium_radar\resource\overviews\soccer_cssl_stadium_v8.txt"; Target = "game\csgo\resource\overviews\soccer_cssl_stadium_v8.txt" }
)

foreach ($entry in $compiled) {
    if (-not (Test-Path -LiteralPath $entry.Source -PathType Leaf)) {
        throw "Missing release input: $($entry.Source)"
    }
}

New-Item -ItemType Directory -Force -Path $artifactsRoot | Out-Null
if (Test-Path -LiteralPath $stageRoot) {
    Remove-Item -LiteralPath $stageRoot -Recurse -Force
}
if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}
if (Test-Path -LiteralPath $zipHashPath) {
    Remove-Item -LiteralPath $zipHashPath -Force
}
New-Item -ItemType Directory -Force -Path $stageRoot | Out-Null

Copy-Item -LiteralPath (Join-Path $repoRoot "deploy\release\README.md") -Destination (Join-Path $stageRoot "README.md")
Copy-Item -LiteralPath (Join-Path $repoRoot "deploy\release\install.sh") -Destination (Join-Path $stageRoot "install.sh")
Copy-Item -LiteralPath (Join-Path $repoRoot "deploy\release\verify.sh") -Destination (Join-Path $stageRoot "verify.sh")
New-Item -ItemType Directory -Force -Path (Join-Path $stageRoot "examples") | Out-Null
Copy-Item -LiteralPath (Join-Path $repoRoot "deploy\release\soccermod_server.cfg") -Destination (Join-Path $stageRoot "examples\soccermod_server.cfg")

foreach ($script in @("install.sh", "verify.sh")) {
    $path = Join-Path $stageRoot $script
    $content = [IO.File]::ReadAllText($path).Replace("`r`n", "`n")
    [IO.File]::WriteAllText($path, $content, [Text.UTF8Encoding]::new($false))
}

foreach ($entry in $compiled) {
    $target = Join-Path $stageRoot $entry.Target
    New-Item -ItemType Directory -Force -Path (Split-Path $target -Parent) | Out-Null
    Copy-Item -LiteralPath $entry.Source -Destination $target
}

$commit = (& git -C $repoRoot rev-parse HEAD).Trim()
[IO.File]::WriteAllText((Join-Path $stageRoot "VERSION"), "$Version`ncommit=$commit`n", [Text.UTF8Encoding]::new($false))

$manifestFiles = Get-ChildItem -LiteralPath $stageRoot -Recurse -File | Sort-Object FullName
$manifest = foreach ($file in $manifestFiles) {
    $relative = [IO.Path]::GetRelativePath($stageRoot, $file.FullName).Replace('\', '/')
    $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $relative"
}
[IO.File]::WriteAllText(
    (Join-Path $stageRoot "SHA256SUMS"),
    ($manifest -join "`n") + "`n",
    [Text.UTF8Encoding]::new($false))

Compress-Archive -LiteralPath $stageRoot -DestinationPath $zipPath -CompressionLevel Optimal
$zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText($zipHashPath, "$zipHash  $packageName.zip`n", [Text.UTF8Encoding]::new($false))

Write-Output "package=$zipPath"
Write-Output "sha256=$zipHash"
Write-Output "files=$($manifestFiles.Count)"
