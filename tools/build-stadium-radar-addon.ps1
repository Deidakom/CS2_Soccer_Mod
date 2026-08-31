[CmdletBinding()]
param(
    [string]$Cs2ToolsRoot = 'E:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive',
    [string]$NodeExe = 'C:\Users\sergi\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe',
    [string]$NodeModules = 'C:\Users\sergi\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\node_modules'
)

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$sourceRoot = Join-Path $repoRoot 'src\workshop-addon\soccermod_stadium_radar'
$contentAddonRoot = Join-Path $Cs2ToolsRoot 'content\csgo_addons\soccermod_stadium_radar'
$gameCsgoRoot = Join-Path $Cs2ToolsRoot 'game\csgo'
$gameAddonRoot = Join-Path $Cs2ToolsRoot 'game\csgo_addons\soccermod_stadium_radar'
$compiler = Join-Path $Cs2ToolsRoot 'game\bin\win64\resourcecompiler.exe'
$renderer = Join-Path $repoRoot 'tools\render-stadium-radar.cjs'

if (-not (Test-Path -LiteralPath $compiler)) {
    throw "resourcecompiler.exe not found at $compiler"
}
if (-not (Test-Path -LiteralPath $sourceRoot)) {
    throw "Stadium radar addon source not found at $sourceRoot"
}
if (-not (Test-Path -LiteralPath $NodeExe)) {
    $NodeExe = (Get-Command node -ErrorAction Stop).Source
}

$expectedContentParent = (Join-Path $Cs2ToolsRoot 'content\csgo_addons') + '\'
if (-not $contentAddonRoot.StartsWith($expectedContentParent, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing unexpected content target: $contentAddonRoot"
}

$previousNodePath = $env:NODE_PATH
try {
    if (Test-Path -LiteralPath $NodeModules) {
        $env:NODE_PATH = $NodeModules
    }
    & $NodeExe $renderer
    if ($LASTEXITCODE -ne 0) {
        throw "Radar image rendering failed with exit code $LASTEXITCODE"
    }
}
finally {
    $env:NODE_PATH = $previousNodePath
}

New-Item -ItemType Directory -Force -Path $contentAddonRoot | Out-Null
Get-ChildItem -LiteralPath $sourceRoot | Copy-Item -Destination $contentAddonRoot -Recurse -Force

$compilerInputs = @(
    (Join-Path $contentAddonRoot 'panorama\images\overheadmaps\soccer_cssl_stadium_v8_radar_psd.vtex'),
    (Join-Path $contentAddonRoot 'panorama\images\map_icons\screenshots\1080p\soccer_cssl_stadium_v8_png.vtex')
)

& $compiler -nop4 -f -game $gameCsgoRoot -i $compilerInputs
if ($LASTEXITCODE -ne 0) {
    throw "Stadium radar resource compilation failed with exit code $LASTEXITCODE"
}

$overviewSource = Join-Path $sourceRoot 'resource\overviews\soccer_cssl_stadium_v8.txt'
$overviewOutput = Join-Path $gameAddonRoot 'resource\overviews\soccer_cssl_stadium_v8.txt'
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $overviewOutput) | Out-Null
Copy-Item -LiteralPath $overviewSource -Destination $overviewOutput -Force
Copy-Item -LiteralPath (Join-Path $sourceRoot 'addoninfo.txt') -Destination (Join-Path $gameAddonRoot 'addoninfo.txt') -Force

$expectedOutputs = @(
    (Join-Path $gameAddonRoot 'panorama\images\overheadmaps\soccer_cssl_stadium_v8_radar_psd.vtex_c'),
    (Join-Path $gameAddonRoot 'panorama\images\map_icons\screenshots\1080p\soccer_cssl_stadium_v8_png.vtex_c'),
    $overviewOutput
)
foreach ($output in $expectedOutputs) {
    if (-not (Test-Path -LiteralPath $output)) {
        throw "Expected radar resource was not produced: $output"
    }
}

Write-Host "Stadium radar addon compiled successfully: $gameAddonRoot"
