# Compiles the soccermod_jerseys addon materials (all four kits).
# Run after any kit texture changes. Validated 2026-09-07: 25 compiled, 0 failed.
#
#   powershell -ExecutionPolicy Bypass -File docs\jerseys\compile-kits.ps1
#
# Add -Force to recompile everything even if it looks up to date.

param(
    [string]$CsRoot = "E:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive",
    [switch]$Force
)

$rc      = Join-Path $CsRoot "game\bin\win64\resourcecompiler.exe"
$gameDir = Join-Path $CsRoot "game\csgo"
$matDir  = Join-Path $CsRoot "content\csgo_addons\soccermod_jerseys\characters\models\tm_leet\materials"
$gameAddon = Join-Path $CsRoot "game\csgo_addons\soccermod_jerseys"

if (-not (Test-Path $rc))     { throw "resourcecompiler.exe not found at $rc" }
if (-not (Test-Path $matDir)) { throw "addon material folder not found at $matDir" }

# The game-side addon folder needs an addoninfo.txt (empty KV3), or the addon
# is not recognised. Created here if missing, matching soccermod_phase1.
if (-not (Test-Path (Join-Path $gameAddon "addoninfo.txt"))) {
    New-Item -ItemType Directory -Force -Path $gameAddon | Out-Null
    $kv3 = '<!-- kv3 encoding:text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d} format:generic:version{7412167c-06e9-4698-aff2-e63eb59037e7} -->' + "`r`n{`r`n}"
    Set-Content -Path (Join-Path $gameAddon "addoninfo.txt") -Value $kv3 -Encoding ascii
    Write-Host "Created game-side addoninfo.txt" -ForegroundColor Yellow
}

$compilerArgs = @("-nop4", "-game", $gameDir, "-i", (Join-Path $matDir "*.vmat"))
if ($Force) { $compilerArgs = @("-f") + $compilerArgs }

Push-Location (Split-Path $rc)
try {
    & $rc @compilerArgs 2>&1 | Select-String -Pattern "OK:|failed|Error" | Select-Object -Last 10
    $code = $LASTEXITCODE
} finally { Pop-Location }

$vmatc = (Get-ChildItem -Path $gameAddon -Recurse -Filter *.vmat_c -ErrorAction SilentlyContinue).Count
$vtexc = (Get-ChildItem -Path $gameAddon -Recurse -Filter *.vtex_c -ErrorAction SilentlyContinue).Count
Write-Host ""
Write-Host ("exit={0}  compiled materials={1} (expect 8)  textures={2}" -f $code, $vmatc, $vtexc) -ForegroundColor Cyan
if ($code -ne 0 -or $vmatc -lt 8) { Write-Host "COMPILE INCOMPLETE - check output above" -ForegroundColor Red }
else { Write-Host "OK - all four kits compiled" -ForegroundColor Green }
