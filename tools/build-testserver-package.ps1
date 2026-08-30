[CmdletBinding()]
param(
    [string] $Cs2Install = 'E:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive',
    [string] $OutputRoot = ''
)

$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $OutputRoot) {
    $OutputRoot = Join-Path $projectRoot 'artifacts\testserver'
}

$addonName = 'soccermod_phase1'
$addonSource = Join-Path $Cs2Install "game\csgo_addons\$addonName"
$expectedMapSha256 = '5B54AF803F00DB83FD5D123FC6E93AB28609ADA4989532A02AD2078A540CDD52'
$expectedAdapterSha256 = '8D8A5ED7BD0A5564ED956B218E9F8C50B5B3C4740C8C4E472302B5C13D6B8E6C'
$mapSource = Join-Path $addonSource 'maps\soccermod_phase1_lab.vpk'
$adapterSource = Join-Path $addonSource 'maps\scripts\ball_lab\adapter.vjs_c'

foreach ($required in @($addonSource, $mapSource, $adapterSource)) {
    if (-not (Test-Path -LiteralPath $required)) {
        throw "Missing test-server input: '$required'."
    }
}

$mapHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $mapSource).Hash
$adapterHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $adapterSource).Hash
if ($mapHash -ne $expectedMapSha256) {
    throw "Compiled map drifted. Expected '$expectedMapSha256', found '$mapHash'."
}
if ($adapterHash -ne $expectedAdapterSha256) {
    throw "Compiled adapter drifted. Expected '$expectedAdapterSha256', found '$adapterHash'."
}

$buildId = Get-Date -Format 'yyyyMMdd-HHmmss'
$stageRoot = Join-Path $OutputRoot "stage-$buildId"
$payloadAddon = Join-Path $stageRoot "payload\game\csgo_addons\$addonName"
$deployTarget = Join-Path $stageRoot 'deploy\testserver'
New-Item -ItemType Directory -Path $payloadAddon,$deployTarget -Force | Out-Null

$runtimeItems = @(
    'addoninfo.txt',
    'cfg',
    'maps\soccermod_phase1_cameras.txt',
    'maps\soccermod_phase1_lab.vpk',
    'maps\soccermod_phase1_retakes.txt',
    'maps\scripts',
    'postprocess',
    'soundevents',
    'sounds'
)
foreach ($relative in $runtimeItems) {
    $source = Join-Path $addonSource $relative
    if (-not (Test-Path -LiteralPath $source)) {
        throw "Missing runtime addon item: '$source'."
    }
    $destination = Join-Path $payloadAddon $relative
    $destinationParent = Split-Path -Parent $destination
    New-Item -ItemType Directory -Path $destinationParent -Force | Out-Null
    Copy-Item -LiteralPath $source -Destination $destination -Recurse -Force
}

Get-ChildItem -LiteralPath (Join-Path $projectRoot 'deploy\testserver') -File |
    ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $deployTarget -Force
    }

$hashLines = Get-ChildItem -LiteralPath $stageRoot -Recurse -File |
    Where-Object { $_.Name -ne 'SHA256SUMS' } |
    Sort-Object FullName |
    ForEach-Object {
        $relative = $_.FullName.Substring($stageRoot.Length + 1).Replace('\', '/')
        $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName).Hash.ToLowerInvariant()
        "$hash  $relative"
    }
Set-Content -LiteralPath (Join-Path $stageRoot 'SHA256SUMS') -Value $hashLines -Encoding utf8NoBOM

$archive = Join-Path $OutputRoot "cs2-soccermod-testserver-$buildId.zip"
Compress-Archive -Path (Join-Path $stageRoot '*') -DestinationPath $archive -CompressionLevel Optimal
$archiveHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $archive).Hash

[PSCustomObject]@{
    Archive = $archive
    ArchiveSha256 = $archiveHash
    MapSha256 = $mapHash
    AdapterSha256 = $adapterHash
    StageRoot = $stageRoot
} | ConvertTo-Json -Depth 3
