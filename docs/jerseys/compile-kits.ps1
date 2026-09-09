# Compile Route 2: all four custom player models and their eight materials.
# This builds local assets; it does not publish or deliver them to clients.
# LegacyMaterials is retained only for reproducing the failed Route 1 experiment.
param(
    [string]$CsRoot = "E:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive",
    [switch]$Force,
    [ValidateSet("CustomModels", "LegacyMaterials")]
    [string]$Route = "CustomModels",
    [string]$CompilerPath = "",
    [ValidateSet("soccermod_jerseys", "soccermod_jerseys_gearless", "soccermod_jerseys_attachment", "soccermod_jerseys_dynamic")]
    [string]$AddonName = "soccermod_jerseys"
)

$ErrorActionPreference = "Stop"
$rc = if ($CompilerPath) { $CompilerPath } else { Join-Path $CsRoot "game/bin/win64/resourcecompiler.exe" }
$gameDir = Join-Path $CsRoot "game/csgo"
$contentAddon = Join-Path $CsRoot "content/csgo_addons/$AddonName"
$gameAddon = Join-Path $CsRoot "game/csgo_addons/$AddonName"
if (-not (Test-Path -LiteralPath $rc -PathType Leaf)) { throw "Resource compiler not found at $rc" }

$resources = @()
foreach ($variant in @("a", "b", "c", "d")) {
    foreach ($part in @("body", "lower_body")) {
        $resources += if ($Route -eq "CustomModels") {
            "materials/soccermod/kits/kit_${variant}_${part}.vmat"
        } else {
            "characters/models/tm_leet/materials/tm_leet_v2_${part}_variant${variant}.vmat"
        }
    }
}
if ($Route -eq "CustomModels") {
    foreach ($kit in @("home", "away", "gkhome", "gkaway")) {
        $resources += "models/soccermod/kits/kit_${kit}.vmdl"
    }
    if ($AddonName -in @("soccermod_jerseys_gearless", "soccermod_jerseys_dynamic")) {
        foreach ($kit in @("gkhome", "gkaway")) {
            $resources += "materials/soccermod/kits/kit_${kit}_body.vmat"
            $resources += "materials/soccermod/kits/kit_${kit}_lower_body.vmat"
            $resources += "materials/soccermod/kits/kit_${kit}_gloves.vmat"
        }
    }
} else {
    Write-Warning "Legacy Route 1 did not render the painted jerseys. This is not a playable kit release."
}

# Count exact outputs from this route, not old .vmat_c files elsewhere.
foreach ($relative in $resources) {
    if (-not (Test-Path -LiteralPath (Join-Path $contentAddon $relative) -PathType Leaf)) {
        throw "Required $Route source missing: $relative"
    }
}
New-Item -ItemType Directory -Force -Path $gameAddon | Out-Null
if (-not (Test-Path -LiteralPath (Join-Path $gameAddon "addoninfo.txt"))) {
    $kv3 = '<!-- kv3 encoding:text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d} format:generic:version{7412167c-06e9-4698-aff2-e63eb59037e7} -->' + "`r`n{`r`n}"
    Set-Content -LiteralPath (Join-Path $gameAddon "addoninfo.txt") -Value $kv3 -Encoding ascii
}

Push-Location (Split-Path $rc)
try {
    foreach ($relative in $resources) {
        $compilerArgs = @("-nop4", "-game", $gameDir, "-i", (Join-Path $contentAddon $relative))
        if ($Force) { $compilerArgs = @("-f") + $compilerArgs }
        $output = @(& $rc @compilerArgs 2>&1)
        $code = $LASTEXITCODE
        $failed = ($output | Out-String) -match '(?im)(^|\s)error\s*:|\b[1-9][0-9]*\s+failed\b|\bfailed to\b'
        if ($code -ne 0 -or $failed) {
            $output | Select-Object -Last 30 | Out-Host
            throw "Compilation failed for $relative (exit $code). Existing compiled files are not proof of success."
        }
        $compiled = Join-Path $gameAddon ($relative + "_c")
        if (-not (Test-Path -LiteralPath $compiled -PathType Leaf) -or (Get-Item -LiteralPath $compiled).Length -eq 0) {
            throw "Compiler produced no nonempty output for $relative"
        }
        Write-Host "Verified compiled output: $relative"
    }
} finally { Pop-Location }

Write-Host "$Route local build verified ($($resources.Count) required resources). Publish the complete package and verify it on a clean client before enabling kits."
